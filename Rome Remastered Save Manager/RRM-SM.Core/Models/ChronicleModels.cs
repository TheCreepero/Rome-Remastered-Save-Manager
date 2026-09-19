using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RRM_SM.Models;

namespace RRM_SM.Core.Models
{
    public class ChronicleMilestone : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _playerNotes = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public int Turn { get; set; }
        public DateTime Timestamp { get; set; }
        public string Title 
        { 
            get => _title; 
            set { if (_title != value) { _title = value; OnPropertyChanged(); } } 
        }
        public string PlayerNotes 
        { 
            get => _playerNotes; 
            set { if (_playerNotes != value) { _playerNotes = value; OnPropertyChanged(); } } 
        }
        public SaveFileType SaveType { get; set; }
        public long SaveSizeBytes { get; set; }
        public string SaveFileName { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        
        // In-game calendar and mod metadata
        public string? InGameDate { get; set; }
        public int? CalendarYear { get; set; }
        public string? Season { get; set; }
        public string? ModName { get; set; }
        public int? DeltaYears { get; set; }
        public int? DeltaTurns { get; set; }

        // This makes sure we can uniquely identify this milestone when updating notes.
        public string Id => $"{Turn}_{SaveFileName}";
    }

    public class CampaignChronicle
    {
        public string? CampaignId { get; set; }
        public string CampaignName { get; set; } = string.Empty;
        public string ModName { get; set; } = string.Empty;
        public List<string> ActiveMods { get; set; } = new List<string>();
        public string? StartYear { get; set; }
        public string? EndYear { get; set; }
        public int? TotalYearsSpan { get; set; }
        public string? EraSummary { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime LastPlayedAt { get; set; }
        public int MaxTurn { get; set; }
        public string CampaignSummary { get; set; } = string.Empty;
        public List<ChronicleMilestone> Milestones { get; set; } = new List<ChronicleMilestone>();
    }
}
