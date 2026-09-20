using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ChromaticMenu.Native;
using ChromaticMenu.ViewModels;

namespace ChromaticMenu.Services
{
    public class LaunchService
    {
        private static LaunchService _instance;
        public static LaunchService Instance => _instance ?? (_instance = new LaunchService());

        public const int ERROR_ELEVATION_REQUIRED = 740;
        public const int ERROR_CANCELLED = 1223;

        public bool LaunchItem(ProgramItemViewModel item, out string errorMessage)
        {
            errorMessage = null;

            if (item == null)
            {
                errorMessage = "Invalid program item.";
                return false;
            }

            // Check if broken
            item.CheckTargetExists();
            if (item.IsBroken)
            {
                string exp = Environment.ExpandEnvironmentVariables(item.Target ?? string.Empty);
                errorMessage = $"Program not found: {exp}";
                return false;
            }

            string expandedTarget = Environment.ExpandEnvironmentVariables(item.Target ?? string.Empty);
            string expandedArgs = Environment.ExpandEnvironmentVariables(item.Arguments ?? string.Empty);
            string expandedWorkDir = Environment.ExpandEnvironmentVariables(item.WorkingDirectory ?? string.Empty);

            if (string.IsNullOrEmpty(expandedWorkDir) && File.Exists(expandedTarget))
            {
                expandedWorkDir = Path.GetDirectoryName(expandedTarget);
            }

            var psi = new ProcessStartInfo
            {
                FileName = expandedTarget,
                Arguments = expandedArgs,
                WorkingDirectory = expandedWorkDir,
                UseShellExecute = true
            };

            try
            {
                Process.Start(psi);
                LoggerService.Instance.Info($"Launched program: {item.Name} ({expandedTarget})");
                return true;
            }
            catch (Win32Exception winEx) when (winEx.NativeErrorCode == ERROR_ELEVATION_REQUIRED)
            {
                // SPEC section 9: If Windows says elevation is required (Win32 error 740), retry once with verb runas
                try
                {
                    psi.Verb = "runas";
                    Process.Start(psi);
                    LoggerService.Instance.Info($"Launched program with elevation: {item.Name}");
                    return true;
                }
                catch (Win32Exception uacEx) when (uacEx.NativeErrorCode == ERROR_CANCELLED)
                {
                    // User cancelled UAC prompt (error 1223): do nothing silently per SPEC section 9
                    LoggerService.Instance.Info($"User cancelled UAC elevation for {item.Name}");
                    return true;
                }
                catch (Exception ex)
                {
                    LoggerService.Instance.Warn($"Failed elevated launch for {item.Name}: {ex.Message}");
                    errorMessage = $"Failed to start {item.Name}: {ex.Message}";
                    return false;
                }
            }
            catch (Win32Exception winEx) when (winEx.NativeErrorCode == ERROR_CANCELLED)
            {
                // User cancelled prompt silently
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to launch {item.Name}: {ex.Message}");
                errorMessage = $"Failed to start {item.Name}: {ex.Message}";
                return false;
            }
        }

        public bool RunAsAdmin(ProgramItemViewModel item, out string errorMessage)
        {
            errorMessage = null;

            if (item == null)
            {
                errorMessage = "Invalid program item.";
                return false;
            }

            string expandedTarget = Environment.ExpandEnvironmentVariables(item.Target ?? string.Empty);
            string expandedArgs = Environment.ExpandEnvironmentVariables(item.Arguments ?? string.Empty);
            string expandedWorkDir = Environment.ExpandEnvironmentVariables(item.WorkingDirectory ?? string.Empty);

            if (string.IsNullOrEmpty(expandedWorkDir) && File.Exists(expandedTarget))
            {
                expandedWorkDir = Path.GetDirectoryName(expandedTarget);
            }

            var psi = new ProcessStartInfo
            {
                FileName = expandedTarget,
                Arguments = expandedArgs,
                WorkingDirectory = expandedWorkDir,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process.Start(psi);
                LoggerService.Instance.Info($"Ran as admin: {item.Name}");
                return true;
            }
            catch (Win32Exception winEx) when (winEx.NativeErrorCode == ERROR_CANCELLED)
            {
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to run as admin {item.Name}: {ex.Message}");
                errorMessage = $"Failed to run as administrator {item.Name}: {ex.Message}";
                return false;
            }
        }

        public bool OpenFileLocation(ProgramItemViewModel item, out string errorMessage)
        {
            errorMessage = null;

            if (item == null || string.Equals(item.Kind, "url", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                string target = Environment.ExpandEnvironmentVariables(item.Target ?? string.Empty);
                if (string.Equals(item.Kind, "lnk", StringComparison.OrdinalIgnoreCase))
                {
                    target = IconService.Instance.ResolveShortcutTarget(target);
                }

                if (File.Exists(target))
                {
                    Process.Start("explorer.exe", $"/select,\"{target}\"");
                    return true;
                }
                else if (Directory.Exists(target))
                {
                    Process.Start("explorer.exe", $"\"{target}\"");
                    return true;
                }

                errorMessage = $"Location not found: {target}";
                return false;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to open file location for {item.Name}: {ex.Message}");
                errorMessage = $"Failed to open file location: {ex.Message}";
                return false;
            }
        }

        public bool ShowProperties(ProgramItemViewModel item, out string errorMessage)
        {
            errorMessage = null;

            if (item == null || string.Equals(item.Kind, "url", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                string target = Environment.ExpandEnvironmentVariables(item.Target ?? string.Empty);
                if (string.Equals(item.Kind, "lnk", StringComparison.OrdinalIgnoreCase))
                {
                    target = IconService.Instance.ResolveShortcutTarget(target);
                }

                if (!File.Exists(target) && !Directory.Exists(target))
                {
                    errorMessage = $"Item not found: {target}";
                    return false;
                }

                var info = new NativeMethods.SHELLEXECUTEINFO();
                info.cbSize = Marshal.SizeOf(info);
                info.lpVerb = "properties";
                info.lpFile = target;
                info.nShow = 5;
                info.fMask = NativeMethods.SEE_MASK_INVOKEIDLIST;

                bool success = NativeMethods.ShellExecuteEx(ref info);
                if (!success)
                {
                    int err = Marshal.GetLastWin32Error();
                    errorMessage = $"Failed to show properties: Win32 error {err}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to show properties for {item.Name}: {ex.Message}");
                errorMessage = $"Failed to show properties: {ex.Message}";
                return false;
            }
        }
    }
}
