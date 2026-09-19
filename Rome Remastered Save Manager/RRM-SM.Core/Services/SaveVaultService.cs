using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using RRM_SM.Models;
using RRM_SM.Core.Models;
using RRM_SM.Core.Security;

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

        public IReadOnlyList<VaultSaveItem> GetCampaignSaves(string campaignName, string? campaignId = null)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(campaignId))
                {
                    return _manifest.Saves
                        .Where(s => s.CampaignId == campaignId)
                        .OrderByDescending(s => s.CreatedAt)
                        .ToList();
                }

                return _manifest.Saves
                    .Where(s => s.CampaignName.Equals(campaignName, StringComparison.OrdinalIgnoreCase) ||
                                (s.CampaignId != null && s.CampaignId.Equals(campaignName, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(s => s.CreatedAt)
                    .ToList();
            }
        }

        public IReadOnlyDictionary<string, CampaignMetadata> GetCampaigns()
        {
            lock (_lock)
            {
                if (_manifest.Campaigns == null)
                {
                    _manifest.Campaigns = new Dictionary<string, CampaignMetadata>();
                }
                return new Dictionary<string, CampaignMetadata>(_manifest.Campaigns);
            }
        }

        public CampaignMetadata? GetCampaignMetadata(string campaignIdOrName)
        {
            lock (_lock)
            {
                if (_manifest.Campaigns == null) return null;

                if (_manifest.Campaigns.TryGetValue(campaignIdOrName, out var exact))
                    return exact;

                return _manifest.Campaigns.Values.FirstOrDefault(c =>
                    c.DisplayName.Equals(campaignIdOrName, StringComparison.OrdinalIgnoreCase));
            }
        }

        public CampaignMetadata? GetCampaignMetadataByGameCampaignId(string? gameCampaignId)
        {
            if (string.IsNullOrWhiteSpace(gameCampaignId)) return null;

            lock (_lock)
            {
                if (_manifest.Campaigns == null) return null;

                return _manifest.Campaigns.Values.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.GameCampaignId) &&
                    c.GameCampaignId.Equals(gameCampaignId, StringComparison.OrdinalIgnoreCase));
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
                string detectedFaction = !string.IsNullOrWhiteSpace(saveInfo.FactionName) && !saveInfo.FactionName.Equals("General", StringComparison.OrdinalIgnoreCase)
                    ? saveInfo.FactionName
                    : (!string.IsNullOrWhiteSpace(campaignName) ? campaignName : "General");

                var (resolvedCampaignId, resolvedCampaignName, resolvedFaction) = ResolveCampaignForNewSave(
                    detectedFaction,
                    saveInfo.Turn,
                    fileInfo.LastWriteTime,
                    campaignName,
                    saveInfo.GameCampaignId);

                // 1. De-duplication check: if a save with identical SHA-256 hash already exists in this campaign
                var existingSameHash = _manifest.Saves.FirstOrDefault(s => 
                    s.Sha256Hash.Equals(hash, StringComparison.OrdinalIgnoreCase) &&
                    ((s.CampaignId != null && s.CampaignId == resolvedCampaignId) || s.CampaignName.Equals(resolvedCampaignName, StringComparison.OrdinalIgnoreCase)) &&
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
                    CampaignId = resolvedCampaignId,
                    Faction = resolvedFaction,
                    GameCampaignId = saveInfo.GameCampaignId,
                    StoredFileName = storedFileName,
                    OriginalGameFileName = originalGameFileName,
                    CampaignName = resolvedCampaignName,
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
                    IsPinned = false,
                    InGameDate = saveInfo.InGameDate,
                    ModName = saveInfo.ModName,
                    CalendarYear = saveInfo.CalendarYear
                };

                _manifest.Saves.Add(newItem);
                EnforceRetentionLimit(resolvedCampaignId);
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
                string vaultFilePath = PathSecurity.EnsureSafeChildPath(_config.BackupDirectory, item.StoredFileName);
                if (!File.Exists(vaultFilePath))
                {
                    throw new FileNotFoundException($"Vault save file missing from disk: {vaultFilePath}");
                }

                if (!Directory.Exists(_config.GameSaveDirectory))
                {
                    Directory.CreateDirectory(_config.GameSaveDirectory);
                }

                string targetGamePath = PathSecurity.EnsureSafeChildPath(_config.GameSaveDirectory, item.OriginalGameFileName);

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

                if (PathSecurity.TryGetSafeChildPath(_config.BackupDirectory, item.StoredFileName, out var filePath) && File.Exists(filePath))
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

        public void UpdateMetadata(
            string id, 
            string? customTitle = null, 
            string? notes = null, 
            IEnumerable<string>? tags = null, 
            bool? isPinned = null,
            string? campaignId = null)
        {
            lock (_lock)
            {
                var item = _manifest.Saves.FirstOrDefault(s => s.Id == id);
                if (item == null) return;

                if (customTitle != null) item.CustomTitle = customTitle;
                if (notes != null) item.Notes = notes;
                if (tags != null) item.Tags = tags.Distinct().ToList();
                if (isPinned.HasValue) item.IsPinned = isPinned.Value;

                if (!string.IsNullOrWhiteSpace(campaignId) && _manifest.Campaigns != null && _manifest.Campaigns.TryGetValue(campaignId, out var targetCamp))
                {
                    item.CampaignId = targetCamp.Id;
                    item.CampaignName = targetCamp.DisplayName;
                    item.Faction = targetCamp.Faction;
                }

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
                        .Where(s => (s.CampaignId != null && s.CampaignId.Equals(specificCampaign, StringComparison.OrdinalIgnoreCase)) ||
                                    s.CampaignName.Equals(specificCampaign, StringComparison.OrdinalIgnoreCase))
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
                    var groups = _manifest.Saves.GroupBy(s => !string.IsNullOrWhiteSpace(s.CampaignId) ? s.CampaignId! : s.CampaignName, StringComparer.OrdinalIgnoreCase);
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

                        // If directory name starts with Backup_, Sentinel_, or Safety_, don't use it as campaign name
                        string? folderCampaignHint = dirInfo.Name;
                        if (folderCampaignHint.StartsWith("Backup_", StringComparison.OrdinalIgnoreCase) ||
                            folderCampaignHint.StartsWith("Sentinel_", StringComparison.OrdinalIgnoreCase) ||
                            folderCampaignHint.StartsWith("Safety_", StringComparison.OrdinalIgnoreCase))
                        {
                            folderCampaignHint = null;
                        }

                        AddSaveFile(
                            savFile, 
                            folderCampaignHint, 
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
                RebuildCampaignAssignmentsInternal();
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

            if (_manifest.Campaigns == null)
            {
                _manifest.Campaigns = new Dictionary<string, CampaignMetadata>();
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
                                Faction = !string.IsNullOrWhiteSpace(saveInfo.FactionName) ? saveInfo.FactionName : "General",
                                Turn = saveInfo.Turn,
                                SaveType = saveInfo.Type,
                                Source = fileName.Contains("Sentinel", StringComparison.OrdinalIgnoreCase) 
                                    ? SaveSourceType.Sentinel 
                                    : (fileName.Contains("Safety", StringComparison.OrdinalIgnoreCase) ? SaveSourceType.SafetyBackup : SaveSourceType.Manual),
                                FileSizeBytes = info.Length,
                                Sha256Hash = hash,
                                CreatedAt = info.CreationTime,
                                LastModified = info.LastWriteTime,
                                GameCampaignId = saveInfo.GameCampaignId,
                                InGameDate = saveInfo.InGameDate,
                                ModName = saveInfo.ModName,
                                CalendarYear = saveInfo.CalendarYear
                            });
                            needsSave = true;
                        }
                        catch { }
                    }
                }
            }

            // Enrich existing saves if their ModName is unassigned or set to default "Rome Remastered"
            foreach (var s in _manifest.Saves)
            {
                if (string.IsNullOrWhiteSpace(s.ModName) || s.ModName.Equals("Rome Remastered", StringComparison.OrdinalIgnoreCase))
                {
                    string fullPath = Path.Combine(_config.BackupDirectory, s.StoredFileName);
                    if (File.Exists(fullPath))
                    {
                        var meta = SaveMetadataReader.ReadSaveMetadata(fullPath);
                        if (meta != null && !string.IsNullOrWhiteSpace(meta.PrimaryModName) && !meta.PrimaryModName.Equals("Rome Remastered", StringComparison.OrdinalIgnoreCase))
                        {
                            s.ModName = meta.PrimaryModName;
                            if (string.IsNullOrWhiteSpace(s.InGameDate) && !string.IsNullOrWhiteSpace(meta.InGameDate))
                            {
                                s.InGameDate = meta.InGameDate;
                            }
                            if (!s.CalendarYear.HasValue && meta.CalendarYear.HasValue)
                            {
                                s.CalendarYear = meta.CalendarYear;
                            }
                            needsSave = true;
                        }
                    }
                }
            }

            // Automatic Migration & Campaign Separation:
            // If manifest is Version 1 or has saves missing CampaignId, automatically rebuild campaign assignments
            if (_manifest.Version < 2 || (_manifest.Saves.Count > 0 && _manifest.Saves.Any(s => string.IsNullOrWhiteSpace(s.CampaignId))))
            {
                RebuildCampaignAssignmentsInternal();
            }
            else if (needsSave)
            {
                SaveManifest();
            }
        }

        public (string CampaignId, string CampaignName, string Faction) ResolveCampaignForNewSave(
            string detectedFaction,
            int? turn,
            DateTime lastModified,
            string? explicitCampaignHint = null,
            string? gameCampaignId = null)
        {
            lock (_lock)
            {
                if (_manifest.Campaigns == null)
                {
                    _manifest.Campaigns = new Dictionary<string, CampaignMetadata>();
                }

                // If caller passed an explicit campaign hint that matches an existing campaign ID or DisplayName, check that first
                if (!string.IsNullOrWhiteSpace(explicitCampaignHint))
                {
                    if (_manifest.Campaigns.TryGetValue(explicitCampaignHint, out var exactById))
                    {
                        if (string.IsNullOrWhiteSpace(exactById.GameCampaignId) || string.IsNullOrWhiteSpace(gameCampaignId) ||
                            exactById.GameCampaignId.Equals(gameCampaignId, StringComparison.OrdinalIgnoreCase))
                        {
                            exactById.LastPlayedAt = lastModified > exactById.LastPlayedAt ? lastModified : exactById.LastPlayedAt;
                            if (!string.IsNullOrWhiteSpace(gameCampaignId) && string.IsNullOrWhiteSpace(exactById.GameCampaignId))
                            {
                                exactById.GameCampaignId = gameCampaignId;
                            }
                            return (exactById.Id, exactById.DisplayName, exactById.Faction);
                        }
                    }

                    // Only treat explicitCampaignHint as an exact campaign match if it is NOT just the generic faction name
                    if (!explicitCampaignHint.Equals(detectedFaction, StringComparison.OrdinalIgnoreCase))
                    {
                        var exactByName = _manifest.Campaigns.Values.FirstOrDefault(c =>
                            c.DisplayName.Equals(explicitCampaignHint, StringComparison.OrdinalIgnoreCase));
                        if (exactByName != null)
                        {
                            if (string.IsNullOrWhiteSpace(exactByName.GameCampaignId) || string.IsNullOrWhiteSpace(gameCampaignId) ||
                                exactByName.GameCampaignId.Equals(gameCampaignId, StringComparison.OrdinalIgnoreCase))
                            {
                                exactByName.LastPlayedAt = lastModified > exactByName.LastPlayedAt ? lastModified : exactByName.LastPlayedAt;
                                if (!string.IsNullOrWhiteSpace(gameCampaignId) && string.IsNullOrWhiteSpace(exactByName.GameCampaignId))
                                {
                                    exactByName.GameCampaignId = gameCampaignId;
                                }
                                return (exactByName.Id, exactByName.DisplayName, exactByName.Faction);
                            }
                        }
                    }
                }

                // PRIORITY 1: Match by authoritative internal GameCampaignId if available
                if (!string.IsNullOrWhiteSpace(gameCampaignId))
                {
                    var matchedCampaign = _manifest.Campaigns.Values.FirstOrDefault(c =>
                        !string.IsNullOrWhiteSpace(c.GameCampaignId) &&
                        c.GameCampaignId.Equals(gameCampaignId, StringComparison.OrdinalIgnoreCase));

                    if (matchedCampaign == null)
                    {
                        var existingSave = _manifest.Saves.FirstOrDefault(s =>
                            !string.IsNullOrWhiteSpace(s.GameCampaignId) &&
                            s.GameCampaignId.Equals(gameCampaignId, StringComparison.OrdinalIgnoreCase));

                        if (existingSave != null && !string.IsNullOrWhiteSpace(existingSave.CampaignId) &&
                            _manifest.Campaigns.TryGetValue(existingSave.CampaignId, out var campFromSave))
                        {
                            matchedCampaign = campFromSave;
                            matchedCampaign.GameCampaignId = gameCampaignId;
                        }
                    }

                    if (matchedCampaign != null)
                    {
                        matchedCampaign.LastPlayedAt = lastModified > matchedCampaign.LastPlayedAt ? lastModified : matchedCampaign.LastPlayedAt;
                        if ((string.IsNullOrWhiteSpace(matchedCampaign.Faction) || matchedCampaign.Faction.Equals("General", StringComparison.OrdinalIgnoreCase)) &&
                            !string.IsNullOrWhiteSpace(detectedFaction) && !detectedFaction.Equals("General", StringComparison.OrdinalIgnoreCase))
                        {
                            matchedCampaign.Faction = detectedFaction;
                        }
                        return (matchedCampaign.Id, matchedCampaign.DisplayName, matchedCampaign.Faction);
                    }

                    // GameCampaignId is present and distinct from all known campaigns -> Definitively a new playthrough!
                    var otherCampaigns = _manifest.Campaigns.Values
                        .Where(c => c.Faction.Equals(detectedFaction, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    string newCampId = Guid.NewGuid().ToString("N");
                    string newDisplayName;

                    if (otherCampaigns.Count > 0)
                    {
                        foreach (var existing in otherCampaigns)
                        {
                            if (!existing.IsCustomNamed && existing.DisplayName.Equals(detectedFaction, StringComparison.OrdinalIgnoreCase))
                            {
                                bool sameMonth = existing.CreatedAt.ToString("yyyy-MM").Equals(lastModified.ToString("yyyy-MM"), StringComparison.OrdinalIgnoreCase);
                                string disambiguated = sameMonth
                                    ? $"{detectedFaction} ({existing.CreatedAt:yyyy-MM-dd})"
                                    : $"{detectedFaction} ({existing.CreatedAt:MMM yyyy})";

                                existing.DisplayName = disambiguated;
                                foreach (var s in _manifest.Saves.Where(s => s.CampaignId == existing.Id))
                                {
                                    s.CampaignName = disambiguated;
                                }
                            }
                        }

                        newDisplayName = $"{detectedFaction} ({lastModified:MMM yyyy})";
                        if (_manifest.Campaigns.Values.Any(c => c.DisplayName.Equals(newDisplayName, StringComparison.OrdinalIgnoreCase)))
                        {
                            newDisplayName = $"{detectedFaction} ({lastModified:yyyy-MM-dd})";
                        }
                    }
                    else
                    {
                        newDisplayName = detectedFaction;
                    }

                    var newMeta = new CampaignMetadata
                    {
                        Id = newCampId,
                        Faction = detectedFaction,
                        DisplayName = newDisplayName,
                        IsCustomNamed = false,
                        CreatedAt = lastModified,
                        LastPlayedAt = lastModified,
                        GameCampaignId = gameCampaignId
                    };

                    _manifest.Campaigns[newCampId] = newMeta;
                    return (newCampId, newDisplayName, detectedFaction);
                }

                // PRIORITY 2: Fallback heuristic clustering (14-day gap, 50-turn discontinuity)
                var candidates = _manifest.Campaigns.Values
                    .Where(c => c.Faction.Equals(detectedFaction, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                CampaignMetadata? bestCandidate = null;
                double smallestDayGap = double.MaxValue;

                foreach (var candidate in candidates)
                {
                    var campaignSaves = _manifest.Saves
                        .Where(s => s.CampaignId == candidate.Id)
                        .ToList();

                    if (campaignSaves.Count == 0)
                    {
                        if (bestCandidate == null) bestCandidate = candidate;
                        continue;
                    }

                    // Check minimum time distance to any save in this campaign (14-day gap threshold)
                    double minDays = campaignSaves.Min(s => Math.Abs((lastModified - s.LastModified).TotalDays));
                    if (minDays > 14.0)
                    {
                        continue;
                    }

                    // Turn continuity check: within 50 turns
                    if (turn.HasValue)
                    {
                        var turns = campaignSaves.Where(s => s.Turn.HasValue).Select(s => s.Turn!.Value).ToList();
                        if (turns.Count > 0)
                        {
                            int minTurn = turns.Min();
                            int maxTurn = turns.Max();
                            if (turn.Value < minTurn - 50 || turn.Value > maxTurn + 50)
                            {
                                continue;
                            }
                        }
                    }

                    if (minDays < smallestDayGap)
                    {
                        smallestDayGap = minDays;
                        bestCandidate = candidate;
                    }
                }

                if (bestCandidate != null)
                {
                    bestCandidate.LastPlayedAt = lastModified > bestCandidate.LastPlayedAt ? lastModified : bestCandidate.LastPlayedAt;
                    return (bestCandidate.Id, bestCandidate.DisplayName, bestCandidate.Faction);
                }

                // 2. Need to create a new campaign (via heuristic)
                string newId = Guid.NewGuid().ToString("N");
                string displayName;

                if (candidates.Count > 0)
                {
                    // Existing campaigns for this faction exist -> disambiguate existing non-custom-named campaigns
                    foreach (var existing in candidates)
                    {
                        if (!existing.IsCustomNamed && existing.DisplayName.Equals(detectedFaction, StringComparison.OrdinalIgnoreCase))
                        {
                            bool sameMonth = existing.CreatedAt.ToString("yyyy-MM").Equals(lastModified.ToString("yyyy-MM"), StringComparison.OrdinalIgnoreCase);
                            string disambiguated = sameMonth
                                ? $"{detectedFaction} ({existing.CreatedAt:yyyy-MM-dd})"
                                : $"{detectedFaction} ({existing.CreatedAt:MMM yyyy})";

                            existing.DisplayName = disambiguated;
                            foreach (var s in _manifest.Saves.Where(s => s.CampaignId == existing.Id))
                            {
                                s.CampaignName = disambiguated;
                            }
                        }
                    }

                    displayName = $"{detectedFaction} ({lastModified:MMM yyyy})";
                    if (_manifest.Campaigns.Values.Any(c => c.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                    {
                        displayName = $"{detectedFaction} ({lastModified:yyyy-MM-dd})";
                    }
                }
                else
                {
                    displayName = detectedFaction;
                }

                var metadata = new CampaignMetadata
                {
                    Id = newId,
                    Faction = detectedFaction,
                    DisplayName = displayName,
                    IsCustomNamed = false,
                    CreatedAt = lastModified,
                    LastPlayedAt = lastModified,
                    GameCampaignId = gameCampaignId
                };

                _manifest.Campaigns[newId] = metadata;
                return (newId, displayName, detectedFaction);
            }
        }

        public void RebuildCampaignAssignments()
        {
            lock (_lock)
            {
                RebuildCampaignAssignmentsInternal();
            }
        }

        private void RebuildCampaignAssignmentsInternal()
        {
            if (_manifest.Campaigns == null)
            {
                _manifest.Campaigns = new Dictionary<string, CampaignMetadata>();
            }

            if (_manifest.Saves.Count == 0)
            {
                _manifest.Campaigns.Clear();
                _manifest.Version = 2;
                SaveManifest();
                return;
            }

            // 1. Re-parse original game filename for each save to get accurate faction and turn, and backfill GameCampaignId
            foreach (var save in _manifest.Saves)
            {
                string parseTarget = !string.IsNullOrWhiteSpace(save.OriginalGameFileName)
                    ? save.OriginalGameFileName
                    : save.StoredFileName;

                var info = _parserService.ParseSaveFile(parseTarget);

                if (!string.IsNullOrWhiteSpace(info.FactionName) && !info.FactionName.Equals("General", StringComparison.OrdinalIgnoreCase))
                {
                    save.Faction = info.FactionName;
                }
                else if (string.IsNullOrWhiteSpace(save.Faction) || save.Faction.StartsWith("Backup_", StringComparison.OrdinalIgnoreCase))
                {
                    save.Faction = !string.IsNullOrWhiteSpace(info.FactionName) ? info.FactionName : "General";
                }

                if (info.Turn.HasValue)
                {
                    save.Turn = info.Turn;
                }
                if (info.Type != SaveFileType.Unknown)
                {
                    save.SaveType = info.Type;
                }

                // Backfill deep metadata from disk if file is available in vault directory
                string diskPath = Path.Combine(_config.BackupDirectory, save.StoredFileName);
                if (File.Exists(diskPath))
                {
                    if (string.IsNullOrWhiteSpace(save.GameCampaignId) || string.IsNullOrWhiteSpace(save.InGameDate))
                    {
                        var meta = SaveMetadataReader.ReadSaveMetadata(diskPath);
                        if (meta != null)
                        {
                            if (string.IsNullOrWhiteSpace(save.GameCampaignId) && !string.IsNullOrWhiteSpace(meta.GameCampaignId))
                            {
                                save.GameCampaignId = meta.GameCampaignId;
                            }
                            if (string.IsNullOrWhiteSpace(save.InGameDate)) save.InGameDate = meta.InGameDate;
                            if (!save.CalendarYear.HasValue) save.CalendarYear = meta.CalendarYear;
                            if (string.IsNullOrWhiteSpace(save.ModName)) save.ModName = meta.PrimaryModName;
                            if (!save.Turn.HasValue && meta.TurnNumber.HasValue) save.Turn = meta.TurnNumber;
                        }
                    }
                }
            }

            // 2. Associate unassigned / quicksaves with nearest resolved save or via GameCampaignId
            var resolvedSaves = _manifest.Saves
                .Where(s => !string.IsNullOrWhiteSpace(s.Faction) && !s.Faction.Equals("General", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.LastModified)
                .ToList();

            foreach (var save in _manifest.Saves.Where(s => string.IsNullOrWhiteSpace(s.Faction) || s.Faction.Equals("General", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(save.GameCampaignId))
                {
                    var matchByGuid = resolvedSaves.FirstOrDefault(r => r.GameCampaignId == save.GameCampaignId);
                    if (matchByGuid != null)
                    {
                        save.Faction = matchByGuid.Faction;
                        continue;
                    }
                }

                var closest = resolvedSaves
                    .Select(r => new { Save = r, Diff = Math.Abs((r.LastModified - save.LastModified).TotalHours) })
                    .Where(x => x.Diff <= 48.0)
                    .OrderBy(x => x.Diff)
                    .FirstOrDefault();

                if (closest != null)
                {
                    save.Faction = closest.Save.Faction;
                }
                else
                {
                    save.Faction = "General";
                }
            }

            // 3. Preserve any user-customized campaign names
            var customCampaigns = _manifest.Campaigns.Values
                .Where(c => c.IsCustomNamed)
                .ToList();

            var newCampaigns = new Dictionary<string, CampaignMetadata>();

            // 4. Cluster saves:
            // A. Primary: Global grouping by authoritative GameCampaignId across ALL saves
            var allClusters = new List<SaveCluster>();

            var guidGroups = _manifest.Saves
                .Where(s => !string.IsNullOrWhiteSpace(s.GameCampaignId))
                .GroupBy(s => s.GameCampaignId!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var gg in guidGroups)
            {
                var sc = new SaveCluster();
                sc.Saves.AddRange(gg.OrderBy(s => s.LastModified));

                // Determine canonical faction for this GUID cluster
                string clusterFaction = "General";
                var autoSave = sc.Saves.FirstOrDefault(s => s.SaveType == SaveFileType.Autosave &&
                    !string.IsNullOrWhiteSpace(s.Faction) && !s.Faction.Equals("General", StringComparison.OrdinalIgnoreCase));

                if (autoSave != null)
                {
                    clusterFaction = autoSave.Faction!;
                }
                else
                {
                    var knownSave = sc.Saves.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Faction) &&
                        CampaignParserService.KnownFactions.Any(k => k.Equals(s.Faction, StringComparison.OrdinalIgnoreCase)));
                    if (knownSave != null)
                    {
                        clusterFaction = knownSave.Faction!;
                    }
                    else
                    {
                        var nonGeneral = sc.Saves.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Faction) && !s.Faction.Equals("General", StringComparison.OrdinalIgnoreCase));
                        clusterFaction = nonGeneral?.Faction ?? "General";
                    }
                }

                sc.Faction = clusterFaction;
                foreach (var s in sc.Saves)
                {
                    s.Faction = clusterFaction;
                }

                allClusters.Add(sc);
            }

            // B. Cluster remaining saves without GameCampaignId using heuristics (per faction, 14-day gap, 50-turn discontinuity)
            var savesWithoutGuid = _manifest.Saves
                .Where(s => string.IsNullOrWhiteSpace(s.GameCampaignId))
                .GroupBy(s => s.Faction ?? "General", StringComparer.OrdinalIgnoreCase);

            foreach (var group in savesWithoutGuid)
            {
                string faction = group.Key;
                var sorted = group.OrderBy(s => s.LastModified).ToList();
                var heuristicClusters = ClusterSaves(sorted, maxDayGap: 14.0, maxTurnGap: 50);
                foreach (var hc in heuristicClusters)
                {
                    hc.Faction = faction;
                    allClusters.Add(hc);
                }
            }

            // 5. Build campaigns grouped by faction to handle disambiguation
            var clustersByFaction = allClusters
                .GroupBy(c => c.Faction, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key);

            foreach (var group in clustersByFaction)
            {
                string faction = group.Key;
                var clusters = group.OrderBy(c => c.Saves.Min(s => s.LastModified)).ToList();
                bool needsDisambiguation = clusters.Count > 1;

                foreach (var cluster in clusters)
                {
                    var clusterSaves = cluster.Saves;
                    DateTime earliest = clusterSaves.Min(s => s.LastModified);
                    DateTime latest = clusterSaves.Max(s => s.LastModified);
                    string? clusterGuid = clusterSaves.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.GameCampaignId))?.GameCampaignId;

                    // Check if saves in this cluster belonged to an existing custom campaign
                    var existingCustom = customCampaigns.FirstOrDefault(c =>
                        ((!string.IsNullOrWhiteSpace(clusterGuid) && c.GameCampaignId == clusterGuid) ||
                         c.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase)) &&
                        clusterSaves.Any(s => s.CampaignId == c.Id));

                    var existingCampaign = existingCustom
                        ?? (!string.IsNullOrWhiteSpace(clusterGuid) ? _manifest.Campaigns.Values.FirstOrDefault(c => c.GameCampaignId == clusterGuid) : null)
                        ?? _manifest.Campaigns.Values.FirstOrDefault(c => clusterSaves.Any(s => s.CampaignId == c.Id));

                    string campaignId = existingCampaign?.Id ?? Guid.NewGuid().ToString("N");
                    string displayName;

                    if (existingCustom != null && existingCustom.IsCustomNamed)
                    {
                        displayName = existingCustom.DisplayName;
                    }
                    else if (!needsDisambiguation)
                    {
                        displayName = faction;
                    }
                    else
                    {
                        string dateStr = earliest.ToString("MMM yyyy");
                        displayName = $"{faction} ({dateStr})";

                        if (newCampaigns.Values.Any(c => c.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                        {
                            displayName = $"{faction} ({earliest:yyyy-MM-dd})";
                        }
                    }

                    var meta = new CampaignMetadata
                    {
                        Id = campaignId,
                        Faction = faction,
                        DisplayName = displayName,
                        IsCustomNamed = existingCustom?.IsCustomNamed ?? false,
                        CreatedAt = earliest,
                        LastPlayedAt = latest,
                        GameCampaignId = clusterGuid
                    };

                    newCampaigns[campaignId] = meta;

                    foreach (var s in clusterSaves)
                    {
                        s.CampaignId = campaignId;
                        s.CampaignName = displayName;
                        s.Faction = faction;
                        if (string.IsNullOrWhiteSpace(s.GameCampaignId) && !string.IsNullOrWhiteSpace(clusterGuid))
                        {
                            s.GameCampaignId = clusterGuid;
                        }
                    }
                }
            }

            _manifest.Campaigns = newCampaigns;
            _manifest.Version = 2;
            SaveManifest();
        }

        private class SaveCluster
        {
            public string Faction { get; set; } = "General";
            public List<VaultSaveItem> Saves { get; } = new();
        }

        private static List<SaveCluster> ClusterSaves(List<VaultSaveItem> sortedSaves, double maxDayGap, int maxTurnGap)
        {
            var clusters = new List<SaveCluster>();

            foreach (var save in sortedSaves)
            {
                SaveCluster? bestCluster = null;
                double smallestGap = double.MaxValue;

                foreach (var cluster in clusters)
                {
                    double minDays = cluster.Saves.Min(cs => Math.Abs((save.LastModified - cs.LastModified).TotalDays));
                    if (minDays > maxDayGap)
                        continue;

                    if (save.Turn.HasValue)
                    {
                        var clusterTurns = cluster.Saves.Where(cs => cs.Turn.HasValue).Select(cs => cs.Turn!.Value).ToList();
                        if (clusterTurns.Count > 0)
                        {
                            int minTurn = clusterTurns.Min();
                            int maxTurn = clusterTurns.Max();
                            if (save.Turn.Value < minTurn - maxTurnGap || save.Turn.Value > maxTurn + maxTurnGap)
                            {
                                continue;
                            }
                        }
                    }

                    if (minDays < smallestGap)
                    {
                        smallestGap = minDays;
                        bestCluster = cluster;
                    }
                }

                if (bestCluster != null)
                {
                    bestCluster.Saves.Add(save);
                }
                else
                {
                    var newCluster = new SaveCluster();
                    newCluster.Saves.Add(save);
                    clusters.Add(newCluster);
                }
            }

            return clusters;
        }

        public void MergeCampaigns(string targetCampaignId, IEnumerable<string> sourceCampaignIds)
        {
            lock (_lock)
            {
                if (_manifest.Campaigns == null)
                {
                    _manifest.Campaigns = new Dictionary<string, CampaignMetadata>();
                }

                if (!_manifest.Campaigns.TryGetValue(targetCampaignId, out var targetCampaign))
                {
                    targetCampaign = _manifest.Campaigns.Values.FirstOrDefault(c =>
                        c.DisplayName.Equals(targetCampaignId, StringComparison.OrdinalIgnoreCase));
                    if (targetCampaign == null)
                        throw new ArgumentException($"Target campaign '{targetCampaignId}' not found.");
                }

                var sources = sourceCampaignIds
                    .Where(id => !string.IsNullOrWhiteSpace(id) && !id.Equals(targetCampaign.Id, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (sources.Count == 0) return;

                var resolvedSourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var src in sources)
                {
                    if (_manifest.Campaigns.TryGetValue(src, out var byId))
                    {
                        resolvedSourceIds.Add(byId.Id);
                    }
                    else
                    {
                        var byName = _manifest.Campaigns.Values.FirstOrDefault(c => c.DisplayName.Equals(src, StringComparison.OrdinalIgnoreCase));
                        if (byName != null)
                        {
                            resolvedSourceIds.Add(byName.Id);
                        }
                        else
                        {
                            resolvedSourceIds.Add(src);
                        }
                    }
                }

                foreach (var save in _manifest.Saves)
                {
                    if ((save.CampaignId != null && resolvedSourceIds.Contains(save.CampaignId)) ||
                        (save.CampaignName != null && sources.Contains(save.CampaignName)))
                    {
                        save.CampaignId = targetCampaign.Id;
                        save.CampaignName = targetCampaign.DisplayName;
                        save.Faction = targetCampaign.Faction;
                    }
                }

                foreach (var sourceId in resolvedSourceIds)
                {
                    _manifest.Campaigns.Remove(sourceId);
                }

                SaveManifest();
            }
        }

        public CampaignMetadata SplitCampaign(IEnumerable<string> saveIds, string? newCampaignName = null)
        {
            lock (_lock)
            {
                if (_manifest.Campaigns == null)
                {
                    _manifest.Campaigns = new Dictionary<string, CampaignMetadata>();
                }

                var targetSaves = _manifest.Saves.Where(s => saveIds.Contains(s.Id)).ToList();
                if (targetSaves.Count == 0)
                {
                    throw new ArgumentException("No matching saves found to split.");
                }

                string faction = targetSaves.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Faction))?.Faction ?? "General";
                string newId = Guid.NewGuid().ToString("N");
                DateTime earliest = targetSaves.Min(s => s.LastModified);
                DateTime latest = targetSaves.Max(s => s.LastModified);

                string displayName = !string.IsNullOrWhiteSpace(newCampaignName)
                    ? newCampaignName.Trim()
                    : $"{faction} ({earliest:MMM yyyy} - Split)";

                var metadata = new CampaignMetadata
                {
                    Id = newId,
                    Faction = faction,
                    DisplayName = displayName,
                    IsCustomNamed = !string.IsNullOrWhiteSpace(newCampaignName),
                    CreatedAt = earliest,
                    LastPlayedAt = latest
                };

                _manifest.Campaigns[newId] = metadata;

                foreach (var save in targetSaves)
                {
                    save.CampaignId = newId;
                    save.CampaignName = displayName;
                    save.Faction = faction;
                }

                SaveManifest();
                return metadata;
            }
        }

        public void RenameCampaign(string campaignIdOrName, string newDisplayName)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(newDisplayName))
                    throw new ArgumentException("Campaign name cannot be empty.", nameof(newDisplayName));

                if (_manifest.Campaigns == null)
                {
                    _manifest.Campaigns = new Dictionary<string, CampaignMetadata>();
                }

                CampaignMetadata? campaign = null;
                if (_manifest.Campaigns.TryGetValue(campaignIdOrName, out var byId))
                {
                    campaign = byId;
                }
                else
                {
                    campaign = _manifest.Campaigns.Values.FirstOrDefault(c =>
                        c.DisplayName.Equals(campaignIdOrName, StringComparison.OrdinalIgnoreCase));
                }

                string trimmedName = newDisplayName.Trim();

                if (campaign != null)
                {
                    campaign.DisplayName = trimmedName;
                    campaign.IsCustomNamed = true;

                    foreach (var save in _manifest.Saves)
                    {
                        if (save.CampaignId == campaign.Id)
                        {
                            save.CampaignName = trimmedName;
                        }
                    }
                }
                else
                {
                    foreach (var save in _manifest.Saves)
                    {
                        if (save.CampaignName.Equals(campaignIdOrName, StringComparison.OrdinalIgnoreCase))
                        {
                            save.CampaignName = trimmedName;
                        }
                    }
                }

                SaveManifest();
            }
        }

        private void SaveManifest()
        {
            try
            {
                _manifest.LastUpdated = DateTime.Now;
                string json = JsonSerializer.Serialize(_manifest, JsonOptions);
                string tempPath = _manifestPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _manifestPath, overwrite: true);
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
            var regex = new System.Text.RegularExpressions.Regex(
                @"_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(?:_\d+)?$",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromMilliseconds(250));
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
                    CampaignId = s.CampaignId,
                    CampaignName = s.CampaignName,
                    Faction = s.Faction,
                    InGameDate = s.InGameDate,
                    CalendarYear = s.CalendarYear,
                    ModName = s.ModName
                })
                .OrderByDescending(b => b.CreatedAt)
                .ToList();
            }
        }
    }
}
