using System;
using Newtonsoft.Json;

namespace ChromaticMenu.Shared
{
    // One newline-delimited JSON line sent from the launcher to the service.
    public class TelemetryPipeMessage
    {
        public const string ProgramType = "program";
        public const string ReloadType = "reload";

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
        public string Value { get; set; }

        [JsonProperty("ts", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? Timestamp { get; set; }

        public static TelemetryPipeMessage ForProgram(string programName)
        {
            return new TelemetryPipeMessage { Type = ProgramType, Value = programName, Timestamp = DateTime.UtcNow };
        }

        public static TelemetryPipeMessage ForReload()
        {
            return new TelemetryPipeMessage { Type = ReloadType };
        }
    }
}
