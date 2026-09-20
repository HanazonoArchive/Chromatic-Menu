using Newtonsoft.Json;

namespace ChromaticMenu.Models
{
    public class IconConfig
    {
        [JsonProperty("source")]
        public string Source { get; set; } = "auto";

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("index")]
        public int Index { get; set; }
    }

    public class ProgramItem
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("tabId")]
        public string TabId { get; set; } = string.Empty;

        [JsonProperty("order")]
        public int Order { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("kind")]
        public string Kind { get; set; } = "exe";

        [JsonProperty("target")]
        public string Target { get; set; } = string.Empty;

        [JsonProperty("arguments")]
        public string Arguments { get; set; } = string.Empty;

        [JsonProperty("workingDirectory")]
        public string WorkingDirectory { get; set; } = string.Empty;

        [JsonProperty("icon")]
        public IconConfig Icon { get; set; } = new IconConfig();
    }
}
