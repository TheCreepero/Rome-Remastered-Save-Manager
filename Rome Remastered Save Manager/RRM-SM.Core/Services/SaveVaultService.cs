using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using RRM_SM.Models;
using RRM_SM.Core.Models;

namespace RRM_SM.Services
{
    public class SaveVaultService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly AppConfig _config;
        private readonly CampaignParserService _parserService;
        private readonly string _manifestPath;
        private readonly object _lock = new();
        private SaveVaultManifest _manifest = new();

        public SaveVaultService(AppConfig config, CampaignParserService? parserService = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _parserService = parserService ?? new CampaignParserService();
            _manifestPath = Path.Combine(_config.BackupDirectory, "vault.json");

            InitializeVault();
        }

        public string VaultDirectory => _config.BackupDirectory;
        public string ManifestPath => _manifestPath;
        public SaveVaultManifest Manifest
        {
            get
            {
                lock (_lock)
                {
                    return _manifest;
                }
            }
        }

        public void InitializeVault()
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(_config.BackupDirectory)) return;

                if (!Directory.Exists(_config.BackupDirectory))
                {
                    Directory.CreateDirectory(_config.BackupDirectory);
                }

                LoadOrRebuildManifest();
            }
        }

        public bool HasLegacyBackups()
        {
            if (string.IsNullOrWhiteSpace(_config.BackupDirectory) || !Directory.Exists(_config.BackupDirectory))
                return false;

            return Directory.GetDirectories(_config.BackupDirectory).Length > 0;
        }

        public IReadOnlyList<VaultSaveItem> GetAllSaves()
        {
            lock (_lock)
            {
                return _manifest.Saves
                    .OrderByDescending(s => s.CreatedAt)
                    .ToList();
            }
        }

        public IReadOnlyList<VaultSaveItem> GetCampaignSaves(string campaignName)
        {
            lock (_lock)
            {
                return _manifest.Saves
                    .Where(s => s.CampaignName.Equals(campaignName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(s => s.CreatedAt)
                    .ToList();
            }
        }

        public VaultSaveItem? GetSaveById(string id)
        {
            lock (_lock)
            {
                return _manifest.Saves.FirstOrDefault(s => s.Id == id);
            }
        }

        /// <summary>
        /// Adds an individual save file into the flat vault, handling SHA-256 deduplication and collision resolution.
        /// </summary>
        public VaultSaveItem AddSaveFile(
            string sourceFilePath,
            string? campaignName = null,
            SaveSourceType source = SaveSourceType.Manual,
            string? customTitle = null,
            string? notes = null,
            IEnumerable<string>? tags = null,
            bool moveFile = false,
            bool saveManifestImmediately = true)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                throw new FileNotFoundException($"Source save file not found: {sourceFilePath}");
            }

            lock (_lock)
            {
                if (!Directory.Exists(_config.BackupDirectory))
                {
                    Directory.CreateDirectory(_config.BackupDirectory);
                }

                string hash = ComputeSha256(sourceFilePath);
                var fileInfo = new FileInfo(sourceFilePath);
                long fileSize = fileInfo.Length;
                string originalGameFileName = Path.GetFileName(sourceFilePath);

                // Parse save metadata
                var saveInfo = _parserService.ParseSaveFile(sourceFilePath);
                string resolvedCampaign = !string.IsNullOrWhiteSpace(campaignName) 
                    ? campaignName 
                    : (!string.IsNullOrWhiteSpace(saveInfo.FactionName) ? saveInfo.FactionName : "General");

                // 1. De-duplication check: if a save with identical SHA-256 hash already exists in the vault
                var existingSameHash = _manifest.Saves.FirstOrDefault(s => 
                    s.Sha256Hash.Equals(hash, StringComparison.OrdinalIgnoreCase) &&
                    s.CampaignName.Equals(resolvedCampaign, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(_config.BackupDirectory, s.StoredFileName)));

                if (existingSameHash != null)
                {
                    // If moving, delete the redundant source file since it is already preserved in vault
                    if (moveFile)
                    {
                        try { File.Delete(sourceFilePath); } catch { }
                    }

                    // If it's an automated Sentinel trigger and identical content is already saved, return existing!
                    if (source == SaveSourceType.Sentinel)
                    {
                        return existingSameHash;
                    }

                    // For manual saves, update metadata if new title/notes were provided
                    if (!string.IsNullOrWhiteSpace(customTitle)) existingSameHash.CustomTitle = customTitle;
                    if (!string.IsNullOrWhiteSpace(notes)) existingSameHash.Notes = notes;
                    if (tags != null) existingSameHash.Tags = tags.Distinct().ToList();
                    if (saveManifestImmediately) SaveManifest();
                    return existingSameHash;
                }

                // 2. Resolve destination file name in flat vault
                string storedFileName = ResolveDestinationFileName(originalGameFileName, hash, saveInfo.Turn);
                string destinationPath = Path.Combine(_config.BackupDirectory, storedFileName);

                // Move or copy file
                if (moveFile)
                {
                    if (File.Exists(destinationPath))
                    {
                        try { File.Delete(destinationPath); } catch { }
                    }
                    File.Move(sourceFilePath, destinationPath);
                }
                else
                {
                    File.Copy(sourceFilePath, destinationPath, overwrite: true);
                }

                var newItem = new VaultSaveItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    StoredFileName = storedFileName,
                    OriginalGameFileName = originalGameFileName,
                    CampaignName = resolvedCampaign,
                    Turn = saveInfo.Turn,
                    SaveType = saveInfo.Type,
                    Source = source,
                    FileSizeBytes = fileSize,
                    Sha256Hash = hash,
                    CreatedAt = DateTime.Now,
                    LastModified = fileInfo.LastWriteTime,
                    CustomTitle = customTitle,
                    Notes = notes,
                    Tags = tags?.Distinct().ToList() ?? new List<string>(),
                    IsPinned = false
                };

                _manifest.Saves.Add(newItem);
                EnforceRetentionLimit(resolvedCampaign);
                if (saveManifestImmediately) SaveManifest();

                return newItem;
            }
        }

        public List<VaultSaveItem> AddMultipleSaves(
            IEnumerable<string> filePaths,
            string? campaignName = null,
            SaveSourceType source = SaveSourceType.Manual,
            string? customTag = null)
        {
            var results = new List<VaultSaveItem>();
            foreach (var file in filePaths)
            {
                if (File.Exists(file))
                {
                    results.Add(AddSaveFile(file, campaignName, source, customTag));
                }
            }
            return results;
        }

        /// <summary>
        /// Restores a save from the vault back into the active game save folder under its original game file name.
        /// </summary>
        public void RestoreSave(VaultSaveItem item, bool createSafetyBackup = true)
        {
            if (string.IsNullOrWhiteSpace(_config.GameSaveDirectory))
            {
                throw new InvalidOperationException("Game save directory is not configured.");
            }

            lock (_lock)
            {
                string vaultFilePath = Path.Combine(_config.BackupDirectory, item.StoredFileName);
                if (!File.Exists(vaultFilePath))
                {
                    throw new FileNotFoundException($"Vault save file missing from disk: {vaultFilePath}");
                }

                if (!Directory.Exists(_config.GameSaveDirectory))
                {
                    Directory.CreateDirectory(_config.GameSaveDirectory);
                }

                string targetGamePath = Path.Combine(_config.GameSaveDirectory, item.OriginalGameFileName);

                // 1. Safety backup of the existing save before overwriting
                if (createSafetyBackup && File.Exists(targetGamePath))
                {
                    try
                    {
                        AddSaveFile(
                            targetGamePath,
                            item.CampaignName,
                            SaveSourceType.SafetyBackup,
                            $"Pre-Restore Safety: {item.OriginalGameFileName}");
                    }
                    catch
                    {
                        // Proceed even if safety backup fails
                    }
                }

                // 2. Restore file
                File.Copy(vaultFilePath, targetGamePath, overwrite: true);
            }
        }

        public void RestoreSave(string id, bool createSafetyBackup = true)
        {
            var item = GetSaveById(id);
            if (item == null)
            {
                throw new ArgumentException($"Save with id {id} not found in vault.");
            }
            RestoreSave(item, createSafetyBackup);
        }

        public void DeleteSave(string id)
        {
            lock (_lock)
            {
                var item = _manifest.Saves.FirstOrDefault(s => s.Id == id);
                if (item == null) return;

                string filePath = Path.Combine(_config.BackupDirectory, item.StoredFileName);
                if (File.Exists(filePath))
                {
                    try { File.Delete(filePath); } catch { }
                }

                _manifest.Saves.Remove(item);
                SaveManifest();
            }
        }

        public bool TogglePin(string id)
        {
            lock (_lock)
            {
                var item = _manifest.Saves.FirstOrDefault(s => s.Id == id);
                if (item == null) return false;

                item.IsPinned = !item.IsPinned;
                SaveManifest();
                return item.IsPinned;
            }
        }

        public void UpdateMetadata(string id, string? customTitle = null, string? notes = null, IEnumerable<string>? tags = null, bool? isPinned = null)
        {
            lock (_lock)
            {
                var item = _manifest.Saves.FirstOrDefault(s => s.Id == id);
                if (item == null) return;

                if (customTitle != null) item.CustomTitle = customTitle;
                if (notes != null) item.Notes = notes;
                if (tags != null) item.Tags = tags.Distinct().ToList();
                if (isPinned.HasValue) item.IsPinned = isPinned.Value;

                SaveManifest();
            }
        }

        public void RebuildIndexFromDisk()
        {
            lock (_lock)
            {
                _manifest = new SaveVaultManifest();
                LoadOrRebuildManifest();
            }
        }

        public void EnforceRetentionLimit(string? specificCampaign = null)
        {
            lock (_lock)
            {
                if (_config.MaxBackupsToKeep <= 0 && _config.MaxSentinelBackupsToKeep <= 0)
                    return;

                void PruneStream(IEnumerable<VaultSaveItem> stream, int limit)
                {
                    if (limit <= 0) return;
                    // Never prune pinned saves!
                    var candidates = stream.Where(s => !s.IsPinned && s.Source != SaveSourceType.SafetyBackup)
                        .OrderByDescending(s => s.CreatedAt)
                        .ToList();

                    if (candidates.Count > limit)
                    {
                        var toRemove = candidates.Skip(limit).ToList();
                        foreach (var old in toRemove)
                        {
                            DeleteSave(old.Id);
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(specificCampaign))
                {
                    var campaignSaves = _manifest.Saves
                        .Where(s => s.CampaignName.Equals(specificCampaign, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (_config.MaxBackupsToKeep > 0)
                    {
                        PruneStream(campaignSaves.Where(s => s.Source == SaveSourceType.Manual), _config.MaxBackupsToKeep);
                    }

                    if (_config.MaxSentinelBackupsToKeep > 0)
                    {
                        PruneStream(campaignSaves.Where(s => s.Source == SaveSourceType.Sentinel), _config.MaxSentinelBackupsToKeep);
                    }
                }
                else
                {
                    var groups = _manifest.Saves.GroupBy(s => s.CampaignName, StringComparer.OrdinalIgnoreCase);
                    foreach (var g in groups)
                    {
                        if (_config.MaxBackupsToKeep > 0)
                        {
                            PruneStream(g.Where(s => s.Source == SaveSourceType.Manual), _config.MaxBackupsToKeep);
                        }

                        if (_config.MaxSentinelBackupsToKeep > 0)
                        {
                            PruneStream(g.Where(s => s.Source == SaveSourceType.Sentinel), _config.MaxSentinelBackupsToKeep);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Discovers and migrates legacy nested backup subdirectories into the flat vault structure.
        /// </summary>
        public int MigrateLegacyBackups()
        {
            if (string.IsNullOrWhiteSpace(_config.BackupDirectory) || !Directory.Exists(_config.BackupDirectory))
                return 0;

            var subDirs = Directory.GetDirectories(_config.BackupDirectory);
            if (subDirs.Length == 0) return 0;

            int migratedCount = 0;

            foreach (var subDir in subDirs)
            {
                var dirInfo = new DirectoryInfo(subDir);

                // Collect all .sav files inside this subfolder hierarchy
                var savFiles = Directory.GetFiles(subDir, "*.sav", SearchOption.AllDirectories);
                foreach (var savFile in savFiles)
                {
                    try
                    {
                        string sourceDirName = Path.GetFileName(Path.GetDirectoryName(savFile) ?? "");
                        SaveSourceType source = SaveSourceType.Manual;
                        if (sourceDirName.Contains("Sentinel", StringComparison.OrdinalIgnoreCase))
                        {
                            source = SaveSourceType.Sentinel;
                        }
                        else if (sourceDirName.Contains("Safety", StringComparison.OrdinalIgnoreCase))
                        {
                            source = SaveSourceType.SafetyBackup;
                        }

                        AddSaveFile(
                            savFile, 
                            dirInfo.Name, 
                            source, 
                            sourceDirName, 
                            moveFile: true, 
                            saveManifestImmediately: false);
                        migratedCount++;
                    }
                    catch
                    {
                        // Ignore individual file migration errors
                    }
                }

                // Check for chronicle.json in campaign directory to salvage notes
                string chroniclePath = Path.Combine(subDir, "chronicle.json");
                if (File.Exists(chroniclePath))
                {
                    try
                    {
                        string json = File.ReadAllText(chroniclePath);
                        var chronicle = JsonSerializer.Deserialize<CampaignChronicle>(json);
                        if (chronicle?.Milestones != null)
                        {
                            foreach (var milestone in chronicle.Milestones)
                            {
                                var matching = _manifest.Saves.FirstOrDefault(s =>
                                    s.CampaignName.Equals(dirInfo.Name, StringComparison.OrdinalIgnoreCase) &&
                                    s.OriginalGameFileName.Equals(milestone.SaveFileName, StringComparison.OrdinalIgnoreCase));

                                if (matching != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(milestone.Title)) matching.CustomTitle = milestone.Title;
                                    if (!string.IsNullOrWhiteSpace(milestone.PlayerNotes)) matching.Notes = milestone.PlayerNotes;
                                    if (milestone.Tags != null && milestone.Tags.Count > 0) matching.Tags = milestone.Tags;
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Delete the migrated folder once files are preserved
                try
                {
                    Directory.Delete(subDir, recursive: true);
                }
                catch { }
            }

            if (migratedCount > 0)
            {
                SaveManifest();
            }

            return migratedCount;
        }

        private void LoadOrRebuildManifest()
        {
            bool needsSave = false;

            if (File.Exists(_manifestPath))
            {
                try
                {
                    string json = File.ReadAllText(_manifestPath);
                    var loaded = JsonSerializer.Deserialize<SaveVaultManifest>(json);
                    if (loaded != null)
                    {
                        _manifest = loaded;
                    }
                }
                catch
                {
                    _manifest = new SaveVaultManifest();
                }
            }
            else
            {
                _manifest = new SaveVaultManifest();
                needsSave = true;
            }

            // Verify existing manifest items still exist on disk
            int beforeCount = _manifest.Saves.Count;
            _manifest.Saves.RemoveAll(s => !File.Exists(Path.Combine(_config.BackupDirectory, s.StoredFileName)));
            if (_manifest.Saves.Count != beforeCount)
            {
                needsSave = true;
            }

            // Index any unindexed .sav files residing directly in the vault directory
            if (Directory.Exists(_config.BackupDirectory))
            {
                var topSavFiles = Directory.GetFiles(_config.BackupDirectory, "*.sav", SearchOption.TopDirectoryOnly);
                foreach (var sav in topSavFiles)
                {
                    string fileName = Path.GetFileName(sav);
                    if (!_manifest.Saves.Any(s => s.StoredFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                    {
                        try
                        {
                            var saveInfo = _parserService.ParseSaveFile(sav);
                            var info = new FileInfo(sav);
                            string hash = ComputeSha256(sav);

                            _manifest.Saves.Add(new VaultSaveItem
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                StoredFileName = fileName,
                                OriginalGameFileName = ExtractOriginalFileName(fileName),
                                CampaignName = !string.IsNullOrWhiteSpace(saveInfo.FactionName) ? saveInfo.FactionName : "General",
                                Turn = saveInfo.Turn,
                                SaveType = saveInfo.Type,
                                Source = fileName.Contains("Sentinel", StringComparison.OrdinalIgnoreCase) 
                                    ? SaveSourceType.Sentinel 
                                    : (fileName.Contains("Safety", StringComparison.OrdinalIgnoreCase) ? SaveSourceType.SafetyBackup : SaveSourceType.Manual),
                                FileSizeBytes = info.Length,
                                Sha256Hash = hash,
                                CreatedAt = info.CreationTime,
                                LastModified = info.LastWriteTime
                            });
                            needsSave = true;
                        }
                        catch { }
                    }
                }
            }

            if (needsSave)
            {
                SaveManifest();
            }
        }

        private void SaveManifest()
        {
            try
            {
                _manifest.LastUpdated = DateTime.Now;
                string json = JsonSerializer.Serialize(_manifest, JsonOptions);
                File.WriteAllText(_manifestPath, json);
            }
            catch { }
        }

        private string ResolveDestinationFileName(string originalGameFileName, string hash, int? turn)
        {
            string candidate = originalGameFileName;
            string fullPath = Path.Combine(_config.BackupDirectory, candidate);

            // If no file exists with this name, we can use the clean original name directly!
            if (!File.Exists(fullPath))
            {
                return candidate;
            }

            // If a file exists and has the identical hash, we can reuse it
            if (ComputeSha256(fullPath).Equals(hash, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            // Filename collision with different contents (e.g. repeated quicksave or replayed turn).
            // Append a clean timestamp identifier so it remains directly recognizable as a .sav file.
            string ext = Path.GetExtension(originalGameFileName);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(originalGameFileName);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            candidate = $"{nameWithoutExt}_{timestamp}{ext}";
            int counter = 1;
            while (File.Exists(Path.Combine(_config.BackupDirectory, candidate)))
            {
                candidate = $"{nameWithoutExt}_{timestamp}_{counter}{ext}";
                counter++;
            }

            return candidate;
        }

        private static string ExtractOriginalFileName(string storedFileName)
        {
            // If storedFileName has a timestamp suffix like save_Quicksave_2026-09-18_23-15-00.sav, return save_Quicksave.sav
            string ext = Path.GetExtension(storedFileName);
            string nameWithoutExt = Path.GetFileNameWithoutExtension(storedFileName);

            // Match pattern like _YYYY-MM-DD_HH-mm-ss
            var regex = new System.Text.RegularExpressions.Regex(@"_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(?:_\d+)?$");
            if (regex.IsMatch(nameWithoutExt))
            {
                return regex.Replace(nameWithoutExt, "") + ext;
            }

            return storedFileName;
        }

        public static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hashBytes = sha.ComputeHash(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Adapts the vault items to standard BackupEntry models for full backward compatibility with existing views/UI/CLI.
        /// </summary>
        public List<BackupEntry> GetBackupsAsEntries()
        {
            lock (_lock)
            {
                return _manifest.Saves.Select(s => new BackupEntry
                {
                    VaultId = s.Id,
                    Name = s.DisplayName,
                    OriginalGameFileName = s.OriginalGameFileName,
                    FullPath = Path.Combine(_config.BackupDirectory, s.StoredFileName),
                    CreatedAt = s.CreatedAt,
                    TotalSizeBytes = s.FileSizeBytes,
                    FileCount = 1,
                    Type = BackupType.SaveFile,
                    IsSafetyBackup = s.IsSafety,
                    IsPinned = s.IsPinned,
                    Turn = s.Turn,
                    Notes = s.Notes,
                    Tags = s.Tags,
                    Source = s.Source,
                    CampaignName = s.CampaignName
                })
                .OrderByDescending(b => b.CreatedAt)
                .ToList();
            }
        }
    }
}
