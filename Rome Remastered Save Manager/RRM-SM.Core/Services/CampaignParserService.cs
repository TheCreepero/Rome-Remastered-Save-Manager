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
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        private static readonly Regex AutosaveRegex = new(
            @"^save_Autosave\s+(?<faction>.+?)\s+Turn\s*(?<turn>\d+)(?:\s+(?<suffix>Start|End))?\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex DelimitedSaveRegex = new(
            @"^save_(?<faction>.+?)\s*[-_]\s*(?<details>.+)\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex DirectSaveRegex = new(
            @"^save_(?<faction>.+?)\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        private static readonly Regex QuicksaveRegex = new(
            @"^(?:save_)?quicksave(?:\s*[-_].*)?\.sav$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            RegexTimeout);

        public static readonly string[] KnownFactions = new[]
        {
            "Senate and People of Rome", "Republic of Rome", "Western Roman Empire", "Eastern Roman Empire",
            "The House of Julii", "The House of Brutii", "The House of Scipii", "The House of Claudii",
            "Kingdom of Macedon", "The Seleucid Empire", "Germanic Tribes", "Greek Cities", "Romano-British",
            "SPQR I", "SPQR", "Rome", "Macedon", "Macedonia", "Egypt", "Seleucid", "Seleucids",
            "Carthage", "Parthia", "Pontus", "Gaul", "Gauls", "Germania", "Britannia", "Britons",
            "Armenia", "Dacia", "Numidia", "Scythia", "Spain", "Thrace", "Bactria", "Rhodes", "Syracuse",
            "Huns", "Goths", "Vandals", "Sarmatians", "Saxons", "Franks", "Alamanni", "Sassanids",
            "Celts", "Burgundii", "Lombards", "Roxolani", "Slavs", "Berbers", "Ostrogoths",
            "Alexander", "Persia", "India", "Dahae", "Illyria"
        };

        public CampaignSaveInfo ParseSaveFile(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            string fileName = fileInfo.Name;

            var saveInfo = new CampaignSaveInfo
            {
                FilePath = filePath,
                FileName = fileName,
                LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now,
                FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                GameCampaignId = TryReadInternalCampaignGuid(filePath)
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

            // 3. Delimited manual save (e.g. save_Kingdom of Macedon - 101.sav or save_Pontus_Turn 10.sav)
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
                var turnMatch = Regex.Match(details, @"^(?:turn\s*)?(?<turn>\d+)(?:[\s_-].*)?$", RegexOptions.IgnoreCase, RegexTimeout);
                if (turnMatch.Success && int.TryParse(turnMatch.Groups["turn"].Value, out int turn))
                {
                    saveInfo.Turn = turn;
                }
                return saveInfo;
            }

            // 4. Space-separated turn manual save (e.g. save_Pontus 5.sav or save_Pontus Turn 5.sav)
            var turnSpaceMatch = Regex.Match(fileName, @"^save_(?<faction>.+?)\s+(?:turn\s*)?(?<turn>\d+)(?:\s+(?<details>.*))?\.sav$", RegexOptions.IgnoreCase, RegexTimeout);
            if (turnSpaceMatch.Success)
            {
                saveInfo.Type = SaveFileType.Manual;
                saveInfo.FactionName = CleanFactionName(turnSpaceMatch.Groups["faction"].Value);
                if (int.TryParse(turnSpaceMatch.Groups["turn"].Value, out int turn))
                {
                    saveInfo.Turn = turn;
                }
                string details = turnSpaceMatch.Groups["details"].Value.Trim();
                if (details.IndexOf("battle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    saveInfo.Type = SaveFileType.Battle;
                }
                return saveInfo;
            }

            // 5. Check if filename matches a known canonical faction followed by space qualifier (e.g. save_Pontus Second.sav, save_Rome 2.sav)
            if (fileName.StartsWith("save_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
            {
                string payload = fileName.Substring(5, fileName.Length - 9).Trim();
                foreach (var known in KnownFactions)
                {
                    if (payload.Equals(known, StringComparison.OrdinalIgnoreCase))
                    {
                        saveInfo.Type = SaveFileType.Manual;
                        saveInfo.FactionName = known;
                        return saveInfo;
                    }
                    if (payload.StartsWith(known + " ", StringComparison.OrdinalIgnoreCase))
                    {
                        saveInfo.Type = SaveFileType.Manual;
                        saveInfo.FactionName = known;
                        string remainder = payload.Substring(known.Length + 1).Trim();
                        if (remainder.IndexOf("battle", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            saveInfo.Type = SaveFileType.Battle;
                        }
                        var remTurn = Regex.Match(remainder, @"^(?:turn\s*)?(?<turn>\d+)", RegexOptions.IgnoreCase, RegexTimeout);
                        if (remTurn.Success && int.TryParse(remTurn.Groups["turn"].Value, out int t))
                        {
                            saveInfo.Turn = t;
                        }
                        return saveInfo;
                    }
                }
            }

            // 6. Direct save (e.g. save_Bactria.sav)
            var directMatch = DirectSaveRegex.Match(fileName);
            if (directMatch.Success)
            {
                saveInfo.Type = SaveFileType.Manual;
                saveInfo.FactionName = CleanFactionName(directMatch.Groups["faction"].Value);
                return saveInfo;
            }

            // 7. Fallback for files without save_ prefix
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

        /// <summary>
        /// Reads the authoritative 16-byte internal Campaign GUID from bytes 36..51 of a Total War: ROME REMASTERED .sav file header.
        /// Returns the standard lowercase hyphenated GUID string (e.g. "6ac7cc5e-1378-cbde-8b61-a0844b08a46a"), or null if unavailable.
        /// </summary>
        public static string? TryReadInternalCampaignGuid(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            try
            {
                if (!File.Exists(filePath)) return null;

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 52) return null;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] header = new byte[52];
                int bytesRead = fs.Read(header, 0, 52);
                if (bytesRead < 52) return null;

                byte[] guidBytes = new byte[16];
                Buffer.BlockCopy(header, 36, guidBytes, 0, 16);
                var guid = new Guid(guidBytes);
                if (guid != Guid.Empty)
                {
                    return guid.ToString("D");
                }
            }
            catch
            {
                // Non-blocking fallback for inaccessible, locked, or mock test files
            }

            return null;
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
            foreach (var save in parsedList.Where(s => string.IsNullOrWhiteSpace(s.FactionName) || s.FactionName.Equals("General", StringComparison.OrdinalIgnoreCase)))
            {
                // 1. Primary: Ground-truth match by GameCampaignId if available
                if (!string.IsNullOrWhiteSpace(save.GameCampaignId))
                {
                    var matchingByGuid = resolvedSaves.FirstOrDefault(s => s.GameCampaignId == save.GameCampaignId);
                    if (matchingByGuid != null && !string.IsNullOrWhiteSpace(matchingByGuid.FactionName))
                    {
                        save.FactionName = matchingByGuid.FactionName;
                        continue;
                    }
                }

                // 2. Fallback: Proximity heuristic
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
            cleaned = Regex.Replace(cleaned, @"\s+", " ", RegexOptions.None, RegexTimeout);

            cleaned = cleaned.Trim(' ', '.', '-');

            return string.IsNullOrWhiteSpace(cleaned) ? "General" : cleaned;
        }
    }
}

