using System;

namespace RRM_SM.Models
{
    public enum BackupType
    {
        Directory,
        ZipArchive,
        SaveFile
    }

    public class BackupEntry
    {
        public string? VaultId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string OriginalGameFileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long TotalSizeBytes { get; set; }
        public int FileCount { get; set; }
        public BackupType Type { get; set; }
        public bool IsSafetyBackup { get; set; }
        public bool IsSentinelBackup => Source == SaveSourceType.Sentinel || Name.IndexOf("Sentinel", StringComparison.OrdinalIgnoreCase) >= 0;
        public bool IsPinned { get; set; }
        public int? Turn { get; set; }
        public string? Notes { get; set; }
        public List<string> Tags { get; set; } = new();
        public string TagsDisplay => Tags != null && Tags.Count > 0 ? string.Join(", ", Tags) : string.Empty;
        public SaveSourceType Source { get; set; } = SaveSourceType.Manual;
        public string? CampaignId { get; set; }
        public string CampaignName { get; set; } = "General";
        public string? Faction { get; set; }
        public string? InGameDate { get; set; }
        public int? CalendarYear { get; set; }
        public string? ModName { get; set; }

        public string FormattedSize
        {
            get
            {
                if (TotalSizeBytes >= 1024 * 1024 * 1024)
                    return $"{TotalSizeBytes / (1024.0 * 1024 * 1024):F2} GB";
                if (TotalSizeBytes >= 1024 * 1024)
                    return $"{TotalSizeBytes / (1024.0 * 1024):F2} MB";
                if (TotalSizeBytes >= 1024)
                    return $"{TotalSizeBytes / 1024.0:F2} KB";
                return $"{TotalSizeBytes} B";
            }
        }

        public string TypeLabel
        {
            get
            {
                if (Type == BackupType.ZipArchive) return ".zip";
                if (Type == BackupType.SaveFile) return ".sav";
                return "Folder";
            }
        }
    }
}

