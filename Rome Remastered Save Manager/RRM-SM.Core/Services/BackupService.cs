using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RRM_SM.Models;

namespace RRM_SM.Services
{
    public class BackupService
    {
        private readonly AppConfig _config;
        private readonly CampaignParserService _parserService = new();

        public BackupService(AppConfig config)
        {
            _config = config;
        }

        public CampaignParserService ParserService => _parserService;

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

            if (!Directory.Exists(_config.BackupDirectory))
            {
                Directory.CreateDirectory(_config.BackupDirectory);
            }

            string cleanCampaign = CampaignParserService.CleanFactionName(campaignName);
            string campaignTargetDir = Path.Combine(_config.BackupDirectory, cleanCampaign);
            if (!Directory.Exists(campaignTargetDir))
            {
                Directory.CreateDirectory(campaignTargetDir);
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string prefix = isSafetyBackup ? "SafetyBackup_PreRestore" : "Backup";
            string baseFolderName;

            if (!string.IsNullOrWhiteSpace(customName))
            {
                string safeCustomName = SanitizeFileName(customName);
                baseFolderName = $"{prefix}_{timestamp}_{safeCustomName}";
            }
            else
            {
                baseFolderName = $"{prefix}_{timestamp}";
            }

            // Get save files belonging to this campaign
            var activeCampaigns = GetActiveCampaigns();
            List<CampaignSaveInfo> targetSaves;
            if (activeCampaigns.TryGetValue(campaignName, out var saves) && saves.Count > 0)
            {
                targetSaves = saves;
            }
            else
            {
                // Fallback: all saves in root directory
                var allSaves = Directory.GetFiles(_config.GameSaveDirectory, "*.sav", SearchOption.TopDirectoryOnly);
                targetSaves = allSaves.Select(_parserService.ParseSaveFile).ToList();
            }

            if (_config.CompressBackups)
            {
                string zipPath = Path.Combine(campaignTargetDir, baseFolderName + ".zip");
                using (var zipStream = new FileStream(zipPath, FileMode.Create))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (var save in targetSaves)
                    {
                        if (File.Exists(save.FilePath))
                        {
                            archive.CreateEntryFromFile(save.FilePath, Path.GetFileName(save.FilePath));
                        }
                    }
                }

                var fileInfo = new FileInfo(zipPath);
                int count = 0;
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    count = archive.Entries.Count;
                }

                EnforceRetentionLimit(cleanCampaign);

