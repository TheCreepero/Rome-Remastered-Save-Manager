using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RRM_SM.Models;
using RRM_SM.Services;
using Xunit;

namespace RRM_SM.Tests
{
    public class SaveWatcherTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _gameSaveDir;
        private readonly string _backupDir;

        public SaveWatcherTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "RRM_SM_WatcherTests_" + Guid.NewGuid().ToString("N"));
            _gameSaveDir = Path.Combine(_testRoot, "GameSaves");
            _backupDir = Path.Combine(_testRoot, "Backups");

            Directory.CreateDirectory(_gameSaveDir);
            Directory.CreateDirectory(_backupDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                {
                    Directory.Delete(_testRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [Fact]
        public void SaveWatcherService_DetectsSaveFileChange_TriggersBackupAfterDebounce()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                AutoWatcherEnabled = true,
                AutoWatcherDebounceMs = 200
            };

            var backupService = new BackupService(config);
            using var watcherService = new SaveWatcherService(config, backupService);

            BackupCreatedEventArgs? eventArgs = null;
            using var backupFired = new ManualResetEventSlim(false);

            watcherService.BackupCreated += (s, e) =>
            {
                eventArgs = e;
                backupFired.Set();
            };

            watcherService.Start();
            Assert.True(watcherService.IsRunning);

            // Write a new save file
            string saveFile = Path.Combine(_gameSaveDir, "save_Autosave   Kingdom of Scotland   Turn 12.sav");
            File.WriteAllText(saveFile, "Sample save game binary content");

            // Wait for debounce and backup completion
            bool triggered = backupFired.Wait(TimeSpan.FromSeconds(4));

            Assert.True(triggered, "BackupCreated event should have fired after save file creation.");
            Assert.NotNull(eventArgs);
            Assert.Equal("Kingdom of Scotland", eventArgs.CampaignName);
            Assert.True(Directory.Exists(eventArgs.Backup.FullPath) || File.Exists(eventArgs.Backup.FullPath));

            var allBackups = backupService.GetBackups();
            Assert.Contains(allBackups, b => b.CampaignName == "Kingdom of Scotland" && b.Name.Contains("AutosaveSentinel"));
        }

        [Fact]
        public void SaveWatcherService_FileLockHandling_RetriesUntilUnlocked()
        {
            string lockedFile = Path.Combine(_gameSaveDir, "test_locked.sav");
            File.WriteAllText(lockedFile, "Initial data");

            // Open file with exclusive lock (FileShare.None)
            var fs = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            // Background task releases lock after 400ms
            Task.Run(async () =>
            {
                await Task.Delay(400);
                fs.Dispose();
            });

            // WaitForFileAvailable should retry and succeed once released
            bool available = SaveWatcherService.WaitForFileAvailable(lockedFile, maxRetries: 8, delayMs: 150);

            Assert.True(available, "File should become available after the lock is released.");
        }

        [Fact]
        public void SaveWatcherService_Disabled_DoesNotTrigger()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                AutoWatcherEnabled = false,
                AutoWatcherDebounceMs = 150
            };

            var backupService = new BackupService(config);
            using var watcherService = new SaveWatcherService(config, backupService);

            bool backupFired = false;
            watcherService.BackupCreated += (s, e) => { backupFired = true; };

            // Explicitly do not start or call Stop()
            watcherService.Stop();
            Assert.False(watcherService.IsRunning);

            string saveFile = Path.Combine(_gameSaveDir, "save_Autosave   Republic of Rome   Turn 1.sav");
            File.WriteAllText(saveFile, "Save content");

            Thread.Sleep(500);

            Assert.False(backupFired, "BackupCreated event should not fire when watcher is stopped.");
        }
    }
}
