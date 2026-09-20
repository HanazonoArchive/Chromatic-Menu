using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace ChromaticMenu.Services
{
    public class FontService
    {
        private static readonly string[] PreferredFontFamilies = new[]
        {
            "Segoe UI",
            "Arial",
            "Tahoma",
            "Verdana",
            "Calibri",
            "Bahnschrift",
            "Consolas"
        };

        private static FontService _instance;
        public static FontService Instance => _instance ?? (_instance = new FontService());

        public List<string> GetCuratedInstalledFonts()
        {
            var installedFamilyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var family in Fonts.SystemFontFamilies)
                {
                    // Use Source name
                    if (!string.IsNullOrEmpty(family.Source))
                    {
                        installedFamilyNames.Add(family.Source);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to enumerate system font families: {ex.Message}");
            }

            var curated = new List<string>();
            foreach (var font in PreferredFontFamilies)
            {
                if (installedFamilyNames.Contains(font))
                {
                    curated.Add(font);
                }
            }

            // Always ensure at least Segoe UI is in the list as default
            if (curated.Count == 0)
            {
                curated.Add("Segoe UI");
            }

            return curated;
        }
    }
}
