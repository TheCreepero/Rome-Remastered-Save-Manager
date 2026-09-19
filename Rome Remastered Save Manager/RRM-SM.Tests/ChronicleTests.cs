using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RRM_SM.Core.Models;
using RRM_SM.Models;
using RRM_SM.Services;
using Xunit;

namespace RRM_SM.Tests
{
    public class ChronicleTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _activeDir;
        private readonly string _backupDir;
        private readonly AppConfig _config;
        private readonly CampaignParserService _parserService;
        private readonly ChronicleService _chronicleService;

        public ChronicleTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "RRM_SM_ChronicleTests_" + Guid.NewGuid().ToString("N"));
            _activeDir = Path.Combine(_testDir, "Active");
            _backupDir = Path.Combine(_testDir, "Backups");

            Directory.CreateDirectory(_activeDir);
            Directory.CreateDirectory(_backupDir);

            _config = new AppConfig
            {
                GameSaveDirectory = _activeDir,
                BackupDirectory = _backupDir
            };

            _parserService = new CampaignParserService();
            _chronicleService = new ChronicleService(_config, _parserService);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, true); } catch { }
            }
        }

        private void CreateDummySave(string folder, string fileName, DateTime modified)
        {
            string path = Path.Combine(folder, fileName);
            File.WriteAllText(path, "dummy data");
            File.SetLastWriteTime(path, modified);
        }

        [Fact]
        public void ChronicleService_BuildsChronicleFromSaves_SortsChronologically()
        {
            // Arrange
            string faction = "Julii";
            CreateDummySave(_activeDir, "save_Autosave Julii Turn 4 Start.sav", new DateTime(2023, 1, 1, 10, 0, 0));
            CreateDummySave(_activeDir, "save_Julii - Turn 12.sav", new DateTime(2023, 1, 1, 12, 0, 0));
            
            string backupFactionDir = Path.Combine(_backupDir, faction);
            Directory.CreateDirectory(backupFactionDir);
            CreateDummySave(backupFactionDir, "save_Autosave Julii Turn 1 Start.sav", new DateTime(2023, 1, 1, 8, 0, 0));
            CreateDummySave(backupFactionDir, "save_Julii - Turn 12 Battle.sav", new DateTime(2023, 1, 1, 12, 30, 0));

            // Act
            var chronicle = _chronicleService.BuildChronicle(faction);

            // Assert
            Assert.Equal(faction, chronicle.CampaignName);
            Assert.Equal(4, chronicle.Milestones.Count);

            var ordered = chronicle.Milestones.ToList();
            Assert.Equal(1, ordered[0].Turn);
            Assert.Equal(SaveFileType.Autosave, ordered[0].SaveType);

            Assert.Equal(4, ordered[1].Turn);
            
            Assert.Equal(12, ordered[2].Turn);
            Assert.Equal(SaveFileType.Manual, ordered[2].SaveType);
            
            Assert.Equal(12, ordered[3].Turn);
            Assert.Equal(SaveFileType.Battle, ordered[3].SaveType);
            
            Assert.Equal(12, chronicle.MaxTurn);
        }

        [Fact]
        public void ChronicleService_PersistsAndReloadsNotes()
        {
            // Arrange
            string faction = "Brutii";
            string backupFactionDir = Path.Combine(_backupDir, faction);
            Directory.CreateDirectory(backupFactionDir);
            
            CreateDummySave(_activeDir, "save_Autosave Brutii Turn 5 Start.sav", new DateTime(2023, 1, 2, 10, 0, 0));

            var chronicle = _chronicleService.BuildChronicle(faction);
            var milestone = chronicle.Milestones.First();
            milestone.Title = "The Great Expansion";
            milestone.PlayerNotes = "We took Athens.";
            milestone.Tags = new List<string> { "Expansion", "Battle" };

            // Act
            _chronicleService.SaveChronicleNotes(chronicle);

            // Reload
            var reloadedChronicle = _chronicleService.BuildChronicle(faction);
            var reloadedMilestone = reloadedChronicle.Milestones.First();

            // Assert
            Assert.Equal("The Great Expansion", reloadedMilestone.Title);
            Assert.Equal("We took Athens.", reloadedMilestone.PlayerNotes);
            Assert.Equal(2, reloadedMilestone.Tags.Count);
            Assert.Contains("Expansion", reloadedMilestone.Tags);
        }

        [Fact]
        public void ChronicleService_GeneratesValidHtmlAndMarkdown()
        {
            // Arrange
            var chronicle = new CampaignChronicle
            {
                CampaignName = "Scipii",
                CampaignSummary = "A glorious campaign.",
                Milestones = new List<ChronicleMilestone>
                {
                    new ChronicleMilestone
                    {
                        Turn = 10,
                        Title = "Carthage Falls",
                        PlayerNotes = "The city is ours.",
                        SaveType = SaveFileType.Battle,
                        Timestamp = new DateTime(2023, 5, 5, 14, 0, 0)
                    }
                }
            };

            // Act
            string html = _chronicleService.GenerateHtmlReport(chronicle);
            string md = _chronicleService.GenerateMarkdownReport(chronicle);

            // Assert
            Assert.Contains("<!DOCTYPE html>", html);
            Assert.Contains("Carthage Falls", html);
            Assert.Contains("The city is ours.", html);
            
            Assert.Contains("# Scipii - Campaign Chronicle", md);
            Assert.Contains("### Carthage Falls", md);
            Assert.Contains("The city is ours.", md);
        }

        private byte[] CreateMockSaveHeader(Guid campaignGuid, int turnNumber, int calendarYear, int season, string? modName = null)
        {
            byte[] header = new byte[2048];

            // GUID at bytes 36..51
            byte[] guidBytes = campaignGuid.ToByteArray();
            Buffer.BlockCopy(guidBytes, 0, header, 36, 16);

            // Mod table marker 0xD2, 0x02, 0x96, 0x49 at offset 100
            int pos = 100;
            if (!string.IsNullOrEmpty(modName))
            {
                header[pos++] = 0xD2;
                header[pos++] = 0x02;
                header[pos++] = 0x96;
                header[pos++] = 0x49;

                BitConverter.GetBytes(1).CopyTo(header, pos); // count = 1
                pos += 4;

                BitConverter.GetBytes((long)98765432).CopyTo(header, pos); // mod ID
                pos += 8;

                BitConverter.GetBytes((ushort)modName.Length).CopyTo(header, pos);
                pos += 2;

                byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(modName);
                Buffer.BlockCopy(nameBytes, 0, header, pos, nameBytes.Length);
            }

            // descr_strat.txt at offset 400
            pos = 400;
            byte[] dsBytes = System.Text.Encoding.Unicode.GetBytes("descr_strat.txt");
            Buffer.BlockCopy(dsBytes, 0, header, pos, dsBytes.Length);
            pos += dsBytes.Length;

            // token/pointer (4 bytes)
            BitConverter.GetBytes(0x000044A8).CopyTo(header, pos);
            pos += 4;
            header[pos++] = 0x01; // marker

            // rawTurn (0-indexed)
            BitConverter.GetBytes(turnNumber - 1).CopyTo(header, pos);
            pos += 4;

            // rawYear (signed int32)
            BitConverter.GetBytes(calendarYear).CopyTo(header, pos);
            pos += 4;

            // rawSeason (0 = Summer, 2 = Winter)
            BitConverter.GetBytes(season).CopyTo(header, pos);
            pos += 4;

            return header;
        }

        [Fact]
        public void SaveMetadataReader_ReadsCalendarAndGuid_Correctly()
        {
            // Arrange
            var expectedGuid = Guid.NewGuid();
            string modName = "[PublicBETA] RIS 0.7.0 v7.18";
            byte[] header = CreateMockSaveHeader(expectedGuid, 101, -245, 2, modName);

            string savePath = Path.Combine(_activeDir, "test_macedon.sav");
            File.WriteAllBytes(savePath, header);

            // Act
            var meta = SaveMetadataReader.ReadSaveMetadata(savePath);

            // Assert
            Assert.NotNull(meta);
            Assert.Equal(expectedGuid.ToString("D"), meta.GameCampaignId);
            Assert.Equal(101, meta.TurnNumber);
            Assert.Equal(-245, meta.CalendarYear);
            Assert.Equal("Winter", meta.Season);
            Assert.Equal("Winter 245 BC", meta.InGameDate);
            Assert.Equal(modName, meta.PrimaryModName);
        }

        [Fact]
        public void SaveMetadataReader_HandlesADDatesAndSummer()
        {
            // Arrange
            var expectedGuid = Guid.NewGuid();
            string modName = "Chivalry Total War: REMASTERED";
            byte[] header = CreateMockSaveHeader(expectedGuid, 6, 1074, 0, modName);

            string savePath = Path.Combine(_activeDir, "test_scotland.sav");
            File.WriteAllBytes(savePath, header);

            // Act
            var meta = SaveMetadataReader.ReadSaveMetadata(savePath);

            // Assert
            Assert.NotNull(meta);
            Assert.Equal(6, meta.TurnNumber);
            Assert.Equal(1074, meta.CalendarYear);
            Assert.Equal("Summer", meta.Season);
            Assert.Equal("Summer 1074 AD", meta.InGameDate);
            Assert.Equal(modName, meta.PrimaryModName);
        }

        [Fact]
        public void ChronicleService_CalculatesEraAndDeltas_Correctly()
        {
            // Arrange
            string faction = "Pontus";
            var guid = Guid.NewGuid();

            byte[] header1 = CreateMockSaveHeader(guid, 1, -270, 2, "[PublicBETA] RIS 0.7.0");
            byte[] header2 = CreateMockSaveHeader(guid, 21, -265, 0, "[PublicBETA] RIS 0.7.0");

            string p1 = Path.Combine(_activeDir, "save_Autosave Pontus Turn 1 End.sav");
            string p2 = Path.Combine(_activeDir, "save_Autosave Pontus Turn 21.sav");

            File.WriteAllBytes(p1, header1);
            File.SetLastWriteTime(p1, new DateTime(2026, 1, 1, 10, 0, 0));

            File.WriteAllBytes(p2, header2);
            File.SetLastWriteTime(p2, new DateTime(2026, 1, 2, 15, 0, 0));

            // Act
            var chronicle = _chronicleService.BuildChronicle(faction);

            // Assert
            Assert.Equal("[PublicBETA] RIS 0.7.0", chronicle.ModName);
            Assert.Equal("270 BC", chronicle.StartYear);
            Assert.Equal("265 BC", chronicle.EndYear);
            Assert.Equal(5, chronicle.TotalYearsSpan);
            Assert.Contains("270 BC - 265 BC", chronicle.EraSummary);

            Assert.Equal(2, chronicle.Milestones.Count);
            var m1 = chronicle.Milestones[0];
            var m2 = chronicle.Milestones[1];

            Assert.Equal("Winter 270 BC", m1.InGameDate);
            Assert.Null(m1.DeltaTurns);

            Assert.Equal("Summer 265 BC", m2.InGameDate);
            Assert.Equal(20, m2.DeltaTurns);
            Assert.Equal(5, m2.DeltaYears);

            // Reports
            string md = _chronicleService.GenerateMarkdownReport(chronicle);
            string html = _chronicleService.GenerateHtmlReport(chronicle);

            Assert.Contains("Historical Era", md);
            Assert.Contains("270 BC - 265 BC", md);
            Assert.Contains("+20 turns, +5 yrs", md);

            Assert.Contains("era-badge", html);
            Assert.Contains("mod-badge", html);
            Assert.Contains("date-pill", html);
        }

        [Fact]
        public void SaveMetadataReader_ParsesRealSaveFiles_IfAvailable()
        {
            string realDir = @"C:\Users\eetup.DESKTOP-UM7MOIP\AppData\Local\Feral Interactive\Total War ROME REMASTERED\VFS\Local\Rome\saves";
            if (!Directory.Exists(realDir)) return;

            string sample1 = Path.Combine(realDir, "save_Autosave   Athens   Turn 20.sav");
            if (File.Exists(sample1))
            {
                var meta = SaveMetadataReader.ReadSaveMetadata(sample1);
                Assert.NotNull(meta);
                Assert.Equal(20, meta.TurnNumber);
                Assert.Equal(-266, meta.CalendarYear);
                Assert.Equal("Summer", meta.Season);
                Assert.Equal("Summer 266 BC", meta.InGameDate);
            }

            string sample2 = Path.Combine(realDir, "save_Autosave   Kingdom of Scotland   Turn 6.sav");
            if (File.Exists(sample2))
            {
                var meta = SaveMetadataReader.ReadSaveMetadata(sample2);
                Assert.NotNull(meta);
                Assert.Equal(6, meta.TurnNumber);
                Assert.Equal(1074, meta.CalendarYear);
                Assert.Equal("Summer 1074 AD", meta.InGameDate);
            }
        }
    }
}

