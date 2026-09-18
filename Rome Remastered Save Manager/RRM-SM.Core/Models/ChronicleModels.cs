using System;
using System.Collections.Generic;
using RRM_SM.Models;

namespace RRM_SM.Core.Models
{
    public class ChronicleMilestone
    {
        public int Turn { get; set; }
        public DateTime Timestamp { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PlayerNotes { get; set; } = string.Empty;
        public SaveFileType SaveType { get; set; }
        public long SaveSizeBytes { get; set; }
        public string SaveFileName { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        
        // This makes sure we can uniquely identify this milestone when updating notes.
        public string Id => $"{Turn}_{SaveFileName}";
    }

    public class CampaignChronicle
    {
        public string? CampaignId { get; set; }
        public string CampaignName { get; set; } = string.Empty;
        public string ModName { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime LastPlayedAt { get; set; }
        public int MaxTurn { get; set; }
        public string CampaignSummary { get; set; } = string.Empty;
        public List<ChronicleMilestone> Milestones { get; set; } = new List<ChronicleMilestone>();
    }
}
