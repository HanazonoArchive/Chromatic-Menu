param()

$ErrorActionPreference = "Continue"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " CHROMATIC MENU - FRESH OS SIMULATION TEST HARNESS              " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

$exePath = (Resolve-Path ".\src\Chromatic Menu\bin\Release\net48\Chromatic Menu.exe").Path
$asm = [System.Reflection.Assembly]::LoadFrom($exePath)
Write-Host "Loaded Assembly: $($asm.FullName)" -ForegroundColor Gray

# Initialize WPF Application context
[System.Reflection.Assembly]::LoadWithPartialName("PresentationFramework") | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("PresentationCore") | Out-Null
[System.Reflection.Assembly]::LoadWithPartialName("WindowsBase") | Out-Null

if ([System.Windows.Application]::Current -eq $null) {
    $null = New-Object System.Windows.Application
}
[System.Windows.Application]::ResourceAssembly = $asm

$passed = 0
$failed = 0

function Assert-Condition([bool]$cond, [string]$testName, [string]$details = "") {
    if ($cond) {
        Write-Host "[PASS] $testName" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "[FAIL] $testName - $details" -ForegroundColor Red
        $script:failed++
    }
}

$localAppData = Join-Path $env:LOCALAPPDATA "ChromaticMenu"
$backupDir = Join-Path $env:TEMP ("ChromaticMenu_Backup_" + [Guid]::NewGuid().ToString("N"))
$binDataDir = Join-Path (Split-Path $exePath -Parent) "data"
$binConfigPath = Join-Path $binDataDir "config.json"
$binBakPath = Join-Path $binDataDir "config.json.bak"
$binBackupDir = Join-Path $env:TEMP ("ChromaticMenu_BinBackup_" + [Guid]::NewGuid().ToString("N"))

$hadExistingDir = $false
$hadBinConfig = $false

