using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ChromaticMenu.Services
{
    public enum DeepFreezeState
    {
        NotInstalled,
        Frozen,
        Thawed
    }

    public class DeepFreezeService
    {
        private static DeepFreezeService _instance;
        public static DeepFreezeService Instance => _instance ?? (_instance = new DeepFreezeService());

        public bool IsDeepFreezeInstalled()
        {
            try
            {
                // 1. Check Registry for Faronics Deep Freeze installations
                string[] regKeys = new[]
                {
                    @"SOFTWARE\Faronics\Deep Freeze 6",
                    @"SOFTWARE\WOW6432Node\Faronics\Deep Freeze 6",
                    @"SYSTEM\CurrentControlSet\Services\DeepFrz",
                    @"SYSTEM\CurrentControlSet\Services\DFServ",
                    @"SYSTEM\CurrentControlSet\Services\DF5Serv",
                    @"SYSTEM\CurrentControlSet\Services\FrzState2k"
                };

                foreach (var subKey in regKeys)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(subKey))
                    {
                        if (key != null)
                        {
                            return true;
                        }
                    }
                }

                // 2. Check Driver Files in System32\drivers
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string[] driverFiles = new[]
                {
                    Path.Combine(sys32, @"drivers\DeepFrz.sys"),
                    Path.Combine(sys32, @"drivers\DeepFrz6.sys"),
                    Path.Combine(sys32, @"drivers\DFServ.sys")
                };

                foreach (var driver in driverFiles)
                {
                    if (File.Exists(driver))
                    {
                        return true;
                    }
                }

                // 3. Check Running Processes
                string[] processNames = new[] { "FrzState2k", "DFServ", "DF5Serv" };
                foreach (var name in processNames)
                {
                    var procs = Process.GetProcessesByName(name);
                    if (procs != null && procs.Length > 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed checking Deep Freeze status: {ex.Message}");
            }

            return false;
        }

        public DeepFreezeState GetDeepFreezeState()
        {
            if (!IsDeepFreezeInstalled())
            {
                return DeepFreezeState.NotInstalled;
            }

            try
            {
                // 1. Query Registry "DF Status" under 64-bit and 32-bit Faronics keys
                string[] regKeys = new[]
                {
                    @"SOFTWARE\WOW6432Node\Faronics\Deep Freeze 6",
                    @"SOFTWARE\Faronics\Deep Freeze 6",
                    @"SOFTWARE\WOW6432Node\Faronics\Deep Freeze",
                    @"SOFTWARE\Faronics\Deep Freeze"
                };

                foreach (var subKey in regKeys)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(subKey))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("DF Status") ?? key.GetValue("Status");
                            if (val != null)
                            {
                                string statusStr = val.ToString().Trim();
                                if (statusStr.IndexOf("Thaw", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return DeepFreezeState.Thawed;
                                }
                                if (statusStr.IndexOf("Froz", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return DeepFreezeState.Frozen;
                                }
                            }
                        }
                    }
                }

                // 2. Query DFC.exe (Deep Freeze Command Line Control) if available
                string[] dfcPaths = new[]
                {
                    @"C:\Program Files (x86)\Faronics\Deep Freeze\DFC.exe",
                    @"C:\Program Files\Faronics\Deep Freeze\DFC.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DFC.exe")
                };

                foreach (var dfc in dfcPaths)
                {
                    if (File.Exists(dfc))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = dfc,
                            Arguments = "get /ISFROZEN",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (var proc = Process.Start(psi))
                        {
                            if (proc.WaitForExit(3000))
                            {
                                if (proc.ExitCode == 1) return DeepFreezeState.Frozen;
                                if (proc.ExitCode == 0) return DeepFreezeState.Thawed;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed determining Deep Freeze frozen/thawed state: {ex.Message}");
            }

            // If Deep Freeze is installed but specific state is inconclusive,
            // default safely to Frozen to prevent accidental loss of updates.
            return DeepFreezeState.Frozen;
        }

        public bool CanSafelyUpdate => GetDeepFreezeState() != DeepFreezeState.Frozen;

        public string StatusDisplayName
        {
            get
            {
                switch (GetDeepFreezeState())
                {
                    case DeepFreezeState.NotInstalled:
                        return "Not Installed";
                    case DeepFreezeState.Thawed:
                        return "Thawed (Safe to Update)";
                    case DeepFreezeState.Frozen:
                        return "Boot Frozen (Updates Blocked)";
                    default:
                        return "Unknown";
                }
            }
        }
    }
}

