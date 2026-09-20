# Chromatic Menu - Automated MSI Build Script
param (
    [string]$Version = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$workspaceRoot = $PSScriptRoot
if (-not $workspaceRoot) { $workspaceRoot = Get-Location }

# Auto-detect version from Chromatic Menu.csproj if not specified
$csprojPath = "$workspaceRoot\src\Chromatic Menu\Chromatic Menu.csproj"
if (-not $Version) {
    [xml]$csprojXml = Get-Content $csprojPath
    $Version = $csprojXml.Project.PropertyGroup.Version
    if (-not $Version) { $Version = "1.0.0" }
}

Write-Host "=== Building Chromatic Menu v$Version ($Configuration) ===" -ForegroundColor Cyan
& dotnet build -c $Configuration "/p:Version=$Version" "/p:AssemblyVersion=$Version.0" "/p:FileVersion=$Version.0" "$csprojPath"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$outputDir = "$workspaceRoot\bin\$Configuration"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Host "=== Compiling Uninstall Helper ===" -ForegroundColor Cyan
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /win32icon:"$workspaceRoot\src\Chromatic Menu\Resources\app.ico" /out:"$outputDir\Uninstall.exe" "$workspaceRoot\installer\UninstallHelper.cs"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Compiling Uninstall.exe failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$msiPath = "$outputDir\ChromaticMenu-Setup.msi"
$wxsPath = "$workspaceRoot\installer\ChromaticMenu.wxs"

Write-Host "=== Compiling MSI Installer v$Version via WiX v4 ===" -ForegroundColor Cyan
& wix build -d Version="$Version" -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext "$wxsPath" -o "$msiPath"
if ($LASTEXITCODE -ne 0) {
    Write-Error "WiX MSI build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$msiItem = Get-Item $msiPath
$sizeMb = [Math]::Round($msiItem.Length / 1MB, 2)
Write-Host "`nSUCCESS: MSI Installer v$Version created successfully!" -ForegroundColor Green
Write-Host "MSI File: $msiPath"
Write-Host "Size:     $sizeMb MB ($($msiItem.Length) bytes)"
