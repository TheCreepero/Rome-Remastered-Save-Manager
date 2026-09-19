using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RRM_SM.Services
{
    public class SaveMetadata
    {
        public string? GameCampaignId { get; set; }
        public int? TurnNumber { get; set; }
        public int? CalendarYear { get; set; }
        public string? Season { get; set; }
        public string? InGameDate { get; set; }
        public string? PrimaryModName { get; set; }
        public List<string> ActiveMods { get; set; } = new();
        public string? CampaignMapPath { get; set; }
    }

    public static class SaveMetadataReader
    {
        private static readonly byte[] DescrStratUtf16 = Encoding.Unicode.GetBytes("descr_strat.txt");
        private static readonly byte[] ModMarker = new byte[] { 0xD2, 0x02, 0x96, 0x49 };

        /// <summary>
        /// Reads deep metadata (GUID, turn number, calendar year, season, active mods)
        /// directly from the first 64KB of a Total War: ROME REMASTERED .sav file.
        /// </summary>
        public static SaveMetadata? ReadSaveMetadata(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 52)
                    return null;

                int bytesToRead = (int)Math.Min(fileInfo.Length, 65536);
                byte[] buffer = new byte[bytesToRead];

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int bytesRead = fs.Read(buffer, 0, bytesToRead);
                    if (bytesRead < 52)
                        return null;
                }

                var meta = new SaveMetadata();

                // 1. Authoritative internal Campaign GUID (bytes 36..51)
                byte[] guidBytes = new byte[16];
                Buffer.BlockCopy(buffer, 36, guidBytes, 0, 16);
                var guid = new Guid(guidBytes);
                if (guid != Guid.Empty)
                {
                    meta.GameCampaignId = guid.ToString("D");
                }

                // 2. Active Mods Table
                ExtractMods(buffer, meta);

                // 3. In-Game Calendar (Turn, Year BC/AD, Season)
                ExtractCalendar(buffer, meta);

                return meta;
            }
            catch
            {
                // Non-blocking fallback for inaccessible, locked, or corrupt files
                return null;
            }
        }

        private static void ExtractMods(byte[] buffer, SaveMetadata meta)
        {
            int modMarkerPos = IndexOf(buffer, ModMarker, 0, Math.Min(buffer.Length, 16384));
            var detectedMods = new List<string>();

            if (modMarkerPos != -1 && modMarkerPos + 8 <= buffer.Length)
            {
                int count = BitConverter.ToInt32(buffer, modMarkerPos + 4);
                int pos = modMarkerPos + 8;

                for (int i = 0; i < count && pos + 10 <= buffer.Length; i++)
                {
                    pos += 8; // skip mod ID / hash
                    int nameLen = BitConverter.ToUInt16(buffer, pos);
                    pos += 2;

                    if (nameLen > 0 && pos + nameLen * 2 <= buffer.Length)
                    {
                        string rawName = Encoding.Unicode.GetString(buffer, pos, nameLen * 2);
                        pos += nameLen * 2;

                        string cleaned = CleanModString(rawName);
                        if (!string.IsNullOrWhiteSpace(cleaned) && !detectedMods.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                        {
                            detectedMods.Add(cleaned);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Fallback scan of UTF-16 header pool (0x1000..0x3500) if table was empty or not matched
            if (detectedMods.Count == 0)
            {
                int scanLimit = Math.Min(buffer.Length - 4, 0x3500);
                for (int i = 0x1000; i < scanLimit; i += 2)
                {
                    if (buffer[i + 1] == 0 && buffer[i] >= 32 && buffer[i] <= 126)
                    {
                        int start = i;
                        var sb = new StringBuilder();
                        while (i < scanLimit - 1 && buffer[i + 1] == 0 && buffer[i] >= 32 && buffer[i] <= 126)
                        {
                            sb.Append((char)buffer[i]);
                            i += 2;
                        }

                        string candidate = CleanModString(sb.ToString());
                        if (candidate.Length >= 4 && !candidate.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith("campaign/", StringComparison.OrdinalIgnoreCase))
                        {
                            if (candidate.IndexOf("surrectum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                candidate.IndexOf("chivalry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                candidate.IndexOf("blood mod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                candidate.IndexOf("submod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                candidate.IndexOf("ris ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                candidate.IndexOf("beta", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (!detectedMods.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                                {
                                    detectedMods.Add(candidate);
                                }
                            }
                        }
                    }
                }
            }

            meta.ActiveMods = detectedMods;

            // Pick primary mod name
            string primary = "Rome Remastered";
            foreach (var m in detectedMods)
            {
                if (m.IndexOf("surrectum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("chivalry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("ris ", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    primary = m;
                    break;
                }
            }

            if (primary == "Rome Remastered" && detectedMods.Count > 0)
            {
                primary = detectedMods[0];
            }

            meta.PrimaryModName = primary;
        }

        private static void ExtractCalendar(byte[] buffer, SaveMetadata meta)
        {
            int dsPos = IndexOf(buffer, DescrStratUtf16, 0, buffer.Length);
            if (dsPos == -1) return;

            int after = dsPos + DescrStratUtf16.Length;
            if (after + 24 > buffer.Length) return;

            // Structure immediately following descr_strat.txt:
            // after + 0..4: token/pointer (uint32)
            // after + 4: marker (0x01)
            // after + 5..9: int32 turn number (0-indexed)
            // after + 9..13: int32 calendar year (signed: negative = BC, positive = AD)
            // after + 13..17: int32 season (0 = Summer, 2 = Winter)
            int rawTurn = BitConverter.ToInt32(buffer, after + 5);
            int rawYear = BitConverter.ToInt32(buffer, after + 9);
            int rawSeason = BitConverter.ToInt32(buffer, after + 13);

            meta.TurnNumber = rawTurn + 1;
            meta.CalendarYear = rawYear;
            meta.Season = (rawSeason == 2) ? "Winter" : "Summer";

            if (rawYear != 0)
            {
                string era = rawYear < 0 ? $"{Math.Abs(rawYear)} BC" : $"{rawYear} AD";
                meta.InGameDate = $"{meta.Season} {era}";
            }
        }

        private static string CleanModString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string trimmed = raw.Trim();
            trimmed = Regex.Replace(trimmed, @"^[^\w\[\(]+", "");
            return trimmed.Trim();
        }

        private static int IndexOf(byte[] source, byte[] pattern, int startIndex, int count)
        {
            if (source == null || pattern == null || pattern.Length == 0 || source.Length < pattern.Length)
                return -1;

            int maxIndex = Math.Min(startIndex + count, source.Length) - pattern.Length;
            for (int i = startIndex; i <= maxIndex; i++)
            {
                if (source[i] != pattern[0]) continue;

                bool match = true;
                for (int j = 1; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match) return i;
            }

            return -1;
        }
    }
}

