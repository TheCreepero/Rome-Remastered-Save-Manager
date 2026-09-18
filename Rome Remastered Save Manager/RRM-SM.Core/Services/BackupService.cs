using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RRM_SM.Models;

namespace RRM_SM.Services
{
    public class BackupService
    {
        private readonly AppConfig _config;
        private readonly CampaignParserService _parserService = new();
        private readonly SaveVaultService _vaultService;

        public BackupService(AppConfig config, SaveVaultService? vaultService = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _vaultService = vaultService ?? new SaveVaultService(config, _parserService);
        }

        public CampaignParserService ParserService => _parserService;
        public SaveVaultService VaultService => _vaultService;

        public Dictionary<string, List<CampaignSaveInfo>> GetActiveCampaigns()
        {
            if (string.IsNullOrWhiteSpace(_config.GameSaveDirectory) || !Directory.Exists(_config.GameSaveDirectory))
            {
                return new Dictionary<string, List<CampaignSaveInfo>>(StringComparer.OrdinalIgnoreCase);
            }

            var saveFiles = Directory.GetFiles(_config.GameSaveDirectory, "*.sav", SearchOption.TopDirectoryOnly);
            return _parserService.GroupSaveFiles(saveFiles);
        }

        public string GetMostRecentCampaign()
        {
            var campaigns = GetActiveCampaigns();
            if (campaigns.Count == 0)
            {
                return "General";
            }

            var mostRecent = campaigns
                .SelectMany(kv => kv.Value.Select(s => new { Campaign = kv.Key, s.LastModified }))
                .OrderByDescending(x => x.LastModified)
                .FirstOrDefault();

            return mostRecent?.Campaign ?? campaigns.Keys.First();
        }

        public BackupEntry CreateBackup(string? customName = null, bool isSafetyBackup = false)
        {
            string campaign = GetMostRecentCampaign();
            return CreateCampaignBackup(campaign, customName, isSafetyBackup);
        }

        public BackupEntry CreateCampaignBackup(string campaignName, string? customName = null, bool isSafetyBackup = false)
        {
            if (string.IsNullOrWhiteSpace(_config.GameSaveDirectory) || !Directory.Exists(_config.GameSaveDirectory))
            {
                throw new DirectoryNotFoundException($"Game save directory does not exist: {_config.GameSaveDirectory}");
            }

            var activeCampaigns = GetActiveCampaigns();
            List<CampaignSaveInfo> targetSaves;
            if (activeCampaigns.TryGetValue(campaignName, out var saves) && saves.Count > 0)
            {
                targetSaves = saves;
            }
            else
            {
                var allSaves = Directory.GetFiles(_config.GameSaveDirectory, "*.sav", SearchOption.TopDirectoryOnly);
                targetSaves = allSaves.Select(_parserService.ParseSaveFile).ToList();
            }

            var source = isSafetyBackup ? SaveSourceType.SafetyBackup : SaveSourceType.Manual;
            VaultSaveItem? lastSaved = null;

            foreach (var save in targetSaves)
            {
                if (File.Exists(save.FilePath))
                {
                    lastSaved = _vaultService.AddSaveFile(save.FilePath, campaignName, source, customName);
                }
            }

            if (lastSaved != null)
            {
                return new BackupEntry
                {
                    VaultId = lastSaved.Id,
                    Name = lastSaved.DisplayName,
                    OriginalGameFileName = lastSaved.OriginalGameFileName,
                    FullPath = Path.Combine(_config.BackupDirectory, lastSaved.StoredFileName),
                    CreatedAt = lastSaved.CreatedAt,
                    TotalSizeBytes = lastSaved.FileSizeBytes,
                    FileCount = 1,
                    Type = BackupType.SaveFile,
                    IsSafetyBackup = isSafetyBackup,
                    IsPinned = lastSaved.IsPinned,
                    Turn = lastSaved.Turn,
                    Notes = lastSaved.Notes,
                    Tags = lastSaved.Tags,
                    Source = lastSaved.Source,
                    CampaignId = lastSaved.CampaignId,
                    CampaignName = lastSaved.CampaignName,
                    Faction = lastSaved.Faction
                };
            }

            throw new InvalidOperationException($"No save files found to back up for campaign: {campaignName}");
        }

