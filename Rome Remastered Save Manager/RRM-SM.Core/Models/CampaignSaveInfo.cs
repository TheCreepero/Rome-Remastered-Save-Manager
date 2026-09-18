using System;

namespace RRM_SM.Models
{
    public enum SaveFileType
    {
        Autosave,
        Manual,
        Quicksave,
        Battle,
        Unknown
    }

    public class CampaignSaveInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FactionName { get; set; } = string.Empty;
        public int? Turn { get; set; }
        public SaveFileType Type { get; set; } = SaveFileType.Unknown;
        public DateTime LastModified { get; set; }
        public long FileSizeBytes { get; set; }
    }
}
