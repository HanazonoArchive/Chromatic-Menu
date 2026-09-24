using System;
using System.ServiceProcess;
using System.Threading.Tasks;
using ChromaticMenu.Shared;
using Newtonsoft.Json;

namespace ChromaticMenu.Services
{
    // Launcher-side calls to Supabase (game requests, connection test) and
    // the telemetry service status shown in Settings.
    public sealed class OnlineService
    {
        private static OnlineService _instance;
        public static OnlineService Instance => _instance ?? (_instance = new OnlineService());

        private readonly SupabaseRestClient _client = new SupabaseRestClient();
        private DateTime _lastGameRequestUtc = DateTime.MinValue;

        public TimeSpan GameRequestCooldownRemaining
        {
            get
            {
                var remaining = _lastGameRequestUtc + TelemetryDefaults.GameRequestCooldown - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        public async Task<string> SendGameRequestAsync(TelemetrySettings settings, string menuName, string title, string description)
        {
            if (!TelemetryDefaults.IsConfigured(settings))
            {
                return "Requests are not set up on this PC yet. Please tell the staff.";
            }

            var body = new
            {
                pc_name = TelemetryDefaults.Truncate(Environment.MachineName, TelemetryDefaults.MaxNameLength, "Unknown PC"),
                menu_name = TelemetryDefaults.Truncate(menuName, TelemetryDefaults.MaxNameLength, TelemetryDefaults.DefaultMenuName),
                title = title.Trim(),
                description = (description ?? string.Empty).Trim()
            };

            var result = await _client.InsertAsync(
                TelemetryDefaults.NormalizeUrl(settings.SupabaseUrl),
                TelemetryDefaults.NormalizeKey(settings.SupabaseAnonKey),
                "game_requests",
                JsonConvert.SerializeObject(new[] { body })).ConfigureAwait(false);

            if (result.Success)
            {
                _lastGameRequestUtc = DateTime.UtcNow;
                LoggerService.Instance.Info("Game request sent.");
                return null;
            }

            LoggerService.Instance.Warn($"Game request failed (HTTP {result.StatusCode}): {result.Error}");
            if (result.Error != null && result.Error.StartsWith("cooldown", StringComparison.OrdinalIgnoreCase))
            {
                _lastGameRequestUtc = DateTime.UtcNow;
                return "You can send another request in a few minutes.";
            }
            if (result.StatusCode == 0)
            {
                return "Could not reach the server. Please check the internet connection and try again.";
            }
            return "The request could not be sent right now. Please try again later.";
        }

        public async Task<string> TestConnectionAsync(string url, string anonKey)
        {
            url = TelemetryDefaults.NormalizeUrl(url);
            anonKey = TelemetryDefaults.NormalizeKey(anonKey);
            if (url == null || anonKey == null)
            {
                return "Enter the Supabase project URL and anon key first.";
            }

            var result = await _client.CheckConnectionAsync(url, anonKey).ConfigureAwait(false);
            if (result.Success) return null;
            if (result.StatusCode == 401 || result.StatusCode == 403) return "The anon key was rejected by Supabase.";
            if (result.StatusCode == 0) return "Could not reach Supabase: " + result.Error;
            return $"Supabase answered HTTP {result.StatusCode}: {result.Error}";
        }

        public Task<string> GetServiceStatusAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using (var controller = new ServiceController(TelemetryDefaults.ServiceName))
                    {
                        switch (controller.Status)
                        {
                            case ServiceControllerStatus.Running: return "Running";
                            case ServiceControllerStatus.Stopped: return "Stopped";
                            case ServiceControllerStatus.StartPending: return "Starting";
                            case ServiceControllerStatus.StopPending: return "Stopping";
                            default: return controller.Status.ToString();
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    return "Not installed";
                }
                catch (Exception ex)
                {
                    return "Unknown (" + ex.Message + ")";
                }
            });
        }
    }
}
