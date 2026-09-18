using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RRM_SM.Models;

namespace RRM_SM.Services
{
    public class CampaignParserService
    {
        private static readonly Regex AutosaveRegex = new(
            @"^save_Autosave\s+(?<faction>.+?)\s+Turn\s*(?<turn>\d+)(?:\s+(?<suffix>Start|End))?\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DelimitedSaveRegex = new(
            @"^save_(?<faction>.+?)\s*[-_]\s*(?<details>.+)\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DirectSaveRegex = new(
            @"^save_(?<faction>.+?)\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex QuicksaveRegex = new(
            @"^(?:save_)?quicksave(?:\s*[-_].*)?\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public CampaignSaveInfo ParseSaveFile(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            string fileName = fileInfo.Name;

            var saveInfo = new CampaignSaveInfo
            {
                FilePath = filePath,
                FileName = fileName,
                LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0
            };

            // 1. Quicksave check
            if (QuicksaveRegex.IsMatch(fileName))
            {
                saveInfo.Type = SaveFileType.Quicksave;
                saveInfo.FactionName = string.Empty; // To be resolved via context or default to General
                return saveInfo;
            }

            // 2. Autosave check
            var autoMatch = AutosaveRegex.Match(fileName);
            if (autoMatch.Success)
            {
                saveInfo.Type = SaveFileType.Autosave;
                saveInfo.FactionName = CleanFactionName(autoMatch.Groups["faction"].Value);
                if (int.TryParse(autoMatch.Groups["turn"].Value, out int turn))
                {
                    saveInfo.Turn = turn;
                }
                return saveInfo;
            }

            // 3. Delimited manual save (e.g. save_Kingdom of Macedon - 101.sav)
            var delimMatch = DelimitedSaveRegex.Match(fileName);
            if (delimMatch.Success)
            {
                saveInfo.Type = SaveFileType.Manual;
                saveInfo.FactionName = CleanFactionName(delimMatch.Groups["faction"].Value);
                string details = delimMatch.Groups["details"].Value.Trim();

                if (details.IndexOf("battle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    saveInfo.Type = SaveFileType.Battle;
                }

                // If details contains a number (e.g. Turn 101 or 101 or Turn 101 Battle or 101_2026-...)
                var turnMatch = Regex.Match(details, @"^(?:turn\s*)?(?<turn>\d+)(?:[\s_-].*)?$", RegexOptions.IgnoreCase);
                if (turnMatch.Success && int.TryParse(turnMatch.Groups["turn"].Value, out int turn))
                {
                    saveInfo.Turn = turn;
                }
                return saveInfo;
            }

            // 4. Direct save (e.g. save_Bactria.sav)
            var directMatch = DirectSaveRegex.Match(fileName);
            if (directMatch.Success)
            {
                saveInfo.Type = SaveFileType.Manual;
                saveInfo.FactionName = CleanFactionName(directMatch.Groups["faction"].Value);
                return saveInfo;
            }

            // 5. Fallback for files without save_ prefix
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (!string.IsNullOrWhiteSpace(baseName))
            {
                saveInfo.Type = SaveFileType.Manual;
                saveInfo.FactionName = CleanFactionName(baseName);
            }
            else
            {
                saveInfo.Type = SaveFileType.Unknown;
                saveInfo.FactionName = "General";
            }

            return saveInfo;
        }

        public Dictionary<string, List<CampaignSaveInfo>> GroupSaveFiles(IEnumerable<string> filePaths)
        {
            var parsedList = filePaths
                .Where(p => p.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
                .Select(ParseSaveFile)
                .ToList();

            // Find all files that already have a resolved faction
            var resolvedSaves = parsedList
                .Where(s => !string.IsNullOrWhiteSpace(s.FactionName) && !s.FactionName.Equals("General", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.LastModified)
                .ToList();

            // Resolve quicksaves and unassigned files
            foreach (var save in parsedList.Where(s => string.IsNullOrWhiteSpace(s.FactionName)))
            {
                string? associatedFaction = ResolveAssociatedFaction(save, resolvedSaves);
                save.FactionName = !string.IsNullOrWhiteSpace(associatedFaction) ? associatedFaction : "General";
            }

            // Group by FactionName
            var groups = new Dictionary<string, List<CampaignSaveInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var save in parsedList)
            {
                string factionKey = string.IsNullOrWhiteSpace(save.FactionName) ? "General" : save.FactionName;
                if (!groups.TryGetValue(factionKey, out var list))
                {
                    list = new List<CampaignSaveInfo>();
                    groups[factionKey] = list;
                }
                list.Add(save);
            }

            return groups;
        }

        private string? ResolveAssociatedFaction(CampaignSaveInfo quicksave, List<CampaignSaveInfo> resolvedSaves)
        {
            if (resolvedSaves.Count == 0)
            {
                return null;
            }

            // Option A: Look for the most recently modified resolved save within a 3-hour window
            var closest = resolvedSaves
                .Select(s => new { Save = s, Diff = Math.Abs((s.LastModified - quicksave.LastModified).TotalHours) })
                .Where(x => x.Diff <= 3.0)
                .OrderBy(x => x.Diff)
                .FirstOrDefault();

            if (closest != null)
            {
                return closest.Save.FactionName;
            }

            // If all resolved saves belong to a single faction, use that faction
            var uniqueFactions = resolvedSaves.Select(s => s.FactionName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (uniqueFactions.Count == 1)
            {
                return uniqueFactions[0];
            }

            // Most recent save before or near the quicksave
            var mostRecent = resolvedSaves.FirstOrDefault();
            if (mostRecent != null && Math.Abs((mostRecent.LastModified - quicksave.LastModified).TotalHours) <= 12.0)
            {
                return mostRecent.FactionName;
            }

            // Fallback to Option B: null -> caller assigns to General
            return null;
        }

        public static string CleanFactionName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "General";
            }

            string cleaned = rawName.Trim();

            // Remove invalid path characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                cleaned = cleaned.Replace(c.ToString(), "");
            }

            // Collapse multiple spaces into single space
            cleaned = Regex.Replace(cleaned, @"\s+", " ");

            cleaned = cleaned.Trim(' ', '.', '-');

            return string.IsNullOrWhiteSpace(cleaned) ? "General" : cleaned;
        }
    }
}

