using Newtonsoft.Json;

namespace ChromaticMenu.Shared
{
    // The "telemetry" block of config.json, read by both the launcher and the service.
    public class TelemetrySettings
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        // Null or empty means "not set up": nothing is sent until both are filled in.
        [JsonProperty("supabaseUrl")]
        public string SupabaseUrl { get; set; }

        [JsonProperty("supabaseAnonKey")]
        public string SupabaseAnonKey { get; set; }

        [JsonProperty("heartbeatIntervalSeconds")]
        public int HeartbeatIntervalSeconds { get; set; } = TelemetryDefaults.DefaultIntervalSeconds;
    }
}
