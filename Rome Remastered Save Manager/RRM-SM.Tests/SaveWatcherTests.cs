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
            Assert.Contains(allBackups, b => b.CampaignName == "Kingdom of Scotland" && b.IsSentinelBackup);
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

        [Fact]
        public void SaveWatcherService_OnlyBacksUpChangedSaveFiles_NotAllCampaignSaves()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                AutoWatcherEnabled = true,
                AutoWatcherDebounceMs = 200,
                CompressBackups = false
            };

            // Seed active save folder with multiple saves for the same faction
            string oldManual = Path.Combine(_gameSaveDir, "save_Byzantium.sav");
            string oldAutosave = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 183.sav");
            File.WriteAllText(oldManual, "Old manual save data");
            File.WriteAllText(oldAutosave, "Old autosave data");

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

            // Simulate game writing a NEW turn autosave
            string newAutosave = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 188 End.sav");
            File.WriteAllText(newAutosave, "New turn 188 save data");

            bool triggered = backupFired.Wait(TimeSpan.FromSeconds(4));

            Assert.True(triggered, "Watcher should have fired for the new save file.");
            Assert.NotNull(eventArgs);
            Assert.Equal("Byzantium", eventArgs.CampaignName);
            Assert.True(eventArgs.Backup.IsSentinelBackup);

            // Crucial verification: snapshot must contain ONLY the changed file, NOT all campaign saves!
            Assert.Equal(1, eventArgs.Backup.FileCount);
            Assert.True(File.Exists(eventArgs.Backup.FullPath));
            Assert.Equal("save_Autosave   Byzantium   Turn 188 End.sav", Path.GetFileName(eventArgs.Backup.FullPath));
            Assert.False(File.Exists(Path.Combine(_backupDir, "save_Byzantium.sav")), "Old manual save should not be copied into Sentinel snapshot!");
            Assert.False(File.Exists(Path.Combine(_backupDir, "save_Autosave   Byzantium   Turn 183.sav")), "Old autosave should not be copied into Sentinel snapshot!");
        }

        [Fact]
        public void EnforceRetentionLimit_SeparatesManualAndSentinelBackups()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                CompressBackups = false,
                MaxBackupsToKeep = 2,
                MaxSentinelBackupsToKeep = 3
            };

            var backupService = new BackupService(config);

            string saveFile = Path.Combine(_gameSaveDir, "save_Rome_Turn1.sav");

            // 1. Create 3 manual backups for Rome with distinct turn content (retention limit is 2)
            File.WriteAllText(saveFile, "Rome save T1");
            backupService.CreateCampaignBackup("Rome", "M1");
            File.WriteAllText(saveFile, "Rome save T2");
            backupService.CreateCampaignBackup("Rome", "M2");
            File.WriteAllText(saveFile, "Rome save T3");
            backupService.CreateCampaignBackup("Rome", "M3");

            // 2. Create 5 Sentinel backups for Rome with distinct turn content (retention limit is 3)
            for (int i = 1; i <= 5; i++)
            {
                File.WriteAllText(saveFile, $"Rome sentinel save T{i}");
                backupService.CreateSentinelBackup("Rome", new[] { saveFile }, $"Sentinel_T{i}");
            }

            var all = backupService.GetBackups().Where(b => !b.IsSafetyBackup && b.CampaignName == "Rome").ToList();
            var manual = all.Where(b => !b.IsSentinelBackup).ToList();
            var sentinel = all.Where(b => b.IsSentinelBackup).ToList();

            // Manual backups should be pruned to 2 (M2, M3) and not touched by Sentinel!
            Assert.Equal(2, manual.Count);
            Assert.Contains(manual, b => b.Name.Contains("M3"));
            Assert.Contains(manual, b => b.Name.Contains("M2"));

            // Sentinel backups should be pruned to 3 (most recent 3)
            Assert.Equal(3, sentinel.Count);
        }
    }
}
