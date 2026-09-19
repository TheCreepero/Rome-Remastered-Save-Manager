using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RRM_SM.Models;
using RRM_SM.Services;

namespace RRM_SM
{
    internal class Program
    {
        private static ConfigService _configService = null!;
        private static AppConfig _config = null!;
        private static BackupService _backupService = null!;

        private static void Main(string[] args)
        {
            Console.Title = "Total War: ROME REMASTERED - Save Manager";
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            _configService = new ConfigService();
            _config = _configService.LoadOrCreateConfig();
            _backupService = new BackupService(_config);

            if (args.Length > 0)
            {
                HandleCommandLineArgs(args);
                return;
            }

            bool running = true;
            while (running)
            {
                PrintHeader();
                PrintMainMenu();

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("\nSelect an option [0-7]: ");
                Console.ResetColor();

                string? choice = Console.ReadLine()?.Trim();
                Console.WriteLine();

                switch (choice?.ToLowerInvariant())
                {
                    case "1":
                        PerformQuickBackup();
                        break;
                    case "2":
                        PerformBackupAllCampaigns();
                        break;
                    case "3":
                        PerformRestore();
                        break;
                    case "4":
                        ViewBackupsList();
                        break;
                    case "5":
                        OpenFolderInExplorer(_config.GameSaveDirectory, "Game Save Folder");
                        break;
                    case "6":
                        OpenFolderInExplorer(_config.BackupDirectory, "Backup Folder");
                        break;
                    case "7":
                        ManageSettings();
                        break;
                    case "0":
                    case "exit":
                    case "quit":
                        running = false;
                        Console.WriteLine("Exiting Rome Remastered Save Manager. Valete!");
                        break;
                    default:
                        PrintColored("Invalid selection. Please enter a number between 0 and 7.", ConsoleColor.Red);
                        WaitForKey();
                        break;
                }
            }
        }

