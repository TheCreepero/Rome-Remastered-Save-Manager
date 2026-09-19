using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Timers;
using RRM_SM.Models;

namespace RRM_SM.Services
{
    public class BackupCreatedEventArgs : EventArgs
    {
        public BackupEntry Backup { get; }
        public string ChangedFile { get; }
        public string CampaignName { get; }

        public BackupCreatedEventArgs(BackupEntry backup, string changedFile, string campaignName)
        {
            Backup = backup;
            ChangedFile = changedFile;
            CampaignName = campaignName;
        }
    }

    public class SaveWatcherService : IDisposable
    {
        private readonly AppConfig _config;
        private readonly BackupService _backupService;
        private FileSystemWatcher? _watcher;
        private System.Timers.Timer? _debounceTimer;
        private readonly HashSet<string> _pendingFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _isDisposed;

        public event EventHandler<BackupCreatedEventArgs>? BackupCreated;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<Exception>? WatcherError;

        public bool IsRunning => _watcher != null && _watcher.EnableRaisingEvents;

        public SaveWatcherService(AppConfig config, BackupService backupService)
        {
            _config = config;
            _backupService = backupService;
        }

        public void Start()
        {
            if (_isDisposed) return;

            Stop();

            if (string.IsNullOrWhiteSpace(_config.GameSaveDirectory) || !Directory.Exists(_config.GameSaveDirectory))
            {
                StatusChanged?.Invoke(this, "Sentinel stopped: Game save directory not found.");
                return;
            }

            try
            {
                _debounceTimer = new System.Timers.Timer
                {
                    AutoReset = false,
                    Interval = _config.AutoWatcherDebounceMs > 0 ? _config.AutoWatcherDebounceMs : 1500
                };
                _debounceTimer.Elapsed += OnDebounceTimerElapsed;

                _watcher = new FileSystemWatcher(_config.GameSaveDirectory)
                {
                    Filter = "*.sav",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnFileEvent;
                _watcher.Changed += OnFileEvent;
                _watcher.Renamed += OnRenamedEvent;
                _watcher.Error += OnWatcherError;

                StatusChanged?.Invoke(this, "Autosave Sentinel active: Watching for save file changes.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Sentinel error starting watcher: {ex.Message}");
                WatcherError?.Invoke(this, ex);
            }
        }

        public void Stop()
        {
            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Elapsed -= OnDebounceTimerElapsed;
                _debounceTimer.Dispose();
                _debounceTimer = null;
            }

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileEvent;
                _watcher.Changed -= OnFileEvent;
                _watcher.Renamed -= OnRenamedEvent;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }

            lock (_lock)
            {
                _pendingFiles.Clear();
            }

            StatusChanged?.Invoke(this, "Autosave Sentinel stopped.");
        }

        public void Restart()
        {
            Stop();
            Start();
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            if (!e.FullPath.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) return;

            lock (_lock)
            {
                _pendingFiles.Add(e.FullPath);
            }

            ResetTimer();
        }

        private void OnRenamedEvent(object sender, RenamedEventArgs e)
        {
            if (!e.FullPath.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) return;

            lock (_lock)
            {
                _pendingFiles.Add(e.FullPath);
            }

            ResetTimer();
        }

        private void ResetTimer()
        {
            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Interval = _config.AutoWatcherDebounceMs > 0 ? _config.AutoWatcherDebounceMs : 1500;
                _debounceTimer.Start();
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            Exception ex = e.GetException();
            StatusChanged?.Invoke(this, $"Sentinel watcher error: {ex.Message}");
            WatcherError?.Invoke(this, ex);
        }

        private void OnDebounceTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            List<string> filesToProcess;
            lock (_lock)
            {
                filesToProcess = _pendingFiles.ToList();
                _pendingFiles.Clear();
            }

            if (filesToProcess.Count == 0) return;

            try
            {
                // Group changed files by target campaign
                var campaignFilesMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                string latestFile = string.Empty;

                foreach (var file in filesToProcess)
                {
                    if (!File.Exists(file)) continue;

                    // Ensure Rome Remastered has finished writing
                    if (!WaitForFileAvailable(file)) continue;

                    latestFile = file;
                    var saveInfo = _backupService.ParserService.ParseSaveFile(file);
                    string campaign = saveInfo.FactionName;

                    // Match ground-truth campaign from vault if GameCampaignId is available
                    if (!string.IsNullOrWhiteSpace(saveInfo.GameCampaignId))
                    {
                        var matchingCamp = _backupService.VaultService.GetCampaignMetadataByGameCampaignId(saveInfo.GameCampaignId);
                        if (matchingCamp != null && !string.IsNullOrWhiteSpace(matchingCamp.DisplayName))
                        {
                            campaign = matchingCamp.DisplayName;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(campaign) || campaign.Equals("General", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check if quicksave or fallback to most recent campaign
                        campaign = _backupService.GetMostRecentCampaign();
                    }

                    if (!string.IsNullOrWhiteSpace(campaign))
                    {
                        if (!campaignFilesMap.TryGetValue(campaign, out var list))
                        {
                            list = new List<string>();
                            campaignFilesMap[campaign] = list;
                        }
                        list.Add(file);
                    }
                }

                if (campaignFilesMap.Count == 0 && filesToProcess.Count > 0)
                {
                    string fallbackCampaign = _backupService.GetMostRecentCampaign();
                    if (!string.IsNullOrWhiteSpace(fallbackCampaign))
                    {
                        campaignFilesMap[fallbackCampaign] = filesToProcess.Where(File.Exists).ToList();
                    }
                }

                foreach (var kvp in campaignFilesMap)
                {
                    string campaign = kvp.Key;
                    var changedFiles = kvp.Value;
                    var backup = _backupService.CreateSentinelBackup(campaign, changedFiles);
                    StatusChanged?.Invoke(this, $"Sentinel: Automatic snapshot created for '{campaign}' ({backup.FileCount} file{(backup.FileCount == 1 ? "" : "s")}, {backup.FormattedSize}).");
                    BackupCreated?.Invoke(this, new BackupCreatedEventArgs(backup, latestFile, campaign));
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Sentinel snapshot failed: {ex.Message}");
                WatcherError?.Invoke(this, ex);
            }
        }

        public static bool WaitForFileAvailable(string filePath, int maxRetries = 6, int delayMs = 500)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (!File.Exists(filePath)) return false;

                    using var stream = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);

                    if (stream.Length > 0)
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // Locked by game, wait
                }
                catch (UnauthorizedAccessException)
                {
                    // Not yet accessible
                }

                Thread.Sleep(delayMs);
            }

            return File.Exists(filePath);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}

