using System;

namespace ChromaticMenu.Shared
{
    public static class TelemetryDefaults
    {
        // The anon key is public by design; Supabase Row Level Security only lets it insert.
        public const string SupabaseUrl = "https://iqzssuggmcqburqrpvtr.supabase.co";
        public const string SupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImlxenNzdWdnbWNxYnVycXJwdnRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTAyMjYyNzQsImV4cCI6MjEwNTgwMjI3NH0.WPFeMFK4tRpnSksSO3tal9QhY0dBs5UwGVgAUtGIEps";

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

        public static string ResolveUrl(string configuredUrl)
        {
            return string.IsNullOrWhiteSpace(configuredUrl) ? SupabaseUrl : configuredUrl.Trim().TrimEnd('/');
        }

        public static string ResolveAnonKey(string configuredKey)
        {
            return string.IsNullOrWhiteSpace(configuredKey) ? SupabaseAnonKey : configuredKey.Trim();
        }

        public static string Truncate(string value, int maxLength, string fallback)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0) return fallback;
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
        }
    }
}
