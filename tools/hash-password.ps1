param (
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Password
)

$bytes = [System.Text.Encoding]::UTF8.GetBytes($Password)
$hasher = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $hasher.ComputeHash($bytes)
$hashString = -join ($hashBytes | ForEach-Object { "{0:x2}" -f $_ })

Write-Output $hashString
