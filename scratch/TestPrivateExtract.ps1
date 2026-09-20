$baseDir = Split-Path -Parent $PSScriptRoot
$releaseExe = Join-Path $baseDir "src\Chromatic Menu\bin\Release\net48\Chromatic Menu.exe"
[System.Reflection.Assembly]::LoadFile($releaseExe) | Out-Null

foreach ($exe in @("C:\Windows\explorer.exe", "C:\Windows\System32\cmd.exe", "C:\Windows\System32\notepad.exe")) {
    $phicon = New-Object IntPtr[] 1
    $piconid = New-Object int[] 1
    $ret = [ChromaticMenu.Native.NativeMethods]::PrivateExtractIcons($exe, 0, 256, 256, $phicon, $piconid, 1, 0)
    Write-Host $exe "returned:" $ret "hIcon:" $phicon[0]

    if ($ret -gt 0 -and $phicon[0] -ne [IntPtr]::Zero) {
        $ico = [System.Drawing.Icon]::FromHandle($phicon[0])
        Write-Host "  Icon size:" $ico.Width "x" $ico.Height
        [ChromaticMenu.Native.NativeMethods]::DestroyIcon($phicon[0]) | Out-Null
    }
}
