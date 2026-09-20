$ErrorActionPreference = "Stop"

[System.Reflection.Assembly]::LoadFrom("c:\Users\Hanazono Archive\Desktop\Chromatic Menu\src\Chromatic Menu\bin\Release\net48\Chromatic Menu.exe") | Out-Null
Add-Type -AssemblyName "PresentationCore"
Add-Type -AssemblyName "PresentationFramework"
Add-Type -AssemblyName "WindowsBase"
Add-Type -AssemblyName "System.Windows.Forms"

Write-Host "=== CHROMATIC MENU PHASE 5 VERIFICATION ===" -ForegroundColor Cyan

$failures = 0

# 1. Test PathHelper
Write-Host "`n[1] Testing PathHelper..." -ForegroundColor Yellow
$winDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
$notepadPath = [System.IO.Path]::Combine($winDir, "notepad.exe")
$collapsed = [ChromaticMenu.Services.PathHelper]::CollapsePath($notepadPath)
$expanded = [ChromaticMenu.Services.PathHelper]::ExpandPath($collapsed)

if ($collapsed -like "*%SystemRoot%*" -or $collapsed -like "*%windir%*") {
    Write-Host "  PASS: PathHelper collapsed $notepadPath -> $collapsed" -ForegroundColor Green
} else {
    Write-Host "  FAIL: PathHelper failed to collapse $notepadPath, got $collapsed" -ForegroundColor Red
    $failures++
}

if ($expanded -eq $notepadPath) {
    Write-Host "  PASS: PathHelper expanded $collapsed -> $expanded" -ForegroundColor Green
} else {
    Write-Host "  FAIL: PathHelper failed to expand $collapsed, got $expanded" -ForegroundColor Red
    $failures++
}

# 2. Test MainViewModel Initialization & Item Addition
Write-Host "`n[2] Testing MainViewModel Item Addition & Validation..." -ForegroundColor Yellow
$vm = New-Object ChromaticMenu.ViewModels.MainViewModel

if ($vm.Tabs.Count -gt 0) {
    Write-Host "  PASS: Loaded $($vm.Tabs.Count) tabs. SelectedTab = '$($vm.SelectedTab.Name)'" -ForegroundColor Green
} else {
    Write-Host "  FAIL: No tabs loaded in MainViewModel" -ForegroundColor Red
    $failures++
}

# Create test files
$tempDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "ChromaticTest_" + [Guid]::NewGuid().ToString("N"))
[System.IO.Directory]::CreateDirectory($tempDir) | Out-Null
$testExe = [System.IO.Path]::Combine($tempDir, "TestApp.exe")
[System.IO.File]::WriteAllText($testExe, "MZ_FAKE_EXE")
$testBat = [System.IO.Path]::Combine($tempDir, "TestScript.bat")
[System.IO.File]::WriteAllText($testBat, "@echo off")
$testCmd = [System.IO.Path]::Combine($tempDir, "TestCmd.cmd")
[System.IO.File]::WriteAllText($testCmd, "@echo off")
$testFolder = [System.IO.Path]::Combine($tempDir, "TestFolder")
[System.IO.Directory]::CreateDirectory($testFolder) | Out-Null
$testPdf = [System.IO.Path]::Combine($tempDir, "Report.pdf")
[System.IO.File]::WriteAllText($testPdf, "%PDF_FAKE")

$vm.HandleDropFiles(@($testExe, $testBat, $testCmd, $testFolder, $testPdf))

$addedExe = $vm.AllItems | Where-Object { $_.Name -eq "TestApp" }
$addedBat = $vm.AllItems | Where-Object { $_.Name -eq "TestScript" }
$addedCmd = $vm.AllItems | Where-Object { $_.Name -eq "TestCmd" }
$addedFolder = $vm.AllItems | Where-Object { $_.Name -eq "TestFolder" }
$addedPdf = $vm.AllItems | Where-Object { $_.Name -eq "Report" }

if ($addedExe -and $addedExe.Kind -eq "exe") {
    Write-Host "  PASS: Added .exe successfully (Kind=exe, Target=$($addedExe.Target))" -ForegroundColor Green
} else {
    Write-Host "  FAIL: .exe was not added properly" -ForegroundColor Red
    $failures++
}

if ($addedBat -and $addedBat.Kind -eq "bat") {
    Write-Host "  PASS: Added .bat successfully (Kind=bat)" -ForegroundColor Green
} else {
    Write-Host "  FAIL: .bat was not added properly" -ForegroundColor Red
    $failures++
}