        private static void PrintHeader()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("================================================================================");
            Console.WriteLine("           TOTAL WAR: ROME REMASTERED - SAVE MANAGER & BACKUP TOOL              ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            // Status overview
            bool saveDirExists = Directory.Exists(_config.GameSaveDirectory);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Game Save Folder : ");
            Console.ForegroundColor = saveDirExists ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(_config.GameSaveDirectory + (saveDirExists ? " [OK]" : " [NOT FOUND]"));

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Backup Directory : ");
            Console.ForegroundColor = Directory.Exists(_config.BackupDirectory) ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine(_config.BackupDirectory);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("  Mode             : ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(_config.CompressBackups ? "Compressed (.zip)" : "Folder Snapshots");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(" | Max Backups (per campaign): ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(_config.MaxBackupsToKeep > 0 ? _config.MaxBackupsToKeep.ToString() : "Unlimited");

            // Detected Active Campaigns
            if (saveDirExists)
            {
                var activeDict = _backupService.GetActiveCampaigns();
                string mostRecent = _backupService.GetMostRecentCampaign();

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("  Active Factions  : ");
                if (activeDict.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(string.Join(", ", activeDict.Keys.Take(5)) + (activeDict.Count > 5 ? $" (+{activeDict.Count - 5} more)" : ""));
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write("  Most Recent Play : ");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(mostRecent);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("None detected in save folder.");
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
        }

        private static void PrintMainMenu()
        {
            string mostRecent = _backupService.GetMostRecentCampaign();

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  [1] Quick Backup       - Snapshot most recent campaign [{mostRecent}]");
            Console.WriteLine("  [2] Backup All         - Snapshot all active campaigns into faction folders");
            Console.WriteLine("  [3] Restore Backup     - Restore past campaign state with safety backup");
            Console.WriteLine("  [4] List All Backups   - View existing snapshots grouped by faction");
            Console.WriteLine("  [5] Open Active Saves  - Reveal save folder in File Explorer");
            Console.WriteLine("  [6] Open Backup Folder - Reveal backup folder in File Explorer");
            Console.WriteLine("  [7] Settings           - Configure folders, compression, retention");
            Console.WriteLine("  [0] Exit");
            Console.ResetColor();
        }

        private static void PerformQuickBackup()
        {
            try
            {
                string mostRecent = _backupService.GetMostRecentCampaign();
                PrintColored($"--> Starting Quick Backup for campaign [{mostRecent}]...", ConsoleColor.Cyan);
                var entry = _backupService.CreateCampaignBackup(mostRecent);
                PrintColored($"✔ Backup created: {entry.CampaignName} / {entry.Name}", ConsoleColor.Green);
                Console.WriteLine($"   Files: {entry.FileCount} | Total Size: {entry.FormattedSize}");
                Console.WriteLine($"   Location: {entry.FullPath}");
            }
            catch (Exception ex)
            {
                PrintColored($"✖ Backup failed: {ex.Message}", ConsoleColor.Red);
            }
            WaitForKey();
        }

        private static void PerformBackupAllCampaigns()
        {
            try
            {
                PrintColored("--> Backing up all detected active campaigns...", ConsoleColor.Cyan);
                var entries = _backupService.CreateAllCampaignsBackup();
                PrintColored($"✔ Successfully created {entries.Count} campaign backup(s):", ConsoleColor.Green);
                foreach (var e in entries)
                {
                    Console.WriteLine($"   • [{e.CampaignName}] -> {e.Name} ({e.FormattedSize})");
                }
            }
            catch (Exception ex)
            {
                PrintColored($"✖ Backup all failed: {ex.Message}", ConsoleColor.Red);
            }
            WaitForKey();
        }

        private static void ViewBackupsList()
        {
            var backups = _backupService.GetBackups();
            if (backups.Count == 0)
            {
                PrintColored("No backups found in target directory.", ConsoleColor.Yellow);
                WaitForKey();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Found {backups.Count} backup(s):\n");
            Console.ResetColor();

            Console.WriteLine($"{"#",-4} {"Campaign / Faction",-22} {"Date / Time",-20} {"Size",-10} {"Files",-6} {"Name"}");
            Console.WriteLine(new string('-', 95));

            for (int i = 0; i < backups.Count; i++)
            {
                var b = backups[i];
                string marker = b.IsPinned ? "[★ PINNED] " : (b.IsSentinelBackup ? "[SENTINEL] " : (b.IsSafetyBackup ? "[SAFETY] " : ""));
                Console.ForegroundColor = b.IsPinned ? ConsoleColor.Yellow : (b.IsSafetyBackup ? ConsoleColor.DarkYellow : (b.IsSentinelBackup ? ConsoleColor.Cyan : ConsoleColor.Gray));
                Console.WriteLine($"{i + 1,-4} {Truncate(b.CampaignName, 21),-22} {b.CreatedAt:yyyy-MM-dd HH:mm:ss,-20} {b.FormattedSize,-10} {b.FileCount,-6} {marker}{b.Name}");
            }

            Console.ResetColor();
            WaitForKey();
        }

        private static void PerformRestore()
        {
            var backups = _backupService.GetBackups();
            if (backups.Count == 0)
            {
                PrintColored("No backups available to restore.", ConsoleColor.Yellow);
                WaitForKey();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("RESTORE A BACKUP");
            Console.WriteLine("================");
            Console.ResetColor();

            Console.WriteLine($"{"#",-4} {"Campaign / Faction",-22} {"Date / Time",-20} {"Size",-10} {"Name"}");
            Console.WriteLine(new string('-', 85));

            for (int i = 0; i < backups.Count; i++)
            {
                var b = backups[i];
                string marker = b.IsPinned ? "[★ PINNED] " : (b.IsSentinelBackup ? "[SENTINEL] " : (b.IsSafetyBackup ? "[SAFETY] " : ""));
                Console.WriteLine($"{i + 1,-4} {Truncate(b.CampaignName, 21),-22} {b.CreatedAt:yyyy-MM-dd HH:mm:ss,-20} {b.FormattedSize,-10} {marker}{b.Name}");
            }

            Console.Write("\nEnter the backup number to restore (or press Enter / 0 to cancel): ");
            string? input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input) || input == "0" || !int.TryParse(input, out int selection) || selection < 1 || selection > backups.Count)
            {
                PrintColored("Restore cancelled.", ConsoleColor.DarkGray);
                WaitForKey();
                return;
            }

            var chosen = backups[selection - 1];

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"\nWARNING: This will restore [{chosen.CampaignName}] saves into your game directory from '{chosen.Name}'!");
            Console.ResetColor();
            Console.Write("\nA safety backup will be created first. Proceed? (yes/no): ");

            string? confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (confirm != "yes" && confirm != "y")
            {
                PrintColored("Restore aborted by user.", ConsoleColor.Yellow);
                WaitForKey();
                return;
            }

            try
            {
                PrintColored($"--> Restoring {chosen.Name}...", ConsoleColor.Cyan);
                _backupService.RestoreBackup(chosen, createSafetyBackup: true);
                PrintColored($"✔ Restored successfully! Active saves updated to: {chosen.Name}", ConsoleColor.Green);
                PrintColored("✔ Pre-restore safety backup was also saved in the backups directory.", ConsoleColor.DarkGreen);
            }
            catch (Exception ex)
            {
                PrintColored($"✖ Restore failed: {ex.Message}", ConsoleColor.Red);
            }
            WaitForKey();
        }

        private static void OpenFolderInExplorer(string folderPath, string label)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    PrintColored($"Cannot open {label}: Path is empty.", ConsoleColor.Yellow);
                    WaitForKey();
                    return;
                }

                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
                PrintColored($"Opened {label} in Windows File Explorer.", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                PrintColored($"Failed to open folder: {ex.Message}", ConsoleColor.Red);
            }
            WaitForKey();
        }

        private static void ManageSettings()
        {
            bool inSettings = true;
            while (inSettings)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("================================================================================");
                Console.WriteLine("                          CONFIGURATION & SETTINGS                              ");
                Console.WriteLine("================================================================================");
                Console.ResetColor();

                Console.WriteLine($"  [1] Game Save Directory   : {_config.GameSaveDirectory}");
                Console.WriteLine($"  [2] Auto-Detect Save Dir  : Scan known locations");
                Console.WriteLine($"  [3] Backup Directory      : {_config.BackupDirectory}");
                Console.WriteLine($"  [4] Compression (.zip)    : {(_config.CompressBackups ? "Enabled" : "Disabled")}");
                Console.WriteLine($"  [5] Max Backups to Keep   : {(_config.MaxBackupsToKeep > 0 ? _config.MaxBackupsToKeep.ToString() : "Unlimited (0)")}");
                Console.WriteLine($"  [6] Open config.json      : Open file in default editor");
                Console.WriteLine($"  [7] Rebuild Vault Index   : Scan on-disk .sav files and repair vault.json");
                Console.WriteLine($"  [8] Migrate Legacy Backups: Import old nested folder snapshots into flat vault");
                Console.WriteLine($"  [0] Back to Main Menu");
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("--------------------------------------------------------------------------------");
                Console.ResetColor();
                Console.Write("\nSelect a setting to edit [0-8]: ");

                string? choice = Console.ReadLine()?.Trim();
                switch (choice)
                {
                    case "1":
                        Console.Write("\nEnter new full path to Game Save Directory: ");
                        string? newSave = Console.ReadLine()?.Trim();
                        if (!string.IsNullOrWhiteSpace(newSave))
                        {
                            _config.GameSaveDirectory = newSave;
                            _configService.SaveConfig(_config);
                            PrintColored("Game save directory updated.", ConsoleColor.Green);
                        }
                        WaitForKey();
                        break;
                    case "2":
                        string? detected = ConfigService.AutoDetectRomeSaveDirectory();
                        if (!string.IsNullOrWhiteSpace(detected))
                        {
                            _config.GameSaveDirectory = detected;
                            _configService.SaveConfig(_config);
                            PrintColored($"Detected & set: {detected}", ConsoleColor.Green);
                        }
                        else
                        {
                            PrintColored("Could not auto-detect Rome Remastered save directory on this system.", ConsoleColor.Yellow);
                        }
                        WaitForKey();
                        break;
                    case "3":
                        Console.Write("\nEnter new full path to Backup Directory: ");
                        string? newBackup = Console.ReadLine()?.Trim();
                        if (!string.IsNullOrWhiteSpace(newBackup))
                        {
                            _config.BackupDirectory = newBackup;
                            _configService.SaveConfig(_config);
                            PrintColored("Backup directory updated.", ConsoleColor.Green);
                        }
                        WaitForKey();
                        break;
                    case "4":
                        _config.CompressBackups = !_config.CompressBackups;
                        _configService.SaveConfig(_config);
                        PrintColored($"Compression changed to: {(_config.CompressBackups ? "Enabled (.zip)" : "Disabled (Folders)")}", ConsoleColor.Green);
                        WaitForKey();
                        break;
                    case "5":
                        Console.Write("\nEnter maximum user backups to keep per campaign (0 for unlimited): ");
                        if (int.TryParse(Console.ReadLine()?.Trim(), out int maxBackups) && maxBackups >= 0)
                        {
                            _config.MaxBackupsToKeep = maxBackups;
                            _configService.SaveConfig(_config);
                            PrintColored($"Retention limit set to: {(_config.MaxBackupsToKeep > 0 ? _config.MaxBackupsToKeep.ToString() : "Unlimited")}", ConsoleColor.Green);
                        }
                        else
                        {
                            PrintColored("Invalid number entered.", ConsoleColor.Red);
                        }
                        WaitForKey();
                        break;
                    case "6":
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = _configService.ConfigFilePath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            PrintColored($"Failed to open config file: {ex.Message}", ConsoleColor.Red);
                            WaitForKey();
                        }
                        break;
                    case "7":
                        try
                        {
                            PrintColored("--> Rebuilding vault index from on-disk .sav files...", ConsoleColor.Cyan);
                            _backupService.VaultService.RebuildIndexFromDisk();
                            PrintColored("✔ Save Vault index successfully rebuilt from disk.", ConsoleColor.Green);
                        }
                        catch (Exception ex)
                        {
                            PrintColored($"✖ Rebuild failed: {ex.Message}", ConsoleColor.Red);
                        }
                        WaitForKey();
                        break;
                    case "8":
                        try
                        {
                            PrintColored("--> Scanning for legacy nested backup folders...", ConsoleColor.Cyan);
                            int migrated = _backupService.VaultService.MigrateLegacyBackups();
                            PrintColored($"✔ Successfully migrated {migrated} legacy save file(s) into flat vault.", ConsoleColor.Green);
                        }
                        catch (Exception ex)
                        {
                            PrintColored($"✖ Migration failed: {ex.Message}", ConsoleColor.Red);
                        }
                        WaitForKey();
                        break;
                    case "0":
                    case "exit":
                        inSettings = false;
                        break;
                }
            }
        }

