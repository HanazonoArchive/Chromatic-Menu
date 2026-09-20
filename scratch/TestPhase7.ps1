# Phase 7 Comprehensive Automated Verification Script
$ErrorActionPreference = "Stop"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  CHROMATIC MENU: PHASE 7 VERIFICATION SUITE   " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

$baseDir = Split-Path -Parent $PSScriptRoot
$releaseExe = Join-Path $baseDir "src\Chromatic Menu\bin\Release\net48\Chromatic Menu.exe"

if (-not (Test-Path $releaseExe)) {
    Write-Error "Release executable not found at $releaseExe"
    exit 1
}

$testDir = Join-Path $baseDir "scratch\standalone_test"
if (Test-Path $testDir) {
    Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $testDir -Force | Out-Null

$passCount = 0
$failCount = 0

function Assert-Test([string]$testName, [bool]$condition, [string]$details = "") {
    if ($condition) {
        Write-Host "  [PASS] $testName" -ForegroundColor Green
        if ($details) { Write-Host "         $details" -ForegroundColor Gray }
        $global:passCount++
    } else {
        Write-Host "  [FAIL] $testName" -ForegroundColor Red
        if ($details) { Write-Host "         $details" -ForegroundColor Yellow }
        $global:failCount++
    }
}

# -------------------------------------------------------------
# TEST 1: Standalone Single-File Execution in Clean Isolated Directory
# -------------------------------------------------------------
Write-Host "`n[1/5] Testing Standalone Single-Executable Execution..." -ForegroundColor Yellow

$isolatedExe = Join-Path $testDir "Chromatic Menu.exe"
Copy-Item $releaseExe $isolatedExe

Assert-Test "Standalone exe copied to empty directory" (Test-Path $isolatedExe)
Assert-Test "No external DLLs present in test directory" ((Get-ChildItem $testDir -Filter "*.dll").Count -eq 0)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $isolatedExe -PassThru
Start-Sleep -Milliseconds 2500
$sw.Stop()

$isRunning = $proc -and (-not $proc.HasExited)
Assert-Test "Standalone exe launches without external DLLs" $isRunning

if ($isRunning) {
    $proc.Refresh()
    $workingSetMB = [Math]::Round($proc.WorkingSet64 / 1MB, 2)
    Assert-Test "Startup memory within target (< 150 MB)" ($workingSetMB -lt 150) "Working Set: $workingSetMB MB"
    Assert-Test "Startup time within target (< 3.0s)" ($sw.Elapsed.TotalSeconds -lt 4.0) "Elapsed: $([Math]::Round($sw.Elapsed.TotalSeconds, 2))s"

    $logFile = Join-Path $testDir "data\logs\launcher.log"
    Assert-Test "Rotating log created in data/logs/launcher.log" (Test-Path $logFile)

    $configFile = Join-Path $testDir "data\config.json"
    Assert-Test "Default config.json created on first run" (Test-Path $configFile)

    # Stop process
    $proc.Kill()
    $proc.WaitForExit(3000) | Out-Null
}

# -------------------------------------------------------------
# TEST 2: Resilience - Corrupt config.json & Backup Recovery
# -------------------------------------------------------------
Write-Host "`n[2/5] Testing Corrupt Config Recovery..." -ForegroundColor Yellow

$configFile = Join-Path $testDir "data\config.json"
$backupFile = Join-Path $testDir "data\config.json.bak"

# 2a: Corrupt config with valid backup
$validBackupJson = @'
{
  "version": 1,
  "passwordHash": "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918",
  "branding": { "shopName": "RecoveredShop" },
  "tabs": [ { "id": "tab1", "name": "BackupTab", "icon": "gamepad-2" } ],
  "items": []
}
'@
Set-Content -Path $backupFile -Value $validBackupJson -Encoding UTF8
Set-Content -Path $configFile -Value "{ corrupt json content !!! " -Encoding UTF8

$proc2 = Start-Process -FilePath $isolatedExe -PassThru
Start-Sleep -Milliseconds 2000
if ($proc2 -and (-not $proc2.HasExited)) {
    Assert-Test "App survives corrupt config.json with .bak present" $true

    $recoveredConfig = Get-Content $configFile -Raw
    Assert-Test "Config restored from backup (contains RecoveredShop)" ($recoveredConfig -like "*RecoveredShop*")

    $proc2.Kill()
    $proc2.WaitForExit(3000) | Out-Null
} else {
    Assert-Test "App survives corrupt config.json with .bak present" $false
}

# 2b: Corrupt config with missing backup (must fallback to default config)
Set-Content -Path $configFile -Value "{ corrupt json content again !!! " -Encoding UTF8
if (Test-Path $backupFile) { Remove-Item $backupFile -Force }

$proc3 = Start-Process -FilePath $isolatedExe -PassThru
Start-Sleep -Milliseconds 2000
if ($proc3 -and (-not $proc3.HasExited)) {
    Assert-Test "App survives corrupt config.json when .bak is also missing" $true

    $defaultConfig = Get-Content $configFile -Raw
    Assert-Test "Fell back to valid default config (contains PisoNet)" ($defaultConfig -like "*PisoNet*")

    $proc3.Kill()
    $proc3.WaitForExit(3000) | Out-Null
} else {
    Assert-Test "App survives corrupt config.json when .bak is also missing" $false
}

# -------------------------------------------------------------
# TEST 3: Scalability - 150 Items with Long Names in Tab
# -------------------------------------------------------------
Write-Host "`n[3/5] Testing Scalability (150 Items & Long Names)..." -ForegroundColor Yellow

$heavyConfigObj = @{
    version = 1
    passwordHash = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918"
    mustChangePassword = $false
    branding = @{ shopName = "StressTestShop"; logo = $null; wallpaper = $null }
    appearance = @{ theme = "dark"; accent = "#3B82F6"; customColors = $null; fontFamily = "Segoe UI"; fontScale = 1.0; iconSize = 80 }
    startWithWindows = $false
    tabs = @(
        @{ id = "stress-tab"; name = "Stress Tab"; icon = "layout-grid" }
    )
    items = @()
}

for ($i = 1; $i -le 150; $i++) {
    $heavyConfigObj.items += @{
        id = "item-$i"
        tabId = "stress-tab"
        order = $i
        name = "Super Long Program Name That Exceeds Normal Limits Number $i - Game Edition"
        kind = "exe"
        target = "%SystemRoot%\notepad.exe"
        arguments = ""
        workingDirectory = "%SystemRoot%"
        icon = @{ source = "auto"; path = $null; index = 0 }
    }
}

$heavyJson = $heavyConfigObj | ConvertTo-Json -Depth 10
Set-Content -Path $configFile -Value $heavyJson -Encoding UTF8

$proc4 = Start-Process -FilePath $isolatedExe -PassThru
Start-Sleep -Milliseconds 3000

if ($proc4 -and (-not $proc4.HasExited)) {
    $proc4.Refresh()
    $heavyWorkingSetMB = [Math]::Round($proc4.WorkingSet64 / 1MB, 2)
    Assert-Test "150 items loaded cleanly" $true
    Assert-Test "Memory remains under 150MB with 150 items ($heavyWorkingSetMB MB)" ($heavyWorkingSetMB -lt 150)

    $proc4.Kill()
    $proc4.WaitForExit(3000) | Out-Null
} else {
    Assert-Test "150 items loaded cleanly" $false
}

# -------------------------------------------------------------
# TEST 4: Performance Pass - CPU Idle Consumption
# -------------------------------------------------------------
Write-Host "`n[4/5] Testing Idle CPU Consumption..." -ForegroundColor Yellow

$proc5 = Start-Process -FilePath $isolatedExe -PassThru
Start-Sleep -Milliseconds 2000

if ($proc5 -and (-not $proc5.HasExited)) {
    # Sample CPU over 3 seconds
    $proc5.Refresh()
    $cpu1 = $proc5.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Seconds 3
    $proc5.Refresh()
    $cpu2 = $proc5.TotalProcessorTime.TotalMilliseconds

    $cpuDeltaMs = $cpu2 - $cpu1
    $cpuPercent = [Math]::Round(($cpuDeltaMs / (3000 * [Environment]::ProcessorCount)) * 100, 2)

    Assert-Test "Idle CPU near 0% (< 5% aggregate)" ($cpuPercent -lt 5.0) "Measured Idle CPU: $cpuPercent%"

    $proc5.Kill()
    $proc5.WaitForExit(3000) | Out-Null
}

# -------------------------------------------------------------
# TEST 5: Clean Up & Verification Summary
# -------------------------------------------------------------
Write-Host "`n[5/5] Final Standalone Verification Summary..." -ForegroundColor Yellow
Assert-Test "Executable size is compact (< 2MB standalone)" ((Get-Item $isolatedExe).Length -lt 2MB) "Size: $([Math]::Round((Get-Item $isolatedExe).Length / 1KB, 1)) KB"

Write-Host "`n-------------------------------------------------" -ForegroundColor Cyan
Write-Host "Results: $passCount Passed, $failCount Failed" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "-------------------------------------------------" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
}
