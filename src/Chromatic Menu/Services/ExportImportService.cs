using System;
using System.IO;
using System.IO.Compression;
using ChromaticMenu.Models;
using Newtonsoft.Json;

namespace ChromaticMenu.Services
{
    public class ExportManifest
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("appVersion")]
        public string AppVersion { get; set; } = "1.0.0";

        [JsonProperty("exportDate")]
        public string ExportDate { get; set; } = DateTime.UtcNow.ToString("o");
    }

    public class ExportImportService
    {
        private static ExportImportService _instance;
        public static ExportImportService Instance => _instance ?? (_instance = new ExportImportService());

        public bool ExportBackup(string destinationZipPath, out string error)
        {
            error = null;
            try
            {
                string dataDir = ConfigService.Instance.DataDirectory;
                string configPath = ConfigService.Instance.ConfigPath;
                string assetsDir = ConfigService.Instance.AssetsDirectory;

                if (!File.Exists(configPath))
                {
                    error = "Configuration file does not exist.";
                    return false;
                }

                if (File.Exists(destinationZipPath))
                {
                    File.Delete(destinationZipPath);
                }

                using (var zipStream = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    // 1. Add manifest.json
                    var manifest = new ExportManifest();
                    string manifestJson = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(manifestEntry.Open()))
                    {
                        writer.Write(manifestJson);
                    }

                    // 2. Add config.json
                    var configEntry = archive.CreateEntry("config.json", CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(configEntry.Open()))
                    {
                        writer.Write(File.ReadAllText(configPath));
                    }

                    // 3. Add assets directory
                    if (Directory.Exists(assetsDir))
                    {
                        var files = Directory.GetFiles(assetsDir, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            string relativePath = file.Substring(dataDir.Length).TrimStart('\\', '/');
                            string entryName = relativePath.Replace('\\', '/');
                            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                        }
                    }
                }

                LoggerService.Instance.Info($"Successfully exported configuration backup to '{destinationZipPath}'.");
                return true;
            }
            catch (Exception ex)
            {
                error = $"Export failed: {ex.Message}";
                LoggerService.Instance.Error("Export failed.", ex);
                return false;
            }
        }

        public bool ImportBackup(string sourceZipPath, out int brokenCount, out string error)
        {
            brokenCount = 0;
            error = null;

            try
            {
                if (!File.Exists(sourceZipPath))
                {
                    error = "Selected backup file does not exist.";
                    return false;
                }

                string dataDir = ConfigService.Instance.DataDirectory;
                string configPath = ConfigService.Instance.ConfigPath;
                string backupConfigPath = Path.Combine(dataDir, "config.json.bak");
                string assetsDir = ConfigService.Instance.AssetsDirectory;

                // 1. Validate ZIP structure & security (prevent Zip Slip)
                bool hasManifest = false;
                bool hasConfig = false;

                using (var zipStream = new FileStream(sourceZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    string fullDestDir = Path.GetFullPath(dataDir) + Path.DirectorySeparatorChar;

                    foreach (var entry in archive.Entries)
                    {
                        if (string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase))
                        {
                            hasManifest = true;
                        }
                        if (string.Equals(entry.FullName, "config.json", StringComparison.OrdinalIgnoreCase))
                        {
                            hasConfig = true;
                        }

                        // Path traversal check
                        string destPath = Path.GetFullPath(Path.Combine(dataDir, entry.FullName));
                        if (!destPath.StartsWith(fullDestDir, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(destPath, Path.GetFullPath(dataDir), StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"Security check failed: ZIP entry '{entry.FullName}' contains an invalid path.";
                            LoggerService.Instance.Warn(error);
                            return false;
                        }
                    }

                    if (!hasManifest || !hasConfig)
                    {
                        error = "The selected file is not a valid Chromatic Menu backup (missing manifest or config).";
                        return false;
                    }
                }

                // 2. Backup current config
                if (File.Exists(configPath))
                {
                    File.Copy(configPath, backupConfigPath, true);
                }

                // 3. Extract ZIP contents safely
                using (var zipStream = new FileStream(sourceZipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        // Skip directory entries
                        if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                        {
                            continue;
                        }

                        // Skip manifest.json when writing to disk
                        if (string.Equals(entry.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string targetPath = Path.Combine(dataDir, entry.FullName);
                        string targetDir = Path.GetDirectoryName(targetPath);
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        entry.ExtractToFile(targetPath, true);
                    }
                }

                // 4. Reload config and count broken paths
                var loadedConfig = ConfigService.Instance.Load();
                if (loadedConfig?.Items != null)
                {
                    foreach (var item in loadedConfig.Items)
                    {
                        if (string.Equals(item.Kind, "url", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string expanded = Environment.ExpandEnvironmentVariables(item.Target ?? string.Empty);
                        if (!File.Exists(expanded) && !Directory.Exists(expanded))
                        {
                            brokenCount++;
                        }
                    }
                }

                LoggerService.Instance.Info($"Successfully imported backup from '{sourceZipPath}'. Broken shortcuts: {brokenCount}.");
                return true;
            }
            catch (Exception ex)
            {
                error = $"Import failed: {ex.Message}";
                LoggerService.Instance.Error("Import failed.", ex);
                return false;
            }
        }
    }
}
