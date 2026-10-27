<#
    make-icon.ps1 - draws AcerFALDGui.ico for AcerFALDv2.

    The artwork is original: a dark tile holding a monitor whose panel is lit only
    in the middle, which is the local-dimming signature this app is about. It is
    drawn here from code rather than traced from any vendor logo, so the icon is
    reproducible and carries no third-party artwork.

    Outputs, next to this script:
      AcerFALDGui.ico        multi-size icon (16-256) for the exe and the setup
      AcerFALDGui-256.png    preview used in the docs

    Usage:  powershell -ExecutionPolicy Bypass -File .\installer\make-icon.ps1
#>

[CmdletBinding()]
param(
    [string] $OutputDirectory = '',
    [string] $BaseName = 'AcerFALDGui'
)

$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) {
    $OutputDirectory = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
}
Add-Type -AssemblyName System.Drawing

# Palette: the app's own dark theme, with Acer's brand green as the accent.
$TileTop     = [Drawing.Color]::FromArgb(255, 22, 35, 58)
$TileBottom  = [Drawing.Color]::FromArgb(255, 7, 12, 22)
$TileBorder  = [Drawing.Color]::FromArgb(255, 36, 57, 92)
$StandColor  = [Drawing.Color]::FromArgb(255, 62, 86, 122)
$ZoneLine    = [Drawing.Color]::FromArgb(34, 0, 0, 0)
$GlowCenter  = [Drawing.Color]::FromArgb(255, 198, 240, 74)
$GlowMiddle  = [Drawing.Color]::FromArgb(255, 131, 184, 26)
$GlowEdge    = [Drawing.Color]::FromArgb(0, 131, 184, 26)

