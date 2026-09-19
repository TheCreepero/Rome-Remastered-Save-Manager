using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using RRM_SM.Models;
using RRM_SM.Core.Models;
using RRM_SM.Core.Security;

namespace RRM_SM.Services
{
    public class ChronicleService
    {
        private readonly AppConfig _config;
        private readonly CampaignParserService _parserService;
        private readonly SaveVaultService? _vaultService;

        public ChronicleService(AppConfig config, CampaignParserService parserService, SaveVaultService? vaultService = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _parserService = parserService ?? throw new ArgumentNullException(nameof(parserService));
            _vaultService = vaultService;
        }

        private string GetChronicleFilePath(string campaignName, string? campaignId = null)
        {
            if (!string.IsNullOrWhiteSpace(campaignId))
            {
                string cleanId = BackupService.SanitizeFileName(campaignId);
                string idPath = PathSecurity.EnsureSafeChildPath(_config.BackupDirectory, $"chronicle_{cleanId}.json");
                if (File.Exists(idPath)) return idPath;
            }

            string cleanCampaign = CampaignParserService.CleanFactionName(campaignName);
            string flatPath = PathSecurity.EnsureSafeChildPath(_config.BackupDirectory, $"chronicle_{cleanCampaign}.json");
            if (File.Exists(flatPath)) return flatPath;

            return !string.IsNullOrWhiteSpace(campaignId)
                ? PathSecurity.EnsureSafeChildPath(_config.BackupDirectory, $"chronicle_{BackupService.SanitizeFileName(campaignId)}.json")
                : flatPath;
        }

