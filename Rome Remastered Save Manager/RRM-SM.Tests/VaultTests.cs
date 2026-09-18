using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RRM_SM.Models;
using RRM_SM.Services;
using Xunit;

namespace RRM_SM.Tests
{
    public class VaultTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _gameSaveDir;
        private readonly string _backupDir;
        private readonly AppConfig _config;
        private readonly CampaignParserService _parser;

        public VaultTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "RRM_SM_VaultTests_" + Guid.NewGuid().ToString("N"));
            _gameSaveDir = Path.Combine(_testRoot, "GameSaves");
            _backupDir = Path.Combine(_testRoot, "Backups");

            Directory.CreateDirectory(_gameSaveDir);
            Directory.CreateDirectory(_backupDir);

            _config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                MaxBackupsToKeep = 5,
                MaxSentinelBackupsToKeep = 5
            };

            _parser = new CampaignParserService();
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
            catch { }
        }

        [Fact]
        public void SaveVaultService_AddSaveFile_DeduplicatesIdenticalContent()
        {
            var vault = new SaveVaultService(_config, _parser);

            string savePath = Path.Combine(_gameSaveDir, "save_Autosave   Rome   Turn 1.sav");
            File.WriteAllText(savePath, "Rome Turn 1 Binary Content");

            // First add
            var item1 = vault.AddSaveFile(savePath, "Rome", SaveSourceType.Manual, "Turn 1 Initial");
            Assert.NotNull(item1);
            Assert.True(File.Exists(Path.Combine(_backupDir, item1.StoredFileName)));

            // Add again with identical file content (same SHA-256)
            var item2 = vault.AddSaveFile(savePath, "Rome", SaveSourceType.Sentinel);
            Assert.Equal(item1.Id, item2.Id);
            Assert.Equal(item1.StoredFileName, item2.StoredFileName);

            // Manifest should only have 1 entry
            var allSaves = vault.GetAllSaves();
            Assert.Single(allSaves);

            // Exactly one .sav file should exist in the flat backup folder
            var savFiles = Directory.GetFiles(_backupDir, "*.sav");
            Assert.Single(savFiles);
        }

        [Fact]
        public void SaveVaultService_AddSaveFile_ResolvesFilenameCollisionWithTimestamp()
        {
            var vault = new SaveVaultService(_config, _parser);

            string quicksave = Path.Combine(_gameSaveDir, "save_Quicksave.sav");

            // Save revision 1
            File.WriteAllText(quicksave, "Quicksave Turn 10 Content");
            var item1 = vault.AddSaveFile(quicksave, "Rome", SaveSourceType.Manual, "Before Battle");
            Assert.Equal("save_Quicksave.sav", item1.StoredFileName);

            // Save revision 2 (modified file content with same filename)
            File.WriteAllText(quicksave, "Quicksave Turn 11 Content (Updated)");
            var item2 = vault.AddSaveFile(quicksave, "Rome", SaveSourceType.Manual, "After Battle");

            // Should have a timestamped suffix to preserve both on disk
            Assert.NotEqual(item1.Id, item2.Id);
            Assert.NotEqual(item1.StoredFileName, item2.StoredFileName);
            Assert.StartsWith("save_Quicksave_", item2.StoredFileName);
            Assert.EndsWith(".sav", item2.StoredFileName);
            Assert.Equal("save_Quicksave.sav", item2.OriginalGameFileName);

            // Both files must physically exist flat on disk
            Assert.True(File.Exists(Path.Combine(_backupDir, item1.StoredFileName)));
            Assert.True(File.Exists(Path.Combine(_backupDir, item2.StoredFileName)));

            var all = vault.GetAllSaves();
            Assert.Equal(2, all.Count);
        }

        [Fact]
        public void SaveVaultService_SelfHealingIndex_RebuildsFromDiskWhenManifestDeleted()
        {
            var vault = new SaveVaultService(_config, _parser);

            string s1 = Path.Combine(_gameSaveDir, "save_Autosave   Kingdom of Scotland   Turn 15.sav");
            string s2 = Path.Combine(_gameSaveDir, "save_Pergamon - 50.sav");
            File.WriteAllText(s1, "Scotland 15");
            File.WriteAllText(s2, "Pergamon 50");

            vault.AddSaveFile(s1);
            vault.AddSaveFile(s2);

            string manifestPath = Path.Combine(_backupDir, "vault.json");
            Assert.True(File.Exists(manifestPath));

            // Delete vault manifest to simulate corrupted or lost index
            File.Delete(manifestPath);
            Assert.False(File.Exists(manifestPath));

            // Create new vault service instance pointing to same directory
            var recoveredVault = new SaveVaultService(_config, _parser);
            recoveredVault.RebuildIndexFromDisk();

            var recoveredSaves = recoveredVault.GetAllSaves();
            Assert.Equal(2, recoveredSaves.Count);

            Assert.Contains(recoveredSaves, s => s.CampaignName == "Kingdom of Scotland" && s.Turn == 15);
            Assert.Contains(recoveredSaves, s => s.CampaignName == "Pergamon" && s.Turn == 50);

            // Manifest file should be regenerated
            Assert.True(File.Exists(manifestPath));
        }

        [Fact]
        public void SaveVaultService_TogglePin_PersistsPinnedState()
        {
            var vault = new SaveVaultService(_config, _parser);

            string s = Path.Combine(_gameSaveDir, "save_Rome_Battle.sav");
            File.WriteAllText(s, "Battle save");

            var item = vault.AddSaveFile(s, "Rome");
            Assert.False(item.IsPinned);

            // Pin it
            bool pinned = vault.TogglePin(item.Id);
            Assert.True(pinned);

            // Reload from disk
            var reloadedVault = new SaveVaultService(_config, _parser);
            var reloadedItem = reloadedVault.GetSaveById(item.Id);
            Assert.NotNull(reloadedItem);
            Assert.True(reloadedItem.IsPinned);

            // Unpin it
            bool unpinned = reloadedVault.TogglePin(item.Id);
            Assert.False(unpinned);
        }

        [Fact]
        public void SaveVaultService_UpdateMetadata_PersistsTitleNotesAndTags()
        {
            var vault = new SaveVaultService(_config, _parser);

            string s = Path.Combine(_gameSaveDir, "save_Rome - Turn 25.sav");
            File.WriteAllText(s, "Turn 25 save");

            var item = vault.AddSaveFile(s, "Rome");

            vault.UpdateMetadata(item.Id, "Fall of Carthage", "Grand victory over Hannibal.", new[] { "Victory", "BossBattle" });

            // Reload from disk
            var reloadedVault = new SaveVaultService(_config, _parser);
            var reloadedItem = reloadedVault.GetSaveById(item.Id);
            Assert.NotNull(reloadedItem);
            Assert.Equal("Fall of Carthage", reloadedItem.CustomTitle);
            Assert.Equal("Grand victory over Hannibal.", reloadedItem.Notes);
            Assert.Equal(2, reloadedItem.Tags.Count);
            Assert.Contains("Victory", reloadedItem.Tags);
            Assert.Contains("BossBattle", reloadedItem.Tags);
        }

        [Fact]
        public void SaveVaultService_MigrateLegacyBackups_ImportsNestedFolders()
        {
            // Set up a legacy nested backup folder structure:
            // <BackupDir>/Julii/Backup_2026-01-01_10-00-00/save_Autosave   Julii   Turn 5.sav
            string legacyCampaignDir = Path.Combine(_backupDir, "Julii");
            string legacySnapshotDir = Path.Combine(legacyCampaignDir, "Backup_2026-01-01_10-00-00");
            Directory.CreateDirectory(legacySnapshotDir);

            string legacySaveFile = Path.Combine(legacySnapshotDir, "save_Autosave   Julii   Turn 5.sav");
            File.WriteAllText(legacySaveFile, "Legacy Julii Save File Data");

            var vault = new SaveVaultService(_config, _parser);
            vault.MigrateLegacyBackups();

            // The save must now exist flat in root backup directory
            string flatSavePath = Path.Combine(_backupDir, "save_Autosave   Julii   Turn 5.sav");
            Assert.True(File.Exists(flatSavePath));

            // It must be registered in the vault manifest
            var saves = vault.GetAllSaves();
            Assert.Single(saves);
            Assert.Equal("Julii", saves[0].CampaignName);
            Assert.Equal(5, saves[0].Turn);

            // The legacy nested directory should be cleaned up
            Assert.False(Directory.Exists(legacySnapshotDir));
        }

        [Fact]
        public void SaveVaultService_RestoreSave_RestoresToActiveFolderWithOriginalName()
        {
            var vault = new SaveVaultService(_config, _parser);

            string originalGameFile = Path.Combine(_gameSaveDir, "save_Quicksave.sav");
            File.WriteAllText(originalGameFile, "Original Quicksave Content v1");

            // Back up initial
            var item1 = vault.AddSaveFile(originalGameFile, "Rome");

            // Simulate quicksave overwrite in game
            File.WriteAllText(originalGameFile, "Quicksave Content v2 Overwritten");
            var item2 = vault.AddSaveFile(originalGameFile, "Rome");

            // Modify active file to simulate a game disaster
            File.WriteAllText(originalGameFile, "Corrupted or Lost Save");

            // Restore initial v1 save
            vault.RestoreSave(item1.Id, createSafetyBackup: true);

            // Active game save should now be restored with original game file name and original content
            Assert.True(File.Exists(originalGameFile));
            Assert.Equal("Original Quicksave Content v1", File.ReadAllText(originalGameFile));

            // A pre-restore safety backup should have been created in the vault
            var safety = vault.GetAllSaves().FirstOrDefault(s => s.Source == SaveSourceType.SafetyBackup);
            Assert.NotNull(safety);
        }
    }
}