                return new BackupEntry
                {
                    Name = baseFolderName + ".zip",
                    FullPath = zipPath,
                    CreatedAt = fileInfo.CreationTime,
                    TotalSizeBytes = fileInfo.Length,
                    FileCount = count,
                    Type = BackupType.ZipArchive,
                    IsSafetyBackup = isSafetyBackup,
                    CampaignName = cleanCampaign
                };
            }
            else
            {
                string destinationDir = Path.Combine(campaignTargetDir, baseFolderName);
                if (!Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                foreach (var save in targetSaves)
                {
                    if (File.Exists(save.FilePath))
                    {
                        string destFile = Path.Combine(destinationDir, Path.GetFileName(save.FilePath));
                        File.Copy(save.FilePath, destFile, true);
                    }
                }

                var (fileCount, totalBytes) = CalculateDirectoryStats(destinationDir);

                EnforceRetentionLimit(cleanCampaign);

                return new BackupEntry
                {
                    Name = baseFolderName,
                    FullPath = destinationDir,
                    CreatedAt = DateTime.Now,
                    TotalSizeBytes = totalBytes,
                    FileCount = fileCount,
                    Type = BackupType.Directory,
                    IsSafetyBackup = isSafetyBackup,
                    CampaignName = cleanCampaign
                };
            }
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
            var backups = new List<BackupEntry>();

            if (!Directory.Exists(_config.BackupDirectory))
            {
                return backups;
            }

            // 1. Scan campaign subdirectories (<BackupDirectory>/<CampaignName>/...)
            foreach (string subDir in Directory.GetDirectories(_config.BackupDirectory))
            {
                var dirInfo = new DirectoryInfo(subDir);
                // If this is a direct backup folder at the root level (legacy)
                if (dirInfo.Name.StartsWith("Backup_") || dirInfo.Name.StartsWith("SafetyBackup_"))
                {
                    var (count, size) = CalculateDirectoryStats(subDir);
                    backups.Add(new BackupEntry
                    {
                        Name = dirInfo.Name,
                        FullPath = subDir,
                        CreatedAt = dirInfo.CreationTime,
                        TotalSizeBytes = size,
                        FileCount = count,
                        Type = BackupType.Directory,
                        IsSafetyBackup = dirInfo.Name.StartsWith("SafetyBackup_"),
                        CampaignName = "General"
                    });
                }
                else
                {
                    // This is a campaign folder! Scan inside for snapshots
                    string campaignName = dirInfo.Name;

                    foreach (string childDir in Directory.GetDirectories(subDir))
                    {
                        var childDirInfo = new DirectoryInfo(childDir);
                        if (childDirInfo.Name.StartsWith("Backup_") || childDirInfo.Name.StartsWith("SafetyBackup_"))
                        {
                            var (count, size) = CalculateDirectoryStats(childDir);
                            backups.Add(new BackupEntry
                            {
                                Name = childDirInfo.Name,
                                FullPath = childDir,
                                CreatedAt = childDirInfo.CreationTime,
                                TotalSizeBytes = size,
                                FileCount = count,
                                Type = BackupType.Directory,
                                IsSafetyBackup = childDirInfo.Name.StartsWith("SafetyBackup_"),
                                CampaignName = campaignName
                            });
                        }
                    }

                    foreach (string zipFile in Directory.GetFiles(subDir, "*.zip"))
                    {
                        var zipInfo = new FileInfo(zipFile);
                        if (zipInfo.Name.StartsWith("Backup_") || zipInfo.Name.StartsWith("SafetyBackup_"))
                        {
                            int count = 0;
                            try
                            {
                                using var archive = ZipFile.OpenRead(zipFile);
                                count = archive.Entries.Count;
                            }
                            catch { }

                            backups.Add(new BackupEntry
                            {
                                Name = zipInfo.Name,
                                FullPath = zipFile,
                                CreatedAt = zipInfo.CreationTime,
                                TotalSizeBytes = zipInfo.Length,
                                FileCount = count,
                                Type = BackupType.ZipArchive,
                                IsSafetyBackup = zipInfo.Name.StartsWith("SafetyBackup_"),
                                CampaignName = campaignName
                            });
                        }
                    }
                }
            }

            // 2. Scan root-level zip archives (legacy)
            foreach (string file in Directory.GetFiles(_config.BackupDirectory, "*.zip"))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Name.StartsWith("Backup_") || fileInfo.Name.StartsWith("SafetyBackup_"))
                {
                    int count = 0;
                    try
                    {
                        using var archive = ZipFile.OpenRead(file);
                        count = archive.Entries.Count;
                    }
                    catch { }

                    backups.Add(new BackupEntry
                    {
                        Name = fileInfo.Name,
                        FullPath = file,
                        CreatedAt = fileInfo.CreationTime,
                        TotalSizeBytes = fileInfo.Length,
                        FileCount = count,
                        Type = BackupType.ZipArchive,
                        IsSafetyBackup = fileInfo.Name.StartsWith("SafetyBackup_"),
                        CampaignName = "General"
                    });
                }
            }

            return backups.OrderByDescending(b => b.CreatedAt).ToList();
        }

        public void RestoreBackup(BackupEntry backup, bool createSafetyBackup = true)
        {
            if (string.IsNullOrWhiteSpace(_config.GameSaveDirectory))
            {
                throw new InvalidOperationException("Game save directory is not configured.");
            }

            // 1. Create a safety backup of existing saves before overwriting
            if (createSafetyBackup && Directory.Exists(_config.GameSaveDirectory))
            {
                var sourceFiles = Directory.GetFiles(_config.GameSaveDirectory, "*.sav", SearchOption.TopDirectoryOnly);
                if (sourceFiles.Length > 0)
                {
                    // Create safety backup for the campaign being restored
                    CreateCampaignBackup(backup.CampaignName, null, isSafetyBackup: true);
                }
            }

            // 2. Ensure target game save directory exists
            if (!Directory.Exists(_config.GameSaveDirectory))
            {
                Directory.CreateDirectory(_config.GameSaveDirectory);
            }

            // 3. Restore files
            if (backup.Type == BackupType.ZipArchive)
            {
                ZipFile.ExtractToDirectory(backup.FullPath, _config.GameSaveDirectory, overwriteFiles: true);
            }
            else
            {
                CopyDirectoryRecursive(backup.FullPath, _config.GameSaveDirectory);
            }
        }

        public void DeleteBackup(BackupEntry backup)
        {
            string? parentDir = null;

            if (backup.Type == BackupType.ZipArchive && File.Exists(backup.FullPath))
            {
                parentDir = Path.GetDirectoryName(backup.FullPath);
                File.Delete(backup.FullPath);
            }
            else if (backup.Type == BackupType.Directory && Directory.Exists(backup.FullPath))
            {
                parentDir = Path.GetDirectoryName(backup.FullPath);
                Directory.Delete(backup.FullPath, true);
            }

            // Clean up empty campaign folder if it has no more snapshots
            if (!string.IsNullOrEmpty(parentDir) &&
                !parentDir.Equals(_config.BackupDirectory, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(parentDir))
            {
                bool hasFiles = Directory.EnumerateFileSystemEntries(parentDir).Any();
                if (!hasFiles)
                {
                    try { Directory.Delete(parentDir); } catch { }
                }
            }
        }

        public void EnforceRetentionLimit(string? specificCampaign = null)
        {
            if (_config.MaxBackupsToKeep <= 0)
                return;

            var allBackups = GetBackups().Where(b => !b.IsSafetyBackup).ToList();

            if (!string.IsNullOrWhiteSpace(specificCampaign))
            {
                var campaignBackups = allBackups
                    .Where(b => b.CampaignName.Equals(specificCampaign, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(b => b.CreatedAt)
                    .ToList();

                if (campaignBackups.Count > _config.MaxBackupsToKeep)
                {
                    var toRemove = campaignBackups.Skip(_config.MaxBackupsToKeep);
                    foreach (var old in toRemove)
                    {
                        DeleteBackup(old);
                    }
                }
            }
            else
            {
                // Enforce across each campaign group independently
                var grouped = allBackups.GroupBy(b => b.CampaignName, StringComparer.OrdinalIgnoreCase);
                foreach (var group in grouped)
                {
                    var sorted = group.OrderByDescending(b => b.CreatedAt).ToList();
                    if (sorted.Count > _config.MaxBackupsToKeep)
                    {
                        var toRemove = sorted.Skip(_config.MaxBackupsToKeep);
                        foreach (var old in toRemove)
                        {
                            DeleteBackup(old);
                        }
                    }
                }
            }
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

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return sanitized.Trim();
        }
    }
}
