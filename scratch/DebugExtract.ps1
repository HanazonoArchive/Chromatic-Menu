$baseDir = Split-Path -Parent $PSScriptRoot
$releaseExe = Join-Path $baseDir "src\Chromatic Menu\bin\Release\net48\Chromatic Menu.exe"
[System.Reflection.Assembly]::LoadFile($releaseExe) | Out-Null

$shfi = [ChromaticMenu.Native.NativeMethods+SHFILEINFO]::new()
$sz = [System.Runtime.InteropServices.Marshal]::SizeOf($shfi)
$ret = [ChromaticMenu.Native.NativeMethods]::SHGetFileInfo("C:\Windows\notepad.exe", 0, [ref]$shfi, $sz, [ChromaticMenu.Native.NativeMethods]::SHGFI_SYSICONINDEX)
Write-Host "SHGetFileInfo ret:" $ret "iIcon:" $shfi.iIcon

$iid = [Guid]::new("46EB5926-582E-4017-9FDF-E8998DAA0950")
$iml = $null
$hr = [ChromaticMenu.Native.NativeMethods]::SHGetImageList([ChromaticMenu.Native.NativeMethods]::SHIL_JUMBO, [ref]$iid, [ref]$iml)
Write-Host "SHGetImageList JUMBO hr:" $hr "iml is null?" ($iml -eq $null)

$hIcon = [IntPtr]::Zero
if ($iml -ne $null) {
    $hr2 = $iml.GetIcon($shfi.iIcon, [ChromaticMenu.Native.NativeMethods]::ILD_TRANSPARENT, [ref]$hIcon)
    Write-Host "GetIcon hr:" $hr2 "hIcon:" $hIcon
}

if ($hIcon -ne [IntPtr]::Zero) {
    $ico = [System.Drawing.Icon]::FromHandle($hIcon)
    Write-Host "Extracted icon size:" $ico.Width "x" $ico.Height
}
