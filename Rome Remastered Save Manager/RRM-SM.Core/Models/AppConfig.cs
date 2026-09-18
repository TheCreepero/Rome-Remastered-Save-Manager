namespace RRM_SM.Models
{
    public class AppConfig
    {
        public string GameSaveDirectory { get; set; } = string.Empty;
        public string BackupDirectory { get; set; } = string.Empty;
        public bool CompressBackups { get; set; } = false;
        public int MaxBackupsToKeep { get; set; } = 0; // 0 = unlimited

        // Automation & Desktop Integration
        public bool AutoWatcherEnabled { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public int AutoWatcherDebounceMs { get; set; } = 1500;
    }
}

