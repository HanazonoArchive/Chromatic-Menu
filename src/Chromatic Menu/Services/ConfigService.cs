using System;
using System.IO;
using ChromaticMenu.Models;
using Newtonsoft.Json;

namespace ChromaticMenu.Services
{
    public class ConfigService
    {
        private static readonly object _lock = new object();
        private static ConfigService _instance;
        public static ConfigService Instance => _instance ?? (_instance = new ConfigService());

        private readonly string _dataDirectory;
        private readonly string _configPath;
        private readonly string _backupConfigPath;
        private readonly string _tempConfigPath;
        private readonly string _assetsDirectory;

        public string DataDirectory => _dataDirectory;
        public string AssetsDirectory => _assetsDirectory;
        public string ConfigPath => _configPath;

        public AppConfig Current { get; private set; }

        public bool IsDirectoryWritable { get; private set; } = true;

        public ConfigService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                var asm = typeof(ConfigService).Assembly;
                if (!string.IsNullOrEmpty(asm.Location))
                {
                    baseDir = Path.GetDirectoryName(asm.Location);
                }
            }
            catch { }

            _dataDirectory = Path.Combine(baseDir, "data");
            _configPath = Path.Combine(_dataDirectory, "config.json");
            _backupConfigPath = Path.Combine(_dataDirectory, "config.json.bak");
            _tempConfigPath = Path.Combine(_dataDirectory, "config.json.tmp");
            _assetsDirectory = Path.Combine(_dataDirectory, "assets");

            CheckDirectoryWritable();
        }

        private void CheckDirectoryWritable()
        {
            try
            {
                if (!Directory.Exists(_dataDirectory))
                {
                    Directory.CreateDirectory(_dataDirectory);
                }
                string testFile = Path.Combine(_dataDirectory, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                IsDirectoryWritable = true;
            }
            catch (Exception ex)
            {
                IsDirectoryWritable = false;
                LoggerService.Instance.Warn("Data directory is not writable: " + ex.Message);
            }
        }

        public AppConfig Load()
        {
            lock (_lock)
            {
                CheckDirectoryWritable();

                if (File.Exists(_configPath))
                {
                    try
                    {
                        string json = File.ReadAllText(_configPath);
                        var loaded = JsonConvert.DeserializeObject<AppConfig>(json);
                        if (loaded != null)
                        {
                            Current = Migrate(loaded);
                            return Current;
                        }
                        LoggerService.Instance.Warn("Config file was empty or deserialized to null. Attempting backup restore.");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Instance.Error("Failed to parse config.json. Attempting backup restore.", ex);
                    }
                }

                // Attempt backup recovery
                if (File.Exists(_backupConfigPath))
                {
                    try
                    {
                        string backupJson = File.ReadAllText(_backupConfigPath);
                        var backupConfig = JsonConvert.DeserializeObject<AppConfig>(backupJson);
                        if (backupConfig != null)
                        {
                            LoggerService.Instance.Info("Restoring config from config.json.bak.");
                            Current = Migrate(backupConfig);
                            Save(Current);
                            return Current;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Instance.Error("Failed to parse config.json.bak. Falling back to defaults.", ex);
                    }
                }

                // Fall back to defaults
                LoggerService.Instance.Info("Creating default configuration.");
                Current = AppConfig.CreateDefault();
                Save(Current);
                return Current;
            }
        }

        public bool Save(AppConfig config)
        {
            if (config == null) return false;

            lock (_lock)
            {
                try
                {
                    if (!Directory.Exists(_dataDirectory))
                    {
                        Directory.CreateDirectory(_dataDirectory);
                    }
                    if (!Directory.Exists(_assetsDirectory))
                    {
                        Directory.CreateDirectory(_assetsDirectory);
                    }

                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);

                    // 1. Write to temp file first to ensure complete serialization
                    File.WriteAllText(_tempConfigPath, json);

                    // 2. Backup existing config if it exists
                    if (File.Exists(_configPath))
                    {
                        try
                        {
                            File.Copy(_configPath, _backupConfigPath, true);
                        }
                        catch (Exception ex)
                        {
                            LoggerService.Instance.Warn("Could not update config.json.bak: " + ex.Message);
                        }
                    }

                    // 3. Atomically replace with robust fallback
                    bool saved = false;
                    try
                    {
                        if (File.Exists(_configPath))
                        {
                            File.Replace(_tempConfigPath, _configPath, _backupConfigPath, true);
                            saved = true;
                        }
                        else
                        {
                            File.Move(_tempConfigPath, _configPath);
                            saved = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Instance.Warn("File.Replace failed (" + ex.Message + "), falling back to direct stream write.");
                    }

                    // Fallback: direct atomic write stream with FileShare.ReadWrite and retry
                    if (!saved)
                    {
                        int maxRetries = 3;
                        for (int attempt = 1; attempt <= maxRetries; attempt++)
                        {
                            try
                            {
                                using (var fs = new FileStream(_configPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                                using (var writer = new StreamWriter(fs, System.Text.Encoding.UTF8))
                                {
                                    writer.Write(json);
                                    writer.Flush();
                                }
                                saved = true;
                                break;
                            }
                            catch (Exception)
                            {
                                if (attempt == maxRetries)
                                {
                                    throw;
                                }
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                    }

                    // 4. Clean up temp file
                    try
                    {
                        if (File.Exists(_tempConfigPath))
                        {
                            File.Delete(_tempConfigPath);
                        }
                    }
                    catch { }

                    Current = config;
                    LoggerService.Instance.Info(string.Format("Configuration saved successfully ({0} items, {1} tabs).",
                        config.Items?.Count ?? 0, config.Tabs?.Count ?? 0));
                    return true;
                }
                catch (Exception ex)
                {
                    LoggerService.Instance.Error("Failed to save config.", ex);
                    return false;
                }
            }
        }

        private AppConfig Migrate(AppConfig config)
        {
            if (config.Version < 1)
            {
                config.Version = 1;
            }
            return config;
        }
    }
}
