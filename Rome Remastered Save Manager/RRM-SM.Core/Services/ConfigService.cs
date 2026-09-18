using System;
using System.IO;
using System.Text.Json;
using RRM_SM.Models;

namespace RRM_SM.Services
{
    public class ConfigService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly string _configFilePath;

        public ConfigService(string? customConfigPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customConfigPath))
            {
                _configFilePath = customConfigPath;
            }
            else
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _configFilePath = Path.Combine(baseDir, "config.json");
            }
        }

        public string ConfigFilePath => _configFilePath;

        public AppConfig LoadOrCreateConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        EnsureDefaults(config);
                        return config;
                    }
                }
            }
            catch (Exception)
            {
                // Silently fallback to defaults in library context
            }

            var newConfig = new AppConfig();
            EnsureDefaults(newConfig);
            SaveConfig(newConfig);
            return newConfig;
        }

        public void SaveConfig(AppConfig config)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception)
            {
                // Silently swallow in library context; callers can handle via try/catch
            }
        }

        private void EnsureDefaults(AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.GameSaveDirectory) || !Directory.Exists(config.GameSaveDirectory))
            {
                string? detected = AutoDetectRomeSaveDirectory();
                if (!string.IsNullOrWhiteSpace(detected))
                {
                    config.GameSaveDirectory = detected;
                }
            }

            if (string.IsNullOrWhiteSpace(config.BackupDirectory))
            {
                string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                config.BackupDirectory = Path.Combine(myDocuments, "Rome Remastered Backups");
            }
        }

        public static string? AutoDetectRomeSaveDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 1. Rome Remastered standard local path:
            string feralRome = Path.Combine(localAppData, "Feral Interactive", "Total War ROME REMASTERED", "VFS", "Local", "Rome", "saves");
            if (Directory.Exists(feralRome))
            {
                return feralRome;
            }

            // 2. Base VFS Local path fallback:
            string feralBase = Path.Combine(localAppData, "Feral Interactive", "Total War ROME REMASTERED", "VFS", "Local");
            if (Directory.Exists(feralBase))
            {
                return feralBase;
            }

            // 3. Check Steam Userdata folders if present
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string steamUserData = Path.Combine(programFilesX86, "Steam", "userdata");
            if (Directory.Exists(steamUserData))
            {
                try
                {
                    // ROME REMASTERED AppID is 885970
                    foreach (string userDir in Directory.GetDirectories(steamUserData))
                    {
                        string gameDir = Path.Combine(userDir, "885970", "remote");
                        if (Directory.Exists(gameDir))
                        {
                            return gameDir;
                        }
                    }
                }
                catch
                {
                    // Ignore access errors scanning Steam folders
                }
            }

            return null;
        }
    }
}

