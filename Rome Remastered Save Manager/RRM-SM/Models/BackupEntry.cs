using System;

namespace RRM_SM.Models
{
    public enum BackupType
    {
        Directory,
        ZipArchive
    }

    public class BackupEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public long TotalSizeBytes { get; set; }
        public int FileCount { get; set; }
        public BackupType Type { get; set; }
        public bool IsSafetyBackup { get; set; }

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
    }
}