        private static void HandleCommandLineArgs(string[] args)
        {
            string command = args[0].ToLowerInvariant();
            switch (command)
            {
                case "--backup":
                case "-b":
                    string? customName = null;
                    string? specificCampaign = null;

                    for (int i = 1; i < args.Length; i++)
                    {
                        if ((args[i] == "--campaign" || args[i] == "-c") && i + 1 < args.Length)
                        {
                            specificCampaign = args[++i];
                        }
                        else if (!args[i].StartsWith("-"))
                        {
                            customName = args[i];
                        }
                    }

                    try
                    {
                        string target = !string.IsNullOrWhiteSpace(specificCampaign)
                            ? specificCampaign
                            : _backupService.GetMostRecentCampaign();

                        var entry = _backupService.CreateCampaignBackup(target, customName);
                        Console.WriteLine($"✔ [OK] Backup successful: [{entry.CampaignName}] {entry.Name} ({entry.FormattedSize}, {entry.FileCount} files)");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"✖ [ERROR] Backup error: {ex.Message}");
                        Environment.ExitCode = 1;
                    }
                    break;

                case "--backup-all":
                    string? allTag = args.Length > 1 && !args[1].StartsWith("-") ? args[1] : null;
                    try
                    {
                        var entries = _backupService.CreateAllCampaignsBackup(allTag);
                        Console.WriteLine($"✔ [OK] Successfully backed up {entries.Count} campaign(s):");
                        foreach (var e in entries)
                        {
                            Console.WriteLine($"  [{e.CampaignName}] -> {e.Name} ({e.FormattedSize})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"✖ [ERROR] Backup error: {ex.Message}");
                        Environment.ExitCode = 1;
                    }
                    break;

                case "--list":
                case "-l":
                    string? filterCampaign = null;
                    for (int i = 1; i < args.Length; i++)
                    {
                        if ((args[i] == "--campaign" || args[i] == "-c") && i + 1 < args.Length)
                        {
                            filterCampaign = args[++i];
                        }
                    }

                    var backups = _backupService.GetBackups();
                    if (!string.IsNullOrWhiteSpace(filterCampaign))
                    {
                        backups = backups.Where(b => b.CampaignName.Equals(filterCampaign, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    Console.WriteLine($"Total Backups: {backups.Count}");
                    foreach (var b in backups)
                    {
                        Console.WriteLine($"[{b.CreatedAt:yyyy-MM-dd HH:mm:ss}] [{b.CampaignName}] {b.Name} ({b.FormattedSize})");
                    }
                    break;

                case "--help":
                case "-h":
                case "/?":
                    Console.WriteLine("Total War: ROME REMASTERED - Save Manager");
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  RRM-SM                                  Launch interactive menu");
                    Console.WriteLine("  RRM-SM --backup [name] [--campaign <c>] Snapshot specified or most recent campaign");
                    Console.WriteLine("  RRM-SM --backup-all [name]              Snapshot all active campaigns into faction folders");
                    Console.WriteLine("  RRM-SM --list [--campaign <c>]          List available snapshots (optionally filter by campaign)");
                    Console.WriteLine("  RRM-SM --help                           Display this help screen");
                    break;

                default:
                    Console.Error.WriteLine($"Unknown option '{args[0]}'. Use --help for usage.");
                    Environment.ExitCode = 1;
                    break;
            }
        }

        private static string Truncate(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLen ? value : value.Substring(0, maxLen - 1) + "…";
        }

        private static void PrintColored(string message, ConsoleColor color)
        {
            string prefix = "";
            string cleanMessage = message;

            if (cleanMessage.StartsWith("✔ "))
            {
                prefix = "✔ [OK] ";
                cleanMessage = cleanMessage.Substring(2);
            }
            else if (cleanMessage.StartsWith("✖ "))
            {
                prefix = "✖ [ERROR] ";
                cleanMessage = cleanMessage.Substring(2);
            }
            else if (cleanMessage.StartsWith("--> "))
            {
                prefix = "ℹ [INFO] ";
                cleanMessage = cleanMessage.Substring(4);
            }
            else if (color == ConsoleColor.Green || color == ConsoleColor.DarkGreen)
            {
                prefix = "✔ [OK] ";
            }
            else if (color == ConsoleColor.Red)
            {
                prefix = "✖ [ERROR] ";
            }
            else if (color == ConsoleColor.Yellow)
            {
                prefix = "⚠ [WARN] ";
            }
            else if (color == ConsoleColor.Cyan)
            {
                prefix = "ℹ [INFO] ";
            }

            Console.ForegroundColor = color;
            Console.WriteLine($"{prefix}{cleanMessage}");
            Console.ResetColor();
        }

        private static void WaitForKey()
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\nPress any key to continue...");
            Console.ResetColor();
            try
            {
                Console.ReadKey(true);
            }
            catch { }
        }
    }
}