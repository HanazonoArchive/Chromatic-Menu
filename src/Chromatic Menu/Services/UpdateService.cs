using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChromaticMenu.Services
{
    public class UpdateCheckResult
    {
        public bool HasUpdate { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleasePageUrl { get; set; } = string.Empty;
        public bool IsBlockedByDeepFreeze { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class UpdateService
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/HanazonoArchive/Chromatic-Menu/releases/latest";
        private const string FallbackReleaseUrl = "https://github.com/HanazonoArchive";

        private static UpdateService _instance;
        public static UpdateService Instance => _instance ?? (_instance = new UpdateService());

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersionStr)
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = currentVersionStr ?? "1.0.0"
            };

            // Deep Freeze Pre-Check: evaluate if Frozen
            var dfState = DeepFreezeService.Instance.GetDeepFreezeState();
            if (dfState == DeepFreezeState.Frozen)
            {
                result.IsBlockedByDeepFreeze = true;
            }

            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "ChromaticMenu-AppUpdater");
                    client.Headers.Add("Accept", "application/vnd.github.v3+json");

                    string jsonResponse = await client.DownloadStringTaskAsync(GitHubApiUrl);
                    if (string.IsNullOrWhiteSpace(jsonResponse))
                    {
                        result.StatusMessage = "No release information returned from update server.";
                        return result;
                    }

                    var release = JObject.Parse(jsonResponse);
                    string tagName = release["tag_name"]?.ToString() ?? string.Empty;
                    result.ReleaseNotes = release["body"]?.ToString() ?? "Bug fixes and performance improvements.";
                    result.ReleasePageUrl = release["html_url"]?.ToString() ?? FallbackReleaseUrl;

                    string cleanTag = tagName.TrimStart('v', 'V');
                    result.LatestVersion = cleanTag;

                    if (IsNewerVersion(cleanTag, result.CurrentVersion))
                    {
                        result.HasUpdate = true;

                        // Find .msi installer asset if present
                        var assets = release["assets"] as JArray;
                        if (assets != null)
                        {
                            var msiAsset = assets.FirstOrDefault(a =>
                                (a["name"]?.ToString() ?? string.Empty).EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
                            if (msiAsset != null)
                            {
                                result.DownloadUrl = msiAsset["browser_download_url"]?.ToString();
                            }
                            else
                            {
                                var exeAsset = assets.FirstOrDefault(a =>
                                    (a["name"]?.ToString() ?? string.Empty).EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                                if (exeAsset != null)
                                {
                                    result.DownloadUrl = exeAsset["browser_download_url"]?.ToString();
                                }
                            }
                        }

                        if (result.IsBlockedByDeepFreeze)
                        {
                            result.StatusMessage = $"Update v{cleanTag} available, but Deep Freeze is Boot Frozen. Reboot into Thawed mode to apply.";
                        }
                        else
                        {
                            result.StatusMessage = $"New version v{cleanTag} is available!";
                        }
                    }
                    else
                    {
                        result.HasUpdate = false;
                        result.StatusMessage = $"Chromatic Menu is up to date (v{result.CurrentVersion}).";
                    }

                    return result;
                }
            }
            catch (WebException wex)
            {
                // Handle 404 (no releases published yet on GitHub repo) or offline
                if (wex.Response is HttpWebResponse httpResp && httpResp.StatusCode == HttpStatusCode.NotFound)
                {
                    result.HasUpdate = false;
                    result.LatestVersion = result.CurrentVersion;
                    result.StatusMessage = $"Chromatic Menu is up to date (v{result.CurrentVersion}).";
                }
                else
                {
                    LoggerService.Instance.Warn("Update check network error: " + wex.Message);
                    result.StatusMessage = "Unable to reach update server (check internet connection).";
                }
                return result;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Update check failed: " + ex.Message);
                result.StatusMessage = "Update check failed: " + ex.Message;
                return result;
            }
        }

        public async Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl, Action<int> onProgressPercent, Action<string> onError)
        {
            // Safeguard: Check Deep Freeze state immediately before download & install
            var dfState = DeepFreezeService.Instance.GetDeepFreezeState();
            if (dfState == DeepFreezeState.Frozen)
            {
                onError?.Invoke("Deep Freeze is currently Boot Frozen! Please thaw Deep Freeze before applying updates so files persist.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                onError?.Invoke("No installer download URL available for this release.");
                return false;
            }

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ChromaticMenu_Update");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                string ext = Path.GetExtension(downloadUrl);
                if (string.IsNullOrWhiteSpace(ext) || (!ext.Equals(".msi", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    ext = ".msi";
                }

                string installerPath = Path.Combine(tempDir, "ChromaticMenu-Update" + ext);

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "ChromaticMenu-AppUpdater");

                    client.DownloadProgressChanged += (s, e) =>
                    {
                        onProgressPercent?.Invoke(e.ProgressPercentage);
                    };

                    await client.DownloadFileTaskAsync(new Uri(downloadUrl), installerPath);
                }

                if (!File.Exists(installerPath) || new FileInfo(installerPath).Length == 0)
                {
                    onError?.Invoke("Downloaded installer file was invalid or empty.");
                    return false;
                }

                LoggerService.Instance.Info("Launching update installer: " + installerPath);

                // Launch MSI installer with standard UI or passive flag
                var psi = new ProcessStartInfo();
                if (ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    psi.FileName = "msiexec.exe";
                    psi.Arguments = $"/i \"{installerPath}\" /passive";
                }
                else
                {
                    psi.FileName = installerPath;
                    psi.Arguments = string.Empty;
                }
                psi.UseShellExecute = true;

                Process.Start(psi);

                // Gracefully shutdown current app to allow MSI to overwrite files
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        System.Windows.Application.Current.Shutdown();
                    }
                    catch
                    {
                        Environment.Exit(0);
                    }
                }));

                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Error("Failed downloading/launching update.", ex);
                onError?.Invoke("Failed to launch update: " + ex.Message);
                return false;
            }
        }

        public static bool IsNewerVersion(string latestStr, string currentStr)
        {
            if (string.IsNullOrWhiteSpace(latestStr)) return false;
            if (string.IsNullOrWhiteSpace(currentStr)) return true;

            string cleanLatest = NormalizeVersionString(latestStr);
            string cleanCurrent = NormalizeVersionString(currentStr);

            if (TryParseVersion(cleanLatest, out int[] latestParts) &&
                TryParseVersion(cleanCurrent, out int[] currentParts))
            {
                int maxLen = Math.Max(latestParts.Length, currentParts.Length);
                for (int i = 0; i < maxLen; i++)
                {
                    int l = i < latestParts.Length ? latestParts[i] : 0;
                    int c = i < currentParts.Length ? currentParts[i] : 0;
                    if (l > c) return true;
                    if (l < c) return false;
                }
                return false;
            }

            return string.Compare(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private static string NormalizeVersionString(string ver)
        {
            if (string.IsNullOrWhiteSpace(ver)) return string.Empty;
            string s = ver.Trim().TrimStart('v', 'V').Trim();
            int dashIdx = s.IndexOfAny(new[] { '-', '+' });
            if (dashIdx >= 0)
            {
                s = s.Substring(0, dashIdx);
            }
            return s.Trim();
        }

        private static bool TryParseVersion(string ver, out int[] parts)
        {
            parts = null;
            if (string.IsNullOrWhiteSpace(ver)) return false;
            string[] raw = ver.Split('.');
            var list = new System.Collections.Generic.List<int>();
            foreach (var r in raw)
            {
                if (int.TryParse(r.Trim(), out int val))
                {
                    list.Add(val);
                }
                else
                {
                    return false;
                }
            }
            if (list.Count == 0) return false;
            parts = list.ToArray();
            return true;
        }
    }
}
