using System;
using System.IO;
using ChromaticMenu.Models;
using ChromaticMenu.Shared;
using Newtonsoft.Json;

namespace ChromaticMenu.Services
{
    public class ConfigService
    {
        private static readonly object _lock = new object();
        private static ConfigService _instance;
        public static ConfigService Instance => _instance ?? (_instance = new ConfigService());

        private string _dataDirectory;
        private string _configPath;
        private string _backupConfigPath;
        private string _tempConfigPath;
        private string _assetsDirectory;

        public string DataDirectory => _dataDirectory;
        public string AssetsDirectory => _assetsDirectory;
        public string ConfigPath => _configPath;

        public AppConfig Current { get; private set; }

        public bool IsDirectoryWritable { get; private set; } = true;

        public ConfigService()
        {
            _instance = this;
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

            InitPaths(baseDir);
            CheckDirectoryWritable(baseDir);
        }

        private void InitPaths(string baseDir)
        {
            _dataDirectory = Path.Combine(baseDir, "data");
            _configPath = Path.Combine(_dataDirectory, "config.json");
            _backupConfigPath = Path.Combine(_dataDirectory, "config.json.bak");
            _tempConfigPath = Path.Combine(_dataDirectory, "config.json.tmp");
            _assetsDirectory = Path.Combine(_dataDirectory, "assets");
            try
            {
                LoggerService.Instance.ConfigureDirectory(_dataDirectory);
            }
            catch { }
        }

        private void CheckDirectoryWritable(string originalBaseDir = null)
        {
            try
            {
                if (!Directory.Exists(_dataDirectory))
                {
                    Directory.CreateDirectory(_dataDirectory);
                    TryGrantDirectoryPermissions(_dataDirectory);
                }
                if (!Directory.Exists(_assetsDirectory))
                {
                    Directory.CreateDirectory(_assetsDirectory);
                    TryGrantDirectoryPermissions(_assetsDirectory);
                }

                string testFile = Path.Combine(_dataDirectory, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                IsDirectoryWritable = true;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Primary data directory is not writable (" + _dataDirectory + "): " + ex.Message);

                // Fallback to LocalAppData so user configurations and assets are always writable
                try
                {
                    string fallbackBase = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ChromaticMenu");
                    string oldConfigPath = _configPath;

                    InitPaths(fallbackBase);

                    if (!Directory.Exists(_dataDirectory))
                    {
                        Directory.CreateDirectory(_dataDirectory);
                    }
                    if (!Directory.Exists(_assetsDirectory))
                    {
                        Directory.CreateDirectory(_assetsDirectory);
                    }

                    // If an existing config exists in the restricted directory, copy it to fallback
                    if (File.Exists(oldConfigPath) && !File.Exists(_configPath))
                    {
                        try { File.Copy(oldConfigPath, _configPath, true); } catch { }
                    }

                    string testFile = Path.Combine(_dataDirectory, ".write_test");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    IsDirectoryWritable = true;
                    LoggerService.Instance.Info("Successfully redirected data directory to: " + _dataDirectory);
                }
                catch (Exception fallbackEx)
                {
                    IsDirectoryWritable = false;
                    LoggerService.Instance.Error("Fallback data directory failed: " + fallbackEx.Message, fallbackEx);
                }
            }
        }

        private static void TryGrantDirectoryPermissions(string dirPath)
        {
            try
            {
                var di = new DirectoryInfo(dirPath);
                var ds = di.GetAccessControl();
                var usersSid = new System.Security.Principal.SecurityIdentifier(
                    System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
                var rule = new System.Security.AccessControl.FileSystemAccessRule(
                    usersSid,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow);
                ds.AddAccessRule(rule);
                di.SetAccessControl(ds);
            }
            catch { }
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
                            Current = loaded;
                            if (Migrate(Current))
                            {
                                Save(Current);
                            }
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
                            Current = backupConfig;
                            Migrate(Current);
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

        // Only ever adds what an older config lacks; existing tabs, items,
        // branding, appearance and password are left exactly as they were.
        private static bool Migrate(AppConfig config)
        {
            if (config.Version >= AppConfig.CurrentVersion && config.Telemetry != null)
            {
                return false;
            }

            if (config.Telemetry == null)
            {
                config.Telemetry = new TelemetrySettings();
            }
            int from = config.Version;
            config.Version = AppConfig.CurrentVersion;
            LoggerService.Instance.Info($"Migrated config from version {from} to {AppConfig.CurrentVersion}.");
            return true;
        }
    }
}
