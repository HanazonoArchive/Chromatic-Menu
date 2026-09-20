using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ChromaticMenu.Models;
using ChromaticMenu.Services;
using ChromaticMenu.ViewModels;

namespace ChromaticMenu.Scratch
{
    public class TestPhase6
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Console.WriteLine("=== Starting Phase 6 Automated Verification ===");
            int passed = 0;
            int failed = 0;

            // 1. Test FontService
            try
            {
                var fonts = FontService.Instance.GetCuratedInstalledFonts();
                Console.WriteLine($"[1] FontService: Found {fonts.Count} curated installed fonts: {string.Join(", ", fonts)}");
                if (fonts.Count > 0 && fonts.Contains("Segoe UI"))
                {
                    Console.WriteLine("    PASS: FontService returned curated fonts and includes 'Segoe UI'.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: FontService did not return Segoe UI.");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: FontService threw exception: {ex.Message}");
                failed++;
            }

            // 2. Test StartupService
            try
            {
                bool isStartup = StartupService.Instance.IsRunAtStartup();
                Console.WriteLine($"[2] StartupService: IsRunAtStartup = {isStartup}");

                // Attempt set startup - may succeed if admin or return friendly error if not admin
                bool success = StartupService.Instance.SetRunAtStartup(false, out string error);
                Console.WriteLine($"    SetRunAtStartup(false) result = {success}, error = '{error}'");
                if (success || !string.IsNullOrEmpty(error))
                {
                    Console.WriteLine("    PASS: StartupService handled registry check cleanly without crashing.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: StartupService returned false without error.");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: StartupService threw exception: {ex.Message}");
                failed++;
            }

            // 3. Test Export and Import Service
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "ChromaticMenu_Phase6_Test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string testZip = Path.Combine(tempDir, "backup_test.zip");

                // Ensure config exists
                ConfigService.Instance.Load();

                bool exportOk = ExportImportService.Instance.ExportBackup(testZip, out string expError);
                Console.WriteLine($"[3] ExportImportService: Export result = {exportOk}, file = {testZip}, error = '{expError}'");

                if (exportOk && File.Exists(testZip))
                {
                    // Inspect ZIP contents
                    using (var archive = ZipFile.OpenRead(testZip))
                    {
                        bool hasManifest = archive.Entries.Any(e => e.FullName == "manifest.json");
                        bool hasConfig = archive.Entries.Any(e => e.FullName == "config.json");
                        Console.WriteLine($"    Archive contains manifest: {hasManifest}, config: {hasConfig}, entry count: {archive.Entries.Count}");
                        if (hasManifest && hasConfig)
                        {
                            Console.WriteLine("    PASS: ZIP archive contains valid manifest.json and config.json.");
                            passed++;
                        }
                        else
                        {
                            Console.WriteLine("    FAIL: ZIP archive missing manifest or config.");
                            failed++;
                        }
                    }

                    // Test Import
                    bool importOk = ExportImportService.Instance.ImportBackup(testZip, out int brokenCount, out string impError);
                    Console.WriteLine($"    Import result = {importOk}, broken count = {brokenCount}, error = '{impError}'");
                    if (importOk)
                    {
                        Console.WriteLine("    PASS: Import completed successfully with broken path reporting.");
                        passed++;
                    }
                    else
                    {
                        Console.WriteLine($"    FAIL: Import failed: {impError}");
                        failed++;
                    }
                }
                else
                {
                    Console.WriteLine($"    FAIL: Export failed: {expError}");
                    failed++;
                }

                // Test Zip Slip Security Check
                string slipZip = Path.Combine(tempDir, "zip_slip.zip");
                using (var zip = new ZipArchive(new FileStream(slipZip, FileMode.Create), ZipArchiveMode.Create))
                {
                    var entry1 = zip.CreateEntry("manifest.json");
                    using (var w = new StreamWriter(entry1.Open())) w.Write("{}");
                    var entry2 = zip.CreateEntry("config.json");
                    using (var w = new StreamWriter(entry2.Open())) w.Write("{}");
                    var malicious = zip.CreateEntry("../../../evil.txt");
                    using (var w = new StreamWriter(malicious.Open())) w.Write("evil");
                }

                bool slipImport = ExportImportService.Instance.ImportBackup(slipZip, out int _, out string slipErr);
                Console.WriteLine($"    Zip Slip attack test: imported = {slipImport}, error = '{slipErr}'");
                if (!slipImport && slipErr != null && slipErr.Contains("Security check failed"))
                {
                    Console.WriteLine("    PASS: Zip Slip attack blocked successfully.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: Zip Slip attack was not blocked!");
                    failed++;
                }

                try { Directory.Delete(tempDir, true); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: ExportImportService test failed: {ex.Message}");
                failed++;
            }

            // 4. Test Tab Management and Minimum 1 Tab Constraint
            try
            {
                var vm = new MainViewModel();

                int initialTabCount = vm.Tabs.Count;
                Console.WriteLine($"[4] Tab Management: Initial tabs count = {initialTabCount}");

                // Add Tab
                vm.AddTabName = "Test Tab Phase 6";
                vm.AddTabIcon = "star";
                vm.AddTab();

                var addedTab = vm.Tabs.FirstOrDefault(t => t.Name == "Test Tab Phase 6");
                if (addedTab != null && addedTab.Icon == "star" && vm.Tabs.Count == initialTabCount + 1)
                {
                    Console.WriteLine("    PASS: Added new tab successfully.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: AddTab failed.");
                    failed++;
                }

                // Move Tab Up / Down
                int idx = vm.Tabs.IndexOf(addedTab);
                vm.MoveTabUp(addedTab);
                int newIdx = vm.Tabs.IndexOf(addedTab);
                if (newIdx == idx - 1)
                {
                    Console.WriteLine("    PASS: MoveTabUp successfully reordered tab.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: MoveTabUp failed.");
                    failed++;
                }

                // Test Tab Delete with items (Move vs Delete All)
                // Add a dummy program to this tab
                var dummyItem = new ProgramItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    TabId = addedTab.Id,
                    Name = "Dummy Test Program",
                    Kind = "exe",
                    Target = @"C:\Windows\notepad.exe"
                };
                var itemVm = new ProgramItemViewModel(dummyItem, addedTab.Name);
                vm.AllItems.Add(itemVm);

                // Prompt delete tab
                vm.PromptDeleteTab(addedTab);
                if (vm.CurrentDialog == DialogType.TabDeletePrompt && vm.PendingDeleteTabItemCount == 1)
                {
                    Console.WriteLine("    PASS: PromptDeleteTab triggered TabDeletePrompt with item count 1.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: PromptDeleteTab did not trigger TabDeletePrompt.");
                    failed++;
                }

                // Submit move to first tab and delete
                var targetTab = vm.Tabs.First(t => t.Id != addedTab.Id);
                vm.SelectedMoveTargetTab = targetTab;
                vm.SubmitDeleteTabMove();

                if (!vm.Tabs.Contains(addedTab) && itemVm.TabId == targetTab.Id)
                {
                    Console.WriteLine("    PASS: SubmitDeleteTabMove moved items to target tab and deleted source tab.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: SubmitDeleteTabMove failed.");
                    failed++;
                }

                // Clean up dummy item
                vm.AllItems.Remove(itemVm);
                vm.SaveConfig();

                // Test Minimum 1 tab constraint: if only 1 tab, delete should be blocked
                while (vm.Tabs.Count > 1)
                {
                    vm.Tabs.RemoveAt(vm.Tabs.Count - 1);
                }
                var lastTab = vm.Tabs[0];
                vm.PromptDeleteTab(lastTab);
                if (vm.Tabs.Count == 1 && vm.InAppMessage != null && vm.InAppMessage.Contains("At least one tab is required"))
                {
                    Console.WriteLine("    PASS: Minimum 1 tab constraint successfully enforced.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: Minimum 1 tab constraint not enforced.");
                    failed++;
                }

                // Restore default tabs
                ConfigService.Instance.ResetToDefaults();
                vm = new MainViewModel();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: Tab management test failed: {ex.Message}");
                failed++;
            }

            // 5. Test Security Password Change
            try
            {
                var vm = new MainViewModel();

                // Wrong current password
                vm.ProcessSecurityPasswordChange("wrongpassword", "newpassword123", "newpassword123");
                if (vm.SecurityError != null && vm.SecurityError.Contains("incorrect"))
                {
                    Console.WriteLine("[5] Security: PASS - Incorrect current password rejected.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("[5] Security: FAIL - Incorrect current password not rejected.");
                    failed++;
                }

                // Short password (< 4 chars)
                vm.ProcessSecurityPasswordChange("admin", "abc", "abc");
                if (vm.SecurityError != null && vm.SecurityError.Contains("at least 4 characters"))
                {
                    Console.WriteLine("    PASS: Short password (< 4 chars) rejected.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: Short password not rejected.");
                    failed++;
                }

                // Passwords mismatch
                vm.ProcessSecurityPasswordChange("admin", "newpass123", "different123");
                if (vm.SecurityError != null && vm.SecurityError.Contains("do not match"))
                {
                    Console.WriteLine("    PASS: Mismatched confirmation password rejected.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: Mismatched password not rejected.");
                    failed++;
                }

                // Valid change
                vm.ProcessSecurityPasswordChange("admin", "newadmin2026", "newadmin2026");
                if (vm.SecuritySuccess != null && vm.SecuritySuccess.Contains("successfully"))
                {
                    Console.WriteLine("    PASS: Valid password change accepted.");
                    passed++;
                }
                else
                {
                    Console.WriteLine($"    FAIL: Valid password change failed: {vm.SecurityError}");
                    failed++;
                }

                // Reset back to default admin for clean test state
                ConfigService.Instance.Current.PasswordHash = PasswordService.DefaultPasswordHash;
                ConfigService.Instance.Current.MustChangePassword = true;
                ConfigService.Instance.Save(ConfigService.Instance.Current);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: Security test failed: {ex.Message}");
                failed++;
            }

            // 6. Test Appearance Live Updates and Serialization
            try
            {
                var vm = new MainViewModel();

                // Change accent
                vm.SelectedAccentColor = "#6366F1";
                if (ConfigService.Instance.Current.Appearance.Accent == "#6366F1")
                {
                    Console.WriteLine("[6] Appearance: PASS - Accent color updated and persisted.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("[6] Appearance: FAIL - Accent color not persisted.");
                    failed++;
                }

                // Font Scale
                vm.FontScale = 1.15;
                if (Math.Abs(ConfigService.Instance.Current.Appearance.FontScale - 1.15) < 0.001 &&
                    vm.FontScaleDisplay == "1.15x" &&
                    Math.Abs(vm.RootFontSize - (14.0 * 1.15)) < 0.001)
                {
                    Console.WriteLine("    PASS: FontScale updated, formatted as 1.15x, and RootFontSize scaled.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: FontScale calculation or persistence failed.");
                    failed++;
                }

                // Reset to defaults
                vm.ResetAppearanceDefaultsCommand.Execute(null);
                if (ConfigService.Instance.Current.Appearance.Accent == "#0284C7" &&
                    ConfigService.Instance.Current.Appearance.CustomColors == null)
                {
                    Console.WriteLine("    PASS: ResetAppearanceDefaults restored default accent and null custom colors.");
                    passed++;
                }
                else
                {
                    Console.WriteLine("    FAIL: ResetAppearanceDefaults failed.");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: Appearance test failed: {ex.Message}");
                failed++;
            }

            Console.WriteLine($"\n=== Summary: {passed} PASSED, {failed} FAILED ===");
            Environment.Exit(failed == 0 ? 0 : 1);
        }
    }
}
