@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Chromatic Menu - Data Backup / Restore

rem Backup/restore helper for the Chromatic Menu 1.0.3 -> 1.0.4 upgrade.
rem Upgrading from 1.0.3 uninstalls the old version first, and the 1.0.3
rem uninstaller deletes the data folder (programs, tabs, icons, logo,
rem wallpaper). Run [0] Backup before updating and [1] Restore after.
rem [2] writes the Supabase project used for telemetry into config.json;
rem Chromatic Menu has no built-in project.

rem Supabase project offered by [2] Set up Supabase. Press Enter there to use
rem these, or type different ones. Only ever put the anon/public key here.
set "DEFAULT_SUPABASE_URL=https://iqzssuggmcqburqrpvtr.supabase.co"
set "DEFAULT_SUPABASE_KEY=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImlxenNzdWdnbWNxYnVycXJwdnRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTAyMjYyNzQsImV4cCI6MjEwNTgwMjI3NH0.WPFeMFK4tRpnSksSO3tal9QhY0dBs5UwGVgAUtGIEps"

net session >nul 2>&1
if errorlevel 1 (
    echo Requesting administrator rights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "APP_EXE=Chromatic Menu.exe"
set "SERVICE_NAME=ChromaticTelemetry"
set "TOOL_PATH=%~f0"
set "BACKUP_ROOT=%SystemDrive%\ChromaticMenu-Backup"
set "LATEST=%BACKUP_ROOT%\latest"
set "PREVIOUS=%BACKUP_ROOT%\previous"
set "PRERESTORE=%BACKUP_ROOT%\pre-restore"

call :FindInstallDir
set "INSTALL_DATA=%INSTALL_DIR%data"
set "LOCAL_DATA=%LOCALAPPDATA%\ChromaticMenu\data"

:Menu
cls
echo ============================================================
echo  Chromatic Menu - Data Backup / Restore
echo ============================================================
echo  Install folder : %INSTALL_DIR%
call :DescribeBackup
echo  Last backup    : !BACKUP_DESC!
call :CheckDeepFreeze
echo.
echo  [0] Backup           (run BEFORE updating)
echo  [1] Restore          (run AFTER updating)
echo  [2] Set up Supabase  (after updating to 1.0.4 or newer)
echo  [3] Open backup folder
echo  [4] Exit
echo.
choice /c 01234 /n /m "Select an option: "
if errorlevel 5 goto :End
if errorlevel 4 goto :OpenFolder
if errorlevel 3 goto :SetupSupabase
if errorlevel 2 goto :Restore
goto :Backup

rem ------------------------------------------------------------------
:Backup
cls
echo === Backup ===
echo.
call :PickSource
if not defined SOURCE_DATA (
    echo No config.json was found in:
    echo   %INSTALL_DATA%
    echo   %LOCAL_DATA%
    echo Nothing to back up.
    goto :Pause
)
echo Source folder : !SOURCE_DATA!
call :CountConfig "!SOURCE_DATA!\config.json"
set "CUR_ITEMS=!CNT_ITEMS!"
set "CUR_TABS=!CNT_TABS!"
echo Current data  : !CUR_ITEMS! programs, !CUR_TABS! tabs
echo.

rem Guard against backing up an already-wiped folder over a good backup.
if exist "%LATEST%\data\config.json" (
    call :CountConfig "%LATEST%\data\config.json"
    set "OLD_ITEMS=!CNT_ITEMS!"
    call :IsLess "!CUR_ITEMS!" "!OLD_ITEMS!"
    if defined IS_LESS (
        echo WARNING: the existing backup has !OLD_ITEMS! programs, but the current data has only !CUR_ITEMS!.
        echo If the update already wiped your data, do NOT back up again. Use Restore instead.
        echo.
        set "CONFIRM="
        set /p "CONFIRM=Type YES to overwrite the existing backup anyway: "
        if /i not "!CONFIRM!"=="YES" (
            echo Backup cancelled. The existing backup was kept.
            goto :Pause
        )
    )
)

if not exist "%BACKUP_ROOT%" mkdir "%BACKUP_ROOT%"
if exist "%PREVIOUS%" rd /s /q "%PREVIOUS%"
if exist "%LATEST%" ren "%LATEST%" previous
mkdir "%LATEST%"

echo Copying files...
robocopy "!SOURCE_DATA!" "%LATEST%\data" /E /R:1 /W:1 /XF config.json.tmp /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto :BackupFailed
if defined OTHER_DATA (
    robocopy "!OTHER_DATA!" "%LATEST%\other-data" /E /R:1 /W:1 /XF config.json.tmp /NFL /NDL /NJH /NJS /NP >nul
)

for %%F in ("!SOURCE_DATA!\config.json") do set "SIZE_SRC=%%~zF"
for %%F in ("%LATEST%\data\config.json") do set "SIZE_DST=%%~zF"
if not "!SIZE_SRC!"=="!SIZE_DST!" goto :BackupFailed

call :Now
set "EXE_PATH=%INSTALL_DIR%%APP_EXE%"
set "APP_VER=unknown"
for /f "delims=" %%v in ('powershell -NoProfile -Command "try { (Get-Item -LiteralPath $env:EXE_PATH).VersionInfo.FileVersion } catch { 'unknown' }" 2^>nul') do set "APP_VER=%%v"
> "%LATEST%\backup-info.txt" (
    echo Date=!NOW!
    echo Source=!SOURCE_DATA!
    echo AppVersion=!APP_VER!
    echo Programs=!CUR_ITEMS!
    echo Tabs=!CUR_TABS!
)

echo.
echo Backup OK: !CUR_ITEMS! programs, !CUR_TABS! tabs saved to
echo   %LATEST%
echo.
echo Next: update Chromatic Menu, then run this tool again and choose [1] Restore.
goto :Pause

:BackupFailed
echo.
echo BACKUP FAILED. The files could not be copied completely.
if exist "%LATEST%" rd /s /q "%LATEST%"
if exist "%PREVIOUS%" ren "%PREVIOUS%" latest
echo Your previous backup, if any, was kept. Do NOT update until a backup succeeds.
goto :Pause

rem ------------------------------------------------------------------
:Restore
cls
echo === Restore ===
echo.
if not exist "%LATEST%\data\config.json" (
    echo No backup was found in %LATEST%
    echo Run option [0] Backup first.
    goto :Pause
)

echo Backup details:
if exist "%LATEST%\backup-info.txt" type "%LATEST%\backup-info.txt"
call :CountConfig "%LATEST%\data\config.json"
set "BK_ITEMS=!CNT_ITEMS!"
set "BK_TABS=!CNT_TABS!"

set "CUR_ITEMS=none"
if exist "%INSTALL_DATA%\config.json" (
    call :CountConfig "%INSTALL_DATA%\config.json"
    set "CUR_ITEMS=!CNT_ITEMS!"
)
echo.
echo Current data  : !CUR_ITEMS! programs
echo Backup data   : !BK_ITEMS! programs, !BK_TABS! tabs
echo.
if "!CUR_ITEMS!"=="!BK_ITEMS!" (
    echo Your data looks intact after the update. Restoring is optional.
)
choice /c YN /m "Restore the backup now"
if errorlevel 2 (
    echo Restore cancelled. Nothing was changed.
    goto :Pause
)

rem A forced kill is required: a normal exit saves the in-memory config
rem and would overwrite the restored file with defaults.
taskkill /IM "%APP_EXE%" /F >nul 2>&1
timeout /t 2 /nobreak >nul

if exist "%PRERESTORE%" rd /s /q "%PRERESTORE%"
if exist "%INSTALL_DATA%" (
    robocopy "%INSTALL_DATA%" "%PRERESTORE%\data" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP >nul
)

if not exist "%INSTALL_DATA%" mkdir "%INSTALL_DATA%"
echo Copying files...
robocopy "%LATEST%\data" "%INSTALL_DATA%" /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 goto :RestoreFailed

rem Same permission the installer grants, so the launcher can keep saving.
icacls "%INSTALL_DATA%" /grant *S-1-5-32-545:(OI)(CI)F /T /C /Q >nul 2>&1

call :CountConfig "%INSTALL_DATA%\config.json"
if not "!CNT_ITEMS!"=="!BK_ITEMS!" goto :RestoreFailed

echo.
echo Restore OK: !CNT_ITEMS! programs, !CNT_TABS! tabs restored to
echo   %INSTALL_DATA%
echo The data from before the restore was saved to %PRERESTORE%
echo.
choice /c YN /m "Start Chromatic Menu now"
if errorlevel 2 goto :Pause
rem Launch through Explorer so the launcher runs as the normal user, not elevated.
start "" explorer.exe "%INSTALL_DIR%%APP_EXE%"
goto :Pause

:RestoreFailed
echo.
echo RESTORE FAILED. The backup is still safe in %LATEST%
echo The data from before the restore attempt is in %PRERESTORE%
goto :Pause

rem ------------------------------------------------------------------
:SetupSupabase
cls
echo === Set up Supabase ===
echo.
if not exist "%INSTALL_DATA%\config.json" (
    echo No config.json was found in %INSTALL_DATA%
    echo Install Chromatic Menu 1.0.4 or newer and start it once first.
    goto :Pause
)
rem 1.0.3 does not know the telemetry settings and would drop them on its next save.
sc query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo The %SERVICE_NAME% service is not installed on this PC.
    echo Update Chromatic Menu to 1.0.4 or newer first, then run this option again.
    goto :Pause
)

