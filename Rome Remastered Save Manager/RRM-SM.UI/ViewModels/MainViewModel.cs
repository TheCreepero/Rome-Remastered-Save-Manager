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

namespace RRM_SM.UI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigService _configService;
        private readonly AppConfig _config;
        private BackupService _backupService;

        private ObservableCollection<BackupEntry> _allBackups = new();
        private ObservableCollection<BackupEntry> _filteredBackups = new();
        private BackupEntry? _selectedBackup;
        private string _searchFilter = string.Empty;
        private string _statusMessage = "Ready.";
        private bool _isBusy;

        // Path properties
        private string _gameSaveDirectory = string.Empty;
        private string _backupDirectory = string.Empty;
        private bool _compressBackups;
        private int _maxBackupsToKeep;
        private bool _gameSaveDirExists;
        private bool _backupDirExists;
        private int _totalBackupCount;
        private string _totalBackupSize = "0 B";

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

            // Commands
            QuickBackupCommand = new RelayCommand(ExecuteQuickBackup, () => !IsBusy);
            NamedBackupCommand = new RelayCommand(ExecuteNamedBackup, () => !IsBusy);
            RestoreCommand = new RelayCommand(ExecuteRestore, () => !IsBusy && SelectedBackup != null);
            DeleteBackupCommand = new RelayCommand(ExecuteDeleteBackup, () => !IsBusy && SelectedBackup != null);
            RefreshBackupsCommand = new RelayCommand(ExecuteRefreshBackups, () => !IsBusy);
            OpenSelectedInExplorerCommand = new RelayCommand(ExecuteOpenSelectedInExplorer, () => SelectedBackup != null);

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
        }

        // ───────────────────── Properties ─────────────────────

        public ObservableCollection<BackupEntry> FilteredBackups
        {
            get => _filteredBackups;
            set { _filteredBackups = value; OnPropertyChanged(); }
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
        public ICommand RestoreCommand { get; }
        public ICommand DeleteBackupCommand { get; }
        public ICommand RefreshBackupsCommand { get; }
        public ICommand OpenSelectedInExplorerCommand { get; }
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
            IsBusy = true;
            StatusMessage = "Creating quick backup...";
            try
            {
                SyncConfigFromViewModel();
                var entry = await Task.Run(() => _backupService.CreateBackup());
                StatusMessage = $"✔ Backup created: {entry.Name} ({entry.FormattedSize}, {entry.FileCount} files)";
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
            // Prompt for name via a simple input dialog
            string? name = PromptForInput("Named Backup", "Enter a tag / custom name for this backup\n(e.g. 'Julii_Turn42', 'Before_Civil_War'):");
            if (string.IsNullOrWhiteSpace(name))
                return;

            IsBusy = true;
            StatusMessage = $"Creating backup '{name}'...";
            try
            {
                SyncConfigFromViewModel();
                var entry = await Task.Run(() => _backupService.CreateBackup(name));
                StatusMessage = $"✔ Named backup created: {entry.Name} ({entry.FormattedSize})";
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

        private async void ExecuteRestore()
        {
            if (SelectedBackup == null) return;

            var chosen = SelectedBackup;
            var result = MessageBox.Show(
                $"This will overwrite files in your active save directory with:\n\n{chosen.Name}\n\nA safety backup of your current saves will be created first.\n\nAre you sure you want to proceed?",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            StatusMessage = "Creating safety backup & restoring...";
            try
            {
                SyncConfigFromViewModel();
                await Task.Run(() => _backupService.RestoreBackup(chosen, createSafetyBackup: true));
                StatusMessage = $"✔ Restored: {chosen.Name} (safety backup created)";
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
                $"Permanently delete backup:\n\n{chosen.Name}\n({chosen.FormattedSize})\n\nThis cannot be undone.",
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
                var backups = _backupService.GetBackups();
                _allBackups = new ObservableCollection<BackupEntry>(backups);
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

        private void ExecuteSaveSettings()
        {
            SyncConfigFromViewModel();
            _configService.SaveConfig(_config);
            _backupService = new BackupService(_config);
            RefreshPathStatuses();
            ExecuteRefreshBackups();
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
            ExecuteSaveSettings();
            StatusMessage = "✔ Settings reset to defaults.";
        }

        // ───────────────────── Helpers ─────────────────────

        private void SyncConfigFromViewModel()
        {
            _config.GameSaveDirectory = GameSaveDirectory;
            _config.BackupDirectory = BackupDirectory;
            _config.CompressBackups = CompressBackups;
            _config.MaxBackupsToKeep = MaxBackupsToKeep;
        }

        private void RefreshPathStatuses()
        {
            GameSaveDirExists = Directory.Exists(GameSaveDirectory);
            BackupDirExists = Directory.Exists(BackupDirectory);
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                FilteredBackups = new ObservableCollection<BackupEntry>(_allBackups);
            }
            else
            {
                var filtered = _allBackups
                    .Where(b => b.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                FilteredBackups = new ObservableCollection<BackupEntry>(filtered);
            }
        }

        private static void OpenPathInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    // Select the file in Explorer
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

        /// <summary>
        /// Simple input prompt using a MessageBox-style approach.
        /// The actual input dialog is implemented in code-behind via an event.
        /// </summary>
        public Func<string, string, string?>? InputDialogRequested { get; set; }

        private string? PromptForInput(string title, string message)
        {
            return InputDialogRequested?.Invoke(title, message);
        }

        /// <summary>
        /// Folder browser callback. Set from code-behind.
        /// </summary>
        public Func<string, string, string?>? FolderBrowserRequested { get; set; }

        private string? BrowseForFolder(string title, string initialDir)
        {
            return FolderBrowserRequested?.Invoke(title, initialDir);
        }

        // ───────────────────── INotifyPropertyChanged ─────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

