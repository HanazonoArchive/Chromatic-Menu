using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ChromaticMenu.Services
{
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
    }
}