echo Press Enter to use the default shown in brackets, or type a different value.
echo.
echo Default URL : %DEFAULT_SUPABASE_URL%
set "NEW_URL="
set /p "NEW_URL=Project URL : "
if not defined NEW_URL set "NEW_URL=%DEFAULT_SUPABASE_URL%"
if "!NEW_URL:~-1!"=="/" set "NEW_URL=!NEW_URL:~0,-1!"
echo.
echo Default key : !DEFAULT_SUPABASE_KEY:~0,24!...
set "NEW_KEY="
set /p "NEW_KEY=Anon key    : "
if not defined NEW_KEY set "NEW_KEY=%DEFAULT_SUPABASE_KEY%"

if /i not "!NEW_URL:~0,8!"=="https://" (
    echo.
    echo The URL must start with https:// - nothing was changed.
    goto :Pause
)
if "!NEW_KEY:~20!"=="" (
    echo.
    echo That key is too short to be a Supabase anon key - nothing was changed.
    goto :Pause
)

echo.
echo This PC will send telemetry to:
echo   !NEW_URL!
choice /c YN /m "Save this to Chromatic Menu"
if errorlevel 2 (
    echo Cancelled. Nothing was changed.
    goto :Pause
)

rem The launcher saves its in-memory config on exit, which would undo the
rem edit, so it is force-closed first.
taskkill /IM "%APP_EXE%" /F >nul 2>&1
timeout /t 2 /nobreak >nul

