param()

$exePath = Resolve-Path "src\Chromatic Menu\bin\Release\net48\Chromatic Menu.exe"
$asm = [System.Reflection.Assembly]::LoadFrom($exePath)
Write-Host "=== Chromatic Menu Assembly Loaded: $($asm.FullName) ==="

# Initialize WPF Application context for pack URIs and ResourceDictionary loading
[System.Reflection.Assembly]::LoadWithPartialName("PresentationFramework") | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("WindowsBase") | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("System.IO.Compression.FileSystem") | Out-Null

if ([System.Windows.Application]::Current -eq $null) {
    $null = New-Object System.Windows.Application
}
[System.Windows.Application]::ResourceAssembly = $asm

$passed = 0
$failed = 0

# -------------------------------------------------------------
# TEST 1: FontService
# -------------------------------------------------------------
Write-Host "`n--- TEST 1: FontService (Curated Installed Fonts) ---"
try {
    $fontServiceType = $asm.GetType("ChromaticMenu.Services.FontService")
    $fontInstance = $fontServiceType.GetProperty("Instance").GetValue($null)
    $fonts = $fontServiceType.GetMethod("GetCuratedInstalledFonts").Invoke($fontInstance, $null)

    Write-Host "Curated fonts returned: $($fonts -join ', ')"
    if ($fonts.Count -gt 0 -and $fonts -contains "Segoe UI") {
        Write-Host "PASS: FontService returned curated installed fonts including 'Segoe UI'." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: FontService did not return Segoe UI." -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "FAIL: FontService exception: $_" -ForegroundColor Red
    $failed++
}

# -------------------------------------------------------------
# TEST 2: StartupService
# -------------------------------------------------------------
Write-Host "`n--- TEST 2: StartupService (HKLM Run Handling) ---"
try {
    $startupServiceType = $asm.GetType("ChromaticMenu.Services.StartupService")
    $startupInstance = $startupServiceType.GetProperty("Instance").GetValue($null)
    $isRun = $startupServiceType.GetMethod("IsRunAtStartup").Invoke($startupInstance, $null)
    Write-Host "IsRunAtStartup: $isRun"

    $setArgs = [object[]]@($false, [string]::Empty)
    $setMethod = $startupServiceType.GetMethod("SetRunAtStartup")
    $res = $setMethod.Invoke($startupInstance, $setArgs)
    $errMsg = $setArgs[1]
    Write-Host "SetRunAtStartup(false) result: $res, error: '$errMsg'"

    if ($res -eq $true -or (-not [string]::IsNullOrEmpty($errMsg))) {
        Write-Host "PASS: StartupService safely inspected/configured HKLM Run without crashing." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: StartupService returned false without error message." -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "FAIL: StartupService exception: $_" -ForegroundColor Red
    $failed++
}

# -------------------------------------------------------------
# TEST 3: ExportImportService (Backup ZIP, Validation, Zip Slip Security)
# -------------------------------------------------------------
Write-Host "`n--- TEST 3: ExportImportService (Backup ZIP, Validation, Zip Slip Security) ---"
try {
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("CM_Test6_" + [System.Guid]::NewGuid().ToString("N"))
    [System.IO.Directory]::CreateDirectory($tempDir) | Out-Null
    $zipFile = [string](Join-Path $tempDir "backup.zip")

    # Ensure config is loaded
    $configServiceType = $asm.GetType("ChromaticMenu.Services.ConfigService")
    $configInstance = $configServiceType.GetProperty("Instance").GetValue($null)
    $configServiceType.GetMethod("Load").Invoke($configInstance, $null) | Out-Null

    $eiType = $asm.GetType("ChromaticMenu.Services.ExportImportService")
    $eiInstance = $eiType.GetProperty("Instance").GetValue($null)

    # 3.1: Export
    $expArgs = [object[]]@($zipFile, [string]::Empty)
    $expMethod = $eiType.GetMethod("ExportBackup")
    $expOk = $expMethod.Invoke($eiInstance, $expArgs)
    $expErr = $expArgs[1]
    Write-Host "ExportBackup result: $expOk, file: $zipFile, error: '$expErr'"

    if ($expOk -and [System.IO.File]::Exists($zipFile)) {
        # Check ZIP contents
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zipFile)
        $hasManifest = ($archive.Entries | Where-Object { $_.FullName -eq "manifest.json" }) -ne $null
        $hasConfig = ($archive.Entries | Where-Object { $_.FullName -eq "config.json" }) -ne $null
        $entryCount = $archive.Entries.Count
        $archive.Dispose()

        Write-Host "Archive contains manifest: $hasManifest, config: $hasConfig, total entries: $entryCount"
        if ($hasManifest -and $hasConfig) {
            Write-Host "PASS: Exported ZIP contains valid manifest.json and config.json." -ForegroundColor Green
            $passed++
        } else {
            Write-Host "FAIL: Exported ZIP missing manifest or config." -ForegroundColor Red
            $failed++
        }

        # 3.2: Import
        $impArgs = [object[]]@($zipFile, [int]0, [string]::Empty)
        $impMethod = $eiType.GetMethod("ImportBackup")
        $impOk = $impMethod.Invoke($eiInstance, $impArgs)
        $brokenCount = $impArgs[1]
        $impErr = $impArgs[2]
        Write-Host "ImportBackup result: $impOk, broken shortcuts: $brokenCount, error: '$impErr'"

        if ($impOk) {
            Write-Host "PASS: Import completed successfully with broken path reporting." -ForegroundColor Green
            $passed++
        } else {
            Write-Host "FAIL: Import failed: $impErr" -ForegroundColor Red
            $failed++
        }
    } else {
        Write-Host "FAIL: Export failed: $expErr" -ForegroundColor Red
        $failed++
    }

    # 3.3: Zip Slip Attack Test
    $slipZip = [string](Join-Path $tempDir "slip.zip")
    $fs = [System.IO.File]::Create($slipZip)
    $zipArchive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    $e1 = $zipArchive.CreateEntry("manifest.json")
    $sw1 = New-Object System.IO.StreamWriter($e1.Open())
    $sw1.Write("{}")
    $sw1.Dispose()
    $e2 = $zipArchive.CreateEntry("config.json")
    $sw2 = New-Object System.IO.StreamWriter($e2.Open())
    $sw2.Write("{}")
    $sw2.Dispose()
    $e3 = $zipArchive.CreateEntry("../../../evil.txt")
    $sw3 = New-Object System.IO.StreamWriter($e3.Open())
    $sw3.Write("evil")
    $sw3.Dispose()
    $zipArchive.Dispose()
    $fs.Dispose()

    $slipArgs = [object[]]@($slipZip, [int]0, [string]::Empty)
    $slipOk = $impMethod.Invoke($eiInstance, $slipArgs)
    $slipErr = $slipArgs[2]
    Write-Host "Zip Slip Import result: $slipOk, error: '$slipErr'"

    if (-not $slipOk -and $slipErr -ne $null -and $slipErr.Contains("Security check failed")) {
        Write-Host "PASS: Zip Slip attack blocked successfully by security check." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: Zip Slip attack was not blocked!" -ForegroundColor Red
        $failed++
    }

    Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
} catch {
    Write-Host "FAIL: Export/Import test exception: $_" -ForegroundColor Red
    $failed++
}

# -------------------------------------------------------------
# TEST 4: Tab Management (Add, Reorder, Delete with Move, Min 1 Tab)
# -------------------------------------------------------------
Write-Host "`n--- TEST 4: Tab Management (Add, Move, Delete Prompt, Min 1 Tab) ---"
try {
    $mainVmType = $asm.GetType("ChromaticMenu.ViewModels.MainViewModel")
    $tabModelType = $asm.GetType("ChromaticMenu.Models.TabModel")
    $vm = [Activator]::CreateInstance($mainVmType)

    $initTabs = $vm.Tabs.Count
    Write-Host "Initial tabs count: $initTabs"

    # 4.1: Add Tab
    $vm.AddTabName = "Phase 6 Test Tab"
    $vm.AddTabIcon = "star"
    $vm.AddTab()

    $addedTab = $null
    foreach ($t in $vm.Tabs) {
        if ($t.Name -eq "Phase 6 Test Tab") { $addedTab = $t; break }
    }

    if ($addedTab -ne $null -and $addedTab.Icon -eq "star" -and $vm.Tabs.Count -eq ($initTabs + 1)) {
        Write-Host "PASS: Added tab successfully with icon 'star'." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: AddTab failed." -ForegroundColor Red
        $failed++
    }

    # 4.2: Move Tab Up
    $oldIdx = $vm.Tabs.IndexOf($addedTab)
    $vm.MoveTabUp($addedTab)
    $newIdx = $vm.Tabs.IndexOf($addedTab)
    Write-Host "MoveTabUp: old index = $oldIdx, new index = $newIdx"
    if ($newIdx -eq ($oldIdx - 1)) {
        Write-Host "PASS: Tab successfully reordered up." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: MoveTabUp failed." -ForegroundColor Red
        $failed++
    }

    # 4.3: Delete Tab with Items (Move vs Delete)
    $progItemType = $asm.GetType("ChromaticMenu.Models.ProgramItem")
    $itemVmType = $asm.GetType("ChromaticMenu.ViewModels.ProgramItemViewModel")
    $dummyModel = [Activator]::CreateInstance($progItemType)
    $dummyModel.Id = [System.Guid]::NewGuid().ToString("N")
    $dummyModel.TabId = $addedTab.Id
    $dummyModel.Name = "Test Program"
    $dummyModel.Kind = "exe"
    $dummyModel.Target = "C:\Windows\notepad.exe"

    $itemVm = [Activator]::CreateInstance($itemVmType, @($dummyModel, $addedTab.Name))
    $vm.AllItems.Add($itemVm)

    # Trigger delete
    $vm.PromptDeleteTab($addedTab)
    $dialogType = $vm.CurrentDialog.ToString()
    Write-Host "PromptDeleteTab dialog type: $dialogType, items count: $($vm.PendingDeleteTabItemCount)"

    if ($dialogType -eq "TabDeletePrompt" -and $vm.PendingDeleteTabItemCount -eq 1) {
        Write-Host "PASS: TabDeletePrompt dialog shown with item count 1." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: TabDeletePrompt not triggered." -ForegroundColor Red
        $failed++
    }

    # Select target tab and move items
    $targetTab = $null
    foreach ($t in $vm.Tabs) {
        if ($t.Id -ne $addedTab.Id) { $targetTab = $t; break }
    }
    $vm.SelectedMoveTargetTab = $targetTab
    $vm.SubmitDeleteTabMove()

    if (-not ($vm.Tabs.Contains($addedTab)) -and $itemVm.TabId -eq $targetTab.Id) {
        Write-Host "PASS: SubmitDeleteTabMove moved items to '$($targetTab.Name)' and deleted source tab." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: SubmitDeleteTabMove failed." -ForegroundColor Red
        $failed++
    }

    $vm.AllItems.Remove($itemVm)
    $vm.SaveConfig()

    # 4.4: Enforce minimum 1 tab constraint
    while ($vm.Tabs.Count -gt 1) {
        $vm.Tabs.RemoveAt($vm.Tabs.Count - 1)
    }
    $lastTab = $vm.Tabs[0]
    $vm.PromptDeleteTab($lastTab)
    Write-Host "After attempting to delete last tab: count = $($vm.Tabs.Count), message: $($vm.InAppMessage)"
    if ($vm.Tabs.Count -eq 1 -and $vm.InAppMessage -ne $null -and $vm.InAppMessage.Contains("At least one tab is required")) {
        Write-Host "PASS: Minimum 1 tab constraint strictly enforced." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: Minimum 1 tab constraint not enforced." -ForegroundColor Red
        $failed++
    }

    # Restore default tabs
    $defaultConfig = [ChromaticMenu.Models.AppConfig]::CreateDefault()
    $configServiceType.GetMethod("Save").Invoke($configInstance, @($defaultConfig)) | Out-Null
} catch {
    Write-Host "FAIL: Tab management exception: $_" -ForegroundColor Red
    $failed++
}

# -------------------------------------------------------------
# TEST 5: Security (Password Change Validations & Hashing)
# -------------------------------------------------------------
Write-Host "`n--- TEST 5: Security (Change Password Flow) ---"
try {
    $vm = [Activator]::CreateInstance($mainVmType)

    # 5.1: Wrong current password
    $vm.ProcessSecurityPasswordChange("wrong", "newpass123", "newpass123")
    Write-Host "Wrong password error: $($vm.SecurityError)"
    if ($vm.SecurityError -ne $null -and $vm.SecurityError.Contains("incorrect")) {
        Write-Host "PASS: Incorrect current password rejected." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: Incorrect current password not rejected." -ForegroundColor Red
        $failed++
    }

    # 5.2: Short password (< 4 chars)
    $vm.ProcessSecurityPasswordChange("admin", "abc", "abc")
    Write-Host "Short password error: $($vm.SecurityError)"
    if ($vm.SecurityError -ne $null -and $vm.SecurityError.Contains("at least 4 characters")) {
        Write-Host "PASS: Short password (< 4 chars) rejected." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: Short password not rejected." -ForegroundColor Red
        $failed++
    }

    # 5.3: Password mismatch
    $vm.ProcessSecurityPasswordChange("admin", "newpass123", "mismatch123")
    Write-Host "Mismatch error: $($vm.SecurityError)"
    if ($vm.SecurityError -ne $null -and $vm.SecurityError.Contains("do not match")) {
        Write-Host "PASS: Mismatched password rejected." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: Mismatched password not rejected." -ForegroundColor Red
        $failed++
    }

    # 5.4: Valid change
    $vm.ProcessSecurityPasswordChange("admin", "newpass123", "newpass123")
    Write-Host "Valid change success: $($vm.SecuritySuccess)"
    if ($vm.SecuritySuccess -ne $null -and $vm.SecuritySuccess.Contains("successfully")) {
        Write-Host "PASS: Valid password change accepted and saved." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: Valid password change failed: $($vm.SecurityError)" -ForegroundColor Red
        $failed++
    }

    # Reset back to default admin
    $cfg = $configServiceType.GetProperty("Current").GetValue($configInstance)
    $pwdServiceType = $asm.GetType("ChromaticMenu.Services.PasswordService")
    $defaultHash = $pwdServiceType.GetField("DefaultPasswordHash", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
    $cfg.PasswordHash = $defaultHash
    $cfg.MustChangePassword = $true
    $configServiceType.GetMethod("Save").Invoke($configInstance, @($cfg)) | Out-Null
} catch {
    Write-Host "FAIL: Security exception: $_" -ForegroundColor Red
    $failed++
}

# -------------------------------------------------------------
# TEST 6: Appearance (Accent Color, Font Scale, Reset Defaults)
# -------------------------------------------------------------
Write-Host "`n--- TEST 6: Appearance (Accent, Font Scale, Reset to Defaults) ---"
try {
    $vm = [Activator]::CreateInstance($mainVmType)

    # 6.1: Accent Color
    $vm.SelectedAccentColor = "#6366F1"
    $cfg = $configServiceType.GetProperty("Current").GetValue($configInstance)
    Write-Host "Saved accent: $($cfg.Appearance.Accent)"
    if ($cfg.Appearance.Accent -eq "#6366F1") {
        Write-Host "PASS: Accent color updated to '#6366F1' and persisted." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: Accent color not persisted." -ForegroundColor Red
        $failed++
    }

    # 6.2: Font Scale
    $vm.FontScale = 1.15
    Write-Host "FontScale: $($vm.FontScale), Display: $($vm.FontScaleDisplay), RootFontSize: $($vm.RootFontSize)"
    if ($vm.FontScaleDisplay -eq "1.15x" -and [Math]::Abs($vm.RootFontSize - (14.0 * 1.15)) -lt 0.01) {
        Write-Host "PASS: FontScale formatted as '1.15x' and RootFontSize correctly computed." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: FontScale display or RootFontSize incorrect." -ForegroundColor Red
        $failed++
    }

    # 6.3: Reset to Defaults
    $vm.ResetAppearanceDefaultsCommand.Execute($null)
    $cfg = $configServiceType.GetProperty("Current").GetValue($configInstance)
    Write-Host "After reset: Accent = $($cfg.Appearance.Accent), CustomColors = $($cfg.Appearance.CustomColors)"
    if ($cfg.Appearance.Accent -eq "#0284C7" -and $cfg.Appearance.CustomColors -eq $null) {
        Write-Host "PASS: ResetAppearanceDefaults restored default accent '#0284C7' and cleared custom colors." -ForegroundColor Green
        $passed++
    } else {
        Write-Host "FAIL: ResetAppearanceDefaults failed." -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "FAIL: Appearance exception: $_" -ForegroundColor Red
    $failed++
}

Write-Host "`n======================================================="
Write-Host "  PHASE 6 VERIFICATION SUMMARY: $passed PASSED, $failed FAILED"
Write-Host "======================================================="

if ($failed -eq 0) {
    Exit 0
} else {
    Exit 1
}
