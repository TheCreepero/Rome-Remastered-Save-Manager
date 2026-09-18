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

                switch (choice)
                {
                    case "1":
                        PerformQuickBackup();
                        break;
                    case "2":
                        PerformNamedBackup();
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
            Console.Write(" | Max Backups: ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(_config.MaxBackupsToKeep > 0 ? _config.MaxBackupsToKeep.ToString() : "Unlimited");

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
        }

        private static void PrintMainMenu()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  [1] Quick Backup (Timestamped snapshot of all active saves)");
            Console.WriteLine("  [2] Named Backup (Tag snapshot e.g. 'Turn 50 Before Siege')");
            Console.WriteLine("  [3] Restore Backup (Restore a saved state with safety fallback)");
            Console.WriteLine("  [4] List All Backups");
            Console.WriteLine("  [5] Open Active Saves in File Explorer");
            Console.WriteLine("  [6] Open Backup Directory in File Explorer");
            Console.WriteLine("  [7] Configuration & Settings");
            Console.WriteLine("  [0] Exit");
            Console.ResetColor();
        }

        private static void PerformQuickBackup()
        {
            try
            {
                PrintColored("--> Starting Quick Backup...", ConsoleColor.Cyan);
                var entry = _backupService.CreateBackup();
                PrintColored($"✔ Backup created successfully: {entry.Name}", ConsoleColor.Green);
                Console.WriteLine($"   Files: {entry.FileCount} | Total Size: {entry.FormattedSize}");
                Console.WriteLine($"   Location: {entry.FullPath}");
            }
            catch (Exception ex)
            {
                PrintColored($"✖ Backup failed: {ex.Message}", ConsoleColor.Red);
            }
            WaitForKey();
        }

        private static void PerformNamedBackup()
        {
            Console.Write("Enter a tag / custom name for this backup (e.g. 'Julii_Turn42'): ");
            string? name = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                PrintColored("No name provided. Cancelling backup.", ConsoleColor.Yellow);
                WaitForKey();
                return;
            }

            try
            {
                PrintColored($"--> Creating backup '{name}'...", ConsoleColor.Cyan);
                var entry = _backupService.CreateBackup(name);
                PrintColored($"✔ Named backup created: {entry.Name}", ConsoleColor.Green);
                Console.WriteLine($"   Files: {entry.FileCount} | Total Size: {entry.FormattedSize}");
                Console.WriteLine($"   Location: {entry.FullPath}");
            }
            catch (Exception ex)
            {
                PrintColored($"✖ Backup failed: {ex.Message}", ConsoleColor.Red);
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

            Console.WriteLine($"{"#",-4} {"Date / Time",-20} {"Size",-12} {"Files",-7} {"Name"}");
            Console.WriteLine(new string('-', 78));

            for (int i = 0; i < backups.Count; i++)
            {
                var b = backups[i];
                string marker = b.IsSafetyBackup ? "[SAFETY] " : "";
                Console.ForegroundColor = b.IsSafetyBackup ? ConsoleColor.DarkYellow : ConsoleColor.Gray;
                Console.WriteLine($"{i + 1,-4} {b.CreatedAt:yyyy-MM-dd HH:mm:ss,-20} {b.FormattedSize,-12} {b.FileCount,-7} {marker}{b.Name}");
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

            Console.WriteLine($"{"#",-4} {"Date / Time",-20} {"Size",-12} {"Name"}");
            Console.WriteLine(new string('-', 70));

            for (int i = 0; i < backups.Count; i++)
            {
                var b = backups[i];
                string marker = b.IsSafetyBackup ? "[SAFETY] " : "";
                Console.WriteLine($"{i + 1,-4} {b.CreatedAt:yyyy-MM-dd HH:mm:ss,-20} {b.FormattedSize,-12} {marker}{b.Name}");
            }

            Console.WriteLine("\nEnter the backup number to restore (or press Enter / 0 to cancel): ");
            string? input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input) || input == "0" || !int.TryParse(input, out int selection) || selection < 1 || selection > backups.Count)
            {
                PrintColored("Restore cancelled.", ConsoleColor.DarkGray);
                WaitForKey();
                return;
            }

            var chosen = backups[selection - 1];

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"\nWARNING: This will overwrite files in your active save directory with '{chosen.Name}'!");
            Console.ResetColor();
            Console.Write("\nAre you sure you want to proceed? (yes/no): ");

            string? confirm = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (confirm != "yes" && confirm != "y")
            {
                PrintColored("Restore aborted by user.", ConsoleColor.Yellow);
                WaitForKey();
                return;
            }

            try
            {
                PrintColored("--> Creating safety backup of current active saves...", ConsoleColor.DarkCyan);
                _backupService.RestoreBackup(chosen, createSafetyBackup: true);
                PrintColored($"✔ Successfully restored: {chosen.Name}", ConsoleColor.Green);
                PrintColored("A pre-restore safety snapshot was created in case you need to revert.", ConsoleColor.DarkGreen);
            }
            catch (Exception ex)
            {
                PrintColored($"✖ Restore failed: {ex.Message}", ConsoleColor.Red);
            }

            WaitForKey();
        }

        private static void OpenFolderInExplorer(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                PrintColored($"{label} path is not configured.", ConsoleColor.Red);
                WaitForKey();
                return;
            }

            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    PrintColored($"Could not create {label}: {ex.Message}", ConsoleColor.Red);
                    WaitForKey();
                    return;
                }
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
                PrintColored($"Opened {label} in File Explorer.", ConsoleColor.Green);
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
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("================================================================================");
                Console.WriteLine("                          SETTINGS & CONFIGURATION                              ");
                Console.WriteLine("================================================================================");
                Console.ResetColor();

                Console.WriteLine($"  [1] Game Save Directory : {_config.GameSaveDirectory}");
                Console.WriteLine($"  [2] Auto-Detect Rome Save Directory");
                Console.WriteLine($"  [3] Backup Directory    : {_config.BackupDirectory}");
                Console.WriteLine($"  [4] Compression         : {(_config.CompressBackups ? "Enabled (.zip)" : "Disabled (Folders)")}");
                Console.WriteLine($"  [5] Retention Limit     : {(_config.MaxBackupsToKeep > 0 ? $"{_config.MaxBackupsToKeep} backups" : "Unlimited")}");
                Console.WriteLine($"  [6] Open config.json in Editor");
                Console.WriteLine($"  [0] Return to Main Menu");
                Console.WriteLine();

                Console.Write("Select an option [0-6]: ");
                string? opt = Console.ReadLine()?.Trim();

                switch (opt)
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
                        Console.Write("\nEnter maximum user backups to keep (0 for unlimited): ");
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
                    string? customName = args.Length > 1 ? args[1] : null;
                    try
                    {
                        var entry = _backupService.CreateBackup(customName);
                        Console.WriteLine($"Backup successful: {entry.Name} ({entry.FormattedSize}, {entry.FileCount} files)");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Backup error: {ex.Message}");
                        Environment.ExitCode = 1;
                    }
                    break;

                case "--list":
                case "-l":
                    var backups = _backupService.GetBackups();
                    Console.WriteLine($"Total Backups: {backups.Count}");
                    foreach (var b in backups)
                    {
                        Console.WriteLine($"[{b.CreatedAt:yyyy-MM-dd HH:mm:ss}] {b.Name} ({b.FormattedSize})");
                    }
                    break;

                case "--help":
                case "-h":
                case "/?":
                    Console.WriteLine("Total War: ROME REMASTERED - Save Manager");
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  RRM-SM                   Launch interactive menu");
                    Console.WriteLine("  RRM-SM --backup [name]   Create an immediate backup (optional custom name)");
                    Console.WriteLine("  RRM-SM --list            List all available snapshots");
                    Console.WriteLine("  RRM-SM --help            Display this help screen");
                    break;

                default:
                    Console.Error.WriteLine($"Unknown option '{args[0]}'. Use --help for usage.");
                    Environment.ExitCode = 1;
                    break;
            }
        }

        private static void PrintColored(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
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
            catch
            {
                // In non-interactive or redirected input streams
            }
        }
    }
}