if not exist "%BACKUP_ROOT%" mkdir "%BACKUP_ROOT%"
set "CFG_PATH=%INSTALL_DATA%\config.json"
set "CFG_COPY=%BACKUP_ROOT%\config-before-supabase.json"
set "SETUP_RESULT="
for /f "delims=" %%r in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "$s = [IO.File]::ReadAllText($env:TOOL_PATH); Invoke-Expression $s.Substring($s.LastIndexOf('#SUPABASE_SETUP#') + 16)"') do set "SETUP_RESULT=%%r"

for /f "tokens=1-5 delims=|" %%a in ("!SETUP_RESULT!") do (
    set "SETUP_STATUS=%%a"
    set "SETUP_ITEMS=%%b"
    set "SETUP_TABS=%%c"
    set "SETUP_STARTUP=%%d"
    set "SETUP_ERROR=%%e"
)
if not "!SETUP_STATUS!"=="OK" (
    echo.
    echo SETUP FAILED: !SETUP_RESULT!
    echo config.json was not changed.
    goto :Pause
)

echo.
echo Saved: telemetry is on and points to !NEW_URL!
echo Programs and tabs unchanged: !SETUP_ITEMS! programs, !SETUP_TABS! tabs.
echo The previous config.json was copied to %CFG_COPY%
if /i not "!SETUP_STARTUP!"=="True" (
    echo.
    echo NOTE: Start with Windows is OFF on this PC. Nothing is sent until you turn it on
    echo in Chromatic Menu, Settings, General.
)

