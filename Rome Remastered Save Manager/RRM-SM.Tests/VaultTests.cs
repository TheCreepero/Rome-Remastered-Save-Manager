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

        [Fact]
        public void CampaignSeparation_SameFaction_DifferentTimeframes_CreatesTwoCampaigns()
        {
            var vault = new SaveVaultService(_config, _parser);

            // Campaign 1: Feb 2025 at Turn 527
            string save1 = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 527 Start.sav");
            File.WriteAllText(save1, "Byzantium Turn 527 Content");
            File.SetLastWriteTime(save1, new DateTime(2025, 2, 15, 23, 38, 0));
            var item1 = vault.AddSaveFile(save1, "Byzantium");

            // Campaign 2: Sep 2026 at Turn 183 (19 months later, turn 183 vs 527)
            string save2 = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 183 Start.sav");
            File.WriteAllText(save2, "Byzantium Turn 183 Content");
            File.SetLastWriteTime(save2, new DateTime(2026, 9, 18, 23, 0, 0));
            var item2 = vault.AddSaveFile(save2, "Byzantium");

            // Verify they have distinct CampaignIds
            Assert.NotNull(item1.CampaignId);
            Assert.NotNull(item2.CampaignId);
            Assert.NotEqual(item1.CampaignId, item2.CampaignId);

            // Verify automatic naming disambiguation with date
            Assert.Contains("2025", item1.CampaignName);
            Assert.Contains("2026", item2.CampaignName);
            Assert.Equal("Byzantium", item1.Faction);
            Assert.Equal("Byzantium", item2.Faction);
        }

        [Fact]
        public void CampaignSeparation_SameFaction_ConsecutiveSessions_SharesCampaignId()
        {
            var vault = new SaveVaultService(_config, _parser);

            // Session 1: Turn 182
            string save1 = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 182 End.sav");
            File.WriteAllText(save1, "Byzantium Turn 182");
            File.SetLastWriteTime(save1, new DateTime(2026, 9, 17, 22, 0, 0));
            var item1 = vault.AddSaveFile(save1, "Byzantium");

            // Session 2: Turn 183 (1 day later)
            string save2 = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 183 Start.sav");
            File.WriteAllText(save2, "Byzantium Turn 183");
            File.SetLastWriteTime(save2, new DateTime(2026, 9, 18, 22, 0, 0));
            var item2 = vault.AddSaveFile(save2, "Byzantium");

            // Same campaign playthrough!
            Assert.Equal(item1.CampaignId, item2.CampaignId);
            Assert.Equal(item1.CampaignName, item2.CampaignName);
        }

        [Fact]
        public void CampaignSeparation_SameFaction_TurnDiscontinuity_CreatesNewCampaign()
        {
            var vault = new SaveVaultService(_config, _parser);

            // High turn save
            string save1 = Path.Combine(_gameSaveDir, "save_Autosave   Rome   Turn 480 Start.sav");
            File.WriteAllText(save1, "Rome Turn 480");
            File.SetLastWriteTime(save1, new DateTime(2026, 9, 10, 12, 0, 0));
            var item1 = vault.AddSaveFile(save1, "Rome");

            // Turn 1 save (479 turns apart, despite only 2 days difference)
            string save2 = Path.Combine(_gameSaveDir, "save_Autosave   Rome   Turn 1 Start.sav");
            File.WriteAllText(save2, "Rome Turn 1");
            File.SetLastWriteTime(save2, new DateTime(2026, 9, 12, 12, 0, 0));
            var item2 = vault.AddSaveFile(save2, "Rome");

            Assert.NotEqual(item1.CampaignId, item2.CampaignId);
        }

        [Fact]
        public void CampaignSeparation_EnforceRetentionLimit_PrunesByCampaignIdIndependently()
        {
            _config.MaxBackupsToKeep = 2; // Only keep 2 manual saves per campaign
            var vault = new SaveVaultService(_config, _parser);

            // Campaign 1: 3 saves
            for (int i = 1; i <= 3; i++)
            {
                string path = Path.Combine(_gameSaveDir, $"save_Autosave   Byzantium   Turn {520 + i}.sav");
                File.WriteAllText(path, $"Camp 1 Turn {520 + i}");
                File.SetLastWriteTime(path, new DateTime(2025, 2, 10 + i, 12, 0, 0));
                vault.AddSaveFile(path, "Byzantium");
            }

            // Campaign 2: 1 save (18 months later)
            string camp2Path = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 1.sav");
            File.WriteAllText(camp2Path, "Camp 2 Turn 1");
            File.SetLastWriteTime(camp2Path, new DateTime(2026, 9, 18, 12, 0, 0));
            var camp2Item = vault.AddSaveFile(camp2Path, "Byzantium");

            var allSaves = vault.GetAllSaves();
            // Campaign 1 should have 2 saves left (pruned from 3 to 2)
            var camp1Saves = allSaves.Where(s => s.CampaignId != camp2Item.CampaignId).ToList();
            Assert.Equal(2, camp1Saves.Count);

            // Campaign 2 should still have its 1 save (not pruned because of campaign 1)
            var camp2Saves = allSaves.Where(s => s.CampaignId == camp2Item.CampaignId).ToList();
            Assert.Single(camp2Saves);
        }

        [Fact]
        public void CampaignSeparation_MergeAndSplitCampaigns()
        {
            var vault = new SaveVaultService(_config, _parser);

            string path1 = Path.Combine(_gameSaveDir, "save_Autosave   Macedon   Turn 10.sav");
            File.WriteAllText(path1, "Macedon Turn 10");
            File.SetLastWriteTime(path1, new DateTime(2026, 1, 1));
            var item1 = vault.AddSaveFile(path1, "Macedon");

            string path2 = Path.Combine(_gameSaveDir, "save_Autosave   Macedon   Turn 50.sav");
            File.WriteAllText(path2, "Macedon Turn 50");
            File.SetLastWriteTime(path2, new DateTime(2026, 6, 1)); // 5 months later -> separate campaign
            var item2 = vault.AddSaveFile(path2, "Macedon");

            Assert.NotEqual(item1.CampaignId, item2.CampaignId);

            // Merge Campaign 2 into Campaign 1
            vault.MergeCampaigns(item1.CampaignId!, new[] { item2.CampaignId! });
            var mergedSaves = vault.GetAllSaves();
            Assert.Equal(item1.CampaignId, mergedSaves.First(s => s.Id == item2.Id).CampaignId);

            // Split item2 into a new campaign
            var splitMeta = vault.SplitCampaign(new[] { item2.Id }, "Macedon Late Game");
            Assert.Equal("Macedon Late Game", splitMeta.DisplayName);

            var afterSplit = vault.GetAllSaves();
            Assert.Equal(splitMeta.Id, afterSplit.First(s => s.Id == item2.Id).CampaignId);
            Assert.Equal("Macedon Late Game", afterSplit.First(s => s.Id == item2.Id).CampaignName);
        }

        [Fact]
        public void CampaignSeparation_RenameCampaign_PreservesFactionAndMatchesNewSaves()
        {
            var vault = new SaveVaultService(_config, _parser);

            string path1 = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 180.sav");
            File.WriteAllText(path1, "Turn 180");
            File.SetLastWriteTime(path1, new DateTime(2026, 9, 18, 10, 0, 0));
            var item1 = vault.AddSaveFile(path1, "Byzantium");

            // Rename to custom name
            vault.RenameCampaign(item1.CampaignId!, "Roman Reclamation");

            var updatedItem = vault.GetSaveById(item1.Id);
            Assert.Equal("Roman Reclamation", updatedItem!.CampaignName);

            // Add a new save for the same faction within 14 days and 50 turns
            string path2 = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 181.sav");
            File.WriteAllText(path2, "Turn 181");
            File.SetLastWriteTime(path2, new DateTime(2026, 9, 18, 12, 0, 0));
            var item2 = vault.AddSaveFile(path2, "Byzantium");

            // It should be assigned to the renamed campaign!
            Assert.Equal(item1.CampaignId, item2.CampaignId);
            Assert.Equal("Roman Reclamation", item2.CampaignName);
        }

        [Fact]
        public void CampaignSeparation_RebuildCampaignAssignments_FixesLegacyFolderNames()
        {
            var vault = new SaveVaultService(_config, _parser);

            // Add a save with legacy folder name as campaign name
            string saveFile = Path.Combine(_gameSaveDir, "save_Autosave   Achaean League   Turn 3 End.sav");
            File.WriteAllText(saveFile, "Achaean League Turn 3");
            File.SetLastWriteTime(saveFile, new DateTime(2024, 8, 29, 17, 30, 0));
            var item = vault.AddSaveFile(saveFile, "Backup_2026-09-18_19-33-14");

            // Rebuild
            vault.RebuildCampaignAssignments();

            var reloaded = vault.GetSaveById(item.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("Achaean League", reloaded.Faction);
            Assert.Equal("Achaean League", reloaded.CampaignName);
            Assert.Equal(3, reloaded.Turn);
        }

        private static void CreateMockSaveWithGuid(string filePath, Guid campaignGuid, string extraContent = "")
        {
            byte[] buffer = new byte[128];
            buffer[0] = 0x0A; buffer[1] = 0x07; buffer[2] = 0x00; buffer[3] = 0x00;
            byte[] guidBytes = campaignGuid.ToByteArray();
            Buffer.BlockCopy(guidBytes, 0, buffer, 36, 16);

            if (!string.IsNullOrEmpty(extraContent))
            {
                byte[] extraBytes = System.Text.Encoding.UTF8.GetBytes(extraContent);
                Array.Resize(ref buffer, buffer.Length + extraBytes.Length);
                Buffer.BlockCopy(extraBytes, 0, buffer, 128, extraBytes.Length);
            }

            File.WriteAllBytes(filePath, buffer);
        }

        [Fact]
        public void TryReadInternalCampaignGuid_ValidHeader_ExtractsGuidCorrectly()
        {
            var expectedGuid = Guid.NewGuid();
            string testFile = Path.Combine(_gameSaveDir, "test_guid_header.sav");
            CreateMockSaveWithGuid(testFile, expectedGuid, "Save content data");

            string? extracted = CampaignParserService.TryReadInternalCampaignGuid(testFile);
            Assert.NotNull(extracted);
            Assert.Equal(expectedGuid.ToString("D"), extracted);
        }

        [Fact]
        public void TryReadInternalCampaignGuid_InvalidOrShortFile_ReturnsNullSafely()
        {
            string shortFile = Path.Combine(_gameSaveDir, "short.sav");
            File.WriteAllBytes(shortFile, new byte[] { 1, 2, 3, 4 });

            string? result = CampaignParserService.TryReadInternalCampaignGuid(shortFile);
            Assert.Null(result);

            string? nonExistent = CampaignParserService.TryReadInternalCampaignGuid(Path.Combine(_gameSaveDir, "missing.sav"));
            Assert.Null(nonExistent);
        }

        [Fact]
        public void DeterministicCampaignSeparation_SameFactionAndDate_DifferentGameCampaignId_CreatesDistinctCampaigns()
        {
            var vault = new SaveVaultService(_config, _parser);

            var guid1 = Guid.NewGuid();
            var guid2 = Guid.NewGuid();

            // Playthrough 1: Byzantium Turn 10
            string save1 = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 10.sav");
            CreateMockSaveWithGuid(save1, guid1);
            File.SetLastWriteTime(save1, new DateTime(2026, 9, 18, 10, 0, 0));
            var item1 = vault.AddSaveFile(save1, "Byzantium");

            // Playthrough 2: Byzantium Turn 10 (same turn, same hour!), but different game campaign GUID
            string save2 = Path.Combine(_gameSaveDir, "save_Autosave   Byzantium   Turn 10 Start.sav");
            CreateMockSaveWithGuid(save2, guid2);
            File.SetLastWriteTime(save2, new DateTime(2026, 9, 18, 10, 30, 0));
            var item2 = vault.AddSaveFile(save2, "Byzantium");

            // Must be definitively separated by GameCampaignId!
            Assert.NotEqual(item1.CampaignId, item2.CampaignId);
            Assert.Equal(guid1.ToString("D"), item1.GameCampaignId);
            Assert.Equal(guid2.ToString("D"), item2.GameCampaignId);
        }

        [Fact]
        public void DeterministicCampaignSeparation_SameGameCampaignId_MatchesExactSameCampaign()
        {
            var vault = new SaveVaultService(_config, _parser);
            var playthroughGuid = Guid.NewGuid();

            // Save 1: Turn 1
            string save1 = Path.Combine(_gameSaveDir, "save_Autosave   Rome   Turn 1.sav");
            CreateMockSaveWithGuid(save1, playthroughGuid);
            File.SetLastWriteTime(save1, new DateTime(2025, 1, 1));
            var item1 = vault.AddSaveFile(save1, "Rome");

            // Save 2: Turn 150 played 6 months later (exceeding 14-day gap and 50 turns)
            // But having the exact same GameCampaignId from the save header
            string save2 = Path.Combine(_gameSaveDir, "save_Autosave   Rome   Turn 150.sav");
            CreateMockSaveWithGuid(save2, playthroughGuid);
            File.SetLastWriteTime(save2, new DateTime(2025, 7, 1));
            var item2 = vault.AddSaveFile(save2, "Rome");

            // GameCampaignId ground truth unites them despite heuristic thresholds!
            Assert.Equal(item1.CampaignId, item2.CampaignId);
            Assert.Equal(playthroughGuid.ToString("D"), item2.GameCampaignId);
        }

        [Fact]
        public void DeterministicQuicksaveRouting_QuicksaveWithGameCampaignId_RoutesToExactCampaign()
        {
            var vault = new SaveVaultService(_config, _parser);
            var macedonGuid = Guid.NewGuid();

            // Active campaign: Macedon
            string campSave = Path.Combine(_gameSaveDir, "save_Autosave   Kingdom of Macedon   Turn 40.sav");
            CreateMockSaveWithGuid(campSave, macedonGuid);
            var campItem = vault.AddSaveFile(campSave, "Kingdom of Macedon");

            // Quicksave with NO faction in the name, but matching Macedon's header GUID
            string quicksave = Path.Combine(_gameSaveDir, "save_Quicksave.sav");
            CreateMockSaveWithGuid(quicksave, macedonGuid);
            var quickItem = vault.AddSaveFile(quicksave);

            Assert.Equal(campItem.CampaignId, quickItem.CampaignId);
            Assert.Equal("Kingdom of Macedon", quickItem.Faction);
            Assert.Equal(campItem.CampaignName, quickItem.CampaignName);
        }
    }
}
