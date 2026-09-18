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

        public BackupService(AppConfig config)
        {
            _config = config;
        }

        public BackupEntry CreateBackup(string? customName = null, bool isSafetyBackup = false)
        {
            if (string.IsNullOrWhiteSpace(_config.GameSaveDirectory) || !Directory.Exists(_config.GameSaveDirectory))
            {
                throw new DirectoryNotFoundException($"Game save directory does not exist: {_config.GameSaveDirectory}");
            }

            if (!Directory.Exists(_config.BackupDirectory))
            {
                Directory.CreateDirectory(_config.BackupDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string prefix = isSafetyBackup ? "SafetyBackup_PreRestore" : "Backup";
            string folderName;

            if (!string.IsNullOrWhiteSpace(customName))
            {
                string safeCustomName = SanitizeFileName(customName);
                folderName = $"{prefix}_{timestamp}_{safeCustomName}";
            }
            else
            {
                folderName = $"{prefix}_{timestamp}";
            }

            if (_config.CompressBackups)
            {
                string zipPath = Path.Combine(_config.BackupDirectory, folderName + ".zip");
                ZipFile.CreateFromDirectory(_config.GameSaveDirectory, zipPath, CompressionLevel.Optimal, false);

                var fileInfo = new FileInfo(zipPath);
                int count = 0;
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    count = archive.Entries.Count;
                }

                EnforceRetentionLimit();

                return new BackupEntry
                {
                    Name = folderName + ".zip",
                    FullPath = zipPath,
                    CreatedAt = fileInfo.CreationTime,
                    TotalSizeBytes = fileInfo.Length,
                    FileCount = count,
                    Type = BackupType.ZipArchive,
                    IsSafetyBackup = isSafetyBackup
                };
            }
            else
            {
                string destinationDir = Path.Combine(_config.BackupDirectory, folderName);
                CopyDirectoryRecursive(_config.GameSaveDirectory, destinationDir);

                var (fileCount, totalBytes) = CalculateDirectoryStats(destinationDir);

                EnforceRetentionLimit();

                return new BackupEntry
                {
                    Name = folderName,
                    FullPath = destinationDir,
                    CreatedAt = DateTime.Now,
                    TotalSizeBytes = totalBytes,
                    FileCount = fileCount,
                    Type = BackupType.Directory,
                    IsSafetyBackup = isSafetyBackup
                };
            }
        }

        public List<BackupEntry> GetBackups()
        {
            var backups = new List<BackupEntry>();

            if (!Directory.Exists(_config.BackupDirectory))
            {
                return backups;
            }

            // Folder-based snapshots
            foreach (string dir in Directory.GetDirectories(_config.BackupDirectory))
            {
                var dirInfo = new DirectoryInfo(dir);
                if (dirInfo.Name.StartsWith("Backup_") || dirInfo.Name.StartsWith("SafetyBackup_"))
                {
                    var (count, size) = CalculateDirectoryStats(dir);
                    backups.Add(new BackupEntry
                    {
                        Name = dirInfo.Name,
                        FullPath = dir,
                        CreatedAt = dirInfo.CreationTime,
                        TotalSizeBytes = size,
                        FileCount = count,
                        Type = BackupType.Directory,
                        IsSafetyBackup = dirInfo.Name.StartsWith("SafetyBackup_")
                    });
                }
            }

            // Zip-based snapshots
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
                    catch
                    {
                        // In case of corrupt zip
                    }

                    backups.Add(new BackupEntry
                    {
                        Name = fileInfo.Name,
                        FullPath = file,
                        CreatedAt = fileInfo.CreationTime,
                        TotalSizeBytes = fileInfo.Length,
                        FileCount = count,
                        Type = BackupType.ZipArchive,
                        IsSafetyBackup = fileInfo.Name.StartsWith("SafetyBackup_")
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
                var sourceFiles = Directory.GetFiles(_config.GameSaveDirectory, "*", SearchOption.AllDirectories);
                if (sourceFiles.Length > 0)
                {
                    CreateBackup(null, isSafetyBackup: true);
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
            if (backup.Type == BackupType.ZipArchive && File.Exists(backup.FullPath))
            {
                File.Delete(backup.FullPath);
            }
            else if (backup.Type == BackupType.Directory && Directory.Exists(backup.FullPath))
            {
                Directory.Delete(backup.FullPath, true);
            }
        }

        public void EnforceRetentionLimit()
        {
            if (_config.MaxBackupsToKeep <= 0)
                return;

            var userBackups = GetBackups().Where(b => !b.IsSafetyBackup).ToList();
            if (userBackups.Count > _config.MaxBackupsToKeep)
            {
                var toRemove = userBackups.Skip(_config.MaxBackupsToKeep);
                foreach (var old in toRemove)
                {
                    DeleteBackup(old);
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

