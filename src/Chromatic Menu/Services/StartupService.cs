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
                using (var key = Registry.LocalMachine.OpenSubKey(RunKeyPath, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(AppValueName);
                        return val != null;
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
                exePath = AppDomain.CurrentDomain.BaseDirectory;
            }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        error = "Unable to open HKLM Run registry key for writing.";
                        return false;
                    }

                    if (enable)
                    {
                        key.SetValue(AppValueName, $"\"{exePath}\"");
                        LoggerService.Instance.Info($"Enabled Start with Windows in HKLM: \"{exePath}\"");
                    }
                    else
                    {
                        key.DeleteValue(AppValueName, false);
                        LoggerService.Instance.Info("Disabled Start with Windows in HKLM.");
                    }
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                error = "Administrator rights are required to configure 'Start with Windows' in HKEY_LOCAL_MACHINE.";
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
