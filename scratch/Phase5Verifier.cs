using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChromaticMenu.Models;
using ChromaticMenu.Services;
using ChromaticMenu.ViewModels;

namespace ChromaticMenu.Tests
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.WriteLine("=== CHROMATIC MENU PHASE 5 VERIFIER ===");
            int failures = 0;

            string testConfigDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            string testConfigFile = Path.Combine(testConfigDir, "config.json");
            Directory.CreateDirectory(testConfigDir);

            // Clean previous test config
            if (File.Exists(testConfigFile))
            {
                File.Delete(testConfigFile);
            }

            try
            {
                // 1. Test PathHelper Collapsing & Expanding
                Console.WriteLine("\n[1] Testing PathHelper collapsing & expanding...");
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string notepadPath = Path.Combine(winDir, "notepad.exe");
                string collapsed = PathHelper.CollapsePath(notepadPath);
                string expanded = PathHelper.ExpandPath(collapsed);

                if (!collapsed.Contains("%SystemRoot%") && !collapsed.Contains("%windir%"))
                {
                    Console.WriteLine(string.Format("  FAIL: PathHelper failed to collapse {0}, got {1}", notepadPath, collapsed));
                    failures++;
                }
                else
                {
                    Console.WriteLine(string.Format("  PASS: PathHelper collapsed {0} -> {1}", notepadPath, collapsed));
                }

                if (!string.Equals(expanded, notepadPath, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(string.Format("  FAIL: PathHelper failed to expand {0}, got {1}", collapsed, expanded));
                    failures++;
                }
                else
                {
                    Console.WriteLine(string.Format("  PASS: PathHelper expanded {0} -> {1}", collapsed, expanded));
                }

                // 2. Test MainViewModel & Item Operations
                Console.WriteLine("\n[2] Testing MainViewModel item addition & validation...");
                var vm = new MainViewModel();

                // Ensure tabs exist
                if (vm.Tabs.Count == 0)
                {
                    Console.WriteLine("  FAIL: No tabs loaded in MainViewModel");
                    failures++;
                }
                else
                {
                    Console.WriteLine(string.Format("  PASS: Loaded {0} tabs. SelectedTab = '{1}'", vm.Tabs.Count, vm.SelectedTab != null ? vm.SelectedTab.Name : "null"));
                }

                // Create test files
                string tempDir = Path.Combine(Path.GetTempPath(), "ChromaticPhase5Test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string testExe = Path.Combine(tempDir, "TestApp.exe");
                File.WriteAllText(testExe, "MZ_FAKE_EXE");
                string testBat = Path.Combine(tempDir, "TestScript.bat");
                File.WriteAllText(testBat, "@echo off\necho test");
                string testCmd = Path.Combine(tempDir, "TestCmd.cmd");
                File.WriteAllText(testCmd, "@echo off\necho cmd");
                string testInvalid = Path.Combine(tempDir, "Document.pdf");
                File.WriteAllText(testInvalid, "%PDF_FAKE");
                string testFolder = Path.Combine(tempDir, "TestSubFolder");
                Directory.CreateDirectory(testFolder);

                // Drop files onto MainViewModel
                vm.HandleDropFiles(new[] { testExe, testBat, testCmd, testFolder, testInvalid });

                // Check added items
                var addedExe = vm.AllItems.FirstOrDefault(i => i.Name == "TestApp");
                var addedBat = vm.AllItems.FirstOrDefault(i => i.Name == "TestScript");
                var addedCmd = vm.AllItems.FirstOrDefault(i => i.Name == "TestCmd");
                var addedFolder = vm.AllItems.FirstOrDefault(i => i.Name == "TestSubFolder");
                var addedInvalid = vm.AllItems.FirstOrDefault(i => i.Name == "Document");

                if (addedExe != null && addedExe.Kind == "exe")
                    Console.WriteLine("  PASS: Added .exe successfully");
                else { Console.WriteLine("  FAIL: .exe was not added properly"); failures++; }

                if (addedBat != null && addedBat.Kind == "bat")
                    Console.WriteLine("  PASS: Added .bat successfully");
                else { Console.WriteLine("  FAIL: .bat was not added properly"); failures++; }

                if (addedCmd != null && addedCmd.Kind == "cmd")
                    Console.WriteLine("  PASS: Added .cmd successfully");
                else { Console.WriteLine("  FAIL: .cmd was not added properly"); failures++; }

                if (addedFolder != null && addedFolder.Kind == "folder")
                    Console.WriteLine("  PASS: Added folder successfully");
                else { Console.WriteLine("  FAIL: folder was not added properly"); failures++; }

                if (addedInvalid == null)
                    Console.WriteLine("  PASS: Invalid file .pdf was correctly rejected");
                else { Console.WriteLine("  FAIL: Invalid file .pdf was incorrectly added"); failures++; }

                // Check in-app message for invalid files
                if (!string.IsNullOrEmpty(vm.InAppMessage) && vm.InAppMessage.Contains("Document.pdf"))
                {
                    Console.WriteLine("  PASS: InAppMessage displayed for rejected file");
                }
                else
                {
                    Console.WriteLine("  FAIL: No warning message displayed for rejected file");
                    failures++;
                }

                // 3. Test Add Website
                Console.WriteLine("\n[3] Testing Add Website...");
                vm.WebsiteName = "Google";
                vm.WebsiteUrl = "https://www.google.com";
                vm.SubmitAddWebsiteCommand.Execute(null);

                var addedUrl = vm.AllItems.FirstOrDefault(i => i.Name == "Google");
                if (addedUrl != null && addedUrl.Kind == "url" && addedUrl.Target == "https://www.google.com")
                {
                    Console.WriteLine("  PASS: Added website item successfully");
                }
                else
                {
                    Console.WriteLine("  FAIL: Failed to add website item");
                    failures++;
                }

                // Test URL validation
                vm.WebsiteName = "Bad URL";
                vm.WebsiteUrl = "ftp://invalid.com";
                vm.SubmitAddWebsiteCommand.Execute(null);
                if (!string.IsNullOrEmpty(vm.WebsiteError))
                {
                    Console.WriteLine(string.Format("  PASS: Invalid URL rejected with error: '{0}'", vm.WebsiteError));
                }
                else
                {
                    Console.WriteLine("  FAIL: Invalid URL was not rejected");
                    failures++;
                }

                // 4. Test Inline Rename
                Console.WriteLine("\n[4] Testing Inline Rename...");
                addedExe.StartRenameCommand.Execute(null);
                if (!addedExe.IsEditingName || addedExe.EditedName != "TestApp")
                {
                    Console.WriteLine("  FAIL: StartRenameCommand did not initialize IsEditingName/EditedName");
                    failures++;
                }
                else
                {
                    Console.WriteLine("  PASS: StartRenameCommand initialized inline rename");
                }

                // Test Cancel
                addedExe.EditedName = "CanceledName";
                addedExe.CancelRenameCommand.Execute(null);
                if (addedExe.IsEditingName || addedExe.Name != "TestApp")
                {
                    Console.WriteLine("  FAIL: CancelRenameCommand failed to revert name");
                    failures++;
                }
                else
                {
                    Console.WriteLine("  PASS: CancelRenameCommand reverted name");
                }

                // Test Commit
                addedExe.StartRenameCommand.Execute(null);
                addedExe.EditedName = "RenamedTestApp";
                addedExe.CommitRenameCommand.Execute(null);
                if (addedExe.IsEditingName || addedExe.Name != "RenamedTestApp")
                {
                    Console.WriteLine("  FAIL: CommitRenameCommand failed to save new name");
                    failures++;
                }
                else
                {
                    Console.WriteLine("  PASS: CommitRenameCommand updated name to 'RenamedTestApp'");
                }

                // 5. Test Reordering
                Console.WriteLine("\n[5] Testing Drag-and-Drop Reordering...");
                var tabItems = vm.AllItems.Where(i => i.TabId == vm.SelectedTab.Id).OrderBy(i => i.Order).ToList();
                int initialCount = tabItems.Count;
                var item0 = tabItems[0];
                var item1 = tabItems[1];

                vm.ReorderItem(item0, 1);
                var reorderedTabItems = vm.AllItems.Where(i => i.TabId == vm.SelectedTab.Id).OrderBy(i => i.Order).ToList();

                if (reorderedTabItems[0] == item1 && reorderedTabItems[1] == item0)
                {
                    Console.WriteLine("  PASS: ReorderItem successfully swapped item positions");
                }
                else
                {
                    Console.WriteLine("  FAIL: ReorderItem failed to reorder items");
                    failures++;
                }

                // 6. Test Move to Tab
                Console.WriteLine("\n[6] Testing Move to Tab...");
                var targetTab = vm.Tabs.FirstOrDefault(t => t.Id != vm.SelectedTab.Id);
                if (targetTab != null)
                {
                    string targetTabId = targetTab.Id;
                    vm.MoveItemsToTab(new[] { addedExe }, targetTabId);
                    if (addedExe.TabId == targetTabId && addedExe.TabName == targetTab.Name)
                    {
                        Console.WriteLine(string.Format("  PASS: MoveItemsToTab moved item to '{0}'", targetTab.Name));
                    }
                    else
                    {
                        Console.WriteLine("  FAIL: MoveItemsToTab failed to update TabId");
                        failures++;
                    }
                }

                // 7. Test Multi-Select Move & Remove
                Console.WriteLine("\n[7] Testing Multi-Select Remove with Confirmation...");
                int beforeRemoveCount = vm.AllItems.Count;
                var itemsToRemove = new List<ProgramItemViewModel> { addedBat, addedCmd };
                vm.PromptRemoveItems(itemsToRemove);

                if (vm.CurrentDialog != DialogType.ConfirmDelete || !vm.ConfirmDeleteMessage.Contains("2 programs"))
                {
                    Console.WriteLine(string.Format("  FAIL: PromptRemoveItems did not show ConfirmDelete dialog, got {0}", vm.CurrentDialog));
                    failures++;
                }
                else
                {
                    Console.WriteLine(string.Format("  PASS: PromptRemoveItems prompted: '{0}'", vm.ConfirmDeleteMessage));
                }

                vm.SubmitConfirmDeleteCommand.Execute(null);
                if (vm.AllItems.Count == beforeRemoveCount - 2 && !vm.AllItems.Contains(addedBat) && !vm.AllItems.Contains(addedCmd))
                {
                    Console.WriteLine("  PASS: SubmitConfirmDelete removed selected items from AllItems");
                }
                else
                {
                    Console.WriteLine("  FAIL: SubmitConfirmDelete failed to remove items");
                    failures++;
                }

                // Target files on disk must NOT be touched
                if (File.Exists(testBat) && File.Exists(testCmd))
                {
                    Console.WriteLine("  PASS: Target files on disk were NOT deleted");
                }
                else
                {
                    Console.WriteLine("  FAIL: Target files on disk WERE deleted!");
                    failures++;
                }

                // 8. Test Icon Size Slider Persistence
                Console.WriteLine("\n[8] Testing Icon Size Slider Persistence...");
                vm.IconSize = 120;
                var reloadedConfig = ConfigService.Instance.Load();
                if (reloadedConfig.Appearance?.IconSize == 120)
                {
                    Console.WriteLine("  PASS: IconSize=120 persisted to config.json");
                }
                else
                {
                    Console.WriteLine(string.Format("  FAIL: IconSize not persisted, found {0}", reloadedConfig.Appearance?.IconSize));
                    failures++;
                }

                // Clean up temp dir
                try { Directory.Delete(tempDir, true); } catch { }

                Console.WriteLine(string.Format("\n=== VERIFICATION FINISHED: {0} FAILURES ===", failures));
                return failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("\nEXCEPTION IN VERIFIER: {0}", ex));
                return 1;
            }
        }
    }
}
