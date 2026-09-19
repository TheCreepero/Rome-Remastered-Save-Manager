using System;
using System.Collections.Generic;

namespace RRM_SM.Models
{
    public enum SaveSourceType
    {
        Manual,
        Sentinel,
        SafetyBackup
    }

    public class CampaignMetadata
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Faction { get; set; } = "General";
        public string DisplayName { get; set; } = "General";
        public bool IsCustomNamed { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastPlayedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// The authoritative internal Campaign GUID extracted from the save file headers (bytes 36..51).
        /// </summary>
        public string? GameCampaignId { get; set; }
    }

    public class VaultSaveItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string? CampaignId { get; set; }
        public string? Faction { get; set; }

        /// <summary>
        /// Authoritative 16-byte internal Campaign GUID from the save file header.
        /// </summary>
        public string? GameCampaignId { get; set; }
        
        /// <summary>
        /// Actual file name residing flat in the backup vault directory (e.g. save_Quicksave_2026-09-19_00-15-00.sav)
        /// </summary>
        public string StoredFileName { get; set; } = string.Empty;

        /// <summary>
        /// Original name expected by the game engine (e.g. save_Quicksave.sav)
        /// </summary>
        public string OriginalGameFileName { get; set; } = string.Empty;

        public string CampaignName { get; set; } = "General";
        public int? Turn { get; set; }
        public SaveFileType SaveType { get; set; } = SaveFileType.Unknown;
        public SaveSourceType Source { get; set; } = SaveSourceType.Manual;
        public long FileSizeBytes { get; set; }
        public string Sha256Hash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;

        public string? CustomTitle { get; set; }
        public string? Notes { get; set; }
        public List<string> Tags { get; set; } = new();
        public bool IsPinned { get; set; }

        public string? InGameDate { get; set; }
        public string? ModName { get; set; }
        public int? CalendarYear { get; set; }

        // Helper getters
        public string DisplayName => !string.IsNullOrWhiteSpace(CustomTitle) ? CustomTitle : StoredFileName;
        public bool IsSentinel => Source == SaveSourceType.Sentinel;
        public bool IsSafety => Source == SaveSourceType.SafetyBackup;
        public bool IsManual => Source == SaveSourceType.Manual;

        public string FormattedSize
        {
            get
            {
                if (FileSizeBytes >= 1024 * 1024 * 1024)
                    return $"{FileSizeBytes / (1024.0 * 1024 * 1024):F2} GB";
                if (FileSizeBytes >= 1024 * 1024)
                    return $"{FileSizeBytes / (1024.0 * 1024):F2} MB";
                if (FileSizeBytes >= 1024)
                    return $"{FileSizeBytes / 1024.0:F2} KB";
                return $"{FileSizeBytes} B";
            }
        }
    }

    public class SaveVaultManifest
    {
        public int Version { get; set; } = 2;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public List<VaultSaveItem> Saves { get; set; } = new();
        public Dictionary<string, CampaignMetadata> Campaigns { get; set; } = new();
    }
}