try {
    # -------------------------------------------------------------
    # STAGE 0: PRISTINE OS ISOLATION
    # -------------------------------------------------------------
    Write-Host "`n--> STAGE 0: Isolating Clean OS Environment..." -ForegroundColor Yellow
    if (Test-Path $localAppData) {
        $hadExistingDir = $true
        Move-Item -Path $localAppData -Destination $backupDir -Force
    }

    if (Test-Path $binConfigPath) {
        $hadBinConfig = $true
        New-Item -ItemType Directory -Path $binBackupDir -Force | Out-Null
        Copy-Item -Path $binConfigPath -Destination $binBackupDir -Force
        Remove-Item -Path $binConfigPath -Force
        if (Test-Path $binBakPath) {
            Copy-Item -Path $binBakPath -Destination $binBackupDir -Force
            Remove-Item -Path $binBakPath -Force
        }
    }

    # Clean HKCU Run registry value
    try {
        $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Run", $true)
        if ($runKey -ne $null) {
            $runKey.DeleteValue("Chromatic Menu", $false)
            $runKey.Dispose()
        }
    } catch { }

    Assert-Condition (-not (Test-Path $localAppData)) "Clean %LOCALAPPDATA% initialized"
    Assert-Condition (-not (Test-Path $binConfigPath)) "No pre-existing config.json (Clean installation state)"

    # -------------------------------------------------------------
    # STAGE 1: FIRST LAUNCH (PRISTINE OS, NO CONFIG EXISTS)
    # -------------------------------------------------------------
    Write-Host "`n--> STAGE 1: Simulating First Launch on Fresh OS..." -ForegroundColor Yellow
    $configServiceType = $asm.GetType("ChromaticMenu.Services.ConfigService")
    $configInstance = $configServiceType.GetProperty("Instance").GetValue($null)
    $initialConfig = $configServiceType.GetMethod("Load").Invoke($configInstance, $null)

    Assert-Condition ($initialConfig -ne $null) "First Launch: Default config generated"
    Assert-Condition ($initialConfig.MustChangePassword -eq $true) "First Launch: MustChangePassword flag is TRUE"
    Assert-Condition ($initialConfig.Tabs.Count -eq 5) "First Launch: 5 default tabs created" "Count: $($initialConfig.Tabs.Count)"
    
    $configPath = $configServiceType.GetProperty("ConfigPath").GetValue($configInstance)
    Assert-Condition (Test-Path $configPath) "First Launch: config.json persisted on disk" $configPath

    # Initialize MainViewModel
    $vmType = $asm.GetType("ChromaticMenu.ViewModels.MainViewModel")
    $vm = [Activator]::CreateInstance($vmType)

    Assert-Condition ($vm.MustChangePassword -eq $true) "ViewModel: MustChangePassword is true"
    Assert-Condition ($vm.IsChangePasswordDialogVisible -eq $true) "ViewModel: First-Time Setup Change Password dialog is visible"
    Assert-Condition ($vm.ModalTitle -like "*First-Time Setup*") "ViewModel: Modal title indicates First-Time Setup" $vm.ModalTitle

    # Test Validation on First Run
    $vm.ProcessNewPassword("", "")
    Assert-Condition ($vm.NewPasswordError -like "*empty*") "Password Validation: Empty password rejected"
    
    $vm.ProcessNewPassword("abc", "abc")
    Assert-Condition ($vm.NewPasswordError -like "*4 characters*") "Password Validation: Short password (<4 chars) rejected"

    $vm.ProcessNewPassword("Secret123", "SecretDifferent")
    Assert-Condition ($vm.NewPasswordError -like "*match*") "Password Validation: Mismatched passwords rejected"

    # Set valid new administrator password
    $chosenPass = "CyberCafe@2026!"
    $vm.ProcessNewPassword($chosenPass, $chosenPass)

    Assert-Condition ($vm.IsChangePasswordDialogVisible -eq $false) "First-Time Setup: Dialog dismissed after setting password"
    Assert-Condition ($vm.IsSessionUnlocked -eq $true) "First-Time Setup: Administrator session unlocked"
    Assert-Condition ($configInstance.Current.MustChangePassword -eq $false) "First-Time Setup: MustChangePassword flag changed to FALSE in memory"

    $savedJson = [System.IO.File]::ReadAllText($configPath)
    Assert-Condition ($savedJson.Contains('"mustChangePassword": false')) "Config Persistence: JSON on disk updated with mustChangePassword=false"

    # -------------------------------------------------------------
    # STAGE 2: SECOND LAUNCH (CLOSING & REOPENING)
    # -------------------------------------------------------------
    Write-Host "`n--> STAGE 2: Simulating Second Launch (Closing & Reopening)..." -ForegroundColor Yellow
    # Create new ViewModel instance to simulate fresh launch
    $secondVm = [Activator]::CreateInstance($vmType)

    Assert-Condition ($secondVm.MustChangePassword -eq $false) "Second Launch: MustChangePassword is false"
    Assert-Condition ($secondVm.IsChangePasswordDialogVisible -eq $false) "Second Launch: Change Password dialog is NOT visible on startup"
    Assert-Condition ($secondVm.IsPasswordDialogVisible -eq $false) "Second Launch: Password prompt is NOT visible on startup"
    Assert-Condition ($secondVm.IsSessionUnlocked -eq $false) "Second Launch: Public user session is securely locked"

    # Unlock attempts
    $unlocked = $false
    $secondVm.RequestUnlock([Action]{ $script:unlocked = $true })
    Assert-Condition ($secondVm.IsPasswordDialogVisible -eq $true) "Admin Action: RequestUnlock triggers authentication dialog"

    # Reject old default "admin"
    $secondVm.ProcessPassword("admin")
    Assert-Condition ($secondVm.IsSessionUnlocked -eq $false) "Security: Default password 'admin' is rejected"
    Assert-Condition (-not [string]::IsNullOrEmpty($secondVm.PasswordError)) "Security: Error displayed for wrong password"

    # Accept chosen password
    $secondVm.ProcessPassword($chosenPass)
    Assert-Condition ($secondVm.IsSessionUnlocked -eq $true) "Security: Session unlocks with chosen password"
    Assert-Condition ($secondVm.IsPasswordDialogVisible -eq $false) "Security: Password dialog dismissed after successful unlock"
    Assert-Condition ($unlocked -eq $true) "Security: Pending admin action executed on unlock"

    # -------------------------------------------------------------
    # STAGE 3: REGISTRY STARTUP (START WITH WINDOWS)
    # -------------------------------------------------------------
    Write-Host "`n--> STAGE 3: Testing 'Start with Windows' HKCU Registry Key..." -ForegroundColor Yellow
    $startupServiceType = $asm.GetType("ChromaticMenu.Services.StartupService")
    $startupInstance = $startupServiceType.GetProperty("Instance").GetValue($null)

    # Enable Startup
    $errOut = ""
    $setMethod = $startupServiceType.GetMethod("SetRunAtStartup")
    $argsArray = [object[]]@($true, $errOut)
    $enableRes = $setMethod.Invoke($startupInstance, $argsArray)

    Assert-Condition ($enableRes -eq $true) "Startup Service: SetRunAtStartup(true) returned true"
    
    $regVal = $null
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Run", $false)
    if ($runKey -ne $null) {
        $regVal = $runKey.GetValue("Chromatic Menu")
        $runKey.Dispose()
    }
    Assert-Condition (-not [string]::IsNullOrEmpty($regVal)) "Startup Registry: 'Chromatic Menu' value exists in HKCU Run" $regVal
    Assert-Condition ($startupInstance.IsRunAtStartup() -eq $true) "Startup Service: IsRunAtStartup reports true"

    # Disable Startup
    $argsArray[0] = $false
    $disableRes = $setMethod.Invoke($startupInstance, $argsArray)
    Assert-Condition ($disableRes -eq $true) "Startup Service: SetRunAtStartup(false) returned true"
    Assert-Condition ($startupInstance.IsRunAtStartup() -eq $false) "Startup Service: IsRunAtStartup reports false after disable"

    # -------------------------------------------------------------
    # STAGE 4: DRAG & DROP AND ICON EXTRACTION ON FRESH OS
    # -------------------------------------------------------------
    Write-Host "`n--> STAGE 4: Testing Drag & Drop and Icon Extraction..." -ForegroundColor Yellow
    $notepadPath = Join-Path $env:SystemRoot "notepad.exe"
    if (-not (Test-Path $notepadPath)) {
        $notepadPath = Join-Path $env:SystemRoot "System32\notepad.exe"
    }
    $cmdPath = Join-Path $env:SystemRoot "System32\cmd.exe"

    Assert-Condition ((Test-Path $notepadPath) -and (Test-Path $cmdPath)) "System binaries exist for drop test"

    $secondVm.HandleDropFiles([string[]]@($notepadPath, $cmdPath))

    Assert-Condition ($secondVm.AllItems.Count -ge 2) "Drop Files: Items added to AllItems" "Count: $($secondVm.AllItems.Count)"
    Assert-Condition ($secondVm.FilteredItems.Count -ge 2) "Drop Files: Items present in FilteredItems" "Count: $($secondVm.FilteredItems.Count)"

    $notepadItem = $null
    foreach ($item in $secondVm.AllItems) {
        if ($item.Name -like "*notepad*") {
            $notepadItem = $item
            break
        }
    }
    Assert-Condition ($notepadItem -ne $null) "Drop Files: Notepad item exists in collection"
    Assert-Condition ($notepadItem.DisplayIcon -ne $null) "Drop Files: Icon extracted to DisplayIcon"

    $assetsDir = $configInstance.AssetsDirectory
    $iconPath = Join-Path (Join-Path $assetsDir "icons") "$($notepadItem.Id).png"
    Assert-Condition (Test-Path $iconPath) "Assets Directory: High-res icon PNG saved to disk" $iconPath

    # -------------------------------------------------------------
    # STAGE 5: HARDWARE DETECTION / BASIC ADAPTER FALLBACK
    # -------------------------------------------------------------
    Write-Host "`n--> STAGE 5: Testing System Hardware Detection & Fallback..." -ForegroundColor Yellow
    $sysInfoType = $asm.GetType("ChromaticMenu.Services.SystemInfoService")
    $sysInfoInstance = $sysInfoType.GetProperty("Instance").GetValue($null)
    $getGpusMethod = $sysInfoType.GetMethod("GetGpuList", [System.Reflection.BindingFlags]"NonPublic, Instance")
    $gpus = $getGpusMethod.Invoke($sysInfoInstance, $null)

    $gpuCount = 0
    if ($gpus -ne $null) {
        $gpuCount = $gpus.Count
    }
    Assert-Condition ($gpuCount -gt 0) "Hardware Detection: Display adapter list is populated on fresh OS" "Count: $gpuCount"

    # -------------------------------------------------------------
    # STAGE 6: UNINSTALLATION CLEANUP SIMULATION
    # -------------------------------------------------------------
    Write-Host "`n--> STAGE 6: Testing Clean Uninstallation..." -ForegroundColor Yellow
    if (Test-Path $localAppData) {
        Remove-Item -Path $localAppData -Recurse -Force
    }
    Assert-Condition (-not (Test-Path $localAppData)) "Uninstallation: %LOCALAPPDATA% directory cleanly purged"

} catch {
    Write-Host "`n[FATAL EXCEPTION]: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    $script:failed++
} finally {
    # Restore original backup if one existed
    try {
        if ($hadExistingDir -and (Test-Path $backupDir)) {
            if (Test-Path $localAppData) {
                Remove-Item -Path $localAppData -Recurse -Force
            }
            Move-Item -Path $backupDir -Destination $localAppData -Force
            Write-Host "`nRestored original user environment from backup." -ForegroundColor Gray
        } elseif (Test-Path $backupDir) {
            Remove-Item -Path $backupDir -Recurse -Force
        }

        if ($hadBinConfig -and (Test-Path $binBackupDir)) {
            Copy-Item -Path "$binBackupDir\*" -Destination $binDataDir -Force
            Remove-Item -Path $binBackupDir -Recurse -Force
        } elseif (Test-Path $binBackupDir) {
            Remove-Item -Path $binBackupDir -Recurse -Force
        }
    } catch { }
}

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host " RESULTS: $passed PASSED, $failed FAILED" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "=================================================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
} else {
    exit 0
}