        public CampaignChronicle BuildChronicle(string campaignName, string? campaignId = null)
        {
            var chronicleFile = GetChronicleFilePath(campaignName, campaignId);
            var chronicle = new CampaignChronicle
            {
                CampaignId = campaignId,
                CampaignName = campaignName,
                ModName = "Rome Remastered" // Default, could be customized
            };

            // Load existing notes
            if (File.Exists(chronicleFile))
            {
                try
                {
                    string json = File.ReadAllText(chronicleFile);
                    var existingChronicle = JsonSerializer.Deserialize<CampaignChronicle>(json);
                    if (existingChronicle != null)
                    {
                        chronicle.CampaignSummary = existingChronicle.CampaignSummary ?? "";
                        chronicle.ModName = !string.IsNullOrWhiteSpace(existingChronicle.ModName) ? existingChronicle.ModName : "Rome Remastered";
                        chronicle.Milestones = existingChronicle.Milestones ?? new List<ChronicleMilestone>();
                    }
                }
                catch (Exception)
                {
                    // Ignore parsing errors for now
                }
            }

            var saveInfos = new List<CampaignSaveInfo>();

            // 1. If vault service is available, query vault saves for this campaign ID or name
            if (_vaultService != null)
            {
                var vaultSaves = _vaultService.GetCampaignSaves(campaignName, campaignId);
                foreach (var vs in vaultSaves)
                {
                    saveInfos.Add(new CampaignSaveInfo
                    {
                        FilePath = Path.Combine(_config.BackupDirectory, vs.StoredFileName),
                        FileName = vs.OriginalGameFileName,
                        FactionName = vs.Faction ?? vs.CampaignName,
                        Turn = vs.Turn,
                        Type = vs.SaveType,
                        LastModified = vs.LastModified,
                        FileSizeBytes = vs.FileSizeBytes,
                        InGameDate = vs.InGameDate,
                        ModName = vs.ModName,
                        CalendarYear = vs.CalendarYear,
                        GameCampaignId = vs.GameCampaignId
                    });
                }
            }
            else
            {
                // Fallback: gather all backup files and parse
                string backupFolder = _config.BackupDirectory;
                if (Directory.Exists(backupFolder))
                {
                    var backupFiles = Directory.GetFiles(backupFolder, "*.sav", SearchOption.AllDirectories);
                    var grouped = _parserService.GroupSaveFiles(backupFiles);
                    if (grouped.TryGetValue(campaignName, out var bSaves))
                    {
                        saveInfos.AddRange(bSaves);
                    }
                }
            }

            // 2. Active game saves: add saves that belong to this campaign
            string activeFolder = _config.GameSaveDirectory;
            if (Directory.Exists(activeFolder))
            {
                var activeFiles = Directory.GetFiles(activeFolder, "*.sav");
                var grouped = _parserService.GroupSaveFiles(activeFiles);
                string faction = campaignName;
                string? campaignGuid = null;
                if (_vaultService != null)
                {
                    var meta = _vaultService.GetCampaignMetadata(campaignId ?? campaignName);
                    if (meta != null)
                    {
                        if (!string.IsNullOrWhiteSpace(meta.Faction))
                        {
                            faction = meta.Faction;
                        }
                        campaignGuid = meta.GameCampaignId;
                    }
                }

                if (grouped.TryGetValue(faction, out var aSaves))
                {
                    foreach (var aSave in aSaves)
                    {
                        // 1. Ground truth GUID match: if both have a GUID, they must match
                        if (!string.IsNullOrWhiteSpace(campaignGuid) && !string.IsNullOrWhiteSpace(aSave.GameCampaignId))
                        {
                            if (!aSave.GameCampaignId.Equals(campaignGuid, StringComparison.OrdinalIgnoreCase))
                            {
                                continue; // Belongs to a different campaign playthrough!
                            }
                            saveInfos.Add(aSave);
                            continue;
                        }

                        // 2. Fallback heuristic for legacy saves without GameCampaignId
                        if (saveInfos.Count == 0)
                        {
                            saveInfos.Add(aSave);
                        }
                        else
                        {
                            double minDays = saveInfos.Min(s => Math.Abs((aSave.LastModified - s.LastModified).TotalDays));
                            if (minDays <= 14.0)
                            {
                                saveInfos.Add(aSave);
                            }
                        }
                    }
                }
            }

            // Deduplicate by file name, taking the one with the latest modified date just in case
            var uniqueSaves = saveInfos
                .GroupBy(s => s.FileName.ToLowerInvariant())
                .Select(g => g.OrderByDescending(s => s.LastModified).First())
                .OrderBy(s => s.LastModified)
                .ToList();

            var newMilestones = new List<ChronicleMilestone>();
            foreach (var save in uniqueSaves)
            {
                // Ensure in-game date metadata is loaded if file is accessible
                if (string.IsNullOrWhiteSpace(save.InGameDate) && File.Exists(save.FilePath))
                {
                    var meta = SaveMetadataReader.ReadSaveMetadata(save.FilePath);
                    if (meta != null)
                    {
                        save.InGameDate = meta.InGameDate;
                        save.CalendarYear = meta.CalendarYear;
                        save.Season = meta.Season;
                        if (string.IsNullOrWhiteSpace(save.ModName)) save.ModName = meta.PrimaryModName;
                        if (!save.Turn.HasValue && meta.TurnNumber.HasValue) save.Turn = meta.TurnNumber;
                    }
                }

                // Check if we already have this milestone
                var existing = chronicle.Milestones.FirstOrDefault(m => m.SaveFileName.Equals(save.FileName, StringComparison.OrdinalIgnoreCase));
                
                if (existing != null)
                {
                    // Update dynamic properties
                    existing.Timestamp = save.LastModified;
                    existing.SaveSizeBytes = save.FileSizeBytes;
                    if (string.IsNullOrWhiteSpace(existing.InGameDate)) existing.InGameDate = save.InGameDate;
                    if (!existing.CalendarYear.HasValue) existing.CalendarYear = save.CalendarYear;
                    if (string.IsNullOrWhiteSpace(existing.Season)) existing.Season = save.Season;
                    if (string.IsNullOrWhiteSpace(existing.ModName)) existing.ModName = save.ModName;
                    newMilestones.Add(existing);
                }
                else
                {
                    int turn = save.Turn ?? 0;
                    string defaultTitle = !string.IsNullOrWhiteSpace(save.InGameDate)
                        ? $"Turn {turn} ({save.InGameDate})"
                        : (string.IsNullOrWhiteSpace(save.Turn?.ToString()) ? save.FileName : $"Turn {save.Turn}");

                    newMilestones.Add(new ChronicleMilestone
                    {
                        Turn = turn,
                        Timestamp = save.LastModified,
                        Title = defaultTitle,
                        SaveType = save.Type,
                        SaveSizeBytes = save.FileSizeBytes,
                        SaveFileName = save.FileName,
                        InGameDate = save.InGameDate,
                        CalendarYear = save.CalendarYear,
                        Season = save.Season,
                        ModName = save.ModName
                    });
                }
            }

            // Sort by timestamp and calculate deltas
            chronicle.Milestones = newMilestones.OrderBy(m => m.Timestamp).ToList();

            ChronicleMilestone? prevMilestone = null;
            foreach (var m in chronicle.Milestones)
            {
                if (prevMilestone != null)
                {
                    m.DeltaTurns = m.Turn - prevMilestone.Turn;
                    if (m.CalendarYear.HasValue && prevMilestone.CalendarYear.HasValue)
                    {
                        m.DeltaYears = Math.Abs(m.CalendarYear.Value - prevMilestone.CalendarYear.Value);
                    }
                }
                prevMilestone = m;
            }

            if (chronicle.Milestones.Any())
            {
                chronicle.StartedAt = chronicle.Milestones.First().Timestamp;
                chronicle.LastPlayedAt = chronicle.Milestones.Last().Timestamp;
                chronicle.MaxTurn = chronicle.Milestones.Max(m => m.Turn);

                // Detect primary mod and all active submods across milestones
                var detectedMods = chronicle.Milestones
                    .Where(m => !string.IsNullOrWhiteSpace(m.ModName) && !m.ModName.Equals("Rome Remastered", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.ModName!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (detectedMods.Count > 0)
                {
                    chronicle.ModName = detectedMods[0];
                    chronicle.ActiveMods = detectedMods;
                }
                else if (string.IsNullOrWhiteSpace(chronicle.ModName))
                {
                    chronicle.ModName = "Rome Remastered";
                }

                // Historical Era span
                var withYears = chronicle.Milestones.Where(m => m.CalendarYear.HasValue).ToList();
                if (withYears.Any())
                {
                    int minYear = withYears.Min(m => m.CalendarYear!.Value);
                    int maxYear = withYears.Max(m => m.CalendarYear!.Value);
                    chronicle.StartYear = FormatYear(minYear);
                    chronicle.EndYear = FormatYear(maxYear);
                    chronicle.TotalYearsSpan = Math.Abs(maxYear - minYear);
                    chronicle.EraSummary = $"{chronicle.StartYear} - {chronicle.EndYear} ({chronicle.TotalYearsSpan} years, {chronicle.MaxTurn} turns)";
                }
                else
                {
                    chronicle.EraSummary = $"{chronicle.MaxTurn} turns";
                }
            }

            return chronicle;
        }

        public static string FormatYear(int year)
        {
            return year < 0 ? $"{Math.Abs(year)} BC" : $"{year} AD";
        }

        public void SaveChronicleNotes(CampaignChronicle chronicle)
        {
            var chronicleFile = GetChronicleFilePath(chronicle.CampaignName, chronicle.CampaignId);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(chronicle, options);
            string tempPath = chronicleFile + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, chronicleFile, overwrite: true);
        }

        public string GenerateMarkdownReport(CampaignChronicle chronicle)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {chronicle.CampaignName} - Campaign Chronicle");
            sb.AppendLine();

            sb.AppendLine($"- **Mod / Version:** {chronicle.ModName}");
            if (chronicle.ActiveMods != null && chronicle.ActiveMods.Count > 1)
            {
                sb.AppendLine($"- **Active Submods:** {string.Join(", ", chronicle.ActiveMods)}");
            }
            if (!string.IsNullOrWhiteSpace(chronicle.EraSummary))
            {
                sb.AppendLine($"- **Historical Era:** {chronicle.EraSummary}");
            }
            if (chronicle.Milestones.Any())
            {
                sb.AppendLine($"- **Real-World Timeline:** {chronicle.StartedAt:yyyy-MM-dd} to {chronicle.LastPlayedAt:yyyy-MM-dd} ({chronicle.Milestones.Count} recorded milestones)");
            }
            sb.AppendLine();
            
            if (!string.IsNullOrWhiteSpace(chronicle.CampaignSummary))
            {
                sb.AppendLine("## Campaign Summary");
                sb.AppendLine(chronicle.CampaignSummary);
                sb.AppendLine();
            }

            sb.AppendLine("## Milestones");
            sb.AppendLine();
            
            foreach (var milestone in chronicle.Milestones.OrderBy(m => m.Timestamp))
            {
                string title = string.IsNullOrWhiteSpace(milestone.Title) ? milestone.SaveFileName : milestone.Title;
                string dateInfo = !string.IsNullOrWhiteSpace(milestone.InGameDate) ? $" - {milestone.InGameDate}" : "";
                string deltaInfo = milestone.DeltaTurns.HasValue
                    ? $" (+{milestone.DeltaTurns.Value} turns" + (milestone.DeltaYears.HasValue && milestone.DeltaYears.Value > 0 ? $", +{milestone.DeltaYears.Value} yrs" : "") + ")"
                    : "";

                sb.AppendLine($"### {title} (Turn {milestone.Turn}{dateInfo}){deltaInfo}");
                sb.AppendLine($"*{milestone.Timestamp:yyyy-MM-dd HH:mm} | {milestone.SaveType}*");
                
                if (milestone.Tags != null && milestone.Tags.Any())
                {
                    sb.AppendLine($"**Tags:** {string.Join(", ", milestone.Tags)}");
                }
                
                sb.AppendLine();
                
                if (!string.IsNullOrWhiteSpace(milestone.PlayerNotes))
                {
                    sb.AppendLine(milestone.PlayerNotes);
                }
                else
                {
                    sb.AppendLine("*No notes recorded.*");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string GenerateHtmlReport(CampaignChronicle chronicle)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine($"    <title>{System.Net.WebUtility.HtmlEncode(chronicle.CampaignName)} - Campaign Chronicle</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine("        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f1ea; color: #333; line-height: 1.6; padding: 20px; }");
            sb.AppendLine("        .container { max-width: 800px; margin: 0 auto; background: #fff; padding: 30px; border-radius: 8px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); border-top: 5px solid #b71c1c; }");
            sb.AppendLine("        h1 { color: #b71c1c; text-align: center; border-bottom: 2px solid #e0e0e0; padding-bottom: 10px; margin-bottom: 15px; }");
            sb.AppendLine("        h2 { color: #d32f2f; margin-top: 30px; }");
            sb.AppendLine("        .meta-bar { background: #fafafa; border: 1px solid #e0e0e0; border-radius: 6px; padding: 14px 18px; margin-bottom: 25px; }");
            sb.AppendLine("        .badge { display: inline-block; padding: 4px 12px; border-radius: 14px; font-size: 0.85em; font-weight: 600; margin-right: 8px; margin-bottom: 6px; }");
            sb.AppendLine("        .mod-badge { background: #5a4a20; color: #f5d77f; border: 1px solid #c9a84c; }");
            sb.AppendLine("        .era-badge { background: #2e2e48; color: #98c379; border: 1px solid #3e3e5e; }");
            sb.AppendLine("        .stat-badge { background: #f0f0f0; color: #424242; border: 1px solid #d0d0d0; }");
            sb.AppendLine("        .summary { background: #fafafa; padding: 15px; border-left: 4px solid #d32f2f; margin-bottom: 30px; font-style: italic; }");
            sb.AppendLine("        .milestone { margin-bottom: 25px; padding-bottom: 15px; border-bottom: 1px solid #eee; }");
            sb.AppendLine("        .milestone-header { display: flex; justify-content: space-between; align-items: baseline; }");
            sb.AppendLine("        .milestone-title { margin: 0; color: #424242; }");
            sb.AppendLine("        .date-pill { background: #e8f0fe; color: #1967d2; padding: 2px 8px; border-radius: 4px; font-weight: 600; font-size: 0.8em; margin-left: 6px; }");
            sb.AppendLine("        .milestone-meta { font-size: 0.85em; color: #757575; }");
            sb.AppendLine("        .tags { margin-top: 5px; }");
            sb.AppendLine("        .tag { display: inline-block; background: #e0e0e0; color: #424242; padding: 2px 8px; border-radius: 12px; font-size: 0.8em; margin-right: 5px; }");
            sb.AppendLine("        .notes { margin-top: 10px; white-space: pre-wrap; }");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("    <div class=\"container\">");
            sb.AppendLine($"        <h1>{System.Net.WebUtility.HtmlEncode(chronicle.CampaignName)} Chronicle</h1>");
            
            // Meta bar with Mod, Era, and Stats
            sb.AppendLine("        <div class=\"meta-bar\">");
            if (!string.IsNullOrWhiteSpace(chronicle.ModName))
            {
                sb.AppendLine($"            <span class=\"badge mod-badge\">{System.Net.WebUtility.HtmlEncode(chronicle.ModName)}</span>");
            }
            if (!string.IsNullOrWhiteSpace(chronicle.EraSummary))
            {
                sb.AppendLine($"            <span class=\"badge era-badge\">{System.Net.WebUtility.HtmlEncode(chronicle.EraSummary)}</span>");
            }
            sb.AppendLine($"            <span class=\"badge stat-badge\">{chronicle.Milestones.Count} Milestones | Max Turn {chronicle.MaxTurn}</span>");
            sb.AppendLine("        </div>");

            if (!string.IsNullOrWhiteSpace(chronicle.CampaignSummary))
            {
                sb.AppendLine("        <h2>Campaign Summary</h2>");
                sb.AppendLine($"        <div class=\"summary\">{System.Net.WebUtility.HtmlEncode(chronicle.CampaignSummary)}</div>");
            }

            sb.AppendLine("        <h2>Timeline</h2>");
            
            foreach (var milestone in chronicle.Milestones.OrderBy(m => m.Timestamp))
            {
                string title = string.IsNullOrWhiteSpace(milestone.Title) ? milestone.SaveFileName : milestone.Title;
                string inGameDateHtml = !string.IsNullOrWhiteSpace(milestone.InGameDate)
                    ? $" <span class=\"date-pill\">{System.Net.WebUtility.HtmlEncode(milestone.InGameDate)}</span>"
                    : "";

                sb.AppendLine("        <div class=\"milestone\">");
                sb.AppendLine("            <div class=\"milestone-header\">");
                sb.AppendLine($"                <h3 class=\"milestone-title\">{System.Net.WebUtility.HtmlEncode(title)} <small>(Turn {milestone.Turn})</small>{inGameDateHtml}</h3>");
                sb.AppendLine($"                <span class=\"milestone-meta\">{milestone.Timestamp:yyyy-MM-dd HH:mm} | {milestone.SaveType}</span>");
                sb.AppendLine("            </div>");
                
                if (milestone.Tags != null && milestone.Tags.Any())
                {
                    sb.AppendLine("            <div class=\"tags\">");
                    foreach(var tag in milestone.Tags)
                    {
                        sb.AppendLine($"                <span class=\"tag\">{System.Net.WebUtility.HtmlEncode(tag)}</span>");
                    }
                    sb.AppendLine("            </div>");
                }
                
                if (!string.IsNullOrWhiteSpace(milestone.PlayerNotes))
                {
                    sb.AppendLine($"            <div class=\"notes\">{System.Net.WebUtility.HtmlEncode(milestone.PlayerNotes)}</div>");
                }
                else
                {
                    sb.AppendLine("            <div class=\"notes\" style=\"color: #9e9e9e; font-style: italic;\">No notes recorded.</div>");
                }
                
                sb.AppendLine("        </div>");
            }

            sb.AppendLine("    </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }
    }
}
