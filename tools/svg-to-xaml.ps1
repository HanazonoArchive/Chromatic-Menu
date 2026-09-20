<#
.SYNOPSIS
    Converts Lucide SVG icons to WPF StreamGeometry in LucideIcons.xaml.
.DESCRIPTION
    Parses SVG elements (path, line, circle, rect, polyline, polygon, ellipse)
    into standard WPF mini-language path data and outputs a ResourceDictionary.
#>
param (
    [string]$SvgDir = "$PSScriptRoot\svg",
    [string]$OutputFile = "$PSScriptRoot\..\src\Chromatic Menu\Resources\Icons\LucideIcons.xaml"
)

$SvgDir = [System.IO.Path]::GetFullPath($SvgDir)
$OutputFile = [System.IO.Path]::GetFullPath($OutputFile)
$ErrorActionPreference = "Stop"

if (-not (Test-Path $SvgDir)) {
    New-Item -ItemType Directory -Path $SvgDir -Force | Out-Null
}

$outputDir = Split-Path -Parent $OutputFile
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

function Normalize-SvgPath {
    param ([string]$d)
    $matches = [regex]::Matches($d, "([a-zA-Z])|([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)")
    $sb = [System.Text.StringBuilder]::new()
    $currentCmd = [char]0
    $coordIndex = 0
    $isFirstCmd = $true

    foreach ($m in $matches) {
        $token = $m.Value
        if ([char]::IsLetter($token[0])) {
            $currentCmd = $token[0]
            $coordIndex = 0
            if ($isFirstCmd -and $currentCmd -eq [char]'m') {
                [void]$sb.Append(" M ")
            } else {
                [void]$sb.Append(" $currentCmd ")
            }
            $isFirstCmd = $false
        } else {
            if ($currentCmd -eq [char]'m') {
                if ($coordIndex -eq 2) {
                    [void]$sb.Append(" l ")
                }
            } elseif ($currentCmd -eq [char]'M') {
                if ($coordIndex -eq 2) {
                    [void]$sb.Append(" L ")
                }
            }
            [void]$sb.Append("$token ")
            $coordIndex++
        }
    }
    return $sb.ToString().Trim()
}

