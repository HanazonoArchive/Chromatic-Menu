using System.Collections.Generic;
using Newtonsoft.Json;

namespace ChromaticMenu.Models
{
    public class BrandingConfig
    {
        [JsonProperty("shopName")]
        public string ShopName { get; set; } = "PisoNet";

        [JsonProperty("logo")]
        public string Logo { get; set; }

        [JsonProperty("wallpaper")]
        public string Wallpaper { get; set; }
    }

    public class CustomColorsConfig
    {
        [JsonProperty("background")]
        public string Background { get; set; }

        [JsonProperty("surface")]
        public string Surface { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("accent")]
        public string Accent { get; set; }
    }

    public class AppearanceConfig
    {
        [JsonProperty("theme")]
        public string Theme { get; set; } = "dark";

        [JsonProperty("accent")]
        public string Accent { get; set; } = "#0284C7";

        [JsonProperty("customColors")]
        public CustomColorsConfig CustomColors { get; set; }

        [JsonProperty("fontFamily")]
        public string FontFamily { get; set; } = "Segoe UI";

        [JsonProperty("fontScale")]
        public double FontScale { get; set; } = 1.0;

        [JsonProperty("iconSize")]
        public int IconSize { get; set; } = 96;
    }

    public class AppConfig
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("passwordHash")]
        public string PasswordHash { get; set; } = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918"; // default "admin"

        [JsonProperty("mustChangePassword")]
        public bool MustChangePassword { get; set; } = true;

        [JsonProperty("branding")]
        public BrandingConfig Branding { get; set; } = new BrandingConfig();

        [JsonProperty("appearance")]
        public AppearanceConfig Appearance { get; set; } = new AppearanceConfig();

        [JsonProperty("startWithWindows")]
        public bool StartWithWindows { get; set; } = false;

        [JsonProperty("tabs")]
        public List<TabModel> Tabs { get; set; } = new List<TabModel>();

        [JsonProperty("items")]
        public List<ProgramItem> Items { get; set; } = new List<ProgramItem>();

        public static AppConfig CreateDefault()
        {
            var config = new AppConfig();
            config.Tabs.Add(new TabModel { Id = "lan-games", Name = "Lan Games", Icon = "gamepad-2" });
            config.Tabs.Add(new TabModel { Id = "online-games", Name = "Online Games", Icon = "globe" });
            config.Tabs.Add(new TabModel { Id = "browser", Name = "Browser", Icon = "app-window" });
            config.Tabs.Add(new TabModel { Id = "school-office", Name = "School & Office", Icon = "graduation-cap" });
            config.Tabs.Add(new TabModel { Id = "others", Name = "Others", Icon = "layout-grid" });
            return config;
        }
    }
}
