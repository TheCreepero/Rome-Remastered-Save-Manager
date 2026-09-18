using System;
using System.IO;
using RRM_SM.Models;
using RRM_SM.Services;
using Xunit;

namespace RRM_SM.Tests
{
    public class SaveManagerTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _gameSaveDir;
        private readonly string _backupDir;
        private readonly string _configFilePath;

        public SaveManagerTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "RRM_SM_Tests_" + Guid.NewGuid().ToString("N"));
            _gameSaveDir = Path.Combine(_testRoot, "GameSaves");
            _backupDir = Path.Combine(_testRoot, "Backups");
            _configFilePath = Path.Combine(_testRoot, "config.json");

            Directory.CreateDirectory(_gameSaveDir);
            Directory.CreateDirectory(_backupDir);

            // Populate sample dummy saves
            File.WriteAllText(Path.Combine(_gameSaveDir, "save_Rome_Turn1.sav"), "Turn 1 Content");
            File.WriteAllText(Path.Combine(_gameSaveDir, "save_Quicksave.sav"), "Quicksave Content");

            string subDir = Path.Combine(_gameSaveDir, "SubFolder");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "nested_save.sav"), "Nested Save Content");
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
                // Ignore cleanup errors in temp
            }
        }

        [Fact]
        public void ConfigService_LoadAndSave_PersistsCorrectly()
        {
            var configService = new ConfigService(_configFilePath);
            var initialConfig = configService.LoadOrCreateConfig();

            initialConfig.GameSaveDirectory = _gameSaveDir;
            initialConfig.BackupDirectory = _backupDir;
            initialConfig.CompressBackups = true;
            initialConfig.MaxBackupsToKeep = 5;

            configService.SaveConfig(initialConfig);

            var reloadedConfig = configService.LoadOrCreateConfig();
            Assert.Equal(_gameSaveDir, reloadedConfig.GameSaveDirectory);
            Assert.Equal(_backupDir, reloadedConfig.BackupDirectory);
            Assert.True(reloadedConfig.CompressBackups);
            Assert.Equal(5, reloadedConfig.MaxBackupsToKeep);
        }

        [Fact]
        public void BackupService_FolderBackup_CreatesSnapshotSuccessfully()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                CompressBackups = false
            };
            var backupService = new BackupService(config);

            var backup = backupService.CreateBackup("TestCampaign");

            Assert.NotNull(backup);
            Assert.True(Directory.Exists(backup.FullPath));
            Assert.Contains("TestCampaign", backup.Name);
            Assert.Equal(3, backup.FileCount); // 2 root saves + 1 nested
            Assert.Equal(BackupType.Directory, backup.Type);

            // Verify files inside snapshot
            Assert.True(File.Exists(Path.Combine(backup.FullPath, "save_Rome_Turn1.sav")));
            Assert.True(File.Exists(Path.Combine(backup.FullPath, "SubFolder", "nested_save.sav")));
        }

        [Fact]
        public void BackupService_ZipBackup_CreatesValidArchive()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                CompressBackups = true
            };
            var backupService = new BackupService(config);

            var backup = backupService.CreateBackup();

            Assert.NotNull(backup);
            Assert.True(File.Exists(backup.FullPath));
            Assert.EndsWith(".zip", backup.Name);
            Assert.Equal(3, backup.FileCount);
            Assert.Equal(BackupType.ZipArchive, backup.Type);
        }

        [Fact]
        public void BackupService_Restore_OverwritesAndCreatesSafetyBackup()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                CompressBackups = false
            };
            var backupService = new BackupService(config);

            // 1. Create initial snapshot
            var initialSnapshot = backupService.CreateBackup("StateBeforeChanges");

            // 2. Modify active saves (simulate continued play or corruption)
            File.WriteAllText(Path.Combine(_gameSaveDir, "save_Rome_Turn1.sav"), "Turn 50 Content (Modified)");
            File.Delete(Path.Combine(_gameSaveDir, "save_Quicksave.sav"));

            Assert.Equal("Turn 50 Content (Modified)", File.ReadAllText(Path.Combine(_gameSaveDir, "save_Rome_Turn1.sav")));
            Assert.False(File.Exists(Path.Combine(_gameSaveDir, "save_Quicksave.sav")));

            // 3. Restore initial snapshot
            backupService.RestoreBackup(initialSnapshot, createSafetyBackup: true);

            // 4. Assert restored files match initial state
            Assert.Equal("Turn 1 Content", File.ReadAllText(Path.Combine(_gameSaveDir, "save_Rome_Turn1.sav")));
            Assert.True(File.Exists(Path.Combine(_gameSaveDir, "save_Quicksave.sav")));

            // 5. Assert safety backup was created before restore
            var backups = backupService.GetBackups();
            Assert.Contains(backups, b => b.IsSafetyBackup);
        }

        [Fact]
        public void BackupService_RetentionPolicy_PrunesOldBackups()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                CompressBackups = false,
                MaxBackupsToKeep = 2
            };
            var backupService = new BackupService(config);

            backupService.CreateBackup("Backup1");
            backupService.CreateBackup("Backup2");
            backupService.CreateBackup("Backup3");

            var activeBackups = backupService.GetBackups();
            // Should keep at most 2 backups
            Assert.Equal(2, activeBackups.Count);
        }
    }
}