function New-RoundedPath {
    param([single] $X, [single] $Y, [single] $W, [single] $H, [single] $R)
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-LightPool {
    param(
        [Drawing.Graphics] $Graphics,
        [Drawing.RectangleF] $Rect,
        [Drawing.Color] $Center,
        [Drawing.Color] $Edge
    )
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($Rect)
    $brush = New-Object Drawing.Drawing2D.PathGradientBrush($path)
    $brush.CenterColor = $Center
    $brush.SurroundColors = @($Edge)
    $Graphics.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()
}

function Draw-Icon {
    param([int] $Size)

    # Draw big and downsample: cheap, reliable antialiasing at every size.
    $scale = if ($Size -le 32) { 16 } elseif ($Size -le 64) { 8 } else { 4 }
    $S = $Size * $scale
    $large = New-Object Drawing.Bitmap($S, $S, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($large)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([Drawing.Color]::Transparent)

    # Detail budget: thin zone lines and the outer glow only survive at larger sizes.
    $fine = $Size -ge 40
    $glow = $Size -ge 24

    # --- tile -------------------------------------------------------------
    $pad = [single]($S * 0.02)
    $tileRect = New-Object Drawing.RectangleF($pad, $pad, ($S - 2 * $pad), ($S - 2 * $pad))
    $radius = [single]($S * 0.215)
    $tilePath = New-RoundedPath $tileRect.X $tileRect.Y $tileRect.Width $tileRect.Height $radius

    $tileBrush = New-Object Drawing.Drawing2D.LinearGradientBrush(
        (New-Object Drawing.PointF(0, 0)),
        (New-Object Drawing.PointF(0, $S)),
        $TileTop, $TileBottom)
    $g.FillPath($tileBrush, $tilePath)

    $borderPen = New-Object Drawing.Pen($TileBorder, [single]([Math]::Max(1, $S * 0.012)))
    $g.DrawPath($borderPen, $tilePath)

    # --- monitor ----------------------------------------------------------
    # Screen sits above centre; the stand is a short neck and a foot.
    $screenW = [single]($S * 0.66)
    $screenH = [single]($S * 0.42)
    $screenX = [single](($S - $screenW) / 2)
    $screenY = [single]($S * 0.235)
    $screenR = [single]($S * 0.035)
    $screenRect = New-Object Drawing.RectangleF($screenX, $screenY, $screenW, $screenH)
    $screenPath = New-RoundedPath $screenX $screenY $screenW $screenH $screenR

    # Outer glow: the light a lit panel throws past its own bezel.
    if ($glow) {
        $spread = [single]($S * 0.10)
        $glowRect = New-Object Drawing.RectangleF(
            ($screenX - $spread), ($screenY - $spread),
            ($screenW + 2 * $spread), ($screenH + 2 * $spread))
        $glowPath = New-RoundedPath $glowRect.X $glowRect.Y $glowRect.Width $glowRect.Height ($screenR + $spread * 0.5)
        $glowBrush = New-Object Drawing.Drawing2D.PathGradientBrush($glowPath)
        $glowBrush.CenterColor = [Drawing.Color]::FromArgb(64, 131, 184, 26)
        $glowBrush.SurroundColors = @([Drawing.Color]::FromArgb(0, 131, 184, 26))
        $g.FillPath($glowBrush, $glowPath)
        $glowBrush.Dispose()
        $glowPath.Dispose()
    }

    # Bezel.
    $bezelBrush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 8, 14, 24))
    $g.FillPath($bezelBrush, $screenPath)
    $bezelPen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(255, 74, 101, 140), [single]([Math]::Max(1, $S * 0.014)))
    $g.DrawPath($bezelPen, $screenPath)

    # Panel: dark at the edges, lit in the middle - the FALD look.
    $inset = [single]($S * 0.022)
    $panelRect = New-Object Drawing.RectangleF(
        ($screenX + $inset), ($screenY + $inset),
        ($screenW - 2 * $inset), ($screenH - 2 * $inset))
    $panelPath = New-RoundedPath $panelRect.X $panelRect.Y $panelRect.Width $panelRect.Height ($screenR * 0.6)

    $g.SetClip($panelPath)
    $g.FillRectangle((New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 3, 8, 14))), $panelRect)

    # Radial light pools, centred slightly high like a highlight on screen. Layered
    # instead of one flat gradient so the lit area has a hot core and a soft bloom.
    $cx = $panelRect.X + $panelRect.Width / 2
    $cy = $panelRect.Y + $panelRect.Height * 0.47
    $rx = $panelRect.Width * 0.47
    $ry = $panelRect.Height * 0.58

    Add-LightPool $g (New-Object Drawing.RectangleF(
            ($cx - $rx * 1.30), ($cy - $ry * 1.30), ($rx * 2.60), ($ry * 2.60))) `
        ([Drawing.Color]::FromArgb(150, 131, 184, 26)) ([Drawing.Color]::FromArgb(0, 131, 184, 26))
    Add-LightPool $g (New-Object Drawing.RectangleF(
            ($cx - $rx * 0.86), ($cy - $ry * 0.86), ($rx * 1.72), ($ry * 1.72))) `
        ([Drawing.Color]::FromArgb(235, 198, 240, 74)) ([Drawing.Color]::FromArgb(0, 150, 205, 40))
    Add-LightPool $g (New-Object Drawing.RectangleF(
            ($cx - $rx * 0.40), ($cy - $ry * 0.40), ($rx * 0.80), ($ry * 0.80))) `
        ([Drawing.Color]::FromArgb(255, 246, 255, 206)) ([Drawing.Color]::FromArgb(0, 214, 246, 96))

    # Dimming-zone grid: vertical bands read as backlight zones, not as a picture.
    if ($fine) {
        $zonePen = New-Object Drawing.Pen($ZoneLine, [single]([Math]::Max(1, $S * 0.007)))
        $columns = 8
        for ($i = 1; $i -lt $columns; $i++) {
            $x = $panelRect.X + $panelRect.Width * $i / $columns
            $g.DrawLine($zonePen, $x, $panelRect.Y, $x, $panelRect.Bottom)
        }
        $rows = 4
        for ($i = 1; $i -lt $rows; $i++) {
            $y = $panelRect.Y + $panelRect.Height * $i / $rows
            $g.DrawLine($zonePen, $panelRect.X, $y, $panelRect.Right, $y)
        }
        $zonePen.Dispose()
    }

    $g.ResetClip()

    # Neck and foot.
    $standBrush = New-Object Drawing.SolidBrush($StandColor)
    $neckW = [single]($S * 0.075)
    $neckTop = [single]($screenY + $screenH)
    $neckH = [single]($S * 0.085)
    $g.FillRectangle($standBrush, [single](($S - $neckW) / 2), $neckTop, $neckW, $neckH)
    $footW = [single]($S * 0.30)
    $footH = [single]($S * 0.045)
    $footPath = New-RoundedPath ([single](($S - $footW) / 2)) ([single]($neckTop + $neckH - $footH * 0.4)) $footW $footH ([single]($footH / 2))
    $g.FillPath($standBrush, $footPath)

    $footPath.Dispose()
    $standBrush.Dispose()
    $neckW = $null
    $bezelPen.Dispose()
    $bezelBrush.Dispose()
    $panelPath.Dispose()
    $screenPath.Dispose()
    $borderPen.Dispose()
    $tileBrush.Dispose()
    $tilePath.Dispose()

    # Downsample.
    $result = New-Object Drawing.Bitmap($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rg = [Drawing.Graphics]::FromImage($result)
    $rg.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $rg.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $rg.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
    $rg.Clear([Drawing.Color]::Transparent)
    $rg.DrawImage($large, (New-Object Drawing.Rectangle(0, 0, $Size, $Size)))
    $rg.Dispose()
    $g.Dispose()
    $large.Dispose()
    return $result
}

function Get-PngBytes {
    param([Drawing.Bitmap] $Bitmap)
    $stream = New-Object IO.MemoryStream
    $Bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

# A 32-bit DIB icon entry: BITMAPINFOHEADER, bottom-up BGRA rows, then the AND mask.
function Get-DibBytes {
    param([Drawing.Bitmap] $Bitmap)
    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)

    $writer.Write([int]40)          # biSize
    $writer.Write([int]$w)          # biWidth
    $writer.Write([int]($h * 2))    # biHeight: XOR image + AND mask
    $writer.Write([int16]1)         # biPlanes
    $writer.Write([int16]32)        # biBitCount
    $writer.Write([int]0)           # biCompression = BI_RGB
    $writer.Write([int]($w * $h * 4))
    $writer.Write([int]0)           # biXPelsPerMeter
    $writer.Write([int]0)           # biYPelsPerMeter
    $writer.Write([int]0)           # biClrUsed
    $writer.Write([int]0)           # biClrImportant

    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $Bitmap.GetPixel($x, $y)
            $writer.Write([byte]$c.B)
            $writer.Write([byte]$c.G)
            $writer.Write([byte]$c.R)
            $writer.Write([byte]$c.A)
        }
    }

    # AND mask: all zero (fully opaque); rows padded to 4 bytes.
    $maskRow = [int]([Math]::Ceiling($w / 32.0) * 4)
    $mask = New-Object 'byte[]' ($maskRow * $h)
    $writer.Write($mask)

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()
    return ,$bytes
}

function Write-Ico {
    param(
        [string] $Path,
        [Drawing.Bitmap[]] $Bitmaps
    )

    $entries = @()
    foreach ($bmp in $Bitmaps) {
        # 32-bit DIB entries for every size: universally understood by the shell,
        # Inno Setup, .NET's Icon class and the Win32 resource compiler. PNG entries
        # would be smaller for 128/256 but several of those readers refuse them.
        $data = Get-DibBytes $bmp
        $entries += [pscustomobject]@{ Bitmap = $bmp; Data = $data }
    }

    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter($stream)
    $writer.Write([int16]0)                 # reserved
    $writer.Write([int16]1)                 # type: icon
    $writer.Write([int16]$entries.Count)

    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $dim = if ($e.Bitmap.Width -ge 256) { 0 } else { $e.Bitmap.Width }
        $writer.Write([byte]$dim)
        $writer.Write([byte]$dim)
        $writer.Write([byte]0)              # colour count
        $writer.Write([byte]0)              # reserved
        $writer.Write([int16]1)             # planes
        $writer.Write([int16]32)            # bits per pixel
        $writer.Write([int]$e.Data.Length)
        $writer.Write([int]$offset)
        $offset += $e.Data.Length
    }
    foreach ($e in $entries) { $writer.Write([byte[]]$e.Data) }

    $writer.Flush()
    [IO.File]::WriteAllBytes($Path, $stream.ToArray())
    $writer.Dispose()
    $stream.Dispose()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$bitmaps = @()
foreach ($s in $sizes) { $bitmaps += Draw-Icon $s }

$icoPath = Join-Path $OutputDirectory "$BaseName.ico"
Write-Ico -Path $icoPath -Bitmaps $bitmaps

# Preview: 256 px on its own, for the README and the release notes.
$preview = $bitmaps[$bitmaps.Count - 1]
$previewPath = Join-Path $OutputDirectory "$BaseName-256.png"
$preview.Save($previewPath, [Drawing.Imaging.ImageFormat]::Png)

foreach ($b in $bitmaps) { $b.Dispose() }

"icon    : $icoPath ($((Get-Item $icoPath).Length) bytes)"
"preview : $previewPath"