        public BackupEntry CreateSentinelBackup(string campaignName, IEnumerable<string> changedFilePaths, string? customTag = null)
        {
            var validFiles = changedFilePaths
                .Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (validFiles.Count == 0)
            {
                return CreateCampaignBackup(campaignName, customTag ?? "AutosaveSentinel");
            }

            VaultSaveItem? lastSaved = null;
            foreach (var file in validFiles)
            {
                lastSaved = _vaultService.AddSaveFile(file, campaignName, SaveSourceType.Sentinel, customTag);
            }

            if (lastSaved != null)
            {
                return new BackupEntry
                {
                    VaultId = lastSaved.Id,
                    Name = lastSaved.DisplayName,
                    OriginalGameFileName = lastSaved.OriginalGameFileName,
                    FullPath = Path.Combine(_config.BackupDirectory, lastSaved.StoredFileName),
                    CreatedAt = lastSaved.CreatedAt,
                    TotalSizeBytes = lastSaved.FileSizeBytes,
                    FileCount = validFiles.Count,
                    Type = BackupType.SaveFile,
                    IsSafetyBackup = false,
                    IsPinned = lastSaved.IsPinned,
                    Turn = lastSaved.Turn,
                    Notes = lastSaved.Notes,
                    Tags = lastSaved.Tags,
                    Source = lastSaved.Source,
                    CampaignId = lastSaved.CampaignId,
                    CampaignName = lastSaved.CampaignName,
                    Faction = lastSaved.Faction
                };
            }

            throw new InvalidOperationException($"Failed to save sentinel backup for campaign: {campaignName}");
        }

        public List<BackupEntry> CreateAllCampaignsBackup(string? customName = null, bool isSafetyBackup = false)
        {
            var results = new List<BackupEntry>();
            var active = GetActiveCampaigns();
            if (active.Count == 0)
            {
                results.Add(CreateBackup(customName, isSafetyBackup));
                return results;
            }

            foreach (var campaign in active.Keys)
            {
                results.Add(CreateCampaignBackup(campaign, customName, isSafetyBackup));
            }

            return results;
        }

        public List<BackupEntry> GetBackups()
        {
            return _vaultService.GetBackupsAsEntries();
        }

        public void RestoreBackup(BackupEntry backup, bool createSafetyBackup = true)
        {
            if (!string.IsNullOrWhiteSpace(backup.VaultId))
            {
                var item = _vaultService.GetSaveById(backup.VaultId);
                if (item != null)
                {
                    _vaultService.RestoreSave(item, createSafetyBackup);
                    return;
                }
            }

            // Fallback for direct file restore if not found in vault manifest
            if (File.Exists(backup.FullPath))
            {
                string destName = !string.IsNullOrWhiteSpace(backup.OriginalGameFileName)
                    ? backup.OriginalGameFileName
                    : Path.GetFileName(backup.FullPath);

                string targetGamePath = Path.Combine(_config.GameSaveDirectory, destName);
                if (createSafetyBackup && File.Exists(targetGamePath))
                {
                    try
                    {
                        _vaultService.AddSaveFile(targetGamePath, backup.CampaignName, SaveSourceType.SafetyBackup, "Pre-Restore Safety");
                    }
                    catch { }
                }

                File.Copy(backup.FullPath, targetGamePath, overwrite: true);
                return;
            }

            if (Directory.Exists(backup.FullPath))
            {
                CopyDirectoryRecursive(backup.FullPath, _config.GameSaveDirectory);
            }
        }

        public void DeleteBackup(BackupEntry backup)
        {
            if (!string.IsNullOrWhiteSpace(backup.VaultId))
            {
                _vaultService.DeleteSave(backup.VaultId);
                return;
            }

            if (File.Exists(backup.FullPath))
            {
                try { File.Delete(backup.FullPath); } catch { }
            }
            else if (Directory.Exists(backup.FullPath))
            {
                try { Directory.Delete(backup.FullPath, true); } catch { }
            }
        }

        public void EnforceRetentionLimit(string? specificCampaign = null)
        {
            _vaultService.EnforceRetentionLimit(specificCampaign);
        }

        public static string SanitizeFileName(string name)
        {
            var invalids = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Select(c => invalids.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Unnamed" : cleaned.Trim();
        }

        public static (int FileCount, long TotalBytes) CalculateDirectoryStats(string path)
        {
            if (!Directory.Exists(path))
                return (0, 0);

            var dir = new DirectoryInfo(path);
            var files = dir.GetFiles("*", SearchOption.AllDirectories);
            long totalBytes = files.Sum(f => f.Length);
            return (files.Length, totalBytes);
        }

        public static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destDir = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectoryRecursive(dir, destDir);
            }
        }
    }
}