echo.
echo Restarting the %SERVICE_NAME% service...
net stop "%SERVICE_NAME%" >nul 2>&1
net start "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo The service could not be restarted. It picks up the change on the next reboot.
) else (
    echo Service restarted.
)
echo.
choice /c YN /m "Start Chromatic Menu now"
if errorlevel 2 goto :Pause
start "" explorer.exe "%INSTALL_DIR%%APP_EXE%"
goto :Pause

rem ------------------------------------------------------------------
:OpenFolder
if not exist "%BACKUP_ROOT%" mkdir "%BACKUP_ROOT%"
start "" explorer.exe "%BACKUP_ROOT%"
goto :Menu

:Pause
echo.
pause
goto :Menu

:End
endlocal
exit /b 0

rem ------------------------------------------------------------------
rem Helpers
rem ------------------------------------------------------------------
:FindInstallDir
set "INSTALL_DIR="
for /f "tokens=2,*" %%A in ('reg query "HKLM\Software\HanazonoArchive\ChromaticMenu" /v InstallDir /reg:64 2^>nul ^| find /i "InstallDir"') do set "INSTALL_DIR=%%B"
if not defined INSTALL_DIR set "INSTALL_DIR=%ProgramFiles%\Chromatic Menu\"
if not "!INSTALL_DIR:~-1!"=="\" set "INSTALL_DIR=!INSTALL_DIR!\"
exit /b

rem Chooses the data folder the launcher actually uses. When both the
rem install folder and the LocalAppData fallback hold a config, the most
rem recently written one wins and the other is kept as other-data.
:PickSource
set "SOURCE_DATA="
set "OTHER_DATA="
set "I_CFG=%INSTALL_DATA%\config.json"
set "L_CFG=%LOCAL_DATA%\config.json"
if not exist "%I_CFG%" (
    if exist "%L_CFG%" set "SOURCE_DATA=%LOCAL_DATA%"
    exit /b
)
if not exist "%L_CFG%" (
    set "SOURCE_DATA=%INSTALL_DATA%"
    exit /b
)
set "NEWER=I"
for /f %%r in ('powershell -NoProfile -Command "if ((Get-Item -LiteralPath $env:I_CFG).LastWriteTime -ge (Get-Item -LiteralPath $env:L_CFG).LastWriteTime) { 'I' } else { 'L' }"') do set "NEWER=%%r"
if "!NEWER!"=="I" (
    set "SOURCE_DATA=%INSTALL_DATA%"
    set "OTHER_DATA=%LOCAL_DATA%"
) else (
    set "SOURCE_DATA=%LOCAL_DATA%"
    set "OTHER_DATA=%INSTALL_DATA%"
)
exit /b

:CountConfig
set "CNT_ITEMS=?"
set "CNT_TABS=?"
if not exist "%~1" exit /b
set "CFG_PATH=%~1"
for /f "tokens=1,2" %%a in ('powershell -NoProfile -Command "try { $j = Get-Content -LiteralPath $env:CFG_PATH -Raw | ConvertFrom-Json; [string]($j.items | Measure-Object).Count + ' ' + [string]($j.tabs | Measure-Object).Count } catch { '? ?' }"') do (
    set "CNT_ITEMS=%%a"
    set "CNT_TABS=%%b"
)
exit /b

:IsLess
set "IS_LESS="
set "CMP_A=%~1"
set "CMP_B=%~2"
if "%CMP_A%"=="?" exit /b
if "%CMP_B%"=="?" exit /b
if %CMP_A% LSS %CMP_B% set "IS_LESS=1"
exit /b