if ($addedCmd -and $addedCmd.Kind -eq "cmd") {
    Write-Host "  PASS: Added .cmd successfully (Kind=cmd)" -ForegroundColor Green
} else {
    Write-Host "  FAIL: .cmd was not added properly" -ForegroundColor Red
    $failures++
}

if ($addedFolder -and $addedFolder.Kind -eq "folder") {
    Write-Host "  PASS: Added folder successfully (Kind=folder)" -ForegroundColor Green
} else {
    Write-Host "  FAIL: folder was not added properly" -ForegroundColor Red
    $failures++
}

if (-not $addedPdf) {
    Write-Host "  PASS: Unsupported .pdf was correctly rejected" -ForegroundColor Green
} else {
    Write-Host "  FAIL: Unsupported .pdf was incorrectly added" -ForegroundColor Red
    $failures++
}

if ($vm.InAppMessage -like "*Report.pdf*") {
    Write-Host "  PASS: InAppMessage displayed for rejected file: $($vm.InAppMessage)" -ForegroundColor Green
} else {
    Write-Host "  FAIL: No warning message for rejected file" -ForegroundColor Red
    $failures++
}

# 3. Test Add Website
Write-Host "`n[3] Testing Add Website..." -ForegroundColor Yellow
$vm.WebsiteName = "DuckDuckGo"
$vm.WebsiteUrl = "https://duckduckgo.com"
$vm.SubmitAddWebsiteCommand.Execute($null)

$addedUrl = $vm.AllItems | Where-Object { $_.Name -eq "DuckDuckGo" }
if ($addedUrl -and $addedUrl.Kind -eq "url" -and $addedUrl.Target -eq "https://duckduckgo.com") {
    Write-Host "  PASS: Added website item successfully" -ForegroundColor Green
} else {
    Write-Host "  FAIL: Failed to add website item" -ForegroundColor Red
    $failures++
}

# Test invalid URL
$vm.WebsiteName = "Invalid Site"
$vm.WebsiteUrl = "ftp://something"
$vm.SubmitAddWebsiteCommand.Execute($null)
if ($vm.WebsiteError -like "*http://*") {
    Write-Host "  PASS: Invalid URL rejected with error: '$($vm.WebsiteError)'" -ForegroundColor Green
} else {
    Write-Host "  FAIL: Invalid URL was not rejected properly" -ForegroundColor Red
    $failures++
}

# 4. Test Inline Rename
Write-Host "`n[4] Testing Inline Rename..." -ForegroundColor Yellow
$addedExe.StartRenameCommand.Execute($null)
if ($addedExe.IsEditingName -and $addedExe.EditedName -eq "TestApp") {
    Write-Host "  PASS: StartRenameCommand initialized IsEditingName and EditedName" -ForegroundColor Green
} else {
    Write-Host "  FAIL: StartRenameCommand failed" -ForegroundColor Red
    $failures++
}

# Cancel rename
$addedExe.EditedName = "WrongName"
$addedExe.CancelRenameCommand.Execute($null)
if (-not $addedExe.IsEditingName -and $addedExe.Name -eq "TestApp") {
    Write-Host "  PASS: CancelRenameCommand reverted name" -ForegroundColor Green
} else {
    Write-Host "  FAIL: CancelRenameCommand failed" -ForegroundColor Red
    $failures++
}

# Commit rename
$addedExe.StartRenameCommand.Execute($null)
$addedExe.EditedName = "MyGame"
$addedExe.CommitRenameCommand.Execute($null)
if (-not $addedExe.IsEditingName -and $addedExe.Name -eq "MyGame") {
    Write-Host "  PASS: CommitRenameCommand updated name to 'MyGame'" -ForegroundColor Green
} else {
    Write-Host "  FAIL: CommitRenameCommand failed" -ForegroundColor Red
    $failures++
}

# 5. Test Reordering
Write-Host "`n[5] Testing Drag-and-Drop Reordering..." -ForegroundColor Yellow
$currentTabId = $vm.SelectedTab.Id
$tabItems = @($vm.AllItems | Where-Object { $_.TabId -eq $currentTabId } | Sort-Object Order)
if ($tabItems.Count -ge 2) {
    $firstItem = $tabItems[0]
    $secondItem = $tabItems[1]

    $vm.ReorderItem($firstItem, 1)
    $reordered = @($vm.AllItems | Where-Object { $_.TabId -eq $currentTabId } | Sort-Object Order)

    if ($reordered[0].Id -eq $secondItem.Id -and $reordered[1].Id -eq $firstItem.Id) {
        Write-Host "  PASS: ReorderItem successfully swapped items" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: ReorderItem failed to swap items" -ForegroundColor Red
        $failures++
    }
}

