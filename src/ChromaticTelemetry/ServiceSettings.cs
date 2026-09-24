using System;
using System.IO;
using System.Threading;
using ChromaticMenu.Shared;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace ChromaticTelemetry
{
    // Immutable snapshot of what the heartbeat loop needs from config.json.
    internal sealed class ServiceSettings
    {
        public bool StartWithWindows { get; private set; }
        public bool TelemetryEnabled { get; private set; }
        public string SupabaseUrl { get; private set; }
        public string SupabaseAnonKey { get; private set; }
        public string MenuName { get; private set; }
        public int IntervalSeconds { get; private set; }

        public bool HasProject => SupabaseUrl != null && SupabaseAnonKey != null;

        // Heartbeats are only sent on PCs the technician has set up to start
        // the launcher with Windows, with telemetry switched on and a Supabase
        // project configured.
        public bool ShouldSend => StartWithWindows && TelemetryEnabled && HasProject;

        public static ServiceSettings Disabled()
        {
            return new ServiceSettings
            {
                MenuName = TelemetryDefaults.DefaultMenuName,
                IntervalSeconds = TelemetryDefaults.DefaultIntervalSeconds
            };
        }

        private sealed class ConfigFile
        {
            [JsonProperty("startWithWindows")]
            public bool StartWithWindows { get; set; }

            [JsonProperty("branding")]
            public BrandingSection Branding { get; set; }

            [JsonProperty("telemetry")]
            public TelemetrySettings Telemetry { get; set; }
        }

        private sealed class BrandingSection
        {
            [JsonProperty("shopName")]
            public string ShopName { get; set; }
        }

        public static string FindDataDirectory()
        {
            string installDir = null;
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"Software\HanazonoArchive\ChromaticMenu"))
                {
                    installDir = key?.GetValue("InstallDir") as string;
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(installDir) && Directory.Exists(Path.Combine(installDir, "data")))
            {
                return Path.Combine(installDir, "data");
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (Directory.Exists(Path.Combine(baseDir, "data")))
            {
                return Path.Combine(baseDir, "data");
            }

            // Fallback for standard installed location
            string defaultProgFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Chromatic Menu", "data");
            if (Directory.Exists(defaultProgFiles))
            {
                return defaultProgFiles;
            }

            // Fallback for dev environment (e.g. running from build folder)
            try
            {
                string devDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Chromatic Menu", "bin", "Release", "net48", "data"));
                if (Directory.Exists(devDir)) return devDir;
            }
            catch { }

            return Path.Combine(baseDir, "data");
        }

        public static ServiceSettings Load(string configPath, out string error)
        {
            error = null;
            string json;
            try
            {
                json = ReadShared(configPath);
            }
            catch (FileNotFoundException)
            {
                error = "config.json not found at " + configPath;
                return Disabled();
            }
            catch (DirectoryNotFoundException)
            {
                error = "data folder not found for " + configPath;
                return Disabled();
            }
            catch (Exception ex)
            {
                error = "config.json could not be read: " + ex.Message;
                return Disabled();
            }

            ConfigFile file;
            try
            {
                file = JsonConvert.DeserializeObject<ConfigFile>(json);
            }
            catch (Exception ex)
            {
                error = "config.json is not valid JSON: " + ex.Message;
                return Disabled();
            }

            if (file == null)
            {
                error = "config.json is empty.";
                return Disabled();
            }

            var telemetry = file.Telemetry ?? new TelemetrySettings();
            return new ServiceSettings
            {
                StartWithWindows = file.StartWithWindows,
                TelemetryEnabled = telemetry.Enabled,
                SupabaseUrl = TelemetryDefaults.NormalizeUrl(telemetry.SupabaseUrl),
                SupabaseAnonKey = TelemetryDefaults.NormalizeKey(telemetry.SupabaseAnonKey),
                MenuName = TelemetryDefaults.Truncate(file.Branding?.ShopName, TelemetryDefaults.MaxNameLength, TelemetryDefaults.DefaultMenuName),
                IntervalSeconds = TelemetryDefaults.ClampInterval(telemetry.HeartbeatIntervalSeconds)
            };
        }

        // The launcher replaces config.json atomically, so allow concurrent
        // replace/delete and retry once if we catch it mid-swap.
        private static string ReadShared(string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException) when (attempt == 1 && File.Exists(path))
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}
