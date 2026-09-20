using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ChromaticMenu.Models;
using ChromaticMenu.Services;
using ChromaticMenu.ViewModels;

namespace VerifyNewFeatures
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.WriteLine("=== NEW FEATURES VERIFICATION START ===");
            bool allPassed = true;

            try
            {
                // TEST 1: Deep Freeze Detection
                Console.WriteLine("\n--- TEST 1: Deep Freeze Detection ---");
                bool dfInstalled = DeepFreezeService.Instance.IsDeepFreezeInstalled();
                Console.WriteLine(string.Format("DeepFreezeService.IsDeepFreezeInstalled: {0}", dfInstalled));
                if (dfInstalled)
                {
                    Console.WriteLine("WARNING: Deep Freeze detected on this machine. Deep Freeze banners will be visible.");
                }
                else
                {
                    Console.WriteLine("PASS: Deep Freeze NOT detected on this machine. Deep Freeze banners will remain hidden.");
                }

                // TEST 2: MainViewModel Initial State & DeepFreeze Binding
                Console.WriteLine("\n--- TEST 2: MainViewModel Initial State ---");
                var vm = new MainViewModel();
                if (vm.IsDeepFreezeInstalled != dfInstalled)
                {
                    Console.WriteLine(string.Format("FAIL: vm.IsDeepFreezeInstalled ({0}) != dfInstalled ({1})", vm.IsDeepFreezeInstalled, dfInstalled));
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine(string.Format("PASS: vm.IsDeepFreezeInstalled = {0}", vm.IsDeepFreezeInstalled));
                }

                if (vm.IsSessionUnlocked)
                {
                    Console.WriteLine("FAIL: vm.IsSessionUnlocked should initially be false.");
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("PASS: vm.IsSessionUnlocked is initially false.");
                }

                // TEST 3: Lock/Unlock Flow with Password Verification
                Console.WriteLine("\n--- TEST 3: Session Lock / Unlock Flow ---");
                
                // Clicking Settings while locked should prompt for password
                vm.RequestSettingsCommand.Execute(null);
                if (vm.CurrentDialog != DialogType.Password && vm.CurrentDialog != DialogType.ChangePassword)
                {
                    Console.WriteLine(string.Format("FAIL: Expected Password dialog when locked, got {0}", vm.CurrentDialog));
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine(string.Format("PASS: RequestSettingsCommand opened {0} dialog when locked.", vm.CurrentDialog));
                }

                // Test unlocking with password
                var config = ConfigService.Instance.Current;
                string currentPass = "default";
                if (vm.CurrentDialog == DialogType.Password)
                {
                    vm.ProcessPassword(currentPass);
                }

                // If first-time change password required
                if (vm.CurrentDialog == DialogType.ChangePassword)
                {
                    Console.WriteLine("Handling mandatory first-time password setup...");
                    vm.ProcessNewPassword("testpass123", "testpass123");
                }

                if (!vm.IsSessionUnlocked)
                {
                    Console.WriteLine("FAIL: vm.IsSessionUnlocked should be true after successful password verification.");
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("PASS: vm.IsSessionUnlocked is true after authentication.");
                }

                // Now that session is unlocked, Settings, Theme, Font, and Edit Mode should open directly!
                Console.WriteLine("\n--- TEST 4: Unlocked Access (No Password Prompt) ---");
                vm.CloseModal();

                // 1. Settings
                vm.RequestSettingsCommand.Execute(null);
                if (vm.CurrentDialog != DialogType.SettingsHost)
                {
                    Console.WriteLine(string.Format("FAIL: Expected SettingsHost, got {0}", vm.CurrentDialog));
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("PASS: RequestSettingsCommand opened directly without password prompt.");
                }
                vm.CloseModal();

                // 2. Theme
                vm.RequestThemeCommand.Execute(null);
                if (vm.CurrentDialog != DialogType.SettingsHost || vm.SettingsPageIndex != 1)
                {
                    Console.WriteLine(string.Format("FAIL: Expected SettingsHost page 1, got {0} page {1}", vm.CurrentDialog, vm.SettingsPageIndex));
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("PASS: RequestThemeCommand opened directly without password prompt.");
                }
                vm.CloseModal();

                // 3. Edit Mode
                vm.RequestEditModeCommand.Execute(null);
                if (!vm.IsEditMode)
                {
                    Console.WriteLine("FAIL: Expected IsEditMode to be true without password prompt.");
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("PASS: RequestEditModeCommand activated Edit Mode directly without password prompt.");
                }

                // TEST 5: Toggle Session Lock (Locking)
                Console.WriteLine("\n--- TEST 5: Locking the Session ---");
                vm.ToggleSessionLockCommand.Execute(null);
                if (vm.IsSessionUnlocked)
                {
                    Console.WriteLine("FAIL: vm.IsSessionUnlocked should be false after ToggleSessionLockCommand.");
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("PASS: Session locked successfully via ToggleSessionLockCommand.");
                }

                if (vm.IsEditMode)
                {
                    Console.WriteLine("FAIL: vm.IsEditMode should be deactivated upon locking.");
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("PASS: Edit mode automatically exited upon locking.");
                }

                // Now clicking Settings while locked should prompt for password again
                vm.RequestSettingsCommand.Execute(null);
                if (vm.CurrentDialog != DialogType.Password)
                {
                    Console.WriteLine(string.Format("FAIL: Expected Password dialog after re-locking, got {0}", vm.CurrentDialog));
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("PASS: RequestSettingsCommand prompted for password again after locking.");
                }
                vm.CloseModal();

                // TEST 6: High-Res Icon Extraction
                Console.WriteLine("\n--- TEST 6: High-Res Icon Extraction (256x256) ---");
                string notepadPath = Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\notepad.exe");
                var iconSrc = IconService.Instance.GetOrExtractIcon("test-verify-notepad", notepadPath, "file", 0);
                if (iconSrc == null)
                {
                    Console.WriteLine("FAIL: Could not extract icon for notepad.exe");
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine(string.Format("PASS: Extracted icon dimensions: {0}x{1} (PixelWidth={2}, PixelHeight={3})",
                        iconSrc.Width, iconSrc.Height, iconSrc.PixelWidth, iconSrc.PixelHeight));
                    if (iconSrc.PixelWidth >= 128 && iconSrc.PixelHeight >= 128)
                    {
                        Console.WriteLine("PASS: Icon is high-resolution (>= 128px)!");
                    }
                    else
                    {
                        Console.WriteLine(string.Format("NOTE: Icon resolution is {0}x{1}", iconSrc.PixelWidth, iconSrc.PixelHeight));
                    }
                }

                // TEST 7: Application Icon File
                Console.WriteLine("\n--- TEST 7: Application Icon File ---");
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Resources\app.ico");
                string fullIcoPath = Path.GetFullPath(icoPath);
                if (File.Exists(fullIcoPath))
                {
                    long icoSize = new FileInfo(fullIcoPath).Length;
                    Console.WriteLine(string.Format("PASS: app.ico exists at '{0}' ({1} bytes).", fullIcoPath, icoSize));
                }
                else
                {
                    Console.WriteLine(string.Format("FAIL: app.ico not found at '{0}'.", fullIcoPath));
                    allPassed = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("UNHANDLED EXCEPTION: " + ex.ToString());
                allPassed = false;
            }

            Console.WriteLine(allPassed ? "\n=== ALL TESTS PASSED! ===" : "\n=== SOME TESTS FAILED! ===");
            return allPassed ? 0 : 1;
        }
    }
}
