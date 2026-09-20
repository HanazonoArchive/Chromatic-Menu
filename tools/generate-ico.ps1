Add-Type -AssemblyName System.Drawing

function Draw-IconFrame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = [double]$size / 256.0
    $radius = 48.0 * $scale
    $margin = 12.0 * $scale
    $rect = New-Object System.Drawing.RectangleF([float]$margin, [float]$margin, [float]($size - 2*$margin), [float]($size - 2*$margin))

    # Rounded rectangle path
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float]($radius * 2)
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc([float]($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc([float]($rect.Right - $d), [float]($rect.Bottom - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, [float]($rect.Bottom - $d), $d, $d, 90, 90)
    $path.CloseFigure()

    # Dark background with gradient
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 24, 24, 32),
        [System.Drawing.Color]::FromArgb(255, 12, 12, 18),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal
    )
    $g.FillPath($bgBrush, $path)
    $bgBrush.Dispose()

    # Subtle border
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, 255, 255, 255), [float](2.0 * $scale))
    $g.DrawPath($borderPen, $path)
    $borderPen.Dispose()
    $path.Dispose()

    # Draw 4 chromatic app tiles (Cyan, Purple, Emerald, Amber)
    $cx = [float]($size / 2.0)
    $cy = [float]($size / 2.0)
    $tileSize = [float](48.0 * $scale)
    $tileGap = [float](12.0 * $scale)
    $tileRadius = [float](14.0 * $scale)

    function Draw-Tile([float]$x, [float]$y, [System.Drawing.Color]$c1, [System.Drawing.Color]$c2) {
        $tRect = New-Object System.Drawing.RectangleF($x, $y, $tileSize, $tileSize)
        $tPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $td = [float]($tileRadius * 2)
        $tPath.AddArc($tRect.X, $tRect.Y, $td, $td, 180, 90)
        $tPath.AddArc([float]($tRect.Right - $td), $tRect.Y, $td, $td, 270, 90)
        $tPath.AddArc([float]($tRect.Right - $td), [float]($tRect.Bottom - $td), $td, $td, 0, 90)
        $tPath.AddArc($tRect.X, [float]($tRect.Bottom - $td), $td, $td, 90, 90)
        $tPath.CloseFigure()

        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($tRect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        $g.FillPath($brush, $tPath)
        $brush.Dispose()
        $tPath.Dispose()
    }

    # Top-Left: Cyan / Sky
    Draw-Tile ([float]($cx - $tileSize - $tileGap/2)) ([float]($cy - $tileSize - $tileGap/2)) ([System.Drawing.Color]::FromArgb(255, 0, 210, 255)) ([System.Drawing.Color]::FromArgb(255, 0, 140, 255))
    # Top-Right: Purple / Violet
    Draw-Tile ([float]($cx + $tileGap/2)) ([float]($cy - $tileSize - $tileGap/2)) ([System.Drawing.Color]::FromArgb(255, 168, 85, 247)) ([System.Drawing.Color]::FromArgb(255, 124, 58, 237))
    # Bottom-Left: Emerald / Mint
    Draw-Tile ([float]($cx - $tileSize - $tileGap/2)) ([float]($cy + $tileGap/2)) ([System.Drawing.Color]::FromArgb(255, 16, 185, 129)) ([System.Drawing.Color]::FromArgb(255, 5, 150, 105))
    # Bottom-Right: Amber / Sunset
    Draw-Tile ([float]($cx + $tileGap/2)) ([float]($cy + $tileGap/2)) ([System.Drawing.Color]::FromArgb(255, 245, 158, 11)) ([System.Drawing.Color]::FromArgb(255, 234, 88, 12))

    $g.Dispose()
    return $bmp
}

$sizes = @(256, 48, 32, 16)
$pngStreams = @()

foreach ($s in $sizes) {
    $frame = Draw-IconFrame $s
    $ms = New-Object System.IO.MemoryStream
    $frame.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngStreams += ,$ms.ToArray()
    $ms.Dispose()
    $frame.Dispose()
}

# Write multi-res ICO file
$icoDir = "c:\Users\Hanazono Archive\Desktop\Chromatic Menu\src\Chromatic Menu\Resources"
if (-not (Test-Path $icoDir)) {
    New-Item -ItemType Directory -Path $icoDir -Force | Out-Null
}
$icoPath = Join-Path $icoDir "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR
$bw.Write([uint16]0) # Reserved
$bw.Write([uint16]1) # Type 1 = ICO
$bw.Write([uint16]$sizes.Count) # Count

$offset = 6 + (16 * $sizes.Count)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $pngStreams[$i]

    # ICONDIRENTRY
    $bw.Write([byte]($s -band 0xFF))
    $bw.Write([byte]($s -band 0xFF))
    $bw.Write([byte]0) # Color count
    $bw.Write([byte]0) # Reserved
    $bw.Write([uint16]1) # Color planes
    $bw.Write([uint16]32) # Bits per pixel
    $bw.Write([uint32]$data.Length) # Image size in bytes
    $bw.Write([uint32]$offset) # Image offset

    $offset += $data.Length
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bw.Write($pngStreams[$i])
}

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Host "Created app.ico successfully at $icoPath ($((Get-Item $icoPath).Length) bytes)"