function Convert-SvgToWpfPath {
    param ([xml]$xmlDoc)
    
    $pathTokens = [System.Collections.Generic.List[string]]::new()
    
    # Process all direct child elements of <svg>
    foreach ($node in $xmlDoc.svg.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        
        switch ($node.LocalName) {
            "path" {
                $d = $node.GetAttribute("d")
                if (-not [string]::IsNullOrWhiteSpace($d)) {
                    $pathTokens.Add((Normalize-SvgPath $d))
                }
            }
            "line" {
                $x1 = $node.GetAttribute("x1")
                $y1 = $node.GetAttribute("y1")
                $x2 = $node.GetAttribute("x2")
                $y2 = $node.GetAttribute("y2")
                $pathTokens.Add("M $x1 $y1 L $x2 $y2")
            }
            "circle" {
                $cx = [double]$node.GetAttribute("cx")
                $cy = [double]$node.GetAttribute("cy")
                $r  = [double]$node.GetAttribute("r")
                $xStart = $cx - $r
                $xEnd   = $cx + $r
                $pathTokens.Add("M $xStart,$cy A $r,$r 0 1 0 $xEnd,$cy A $r,$r 0 1 0 $xStart,$cy")
            }
            "ellipse" {
                $cx = [double]$node.GetAttribute("cx")
                $cy = [double]$node.GetAttribute("cy")
                $rx = [double]$node.GetAttribute("rx")
                $ry = [double]$node.GetAttribute("ry")
                $xStart = $cx - $rx
                $xEnd   = $cx + $rx
                $pathTokens.Add("M $xStart,$cy A $rx,$ry 0 1 0 $xEnd,$cy A $rx,$ry 0 1 0 $xStart,$cy")
            }
            "rect" {
                $x = [double]$node.GetAttribute("x")
                $y = [double]$node.GetAttribute("y")
                $w = [double]$node.GetAttribute("width")
                $h = [double]$node.GetAttribute("height")
                $rxStr = $node.GetAttribute("rx")
                $ryStr = $node.GetAttribute("ry")
                
                if (-not [string]::IsNullOrWhiteSpace($rxStr) -or -not [string]::IsNullOrWhiteSpace($ryStr)) {
                    $rx = if (-not [string]::IsNullOrWhiteSpace($rxStr)) { [double]$rxStr } else { [double]$ryStr }
                    $ry = if (-not [string]::IsNullOrWhiteSpace($ryStr)) { [double]$ryStr } else { [double]$rxStr }
                    
                    $x0 = $x + $rx
                    $x1 = $x + $w - $rx
                    $y0 = $y
                    $y1 = $y + $h - $ry
                    $y2 = $y + $h
                    $x2 = $x
                    $pathTokens.Add("M $x0,$y0 H $x1 A $rx,$ry 0 0 1 $($x+$w),$($y+$ry) V $y1 A $rx,$ry 0 0 1 $x1,$y2 H $x0 A $rx,$ry 0 0 1 $x2,$y1 V $($y+$ry) A $rx,$ry 0 0 1 $x0,$y0 Z")
                } else {
                    $pathTokens.Add("M $x $y H $($x+$w) V $($y+$h) H $x Z")
                }
            }
            "polyline" {
                $pts = $node.GetAttribute("points").Trim() -split "[\s,]+"
                if ($pts.Count -ge 2) {
                    $segments = [System.Collections.Generic.List[string]]::new()
                    $segments.Add("M $($pts[0]) $($pts[1])")
                    for ($i = 2; $i -lt $pts.Count; $i += 2) {
                        $segments.Add("L $($pts[$i]) $($pts[$i+1])")
                    }
                    $pathTokens.Add(($segments -join " "))
                }
            }
            "polygon" {
                $pts = $node.GetAttribute("points").Trim() -split "[\s,]+"
                if ($pts.Count -ge 2) {
                    $segments = [System.Collections.Generic.List[string]]::new()
                    $segments.Add("M $($pts[0]) $($pts[1])")
                    for ($i = 2; $i -lt $pts.Count; $i += 2) {
                        $segments.Add("L $($pts[$i]) $($pts[$i+1])")
                    }
                    $segments.Add("Z")
                    $pathTokens.Add(($segments -join " "))
                }
            }
        }
    }
    
    return ($pathTokens -join " ")
}

$xamlLines = [System.Collections.Generic.List[string]]::new()
$xamlLines.Add('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"')
$xamlLines.Add('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
$xamlLines.Add('    <!-- Converted from official Lucide SVG icons. -->')

$svgFiles = Get-ChildItem -Path $SvgDir -Filter "*.svg" | Sort-Object Name
$geometries = @{}

foreach ($file in $svgFiles) {
    $iconName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    [xml]$xml = Get-Content -Path $file.FullName -Raw
    $pathData = Convert-SvgToWpfPath -xmlDoc $xml
    $geometries[$iconName] = $pathData
    $xamlLines.Add("    <StreamGeometry x:Key=""Icon.$iconName"">$pathData</StreamGeometry>")
}

if ($geometries.ContainsKey("sliders-horizontal") -and -not $geometries.ContainsKey("sliders")) {
    $xamlLines.Add("    <StreamGeometry x:Key=""Icon.sliders"">$($geometries['sliders-horizontal'])</StreamGeometry>")
}

$xamlLines.Add('</ResourceDictionary>')

[System.IO.File]::WriteAllLines($OutputFile, $xamlLines, [System.Text.Encoding]::UTF8)
Write-Host "Successfully generated $OutputFile with $($svgFiles.Count) icons."
