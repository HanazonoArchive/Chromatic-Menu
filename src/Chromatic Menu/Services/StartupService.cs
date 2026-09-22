using System;
using Microsoft.Win32;

namespace ChromaticMenu.Services
{
    public class StartupService
    {
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppValueName = "Chromatic Menu";

        private static StartupService _instance;
        public static StartupService Instance => _instance ?? (_instance = new StartupService());

        public bool IsRunAtStartup()
        {
            try
            {
                // Check CurrentUser (HKCU) first
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(AppValueName);
                        if (val != null) return true;
                    }
                }

                // Check LocalMachine (HKLM) as fallback
                using (var key = Registry.LocalMachine.OpenSubKey(RunKeyPath, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(AppValueName);
                        if (val != null) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to read startup registry key: {ex.Message}");
            }
            return false;
        }

        public bool SetRunAtStartup(bool enable, out string error)
        {
            error = null;
            string exePath = null;
            try
            {
                var asm = typeof(StartupService).Assembly;
                if (!string.IsNullOrEmpty(asm.Location))
                {
                    exePath = asm.Location;
                }
                else
                {
                    exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }
            }
            catch
            {
                exePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chromatic Menu.exe");
            }

            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        error = "Unable to open HKCU Run registry key for writing.";
                        return false;
                    }

                    if (enable)
                    {
                        key.SetValue(AppValueName, $"\"{exePath}\"");
                        LoggerService.Instance.Info($"Enabled Start with Windows in HKCU: \"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue(AppValueName, false);
                        LoggerService.Instance.Info("Disabled Start with Windows in HKCU.");

                        // If previously registered in HKLM (e.g. from an older version), attempt cleanup if elevated
                        try
                        {
                            using (var hklmKey = Registry.LocalMachine.OpenSubKey(RunKeyPath, true))
                            {
                                hklmKey?.DeleteValue(AppValueName, false);
                            }
                        }
                        catch { }
                    }
                    return true;
                }
            }
            catch (UnauthorizedAccessException uex)
            {
                error = "Registry access restricted: " + uex.Message;
                LoggerService.Instance.Warn(error);
                return false;
            }
            catch (Exception ex)
            {
                error = $"Failed to update startup registry entry: {ex.Message}";
                LoggerService.Instance.Error("Registry startup update failed.", ex);
                return false;
            }
        }
    }
}
