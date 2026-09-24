using System;

namespace ChromaticMenu.Shared
{
    public static class TelemetryDefaults
    {
        public const string ServiceName = "ChromaticTelemetry";
        public const string PipeName = "ChromaticTelemetry.Pipe";

        public const int DefaultIntervalSeconds = 60;
        public const int MinIntervalSeconds = 30;
        public const int MaxIntervalSeconds = 600;
        public const int BufferCap = 720;

        public const string DefaultMenuName = "PisoNet";
        public const string LauncherProgramName = "Chromatic Menu";
        public const string WindowsProgramName = "Windows";
        public const string UnknownProgramName = "Unknown";

        public const int MaxNameLength = 64;
        public const int MaxProgramLength = 128;
        public const int GameTitleMaxLength = 100;
        public const int GameDescriptionMaxLength = 500;
        public static readonly TimeSpan GameRequestCooldown = TimeSpan.FromMinutes(5);

        public static int ClampInterval(int seconds)
        {
            if (seconds <= 0) return DefaultIntervalSeconds;
            return Math.Max(MinIntervalSeconds, Math.Min(MaxIntervalSeconds, seconds));
        }

        // There is no built-in Supabase project: nothing is sent until a URL and
        // anon key are configured (Settings > Telemetry or the DataTool).
        public static string NormalizeUrl(string url)
        {
            return string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
        }

        public static string NormalizeKey(string key)
        {
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }

        public static bool IsConfigured(TelemetrySettings settings)
        {
            return settings != null && NormalizeUrl(settings.SupabaseUrl) != null && NormalizeKey(settings.SupabaseAnonKey) != null;
        }

        public static string Truncate(string value, int maxLength, string fallback)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0) return fallback;
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
        }
    }
}
