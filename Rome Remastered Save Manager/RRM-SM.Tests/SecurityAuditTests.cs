using System;
using System.IO;
using System.Text;
using RRM_SM.Models;
using RRM_SM.Core.Models;
using RRM_SM.Services;
using Xunit;

namespace RRM_SM.Tests
{
    public class SecurityAuditTests : IDisposable
    {
        private readonly string _tempTestDir;
        private readonly string _gameSaveDir;
        private readonly string _backupDir;
        private readonly AppConfig _config;
        private readonly CampaignParserService _parser;

        public SecurityAuditTests()
        {
            _tempTestDir = Path.Combine(Path.GetTempPath(), "RRM_SM_SecurityTests_" + Guid.NewGuid().ToString("N"));
            _gameSaveDir = Path.Combine(_tempTestDir, "GameSaves");
            _backupDir = Path.Combine(_tempTestDir, "Backups");

            Directory.CreateDirectory(_gameSaveDir);
            Directory.CreateDirectory(_backupDir);

            _config = new AppConfig
            {
                GameSaveDirectory = _gameSaveDir,
                BackupDirectory = _backupDir,
                MaxBackupsToKeep = 0,
                MaxSentinelBackupsToKeep = 0
            };

            _parser = new CampaignParserService();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempTestDir))
                {
                    Directory.Delete(_tempTestDir, recursive: true);
                }
            }
            catch { }
        }

        [Theory]
        [InlineData("../../../Windows/System32/malicious.sav")]
        [InlineData("..\\..\\..\\malicious.sav")]
        [InlineData("C:\\Windows\\System32\\calc.sav")]
        [InlineData("/etc/passwd")]
        public void PathTraversal_RestoreSave_DoesNotEscapeGameSaveDirectory(string maliciousOriginalName)
        {
            var vault = new SaveVaultService(_config, _parser);

            // Create a valid source file in game dir
            string sourceFile = Path.Combine(_gameSaveDir, "save_Rome.sav");
            File.WriteAllText(sourceFile, "Safe Rome Content");
            var item = vault.AddSaveFile(sourceFile, "Rome");

            // Manually inject a malicious traversal name into the item metadata
            item.OriginalGameFileName = maliciousOriginalName;

            // Restoring MUST throw or sanitize so it never writes outside _gameSaveDir
            try
            {
                vault.RestoreSave(item, createSafetyBackup: false);
            }
            catch (Exception ex)
            {
                // Throwing an exception (e.g. UnauthorizedAccessException or ArgumentException) is secure
                Assert.True(ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException);
            }

            // Verify no file was created outside _gameSaveDir in _tempTestDir root or parent
            string outsidePath1 = Path.GetFullPath(Path.Combine(_gameSaveDir, maliciousOriginalName));
            if (!outsidePath1.StartsWith(_gameSaveDir, StringComparison.OrdinalIgnoreCase))
            {
                Assert.False(File.Exists(outsidePath1), $"Path traversal succeeded! File written to: {outsidePath1}");
            }
        }

        [Theory]
        [InlineData("../../../evil.sav")]
        [InlineData("..\\..\\outside.sav")]
        public void PathTraversal_DeleteSave_DoesNotDeleteOutsideBackupDirectory(string maliciousStoredFileName)
        {
            var vault = new SaveVaultService(_config, _parser);

            // Create a canary file outside backup dir that must NOT be deleted
            string canaryFile = Path.Combine(_tempTestDir, "CANARY_DO_NOT_DELETE.txt");
            File.WriteAllText(canaryFile, "Critical System File");

            // Mock a save item pointing outside
            var item = new VaultSaveItem
            {
                Id = "traversal_test_id",
                StoredFileName = maliciousStoredFileName,
                OriginalGameFileName = "canary.sav"
            };

            // Attempting to delete must safely fail or refuse to delete outside _backupDir
            try
            {
                vault.DeleteSave(item.Id);
            }
            catch { }

            // Canary file must still exist untouched
            Assert.True(File.Exists(canaryFile), "Path traversal vulnerability! File outside backup dir was deleted!");
        }

        [Fact]
        public void ReDoS_ExtremelyLongMalformedFilename_CompletesWithinTimeout()
        {
            // Crafted string designed to stress regex matching without crashing or hanging
            string evilLongString = "save_Autosave   " + new string('A', 5000) + "   Turn " + new string('9', 500) + " Start.sav";

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var info = _parser.ParseSaveFile(evilLongString);
            sw.Stop();

            // Parser should complete well under 2 seconds even for extreme input
            Assert.True(sw.ElapsedMilliseconds < 2000, $"ReDoS detected! Parser took {sw.ElapsedMilliseconds} ms to process long string.");
            Assert.NotNull(info);
        }

        [Fact]
        public void HeaderParser_CorruptedAndMalformedPayloads_HandledGracefully()
        {
            // Test 0-byte file
            string emptyFile = Path.Combine(_gameSaveDir, "empty.sav");
            File.WriteAllBytes(emptyFile, Array.Empty<byte>());
            Assert.Null(CampaignParserService.TryReadInternalCampaignGuid(emptyFile));

            // Test 35-byte file (just before the 36-byte offset)
            string shortFile = Path.Combine(_gameSaveDir, "short_35.sav");
            File.WriteAllBytes(shortFile, new byte[35]);
            Assert.Null(CampaignParserService.TryReadInternalCampaignGuid(shortFile));

            // Test 51-byte file (1 byte short of full Guid)
            string partialFile = Path.Combine(_gameSaveDir, "partial_51.sav");
            File.WriteAllBytes(partialFile, new byte[51]);
            Assert.Null(CampaignParserService.TryReadInternalCampaignGuid(partialFile));

            // Test 100-byte file with Guid.Empty (all zeroes)
            string zeroGuidFile = Path.Combine(_gameSaveDir, "zero_guid.sav");
            File.WriteAllBytes(zeroGuidFile, new byte[100]);
            Assert.Null(CampaignParserService.TryReadInternalCampaignGuid(zeroGuidFile));
        }

        [Fact]
        public void XssPrevention_HtmlReportExport_ProperlyEncodesAllDynamicContent()
        {
            var vault = new SaveVaultService(_config, _parser);
            var chronicleService = new ChronicleService(_config, _parser, vault);
            var chronicle = new CampaignChronicle
            {
                CampaignName = "<script>alert('xss_campaign')</script>",
                CampaignSummary = "<img src=x onerror=alert('xss_summary')>",
                Milestones = new System.Collections.Generic.List<ChronicleMilestone>
                {
                    new ChronicleMilestone
                    {
                        Turn = 1,
                        Title = "<svg/onload=alert('xss_title')>",
                        PlayerNotes = "<script>document.location='http://evil.com'</script>",
                        Tags = new System.Collections.Generic.List<string> { "<b onmouseover=alert('xss_tag')>tag</b>" }
                    }
                }
            };

            string html = chronicleService.GenerateHtmlReport(chronicle);

            // Verify raw dangerous HTML elements are NOT present (must not contain unescaped tags)
            Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<svg", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<b onmouseover", html, StringComparison.OrdinalIgnoreCase);

            // Verify they are safely HTML encoded
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("&lt;img src=x onerror=", html);
            Assert.Contains("&lt;svg/onload=", html);
            Assert.Contains("&lt;b onmouseover=", html);
        }
    }
}
