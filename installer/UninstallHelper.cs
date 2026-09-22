using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ChromaticMenu.Uninstaller
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string currentExe = Application.ExecutablePath;
            string currentDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

            // If running directly from Program Files, spawn from TEMP so this executable doesn't lock the folder
            bool isFromTemp = args.Length >= 1 && string.Equals(args[0], "--from-temp", StringComparison.OrdinalIgnoreCase);

            if (!isFromTemp)
            {
                try
                {
                    string tempExe = Path.Combine(Path.GetTempPath(), "ChromaticMenu_Uninstall.exe");
                    File.Copy(currentExe, tempExe, true);

                    string productCode = FindProductCode();
                    ProcessStartInfo spawnPsi = new ProcessStartInfo
                    {
                        FileName = tempExe,
                        Arguments = string.Format("--from-temp \"{0}\" \"{1}\"", currentDir, productCode ?? string.Empty),
                        UseShellExecute = true
                    };
                    Process.Start(spawnPsi);
                    return; // Exit immediately to free locks on the install folder
                }
                catch
                {
                    // Fallback to in-place execution if temp copy fails
                }
            }

            // Running in temp or fallback mode:
            string targetDir = args.Length >= 2 ? args[1] : currentDir;
            string pCode = args.Length >= 3 ? args[2] : null;

            if (string.IsNullOrEmpty(pCode))
            {
                pCode = FindProductCode();
            }

            DialogResult result = MessageBox.Show(
                "Are you sure you want to completely uninstall Chromatic Menu from this computer?",
                "Uninstall Chromatic Menu",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            // 1. Terminate any running Chromatic Menu instances
            try
            {
                Process[] procs = Process.GetProcessesByName("Chromatic Menu");
                foreach (Process p in procs)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                    }
                    catch { }
                }
            }
            catch { }

            // 2. Invoke msiexec to uninstall
            if (!string.IsNullOrEmpty(pCode))
            {
                try
                {
                    ProcessStartInfo msiPsi = new ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = "/x " + pCode,
                        UseShellExecute = true
                    };
                    Process msiProc = Process.Start(msiPsi);
                    if (msiProc != null)
                    {
                        msiProc.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error launching Windows uninstaller: " + ex.Message, "Uninstall Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // 3. Clean up any remaining runtime files/folders (logs, data, config, etc.)
            Thread.Sleep(1000);

            if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(targetDir))
                        {
                            Directory.Delete(targetDir, true);
                        }
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(800);
                    }
                }
            }

            // Clean up LocalAppData folder if created by non-elevated runs
            try
            {
                string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChromaticMenu");
                if (Directory.Exists(localAppData))
                {
                    Directory.Delete(localAppData, true);
                }
            }
            catch { }

            // 4. Remove shortcut folder if empty or remaining
            try
            {
                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Chromatic Menu");
                if (Directory.Exists(startMenuDir))
                {
                    Directory.Delete(startMenuDir, true);
                }
            }
            catch { }

            // 5. Remove desktop shortcut if left behind
            try
            {
                string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Chromatic Menu.lnk");
                if (File.Exists(desktopShortcut))
                {
                    File.Delete(desktopShortcut);
                }
            }
            catch { }

            // 6. Clean up Run at Startup registry entry
            try
            {
                using (var runKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (runKey != null)
                    {
                        runKey.DeleteValue("Chromatic Menu", false);
                    }
                }
            }
            catch { }

            MessageBox.Show(
                "Chromatic Menu has been completely removed from your computer.",
                "Uninstall Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        static string FindProductCode()
        {
            // 1. Check direct key written by installer
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\HanazonoArchive\ChromaticMenu"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("ProductCode");
                        if (val != null && !string.IsNullOrEmpty(val.ToString()))
                        {
                            return val.ToString();
                        }
                    }
                }
            }
            catch { }

            // 2. Search Windows Uninstall registry
            string[] rootKeys = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string root in rootKeys)
            {
                try
                {
                    using (RegistryKey parent = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (parent == null) continue;
                        foreach (string subName in parent.GetSubKeyNames())
                        {
                            using (RegistryKey subKey = parent.OpenSubKey(subName))
                            {
                                if (subKey == null) continue;
                                object displayName = subKey.GetValue("DisplayName");
                                if (displayName != null && displayName.ToString().IndexOf("Chromatic Menu", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return subName; // GUID product code
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
