# Draws the AGEX icon (three connected agent nodes on a rounded square) and
# writes agex.png (1024), agex.ico (Windows) and agex.icns (macOS).
# Pure geometry, no fonts: the result has no third-party licence.
# Usage: powershell -NoProfile -File tools/make-icons.ps1   (Windows, uses System.Drawing)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\Agex.Desktop\Assets'
New-Item -ItemType Directory -Path $out -Force | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 1024.0
    $radius = 220 * $s
    $rect = New-Object System.Drawing.RectangleF (40 * $s), (40 * $s), (944 * $s), (944 * $s)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x3D, 0x63, 0xDD))), $path)
    $white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    $soft = [System.Drawing.Color]::FromArgb(200, 255, 255, 255)
    $pen = New-Object System.Drawing.Pen $soft, ([float](46 * $s))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $top = New-Object System.Drawing.PointF (512 * $s), (300 * $s)
    $left = New-Object System.Drawing.PointF (300 * $s), (690 * $s)
    $right = New-Object System.Drawing.PointF (724 * $s), (690 * $s)
    $g.DrawLine($pen, $top, $left); $g.DrawLine($pen, $top, $right); $g.DrawLine($pen, $left, $right)
    $brush = New-Object System.Drawing.SolidBrush $white
    foreach ($p in @($top, $left, $right)) { $r = 96 * $s; $g.FillEllipse($brush, $p.X - $r, $p.Y - $r, 2 * $r, 2 * $r) }
    $g.Dispose()
    $bmp
}

function Get-PngBytes([int]$size) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

[IO.File]::WriteAllBytes((Join-Path $out 'agex.png'), (Get-PngBytes 1024))
[IO.File]::WriteAllBytes((Join-Path $out 'agex-256.png'), (Get-PngBytes 256))

# ICO with PNG-compressed images.
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) { , (Get-PngBytes $size) }
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[IO.File]::WriteAllBytes((Join-Path $out 'agex.ico'), $ms.ToArray())

# ICNS with PNG entries (ic07 128, ic08 256, ic09 512, ic10 1024).
function BE([uint32]$v) { $b = [BitConverter]::GetBytes($v); [Array]::Reverse($b); , $b }
$entries = @(@('ic07', 128), @('ic08', 256), @('ic09', 512), @('ic10', 1024))
$body = New-Object System.IO.MemoryStream
foreach ($e in $entries) {
    $png = Get-PngBytes $e[1]
    $body.Write([Text.Encoding]::ASCII.GetBytes($e[0]), 0, 4)
    $len = BE ([uint32]($png.Length + 8)); $body.Write($len, 0, 4)
    $body.Write($png, 0, $png.Length)
}
$icns = New-Object System.IO.MemoryStream
$icns.Write([Text.Encoding]::ASCII.GetBytes('icns'), 0, 4)
$total = BE ([uint32]($body.Length + 8)); $icns.Write($total, 0, 4)
$bodyBytes = $body.ToArray(); $icns.Write($bodyBytes, 0, $bodyBytes.Length)
[IO.File]::WriteAllBytes((Join-Path $out 'agex.icns'), $icns.ToArray())
Get-ChildItem $out | Select-Object Name, Length
