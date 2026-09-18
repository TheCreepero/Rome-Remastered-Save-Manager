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
    }
}