# 6. Test Move to Tab
Write-Host "`n[6] Testing Move to Tab..." -ForegroundColor Yellow
$otherTab = $vm.Tabs | Where-Object { $_.Id -ne $currentTabId } | Select-Object -First 1
if ($otherTab) {
    $moveItems = [ChromaticMenu.ViewModels.ProgramItemViewModel[]]@($addedExe)
    $vm.MoveItemsToTab($moveItems, $otherTab.Id)
    if ($addedExe.TabId -eq $otherTab.Id -and $addedExe.TabName -eq $otherTab.Name) {
        Write-Host "  PASS: MoveItemsToTab moved '$($addedExe.Name)' to '$($otherTab.Name)'" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: MoveItemsToTab failed" -ForegroundColor Red
        $failures++
    }
}

# 7. Test Multi-Select Remove with Confirmation
Write-Host "`n[7] Testing Multi-Select Remove with Confirmation..." -ForegroundColor Yellow
$beforeCount = $vm.AllItems.Count
$removeList = [ChromaticMenu.ViewModels.ProgramItemViewModel[]]@($addedBat, $addedCmd)
$vm.PromptRemoveItems($removeList)

if ($vm.CurrentDialog -eq [ChromaticMenu.Models.DialogType]::ConfirmDelete -and $vm.ConfirmDeleteMessage -like "*2 programs*") {
    Write-Host "  PASS: PromptRemoveItems showed ConfirmDelete dialog: '$($vm.ConfirmDeleteMessage)'" -ForegroundColor Green
} else {
    Write-Host "  FAIL: PromptRemoveItems did not show ConfirmDelete dialog" -ForegroundColor Red
    $failures++
}

$vm.SubmitConfirmDeleteCommand.Execute($null)
if ($vm.AllItems.Count -eq ($beforeCount - 2) -and (-not ($vm.AllItems -contains $addedBat)) -and (-not ($vm.AllItems -contains $addedCmd))) {
    Write-Host "  PASS: SubmitConfirmDelete removed selected items" -ForegroundColor Green
} else {
    Write-Host "  FAIL: SubmitConfirmDelete failed to remove items" -ForegroundColor Red
    $failures++
}

# Verify target files on disk were NOT deleted
if ((Test-Path $testBat) -and (Test-Path $testCmd)) {
    Write-Host "  PASS: Target files on disk were NOT deleted" -ForegroundColor Green
} else {
    Write-Host "  FAIL: Target files on disk WERE deleted!" -ForegroundColor Red
    $failures++
}

# 8. Test Icon Size Slider Persistence
Write-Host "`n[8] Testing Icon Size Slider Persistence..." -ForegroundColor Yellow
$vm.IconSize = 128
$cfg = [ChromaticMenu.Services.ConfigService]::Instance.Load()
if ($cfg.Appearance.IconSize -eq 128) {
    Write-Host "  PASS: IconSize=128 persisted to config.json" -ForegroundColor Green
} else {
    Write-Host "  FAIL: IconSize not persisted, found $($cfg.Appearance.IconSize)" -ForegroundColor Red
    $failures++
}

# 9. Test Edit Mode Context Menu vs Normal Mode Context Menu
Write-Host "`n[9] Testing Context Menu State..." -ForegroundColor Yellow
$vm.IsEditMode = $true
if ($addedUrl.IsEditMode -eq $true) {
    Write-Host "  PASS: ProgramItemViewModel received IsEditMode=true" -ForegroundColor Green
} else {
    Write-Host "  FAIL: ProgramItemViewModel did not receive IsEditMode=true" -ForegroundColor Red
    $failures++
}

$vm.IsEditMode = $false
if ($addedUrl.IsEditMode -eq $false) {
    Write-Host "  PASS: ProgramItemViewModel received IsEditMode=false" -ForegroundColor Green
} else {
    Write-Host "  FAIL: ProgramItemViewModel did not receive IsEditMode=false" -ForegroundColor Red
    $failures++
}

# Clean up temp
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`n=== VERIFICATION COMPLETE: $failures FAILURES ===" -ForegroundColor $(if ($failures -eq 0) { "Green" } else { "Red" })
if ($failures -gt 0) { exit 1 }
