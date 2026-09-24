using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ChromaticMenu.Shared
{
    public sealed class ForegroundDetector
    {
        private static readonly string[] ShellWindowClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
        private readonly string _windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private readonly uint _ownProcessId = (uint)Process.GetCurrentProcess().Id;
        private readonly Dictionary<string, string> _descriptionCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _menuTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _lastDetectedProgram;
        private DateTime _lastConfigLoadUtc = DateTime.MinValue;

        private static class NativeMethods
        {
            public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);
        }

        public string LastDetectedProgram => _lastDetectedProgram;

        public void ReloadMenuTargets(string configPath)
        {
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return;
            try
            {
                string json;
                using (var stream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                var root = JObject.Parse(json);
                var items = root["items"] as JArray;
                if (items == null) return;

                lock (_menuTargets)
                {
                    _menuTargets.Clear();
                    foreach (var item in items)
                    {
                        string name = (string)item["name"];
                        string target = (string)item["target"];
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target)) continue;

                        try
                        {
                            string expanded = Environment.ExpandEnvironmentVariables(target);
                            _menuTargets[expanded] = name;
                        }
                        catch { }
                    }
                }
                _lastConfigLoadUtc = DateTime.UtcNow;
            }
            catch { }
        }

        public string Detect(string configPath = null)
        {
            if (!string.IsNullOrEmpty(configPath) && (DateTime.UtcNow - _lastConfigLoadUtc).TotalMinutes >= 5)
            {
                ReloadMenuTargets(configPath);
            }

            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return _lastDetectedProgram ?? TelemetryDefaults.LauncherProgramName;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                return _lastDetectedProgram ?? TelemetryDefaults.LauncherProgramName;
            }

            if (pid == _ownProcessId)
            {
                _lastDetectedProgram = TelemetryDefaults.LauncherProgramName;
                return TelemetryDefaults.LauncherProgramName;
            }

            var className = new StringBuilder(64);
            if (NativeMethods.GetClassName(hwnd, className, className.Capacity) > 0 &&
                Array.IndexOf(ShellWindowClasses, className.ToString()) >= 0)
            {
                _lastDetectedProgram = TelemetryDefaults.LauncherProgramName;
                return TelemetryDefaults.LauncherProgramName;
            }

            string exePath = GetProcessPath(pid);
            if (exePath == null)
            {
                return _lastDetectedProgram ?? TelemetryDefaults.UnknownProgramName;
            }

            string fileName = Path.GetFileName(exePath);
            if (string.Equals(fileName, "Chromatic Menu.exe", StringComparison.OrdinalIgnoreCase))
            {
                _lastDetectedProgram = TelemetryDefaults.LauncherProgramName;
                return TelemetryDefaults.LauncherProgramName;
            }

            if (exePath.StartsWith(_windowsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _lastDetectedProgram = TelemetryDefaults.WindowsProgramName;
                return TelemetryDefaults.WindowsProgramName;
            }

            lock (_menuTargets)
            {
                if (_menuTargets.TryGetValue(exePath, out string menuName))
                {
                    string name = TelemetryDefaults.Truncate(menuName, TelemetryDefaults.MaxProgramLength, TelemetryDefaults.UnknownProgramName);
                    _lastDetectedProgram = name;
                    return name;
                }
            }

            string described = DescribeExecutable(exePath);
            _lastDetectedProgram = described;
            return described;
        }

        private static string GetProcessPath(uint pid)
        {
            IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var buffer = new StringBuilder(1024);
                uint size = (uint)buffer.Capacity;
                return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        private string DescribeExecutable(string exePath)
        {
            lock (_descriptionCache)
            {
                if (_descriptionCache.TryGetValue(exePath, out string cached)) return cached;
            }

            string name = null;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
            }
            catch { }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(exePath);
            }

            name = TelemetryDefaults.Truncate(name, TelemetryDefaults.MaxProgramLength, TelemetryDefaults.UnknownProgramName);

            lock (_descriptionCache)
            {
                if (_descriptionCache.Count < 256)
                {
                    _descriptionCache[exePath] = name;
                }
            }

            return name;
        }
    }
}
