using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RRM_SM.Models;
using RRM_SM.Services;
using RRM_SM.UI.Services;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using Application = System.Windows.Application;

namespace RRM_SM.UI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigService _configService;
        private readonly AppConfig _config;
        private BackupService _backupService;

        private ObservableCollection<BackupEntry> _allBackups = new();
        private ObservableCollection<BackupEntry> _filteredBackups = new();
        private ObservableCollection<string> _availableCampaignFilters = new() { "All Campaigns" };
        private string _selectedCampaignFilter = "All Campaigns";

        private ObservableCollection<string> _activeCampaigns = new();
        private string? _selectedActiveCampaign;

        private BackupEntry? _selectedBackup;
        private string _searchFilter = string.Empty;
        private string _statusMessage = "Ready.";
        private bool _isBusy;

        private readonly SaveWatcherService _watcherService;
        private readonly TrayService _trayService;

        // Automation & Desktop properties
        private bool _autoWatcherEnabled;
        private bool _showNotifications;
        private bool _minimizeToTray;
        private int _autoWatcherDebounceMs;

        // Path properties
        private string _gameSaveDirectory = string.Empty;
        private string _backupDirectory = string.Empty;
        private bool _compressBackups;
        private int _maxBackupsToKeep;
        private bool _gameSaveDirExists;
        private bool _backupDirExists;
        private int _totalBackupCount;
        private string _totalBackupSize = "0 B";

        public Action? RestoreWindowRequested { get; set; }
        public Action? ExitApplicationRequested { get; set; }
        public TrayService TrayService => _trayService;

        public MainViewModel()
        {
            _configService = new ConfigService();
            _config = _configService.LoadOrCreateConfig();
            _backupService = new BackupService(_config);

            // Initialize from config
            _gameSaveDirectory = _config.GameSaveDirectory;
            _backupDirectory = _config.BackupDirectory;
            _compressBackups = _config.CompressBackups;
            _maxBackupsToKeep = _config.MaxBackupsToKeep;
            _autoWatcherEnabled = _config.AutoWatcherEnabled;
            _showNotifications = _config.ShowNotifications;
            _minimizeToTray = _config.MinimizeToTray;
            _autoWatcherDebounceMs = _config.AutoWatcherDebounceMs > 0 ? _config.AutoWatcherDebounceMs : 1500;

            // Tray Service
            _trayService = new TrayService { ShowNotifications = _showNotifications };
            _trayService.OpenRequested += () => RestoreWindowRequested?.Invoke();
            _trayService.LaunchGameRequested += ExecuteLaunchGame;
            _trayService.ToggleWatcherRequested += ExecuteToggleWatcher;
            _trayService.QuickBackupRequested += ExecuteQuickBackup;
            _trayService.ExitRequested += () => ExitApplicationRequested?.Invoke();
            _trayService.Initialize();

            // Watcher Service
            _watcherService = new SaveWatcherService(_config, _backupService);
            _watcherService.BackupCreated += (s, e) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ExecuteRefreshBackups();
                    StatusMessage = $"🛡 Autosave Sentinel: Backed up '{e.CampaignName}'.";
                });

                _trayService.ShowNotification(
                    "Autosave Sentinel",
                    $"Created snapshot for {e.CampaignName} ({Path.GetFileName(e.ChangedFile)})",
                    System.Windows.Forms.ToolTipIcon.Info);
            };

            _watcherService.StatusChanged += (s, msg) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = msg;
                    OnPropertyChanged(nameof(IsWatcherRunning));
                    OnPropertyChanged(nameof(WatcherStatusText));
                    _trayService.UpdateWatcherState(IsWatcherRunning);
                });
            };

            // Commands
            QuickBackupCommand = new RelayCommand(ExecuteQuickBackup, () => !IsBusy);
            NamedBackupCommand = new RelayCommand(ExecuteNamedBackup, () => !IsBusy);
            BackupAllCampaignsCommand = new RelayCommand(ExecuteBackupAllCampaigns, () => !IsBusy);
            RestoreCommand = new RelayCommand(ExecuteRestore, () => !IsBusy && SelectedBackup != null);
            DeleteBackupCommand = new RelayCommand(ExecuteDeleteBackup, () => !IsBusy && SelectedBackup != null);
            RefreshBackupsCommand = new RelayCommand(ExecuteRefreshBackups, () => !IsBusy);
            OpenSelectedInExplorerCommand = new RelayCommand(ExecuteOpenSelectedInExplorer, () => SelectedBackup != null);
            CopyBackupPathCommand = new RelayCommand(ExecuteCopyBackupPath, () => SelectedBackup != null);

            ToggleWatcherCommand = new RelayCommand(ExecuteToggleWatcher);
            LaunchGameCommand = new RelayCommand(ExecuteLaunchGame);

            BrowseSaveDirCommand = new RelayCommand(ExecuteBrowseSaveDir);
            BrowseBackupDirCommand = new RelayCommand(ExecuteBrowseBackupDir);
            AutoDetectCommand = new RelayCommand(ExecuteAutoDetect);
            OpenSaveDirCommand = new RelayCommand(ExecuteOpenSaveDir);
            OpenBackupDirCommand = new RelayCommand(ExecuteOpenBackupDir);
            OpenConfigCommand = new RelayCommand(ExecuteOpenConfig);
            SaveSettingsCommand = new RelayCommand(ExecuteSaveSettings);
            ResetDefaultsCommand = new RelayCommand(ExecuteResetDefaults);

            // Initial load
            RefreshPathStatuses();
            ExecuteRefreshBackups();

            if (_autoWatcherEnabled && GameSaveDirExists)
            {
                _watcherService.Start();
            }
            _trayService.UpdateWatcherState(_watcherService.IsRunning);
        }

        // ───────────────────── Properties ─────────────────────

        public ObservableCollection<BackupEntry> FilteredBackups
        {
            get => _filteredBackups;
            set { _filteredBackups = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoBackups)); }
        }

        public bool HasNoBackups => FilteredBackups.Count == 0;

        public ObservableCollection<string> AvailableCampaignFilters
        {
            get => _availableCampaignFilters;
            set { _availableCampaignFilters = value; OnPropertyChanged(); }
        }

        public string SelectedCampaignFilter
        {
            get => _selectedCampaignFilter;
            set
            {
                if (_selectedCampaignFilter != value)
                {
                    _selectedCampaignFilter = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<string> ActiveCampaigns
        {
            get => _activeCampaigns;
            set { _activeCampaigns = value; OnPropertyChanged(); }
        }

        public string? SelectedActiveCampaign
        {
            get => _selectedActiveCampaign;
            set { _selectedActiveCampaign = value; OnPropertyChanged(); }
        }

        public BackupEntry? SelectedBackup
        {
            get => _selectedBackup;
            set { _selectedBackup = value; OnPropertyChanged(); }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                _searchFilter = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public string GameSaveDirectory
        {
            get => _gameSaveDirectory;
            set { _gameSaveDirectory = value; OnPropertyChanged(); RefreshPathStatuses(); }
        }

        public string BackupDirectory
        {
            get => _backupDirectory;
            set { _backupDirectory = value; OnPropertyChanged(); RefreshPathStatuses(); }
        }

        public bool CompressBackups
        {
            get => _compressBackups;
            set { _compressBackups = value; OnPropertyChanged(); }
        }

        public int MaxBackupsToKeep
        {
            get => _maxBackupsToKeep;
            set { _maxBackupsToKeep = value; OnPropertyChanged(); }
        }

        public bool AutoWatcherEnabled
        {
            get => _autoWatcherEnabled;
            set
            {
                if (_autoWatcherEnabled != value)
                {
                    _autoWatcherEnabled = value;
                    OnPropertyChanged();
                    UpdateWatcherService();
                }
            }
        }

        public bool ShowNotifications
        {
            get => _showNotifications;
            set
            {
                if (_showNotifications != value)
                {
                    _showNotifications = value;
                    OnPropertyChanged();
                    _trayService.ShowNotifications = value;
                }
            }
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set
            {
                if (_minimizeToTray != value)
                {
                    _minimizeToTray = value;
                    OnPropertyChanged();
                }
            }
        }

        public int AutoWatcherDebounceMs
        {
            get => _autoWatcherDebounceMs;
            set
            {
                if (_autoWatcherDebounceMs != value)
                {
                    _autoWatcherDebounceMs = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsWatcherRunning => _watcherService.IsRunning;

        public string WatcherStatusText => IsWatcherRunning ? "Sentinel: Active" : "Sentinel: Off";

        public bool GameSaveDirExists
        {
            get => _gameSaveDirExists;
            set { _gameSaveDirExists = value; OnPropertyChanged(); }
        }

        public bool BackupDirExists
        {
            get => _backupDirExists;
            set { _backupDirExists = value; OnPropertyChanged(); }
        }

        public int TotalBackupCount
        {
            get => _totalBackupCount;
            set { _totalBackupCount = value; OnPropertyChanged(); }
        }

        public string TotalBackupSize
        {
            get => _totalBackupSize;
            set { _totalBackupSize = value; OnPropertyChanged(); }
        }

        // ───────────────────── Commands ─────────────────────

        public ICommand QuickBackupCommand { get; }
        public ICommand NamedBackupCommand { get; }
        public ICommand BackupAllCampaignsCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand DeleteBackupCommand { get; }
        public ICommand RefreshBackupsCommand { get; }
        public ICommand OpenSelectedInExplorerCommand { get; }
        public ICommand CopyBackupPathCommand { get; }
        public ICommand ToggleWatcherCommand { get; }
        public ICommand LaunchGameCommand { get; }
        public ICommand BrowseSaveDirCommand { get; }
        public ICommand BrowseBackupDirCommand { get; }
        public ICommand AutoDetectCommand { get; }
        public ICommand OpenSaveDirCommand { get; }
        public ICommand OpenBackupDirCommand { get; }
        public ICommand OpenConfigCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetDefaultsCommand { get; }

        // ───────────────────── Backup Operations ─────────────────────

        private async void ExecuteQuickBackup()
        {
            string targetCampaign = !string.IsNullOrWhiteSpace(SelectedActiveCampaign)
                ? SelectedActiveCampaign
                : _backupService.GetMostRecentCampaign();

            IsBusy = true;
            StatusMessage = $"Creating quick backup for [{targetCampaign}]...";
            try
            {
                SyncConfigFromViewModel();
                var entry = await Task.Run(() => _backupService.CreateCampaignBackup(targetCampaign));
                StatusMessage = $"✔ Backup created: {entry.CampaignName}/{entry.Name} ({entry.FormattedSize}, {entry.FileCount} files)";
                ExecuteRefreshBackups();
            }
            catch (Exception ex)
            {
                StatusMessage = $"✖ Backup failed: {ex.Message}";
                MessageBox.Show($"Backup failed:\n{ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteNamedBackup()
        {
            string targetCampaign = !string.IsNullOrWhiteSpace(SelectedActiveCampaign)
                ? SelectedActiveCampaign
                : _backupService.GetMostRecentCampaign();

            string? name = PromptForInput("Named Backup", $"Enter a tag / custom name for [{targetCampaign}] backup\n(e.g. 'Turn42_Siege', 'Before_Civil_War'):");
            if (string.IsNullOrWhiteSpace(name))
                return;

            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                MessageBox.Show("The entered name contains only invalid characters for file names. Please enter a valid name.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            name = sanitized;

            IsBusy = true;
            StatusMessage = $"Creating backup '{name}' for [{targetCampaign}]...";
            try
            {
                SyncConfigFromViewModel();
                var entry = await Task.Run(() => _backupService.CreateCampaignBackup(targetCampaign, name));
                StatusMessage = $"✔ Named backup created: {entry.CampaignName}/{entry.Name} ({entry.FormattedSize})";
                ExecuteRefreshBackups();
            }
            catch (Exception ex)
            {
                StatusMessage = $"✖ Backup failed: {ex.Message}";
                MessageBox.Show($"Backup failed:\n{ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteBackupAllCampaigns()
        {
            IsBusy = true;
            StatusMessage = "Backing up all detected campaigns...";
            try
            {
                SyncConfigFromViewModel();
                var entries = await Task.Run(() => _backupService.CreateAllCampaignsBackup());
                StatusMessage = $"✔ Backed up {entries.Count} campaign(s) successfully.";
                ExecuteRefreshBackups();
            }
            catch (Exception ex)
            {
                StatusMessage = $"✖ Backup all failed: {ex.Message}";
                MessageBox.Show($"Backup all failed:\n{ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteRestore()
        {
            if (SelectedBackup == null) return;

            var chosen = SelectedBackup;
            var result = MessageBox.Show(
                $"This will restore files for campaign [{chosen.CampaignName}] into your active save directory:\n\n{chosen.Name}\n\nA safety backup of your current saves will be created first.\n\nAre you sure you want to proceed?",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            StatusMessage = $"Creating safety backup & restoring [{chosen.CampaignName}]...";
            try
            {
                SyncConfigFromViewModel();
                await Task.Run(() => _backupService.RestoreBackup(chosen, createSafetyBackup: true));
                StatusMessage = $"✔ Restored: {chosen.CampaignName}/{chosen.Name} (safety backup created)";
                ExecuteRefreshBackups();
            }
            catch (Exception ex)
            {
                StatusMessage = $"✖ Restore failed: {ex.Message}";
                MessageBox.Show($"Restore failed:\n{ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteDeleteBackup()
        {
            if (SelectedBackup == null) return;

            var chosen = SelectedBackup;
            var result = MessageBox.Show(
                $"Permanently delete backup:\n\n{chosen.CampaignName} / {chosen.Name}\n({chosen.FormattedSize})\n\nThis cannot be undone.",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            StatusMessage = $"Deleting {chosen.Name}...";
            try
            {
                await Task.Run(() => _backupService.DeleteBackup(chosen));
                StatusMessage = $"✔ Deleted: {chosen.Name}";
                ExecuteRefreshBackups();
            }
            catch (Exception ex)
            {
                StatusMessage = $"✖ Delete failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ExecuteRefreshBackups()
        {
            try
            {
                SyncConfigFromViewModel();

                // 1. Refresh active campaigns detected in the game save folder
                var activeDict = _backupService.GetActiveCampaigns();
                var activeList = activeDict.Keys.OrderBy(k => k).ToList();
                ActiveCampaigns = new ObservableCollection<string>(activeList);

                string mostRecent = _backupService.GetMostRecentCampaign();
                if (SelectedActiveCampaign == null || !activeList.Contains(SelectedActiveCampaign))
                {
                    SelectedActiveCampaign = activeList.Contains(mostRecent) ? mostRecent : activeList.FirstOrDefault();
                }

                // 2. Refresh backup entries
                var backups = _backupService.GetBackups();
                _allBackups = new ObservableCollection<BackupEntry>(backups);

                // 3. Update campaign filters list
                var distinctCampaigns = backups.Select(b => b.CampaignName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
                var filterList = new ObservableCollection<string> { "All Campaigns" };
                foreach (var c in distinctCampaigns)
                {
                    filterList.Add(c);
                }
                AvailableCampaignFilters = filterList;

                if (!filterList.Contains(SelectedCampaignFilter))
                {
                    _selectedCampaignFilter = "All Campaigns";
                    OnPropertyChanged(nameof(SelectedCampaignFilter));
                }

                ApplyFilter();
                TotalBackupCount = backups.Count;
                long total = backups.Sum(b => b.TotalSizeBytes);
                TotalBackupSize = FormatBytes(total);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load backups: {ex.Message}";
            }
        }

        private void ExecuteOpenSelectedInExplorer()
        {
            if (SelectedBackup == null) return;
            OpenPathInExplorer(SelectedBackup.FullPath);
        }

        private void ExecuteCopyBackupPath()
        {
            if (SelectedBackup == null) return;
            try
            {
                Clipboard.SetText(SelectedBackup.FullPath);
                StatusMessage = $"✔ Copied to clipboard: {SelectedBackup.FullPath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to copy path: {ex.Message}";
            }
        }

        // ───────────────────── Settings / Path Operations ─────────────────────

        private void ExecuteBrowseSaveDir()
        {
            string? path = BrowseForFolder("Select Game Save Directory", GameSaveDirectory);
            if (!string.IsNullOrEmpty(path))
            {
                GameSaveDirectory = path;
            }
        }

        private void ExecuteBrowseBackupDir()
        {
            string? path = BrowseForFolder("Select Backup Directory", BackupDirectory);
            if (!string.IsNullOrEmpty(path))
            {
                BackupDirectory = path;
            }
        }

        private void ExecuteAutoDetect()
        {
            string? detected = ConfigService.AutoDetectRomeSaveDirectory();
            if (!string.IsNullOrWhiteSpace(detected))
            {
                GameSaveDirectory = detected;
                StatusMessage = $"✔ Auto-detected: {detected}";
            }
            else
            {
                StatusMessage = "Could not auto-detect Rome Remastered save directory.";
                MessageBox.Show("Could not auto-detect Rome Remastered save directory on this system.\n\nPlease use Browse to manually select the save folder.", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExecuteOpenSaveDir()
        {
            OpenPathInExplorer(GameSaveDirectory);
        }

        private void ExecuteOpenBackupDir()
        {
            if (!Directory.Exists(BackupDirectory))
            {
                try { Directory.CreateDirectory(BackupDirectory); } catch { }
            }
            OpenPathInExplorer(BackupDirectory);
        }

        private void ExecuteOpenConfig()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _configService.ConfigFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open config file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ExecuteToggleWatcher()
        {
            AutoWatcherEnabled = !AutoWatcherEnabled;
            ExecuteSaveSettings();
        }

        public void ExecuteLaunchGame()
        {
            try
            {
                Process.Start(new ProcessStartInfo("steam://run/885970") { UseShellExecute = true });
                StatusMessage = "⚔ Rome Remastered launch signal sent to Steam.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Steam launch failed: {ex.Message}";
                MessageBox.Show($"Could not trigger Steam launch:\n{ex.Message}\n\nPlease ensure Steam is installed and running.", "Steam Launch", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateWatcherService()
        {
            if (_autoWatcherEnabled && GameSaveDirExists)
            {
                if (!_watcherService.IsRunning)
                {
                    _watcherService.Start();
                }
            }
            else
            {
                if (_watcherService.IsRunning)
                {
                    _watcherService.Stop();
                }
            }

            OnPropertyChanged(nameof(IsWatcherRunning));
            OnPropertyChanged(nameof(WatcherStatusText));
            _trayService.UpdateWatcherState(IsWatcherRunning);
        }

        private void ExecuteSaveSettings()
        {
            SyncConfigFromViewModel();
            _configService.SaveConfig(_config);
            _backupService = new BackupService(_config);
            RefreshPathStatuses();
            ExecuteRefreshBackups();
            UpdateWatcherService();
            StatusMessage = "✔ Settings saved.";
        }

        private void ExecuteResetDefaults()
        {
            var result = MessageBox.Show("Reset all settings to defaults?\n\nThis will re-detect game save paths and reset backup preferences.", "Reset Defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            string? detected = ConfigService.AutoDetectRomeSaveDirectory();
            GameSaveDirectory = detected ?? string.Empty;
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            BackupDirectory = Path.Combine(docs, "Rome Remastered Backups");
            CompressBackups = false;
            MaxBackupsToKeep = 0;
            AutoWatcherEnabled = false;
            ShowNotifications = true;
            MinimizeToTray = true;
            AutoWatcherDebounceMs = 1500;
            ExecuteSaveSettings();
            StatusMessage = "✔ Settings reset to defaults.";
        }

        public void Cleanup()
        {
            _watcherService.Dispose();
            _trayService.Dispose();
        }

        // ───────────────────── Helpers ─────────────────────

        private void SyncConfigFromViewModel()
        {
            _config.GameSaveDirectory = GameSaveDirectory;
            _config.BackupDirectory = BackupDirectory;
            _config.CompressBackups = CompressBackups;
            _config.MaxBackupsToKeep = MaxBackupsToKeep;
            _config.AutoWatcherEnabled = AutoWatcherEnabled;
            _config.ShowNotifications = ShowNotifications;
            _config.MinimizeToTray = MinimizeToTray;
            _config.AutoWatcherDebounceMs = AutoWatcherDebounceMs;
        }

        private void RefreshPathStatuses()
        {
            GameSaveDirExists = Directory.Exists(GameSaveDirectory);
            BackupDirExists = Directory.Exists(BackupDirectory);
        }

        private void ApplyFilter()
        {
            var query = _allBackups.AsEnumerable();

            // 1. Campaign Filter
            if (!string.IsNullOrWhiteSpace(_selectedCampaignFilter) &&
                !_selectedCampaignFilter.Equals("All Campaigns", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(b => b.CampaignName.Equals(_selectedCampaignFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Search Text
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                query = query.Where(b =>
                    b.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    b.CampaignName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));
            }

            FilteredBackups = new ObservableCollection<BackupEntry>(query.ToList());
        }

        private static void OpenPathInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
                else if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        public Func<string, string, string?>? InputDialogRequested { get; set; }

        private string? PromptForInput(string title, string message)
        {
            return InputDialogRequested?.Invoke(title, message);
        }

        public Func<string, string, string?>? FolderBrowserRequested { get; set; }

        private string? BrowseForFolder(string title, string initialDir)
        {
            return FolderBrowserRequested?.Invoke(title, initialDir);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
