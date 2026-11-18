# Generates assets\MacKeys.ico: a rounded square with the banner's gradient and a white ⌘ glyph.
# Multi-size (16-256 px, PNG-compressed entries) so it looks sharp in the Start menu, taskbar and Explorer.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'assets\MacKeys.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-IconBitmap([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([Drawing.Color]::Transparent)

    # rounded square (small sizes use less margin so the shape stays readable)
    $m = [Math]::Max(0, [Math]::Round($s * 0.04)); $w = $s - 2 * $m; $r = $w * 0.24; $d = 2 * $r
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, $d, $d, 180, 90); $path.AddArc($m + $w - $d, $m, $d, $d, 270, 90)
    $path.AddArc($m + $w - $d, $m + $w - $d, $d, $d, 0, 90); $path.AddArc($m, $m + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.PointF 0, 0), (New-Object Drawing.PointF $s, $s), [Drawing.Color]::White, [Drawing.Color]::White)
    $blend = New-Object Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([Drawing.Color]::FromArgb(56, 189, 248), [Drawing.Color]::FromArgb(99, 102, 241), [Drawing.Color]::FromArgb(217, 70, 239))
    $blend.Positions = @(0.0, 0.5, 1.0); $brush.InterpolationColors = $blend
    $g.FillPath($brush, $path)

    # soft top highlight for depth (skip on tiny sizes)
    if ($s -ge 32) {
        $hl = New-Object Drawing.Drawing2D.LinearGradientBrush ((New-Object Drawing.PointF 0, $m), (New-Object Drawing.PointF 0, ($m + $w * 0.55)), [Drawing.Color]::FromArgb(60, 255, 255, 255), [Drawing.Color]::FromArgb(0, 255, 255, 255))
        $g.SetClip($path); $g.FillRectangle($hl, $m, $m, $w, $w * 0.55); $g.ResetClip()
    }

    # white ⌘ centred by its actual ink bounds
    $font = New-Object Drawing.Font 'Segoe UI Symbol', ($s * 0.62), ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
    $gp = New-Object Drawing.Drawing2D.GraphicsPath
    $gp.AddString([string][char]0x2318, $font.FontFamily, [int][Drawing.FontStyle]::Regular, $font.Size, (New-Object Drawing.PointF 0, 0), [Drawing.StringFormat]::GenericTypographic)
    $b = $gp.GetBounds()
    $target = $s * 0.56
    $k = $target / [Math]::Max($b.Width, $b.Height)
    $mx = New-Object Drawing.Drawing2D.Matrix
    $mx.Translate(($s - $b.Width * $k) / 2, ($s - $b.Height * $k) / 2)
    $mx.Scale($k, $k); $mx.Translate(-$b.X, -$b.Y)
    $gp.Transform($mx)
    $g.FillPath([Drawing.Brushes]::White, $gp)
    $g.Dispose()
    return $bmp
}

# Classic 32-bit DIB entry (BITMAPINFOHEADER + bottom-up BGRA + empty AND mask): readable by every Windows API.
function ConvertTo-Dib($bmp) {
    $s = $bmp.Width
    $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    $rect = New-Object Drawing.Rectangle 0, 0, $s, $s
    $data = $bmp.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $row = New-Object byte[] ($s * 4)
    for ($y = $s - 1; $y -ge 0; $y--) {
        [Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $row, 0, $s * 4)
        $w.Write($row)
    }
    $bmp.UnlockBits($data)
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush(); return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    if ($s -ge 256) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bytes = $ms.ToArray() }
    else { $bytes = ConvertTo-Dib $bmp }
    $bmp.Dispose()
    , $bytes
}

# ICO container: header + directory + payloads (DIB for small sizes, PNG for 256)
$fs = [IO.File]::Create($out); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "Wrote $out"
