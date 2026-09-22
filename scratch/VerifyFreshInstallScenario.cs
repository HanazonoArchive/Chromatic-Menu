using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using ChromaticMenu.Models;
using ChromaticMenu.Services;
using ChromaticMenu.ViewModels;

namespace ChromaticMenu.Tests
{
    class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;

        static void Assert(bool condition, string testName, string details = "")
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] {testName}");
                _passed++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] {testName} - {details}");
                _failed++;
            }
            Console.ResetColor();
        }

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("=================================================================");
            Console.WriteLine(" CHROMATIC MENU - FRESH OS INSTALLATION SIMULATION TEST SUITE   ");
            Console.WriteLine("=================================================================");

            string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChromaticMenu");
            string backupDir = Path.Combine(Path.GetTempPath(), "ChromaticMenu_Backup_" + Guid.NewGuid().ToString("N"));
            bool hadExistingDir = false;

            try
            {
                // -------------------------------------------------------------
                // STAGE 0: EMULATE CLEAN / PRISTINE OS STATE
                // -------------------------------------------------------------
                Console.WriteLine("\n--> STAGE 0: Emulating Pristine OS (Wiping local app data & registry)...");
                if (Directory.Exists(localAppData))
                {
                    hadExistingDir = true;
                    Directory.Move(localAppData, backupDir);
                }

                // Clean registry startup entry
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        key?.DeleteValue("Chromatic Menu", false);
                    }
                }
                catch { }

                Assert(!Directory.Exists(localAppData), "Environment Isolation: Clean %LOCALAPPDATA% initialized");

                // -------------------------------------------------------------
                // STAGE 1: FIRST-LAUNCH LIFECYCLE (NO CONFIG EXISTS)
                // -------------------------------------------------------------
                Console.WriteLine("\n--> STAGE 1: Simulating First Launch on Fresh OS...");
                var configService = new ConfigService();
                var initialConfig = configService.Load();

                Assert(initialConfig != null, "First Launch: Default configuration created");
                Assert(initialConfig.MustChangePassword == true, "First Launch: MustChangePassword flag is TRUE");
                Assert(initialConfig.Tabs != null && initialConfig.Tabs.Count == 5, "First Launch: 5 default tabs created", $"Count: {initialConfig.Tabs?.Count}");
                Assert(File.Exists(configService.ConfigPath), "First Launch: config.json persisted to disk", configService.ConfigPath);

                // Initialize ViewModel (as MainWindow does on launch)
                var vm = new MainViewModel();

                Assert(vm.MustChangePassword == true, "ViewModel: MustChangePassword reflects config state");
                Assert(vm.IsChangePasswordDialogVisible, "ViewModel: Mandatory Change Password dialog is visible on first launch");
                Assert(vm.ModalTitle.Contains("First-Time Setup"), "ViewModel: Modal title indicates First-Time Setup", vm.ModalTitle);

                // Test Password Validation on First Run
                vm.ProcessNewPassword("", "");
                Assert(!string.IsNullOrEmpty(vm.NewPasswordError) && vm.NewPasswordError.Contains("empty"),
                    "Password Validation: Empty password rejected");

                vm.ProcessNewPassword("abc", "abc");
                Assert(!string.IsNullOrEmpty(vm.NewPasswordError) && vm.NewPasswordError.Contains("4 characters"),
                    "Password Validation: Short password (<4 chars) rejected");

                vm.ProcessNewPassword("Secret123", "SecretMismatch");
                Assert(!string.IsNullOrEmpty(vm.NewPasswordError) && vm.NewPasswordError.Contains("match"),
                    "Password Validation: Mismatched passwords rejected");

                // Submit valid new admin password
                string chosenPassword = "PisoNetAdmin2026!";
                vm.ProcessNewPassword(chosenPassword, chosenPassword);

                Assert(!vm.IsChangePasswordDialogVisible, "First-Time Setup: Dialog closed after setting valid password");
                Assert(vm.IsSessionUnlocked == true, "First-Time Setup: Session unlocked for administrator");
                Assert(configService.Current.MustChangePassword == false, "First-Time Setup: MustChangePassword set to FALSE");

                // Verify saved to file
                string savedJson = File.ReadAllText(configService.ConfigPath);
                Assert(savedJson.Contains("\"mustChangePassword\": false"), "Config Persistence: Saved JSON has mustChangePassword=false");

                // -------------------------------------------------------------
                // STAGE 2: SECOND LAUNCH (CLOSING & REOPENING ON FRESH OS)
                // -------------------------------------------------------------
                Console.WriteLine("\n--> STAGE 2: Simulating Second Launch (App reopen after setup)...");
                var secondConfigService = new ConfigService();
                var loadedConfig = secondConfigService.Load();

                Assert(loadedConfig.MustChangePassword == false, "Second Launch: Config retains MustChangePassword=false");

                var secondVm = new MainViewModel();
                Assert(!secondVm.IsChangePasswordDialogVisible, "Second Launch: Does NOT show Change Password dialog on startup");
                Assert(!secondVm.IsPasswordDialogVisible, "Second Launch: Does NOT block main window with password dialog on startup");
                Assert(!secondVm.IsSessionUnlocked, "Second Launch: Standard user session is locked for kiosk security");

                // Verify unlock flow with the newly chosen password
                secondVm.RequestUnlock(() => { });
                Assert(secondVm.IsPasswordDialogVisible, "Admin Action: RequestUnlock prompts for password");

                // Attempt unlock with default "admin" (must fail!)
                secondVm.ProcessPassword("admin");
                Assert(!secondVm.IsSessionUnlocked, "Security: Old default password 'admin' is rejected");
                Assert(!string.IsNullOrEmpty(secondVm.PasswordError), "Security: Password error displayed for incorrect password");

                // Unlock with chosen password
                secondVm.ProcessPassword(chosenPassword);
                Assert(secondVm.IsSessionUnlocked, "Security: Unlock succeeds with the set administrator password");
                Assert(!secondVm.IsPasswordDialogVisible, "Security: Password dialog dismisses after successful unlock");

                // -------------------------------------------------------------
                // STAGE 3: REGISTRY & START WITH WINDOWS TOGGLE
                // -------------------------------------------------------------
                Console.WriteLine("\n--> STAGE 3: Testing 'Start with Windows' HKCU Registry Key...");
                var startupService = StartupService.Instance;

                // Enable startup
                bool enableSuccess = startupService.SetRunAtStartup(true, out string startErr);
                Assert(enableSuccess && string.IsNullOrEmpty(startErr), "Startup Service: SetRunAtStartup(true) succeeds", startErr);

                // Verify in HKCU Run registry
                string registeredPath = null;
                using (var runKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    registeredPath = runKey?.GetValue("Chromatic Menu") as string;
                }
                Assert(!string.IsNullOrEmpty(registeredPath), "Startup Registry: Value exists in HKCU Run", registeredPath);

                // Verify IsRunAtStartup returns true
                Assert(startupService.IsRunAtStartup() == true, "Startup Service: IsRunAtStartup reports true");

                // Disable startup
                bool disableSuccess = startupService.SetRunAtStartup(false, out string stopErr);
                Assert(disableSuccess && string.IsNullOrEmpty(stopErr), "Startup Service: SetRunAtStartup(false) succeeds", stopErr);
                Assert(startupService.IsRunAtStartup() == false, "Startup Service: IsRunAtStartup reports false after disabling");

                // -------------------------------------------------------------
                // STAGE 4: DRAG & DROP AND ICON EXTRACTION ON FRESH OS
                // -------------------------------------------------------------
                Console.WriteLine("\n--> STAGE 4: Testing Drag & Drop and Icon Extraction on Fresh OS...");
                string notepadPath = Path.Combine(Environment.SystemDirectory, "notepad.exe");
                string cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

                Assert(File.Exists(notepadPath) && File.Exists(cmdPath), "System binaries exist for drop test");

                // Simulate dropping files on first tab
                secondVm.HandleDropFiles(new string[] { notepadPath, cmdPath });

                Assert(secondVm.AllItems.Count >= 2, "Drop Files: Items added to AllItems collection", $"Count: {secondVm.AllItems.Count}");
                Assert(secondVm.FilteredItems.Count >= 2, "Drop Files: Items visible in FilteredItems", $"Count: {secondVm.FilteredItems.Count}");

                var notepadItem = secondVm.AllItems.FirstOrDefault(i => i.Name.IndexOf("notepad", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert(notepadItem != null, "Drop Files: Notepad item present");
                Assert(notepadItem?.IconSource != null, "Drop Files: Icon extracted and loaded into BitmapSource");

                // Verify icon file was written to assets directory
                string iconDir = Path.Combine(secondConfigService.AssetsDirectory, "icons");
                Assert(Directory.Exists(iconDir), "Assets Directory: icons folder exists", iconDir);
                string iconFile = Path.Combine(iconDir, $"{notepadItem.Id}.png");
                Assert(File.Exists(iconFile), "Assets Directory: Icon PNG written to disk", iconFile);

                // -------------------------------------------------------------
                // STAGE 5: SYSTEM INFO & DISPLAY ADAPTER FALLBACK
                // -------------------------------------------------------------
                Console.WriteLine("\n--> STAGE 5: Testing System Hardware Detection & Fallback...");
                var sysInfo = SystemInfoService.Instance;
                var gpus = sysInfo.GetType().GetMethod("GetGpuList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(sysInfo, null) as System.Collections.IEnumerable;

                int gpuCount = 0;
                if (gpus != null)
                {
                    foreach (var g in gpus)
                    {
                        gpuCount++;
                    }
                }
                Assert(gpuCount > 0, "Hardware Detection: Display adapter list is not empty on fresh OS", $"Count: {gpuCount}");

                // -------------------------------------------------------------
                // STAGE 6: UNINSTALLATION CLEANUP SIMULATION
                // -------------------------------------------------------------
                Console.WriteLine("\n--> STAGE 6: Testing Clean Uninstallation...");
                // Simulate uninstaller directory cleanup
                if (Directory.Exists(localAppData))
                {
                    Directory.Delete(localAppData, true);
                }
                Assert(!Directory.Exists(localAppData), "Uninstallation: %LOCALAPPDATA% cleanly removed");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[EXCEPTION ENCOUNTERED]: {ex}");
                Console.ResetColor();
                _failed++;
            }
            finally
            {
                // Restore backup if original existed
                try
                {
                    if (hadExistingDir && Directory.Exists(backupDir))
                    {
                        if (Directory.Exists(localAppData))
                        {
                            Directory.Delete(localAppData, true);
                        }
                        Directory.Move(backupDir, localAppData);
                        Console.WriteLine("\nOriginal user data restored from backup.");
                    }
                    else if (Directory.Exists(backupDir))
                    {
                        Directory.Delete(backupDir, true);
                    }
                }
                catch { }
            }

            Console.WriteLine("\n=================================================================");
            Console.WriteLine($" TEST RESULTS: {_passed} PASSED, {_failed} FAILED");
            Console.WriteLine("=================================================================");

            Environment.Exit(_failed == 0 ? 0 : 1);
        }
    }
}
