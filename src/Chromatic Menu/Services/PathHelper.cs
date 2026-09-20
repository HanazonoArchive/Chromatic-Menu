using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ChromaticMenu.Services
{
    public static class PathHelper
    {
        private static readonly List<Tuple<string, string>> Prefixes;

        static PathHelper()
        {
            var list = new List<Tuple<string, string>>();

            void AddPrefix(string envVar, Environment.SpecialFolder folder)
            {
                try
                {
                    string path = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        list.Add(Tuple.Create(path.TrimEnd('\\'), envVar));
                    }
                }
                catch { }
            }

            void AddEnvPrefix(string envVar, string envName)
            {
                try
                {
                    string path = Environment.GetEnvironmentVariable(envName);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        list.Add(Tuple.Create(path.TrimEnd('\\'), envVar));
                    }
                }
                catch { }
            }

            AddPrefix("%LocalAppData%", Environment.SpecialFolder.LocalApplicationData);
            AddPrefix("%AppData%", Environment.SpecialFolder.ApplicationData);
            AddPrefix("%ProgramFiles(x86)%", Environment.SpecialFolder.ProgramFilesX86);
            AddPrefix("%ProgramFiles%", Environment.SpecialFolder.ProgramFiles);
            AddPrefix("%ProgramData%", Environment.SpecialFolder.CommonApplicationData);
            AddPrefix("%UserProfile%", Environment.SpecialFolder.UserProfile);
            AddPrefix("%SystemRoot%", Environment.SpecialFolder.Windows);

            AddEnvPrefix("%SystemDrive%", "SystemDrive");

            // Sort by path length descending so longer paths match first
            Prefixes = list
                .GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(x => x.Item1.Length)
                .ToList();
        }

        public static string CollapsePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string trimmed = path.Trim();

            // Do not collapse URLs
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            foreach (var prefix in Prefixes)
            {
                if (trimmed.StartsWith(prefix.Item1, StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmed.Length == prefix.Item1.Length)
                    {
                        return prefix.Item2;
                    }
                    if (trimmed[prefix.Item1.Length] == '\\')
                    {
                        return prefix.Item2 + trimmed.Substring(prefix.Item1.Length);
                    }
                }
            }

            return trimmed;
        }

        public static string ExpandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Environment.ExpandEnvironmentVariables(path.Trim());
        }
    }
}
