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
            var detectedMods = new List<string>();
            int limit = Math.Min(buffer.Length - 16, 65536);

            // Scan through occurrences of ModMarker { 0xD2, 0x02, 0x96, 0x49 }
            for (int pos = 0; pos <= limit; pos++)
            {
                if (buffer[pos] == ModMarker[0] &&
                    buffer[pos + 1] == ModMarker[1] &&
                    buffer[pos + 2] == ModMarker[2] &&
                    buffer[pos + 3] == ModMarker[3])
                {
                    int count = BitConverter.ToInt32(buffer, pos + 4);
                    if (count > 0 && count <= 50)
                    {
                        int cur = pos + 8;
                        var candidateMods = new List<string>();
                        bool validTable = true;

                        for (int i = 0; i < count; i++)
                        {
                            if (cur + 10 > buffer.Length) { validTable = false; break; }
                            cur += 8; // skip 8-byte mod id

                            ushort len = BitConverter.ToUInt16(buffer, cur);
                            cur += 2;

                            if (len == 0 || len > 150 || cur + len * 2 > buffer.Length)
                            {
                                validTable = false;
                                break;
                            }

                            string rawName = Encoding.Unicode.GetString(buffer, cur, len * 2);
                            cur += len * 2;

                            // Validate string has valid printable characters and no control chars
                            bool hasPrintable = false;
                            bool hasControl = false;
                            foreach (char c in rawName)
                            {
                                if (char.IsControl(c)) { hasControl = true; break; }
                                if (!char.IsWhiteSpace(c)) hasPrintable = true;
                            }

                            if (hasControl || !hasPrintable)
                            {
                                validTable = false;
                                break;
                            }

                            string cleaned = CleanModString(rawName);
                            if (!string.IsNullOrWhiteSpace(cleaned))
                            {
                                candidateMods.Add(cleaned);
                            }
                        }

                        if (validTable && candidateMods.Count > 0)
                        {
                            foreach (var m in candidateMods)
                            {
                                if (!detectedMods.Contains(m, StringComparer.OrdinalIgnoreCase))
                                {
                                    detectedMods.Add(m);
                                }
                            }
                            // Valid mod table successfully decoded
                            break;
                        }
                    }
                }
            }

            // Fallback scan of UTF-16 header pool (0x1000..0x4000) checking both even & odd byte alignments
            if (detectedMods.Count == 0)
            {
                int scanLimit = Math.Min(buffer.Length - 4, 0x4000);
                for (int startOffset = 0; startOffset < 2; startOffset++)
                {
                    for (int i = 0x1000 + startOffset; i < scanLimit; i += 2)
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
                                    candidate.IndexOf("imperium", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
            }

            meta.ActiveMods = detectedMods;

            // Pick primary mod name: prioritize major total conversion / overhaul mods first
            string primary = "Rome Remastered";
            foreach (var m in detectedMods)
            {
                if (m.IndexOf("surrectum", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("imperium", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("chivalry", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("ris ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("overhaul", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("mundus magnus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("rtr", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    primary = m;
                    break;
                }
            }

            // If no major overhaul matched, pick the first mod that isn't just a UI / camera / graphic tweak
            if (primary == "Rome Remastered")
            {
                foreach (var m in detectedMods)
                {
                    if (m.IndexOf("camera", StringComparison.OrdinalIgnoreCase) < 0 &&
                        m.IndexOf("blood", StringComparison.OrdinalIgnoreCase) < 0 &&
                        m.IndexOf("dark ui", StringComparison.OrdinalIgnoreCase) < 0 &&
                        m.IndexOf("ui", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        primary = m;
                        break;
                    }
                }
            }

            // Fallback to first mod if still unassigned
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

