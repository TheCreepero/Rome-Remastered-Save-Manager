using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public void CampaignParser_ParsesAllKnownFormats()
        {
            var parser = new CampaignParserService();

            // 1. Standard Rome Remastered Autosaves
            var s1 = parser.ParseSaveFile("save_Autosave   Kingdom of Scotland   Turn 6.sav");
            Assert.Equal("Kingdom of Scotland", s1.FactionName);
            Assert.Equal(6, s1.Turn);
            Assert.Equal(SaveFileType.Autosave, s1.Type);

            var s2 = parser.ParseSaveFile("save_Autosave   Odrysian Kingdom   Turn 185 End.sav");
            Assert.Equal("Odrysian Kingdom", s2.FactionName);
            Assert.Equal(185, s2.Turn);

            var s3 = parser.ParseSaveFile("save_Autosave   The House of Claudii   Turn 1.sav");
            Assert.Equal("The House of Claudii", s3.FactionName);
            Assert.Equal(1, s3.Turn);

            var s4 = parser.ParseSaveFile("save_Autosave   Republic of Rome   Turn 1.sav");
            Assert.Equal("Republic of Rome", s4.FactionName);
            Assert.Equal(1, s4.Turn);

            // 2. Delimited manual saves
            var s5 = parser.ParseSaveFile("save_Kingdom of Macedon - 101.sav");
            Assert.Equal("Kingdom of Macedon", s5.FactionName);
            Assert.Equal(101, s5.Turn);
            Assert.Equal(SaveFileType.Manual, s5.Type);

            var s6 = parser.ParseSaveFile("save_Kingdom of Macedon - Battle.sav");
            Assert.Equal("Kingdom of Macedon", s6.FactionName);
            Assert.Equal(SaveFileType.Battle, s6.Type);

            var s7 = parser.ParseSaveFile("save_Byzantine Kingdom - Formation of a Roman Empire.sav");
            Assert.Equal("Byzantine Kingdom", s7.FactionName);

            // 3. Direct faction saves
            var s8 = parser.ParseSaveFile("save_Bactria.sav");
            Assert.Equal("Bactria", s8.FactionName);

            var s9 = parser.ParseSaveFile("save_Kingdom of Pergamon.sav");
            Assert.Equal("Kingdom of Pergamon", s9.FactionName);

            // 4. Quicksave
            var s10 = parser.ParseSaveFile("save_Quicksave.sav");
            Assert.Equal(SaveFileType.Quicksave, s10.Type);
        }

        [Fact]
        public void CampaignParser_AssociatesQuicksaveWithDominantFaction()
        {
            var parser = new CampaignParserService();

            string f1 = Path.Combine(_gameSaveDir, "save_Autosave   Pergamon   Turn 20.sav");
            string f2 = Path.Combine(_gameSaveDir, "save_Pergamon - Turn 20.sav");
            string f3 = Path.Combine(_gameSaveDir, "save_Quicksave.sav");

            File.WriteAllText(f1, "Pergamon autosave");
            File.WriteAllText(f2, "Pergamon manual save");
            File.WriteAllText(f3, "Quicksave data");

            // Ensure last modified timestamps are close
            DateTime now = DateTime.Now;
            File.SetLastWriteTime(f1, now.AddMinutes(-10));
            File.SetLastWriteTime(f2, now.AddMinutes(-5));
            File.SetLastWriteTime(f3, now);

            var groups = parser.GroupSaveFiles(new[] { f1, f2, f3 });

            Assert.True(groups.ContainsKey("Pergamon"));
            // Quicksave should be associated with Pergamon since all saves belong to Pergamon
            var pergamonSaves = groups["Pergamon"];
            Assert.Contains(pergamonSaves, s => s.Type == SaveFileType.Quicksave);
        }

        [Fact]
        public void BackupService_CampaignSortedBackup_OrganizesByFactionDirectory()
        {
            // Setup distinct campaign save files
            string scotSave = Path.Combine(_gameSaveDir, "save_Autosave   Kingdom of Scotland   Turn 6.sav");
            string pergSave = Path.Combine(_gameSaveDir, "save_Autosave   Pergamon   Turn 20.sav");
            File.WriteAllText(scotSave, "Scotland Save Data");
            File.WriteAllText(pergSave, "Pergamon Save Data");

            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                CompressBackups = false
            };
            var backupService = new BackupService(config);

            // Create backup for Kingdom of Scotland
            var scotBackup = backupService.CreateCampaignBackup("Kingdom of Scotland", "HighlandStand");

            Assert.NotNull(scotBackup);
            Assert.Equal("Kingdom of Scotland", scotBackup.CampaignName);
            Assert.True(File.Exists(scotBackup.FullPath));

            // Scotland backup should ONLY contain Scotland save, not Pergamon
            Assert.True(File.Exists(Path.Combine(_backupDir, Path.GetFileName(scotSave))));
            Assert.False(File.Exists(Path.Combine(_backupDir, Path.GetFileName(pergSave))));

            // Backups list should discover it with correct campaign name
            var allBackups = backupService.GetBackups();
            Assert.Contains(allBackups, b => b.CampaignName == "Kingdom of Scotland");
        }

        [Fact]
        public void BackupService_CreateAllCampaignsBackup_CreatesSeparateCampaignBackups()
        {
            string scotSave = Path.Combine(_gameSaveDir, "save_Autosave   Kingdom of Scotland   Turn 6.sav");
            string pergSave = Path.Combine(_gameSaveDir, "save_Autosave   Pergamon   Turn 20.sav");
            File.WriteAllText(scotSave, "Scotland Save Data");
            File.WriteAllText(pergSave, "Pergamon Save Data");

            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                CompressBackups = false
            };
            var backupService = new BackupService(config);

            var created = backupService.CreateAllCampaignsBackup();
            Assert.True(created.Count >= 2);

            var campaigns = created.Select(b => b.CampaignName).ToList();
            Assert.Contains("Kingdom of Scotland", campaigns);
            Assert.Contains("Pergamon", campaigns);
        }

        [Fact]
        public void BackupService_PerCampaignRetention_DoesNotPruneOtherCampaigns()
        {
            var config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                CompressBackups = false,
                MaxBackupsToKeep = 2
            };
            var backupService = new BackupService(config);

            // Create 3 backups for Macedon (retention should keep 2)
            backupService.CreateCampaignBackup("Macedon", "M1");
            backupService.CreateCampaignBackup("Macedon", "M2");
            backupService.CreateCampaignBackup("Macedon", "M3");

            // Create 2 backups for Pergamon
            backupService.CreateCampaignBackup("Pergamon", "P1");
            backupService.CreateCampaignBackup("Pergamon", "P2");

            var all = backupService.GetBackups().Where(b => !b.IsSafetyBackup).ToList();
            var macedonBackups = all.Where(b => b.CampaignName == "Macedon").ToList();
            var pergamonBackups = all.Where(b => b.CampaignName == "Pergamon").ToList();

            // Macedon should be trimmed to 2
            Assert.Equal(2, macedonBackups.Count);
            // Pergamon should still have 2 (not affected by Macedon's backups)
            Assert.Equal(2, pergamonBackups.Count);
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
            var initialSnapshot = backupService.CreateCampaignBackup("Rome", "StateBeforeChanges");

            // 2. Modify active saves (simulate continued play)
            File.WriteAllText(Path.Combine(_gameSaveDir, "save_Rome_Turn1.sav"), "Turn 50 Content (Modified)");

            Assert.Equal("Turn 50 Content (Modified)", File.ReadAllText(Path.Combine(_gameSaveDir, "save_Rome_Turn1.sav")));

            // 3. Restore initial snapshot
            backupService.RestoreBackup(initialSnapshot, createSafetyBackup: true);

            // 4. Assert restored files match initial state
            Assert.Equal("Turn 1 Content", File.ReadAllText(Path.Combine(_gameSaveDir, "save_Rome_Turn1.sav")));

            // 5. Assert safety backup was created
            var backups = backupService.GetBackups();
            Assert.Contains(backups, b => b.IsSafetyBackup);
        }

        [Fact]
        public void BackupEntry_FormattedSize_CalculatesUnitsProperly()
        {
            string sep = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var b1 = new BackupEntry { TotalSizeBytes = 500 };
            Assert.Equal("500 B", b1.FormattedSize);

            var b2 = new BackupEntry { TotalSizeBytes = 2048 };
            Assert.Equal($"2{sep}00 KB", b2.FormattedSize);

            // Verify 50 MB produces 50 MB, not 0.05 MB
            var b3 = new BackupEntry { TotalSizeBytes = 52428800 };
            Assert.Equal($"50{sep}00 MB", b3.FormattedSize);

            var b4 = new BackupEntry { TotalSizeBytes = 1610612736 };
            Assert.Equal($"1{sep}50 GB", b4.FormattedSize);
        }

        [Fact]
        public void BackupEntry_SortsNumericallyByTotalSizeBytes()
        {
            var list = new List<BackupEntry>
            {
                new BackupEntry { Name = "Big", TotalSizeBytes = 104857600 },    // 100 MB
                new BackupEntry { Name = "Tiny", TotalSizeBytes = 51200 },       // 50 KB
                new BackupEntry { Name = "Medium", TotalSizeBytes = 2621440 },   // 2.5 MB
                new BackupEntry { Name = "Huge", TotalSizeBytes = 1288490188 }   // 1.2 GB
            };

            var sorted = list.OrderBy(b => b.TotalSizeBytes).Select(b => b.Name).ToList();
            Assert.Equal(new[] { "Tiny", "Medium", "Big", "Huge" }, sorted);
        }

        [Fact]
        public void BackupService_SanitizesCustomBackupName()
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string rawName = "Turn:50*Siege? <Before>|War";
            string sanitized = new string(rawName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();

            Assert.DoesNotContain(":", sanitized);
            Assert.DoesNotContain("*", sanitized);
            Assert.DoesNotContain("?", sanitized);
            Assert.DoesNotContain("<", sanitized);
            Assert.DoesNotContain(">", sanitized);
            Assert.DoesNotContain("|", sanitized);
            Assert.Equal("Turn50Siege BeforeWar", sanitized);
        }

        [Fact]
        public void Readme_ContainsNoEmojis()
        {
            // Locate README.md by walking up from AppContext.BaseDirectory
            string? dir = AppContext.BaseDirectory;
            string? readmePath = null;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "README.md");
                if (File.Exists(candidate))
                {
                    readmePath = candidate;
                    break;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }

            Assert.True(!string.IsNullOrEmpty(readmePath) && File.Exists(readmePath), "README.md could not be found by traversing ancestor directories.");

            string content = File.ReadAllText(readmePath);

            // Regex covering standard unicode emojis:
            // High/Low Surrogate pairs: \uD83C[\uDF00-\uDFFF] | \uD83D[\uDC00-\uDFFF] | \uD83E[\uDD00-\uDFFF] etc. (astral planes)
            // Miscellaneous Symbols & Dingbats: \u2600-\u27BF
            // Misc Symbols and Arrows: \u2B50-\u2B55
            // Misc Technical: \u2300-\u23FF
            // Geometric / enclosed / punctuation symbols often used as emojis: \u203C, \u2049, \u2122, \u2139, \u2194-\u2199, \u21A9-\u21AA, \u25AA-\u25AB, \u25B6, \u25C0, \u25FB-\u25FE
            var emojiRegex = new System.Text.RegularExpressions.Regex(
                @"[\uD83C-\uDBFF\uDC00-\uDFFF]|[\u2600-\u27BF]|[\u2300-\u23FF]|[\u2B50-\u2B55]|[\u203C\u2049\u2122\u2139\u2194-\u2199\u21A9-\u21AA\u25AA-\u25AB\u25B6\u25C0\u25FB-\u25FE]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            var matches = emojiRegex.Matches(content);
            Assert.True(matches.Count == 0, $"README.md contains {matches.Count} emoji(s): " + string.Join(", ", matches.Select(m => $"{m.Value} (U+{(m.Value.Length == 1 ? ((int)m.Value[0]).ToString("X4") : char.ConvertToUtf32(m.Value, 0).ToString("X"))})")));
        }

        [Fact]
        public void SaveMetadataReader_DetectsOverhaulModFromBinarySave()
        {
            string tempFile = Path.Combine(_testRoot, "test_rtr_save.sav");
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // Write 8KB of zeros
                byte[] zeros = new byte[8192];
                bw.Write(zeros);

                // Write bogus early marker at offset 171
                fs.Seek(171, SeekOrigin.Begin);
                bw.Write(new byte[] { 0xD2, 0x02, 0x96, 0x49 });
                bw.Write(1084227584); // bogus count that would fail validation

                // Write real mod table at 0x1829 (6185)
                fs.Seek(0x1829, SeekOrigin.Begin);
                bw.Write(new byte[] { 0xD2, 0x02, 0x96, 0x49 }); // Marker
                bw.Write((int)3); // 3 mods

                // Mod 1: Camera
                bw.Write((ulong)1001);
                string mod1 = "Enhanced Camera Bundle";
                bw.Write((ushort)mod1.Length);
                bw.Write(System.Text.Encoding.Unicode.GetBytes(mod1));

                // Mod 2: RTR Imperium Surrectum
                bw.Write((ulong)1002);
                string mod2 = "RTR: Imperium Surrectum 0.6.6";
                bw.Write((ushort)mod2.Length);
                bw.Write(System.Text.Encoding.Unicode.GetBytes(mod2));

                // Mod 3: Dark UI
                bw.Write((ulong)1003);
                string mod3 = "Dark UI 2.0";
                bw.Write((ushort)mod3.Length);
                bw.Write(System.Text.Encoding.Unicode.GetBytes(mod3));
            }

            var meta = SaveMetadataReader.ReadSaveMetadata(tempFile);
            Assert.NotNull(meta);
            Assert.Equal(3, meta.ActiveMods.Count);
            Assert.Contains("Enhanced Camera Bundle", meta.ActiveMods);
            Assert.Contains("RTR: Imperium Surrectum 0.6.6", meta.ActiveMods);
            Assert.Contains("Dark UI 2.0", meta.ActiveMods);
            Assert.Equal("RTR: Imperium Surrectum 0.6.6", meta.PrimaryModName);
        }
    }
}
