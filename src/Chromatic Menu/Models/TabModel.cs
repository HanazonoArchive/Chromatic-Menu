using Newtonsoft.Json;

namespace ChromaticMenu.Models
{
    public class TabModel
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("icon")]
        public string Icon { get; set; } = "layout-grid";
    }
}