:DescribeBackup
set "BACKUP_DESC=none"
if not exist "%LATEST%\data\config.json" exit /b
call :CountConfig "%LATEST%\data\config.json"
set "BACKUP_DATE=unknown date"
if exist "%LATEST%\backup-info.txt" (
    for /f "tokens=1,* delims==" %%a in ('findstr /b /c:"Date=" "%LATEST%\backup-info.txt"') do set "BACKUP_DATE=%%b"
)
set "BACKUP_DESC=!BACKUP_DATE!, !CNT_ITEMS! programs, !CNT_TABS! tabs"
exit /b

:Now
set "NOW=unknown"
for /f "delims=" %%t in ('powershell -NoProfile -Command "Get-Date -Format 'yyyy-MM-dd HH:mm'"') do set "NOW=%%t"
exit /b

:CheckDeepFreeze
set "DF_RUNNING="
for %%P in (DFServ.exe FrzState2k.exe DF5Serv.exe) do (
    tasklist /FI "IMAGENAME eq %%P" 2>nul | find /i "%%P" >nul && set "DF_RUNNING=1"
)
if not defined DF_RUNNING (
    echo  Deep Freeze    : not detected
    exit /b
)
set "DF_STATE="
for %%K in ("HKLM\SOFTWARE\WOW6432Node\Faronics\Deep Freeze 6" "HKLM\SOFTWARE\Faronics\Deep Freeze 6") do (
    for /f "tokens=3,*" %%A in ('reg query %%K /v "DF Status" 2^>nul ^| find /i "DF Status"') do set "DF_STATE=%%B"
)
echo !DF_STATE! | find /i "Thaw" >nul
if errorlevel 1 (
    echo  Deep Freeze    : DETECTED - make sure this PC was booted THAWED,
    echo                   otherwise the backup or restore is lost on reboot.
) else (
    echo  Deep Freeze    : Thawed
)
exit /b

rem ------------------------------------------------------------------
rem PowerShell used by [2] Set up Supabase. The batch file never reaches
rem this part; PowerShell reads everything after the marker line below.
rem It edits only the "telemetry" block of config.json and verifies that
rem programs and tabs are unchanged before replacing the file.
rem ------------------------------------------------------------------
#SUPABASE_SETUP#
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Web.Extensions
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = 67108864
    $path = $env:CFG_PATH
    $config = $serializer.DeserializeObject([IO.File]::ReadAllText($path))
    $items = @($config['items']).Count
    $tabs = @($config['tabs']).Count

    $telemetry = $config['telemetry']
    if ($telemetry -isnot [System.Collections.IDictionary]) {
        # Created with ::new() and read back from $config so it is a plain .NET
        # dictionary; a PowerShell-wrapped object cannot be serialized.
        $config['telemetry'] = [System.Collections.Generic.Dictionary[string,object]]::new()
        $telemetry = $config['telemetry']
        $telemetry['heartbeatIntervalSeconds'] = 60
    }
    $telemetry['enabled'] = $true
    $telemetry['supabaseUrl'] = $env:NEW_URL.Trim().TrimEnd('/')
    $telemetry['supabaseAnonKey'] = $env:NEW_KEY.Trim()

    $json = $serializer.Serialize($config)
    $check = $serializer.DeserializeObject($json)
    if (@($check['items']).Count -ne $items -or @($check['tabs']).Count -ne $tabs) { throw 'verification failed: program or tab count changed' }

    Copy-Item -LiteralPath $path -Destination $env:CFG_COPY -Force
    $temp = $path + '.tmp'
    [IO.File]::WriteAllText($temp, $json, (New-Object System.Text.UTF8Encoding $false))
    Move-Item -LiteralPath $temp -Destination $path -Force
    'OK|' + $items + '|' + $tabs + '|' + [bool]$config['startWithWindows'] + '|'
}
catch {
    'ERROR|||' + '|' + ($_.Exception.Message -replace '[|\r\n]', ' ')
}
