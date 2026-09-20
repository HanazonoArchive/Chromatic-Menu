$baseDir = Split-Path -Parent $PSScriptRoot
$releaseExe = Join-Path $baseDir "src\Chromatic Menu\bin\Release\net48\Chromatic Menu.exe"
[System.Reflection.Assembly]::LoadFile($releaseExe) | Out-Null
$iconService = [ChromaticMenu.Services.IconService]::Instance
$img = $iconService.GetOrExtractIcon("test_notepad", "C:\Windows\notepad.exe", "exe", 256)
Write-Host "Notepad icon:" $img.PixelWidth "x" $img.PixelHeight
