# Draws Uproot\app.ico: a sprout with its roots out of the ground, in Uproot's green on the studio's dark tile.
# Same frames and container as SpaceScan's make-icon.ps1.
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'

    $r = [Math]::Max(2, $s * 0.22)
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($s - $r * 2 - 1, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($s - $r * 2 - 1, $s - $r * 2 - 1, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $s - $r * 2 - 1, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $tile = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 0), (New-Object Drawing.Point $s, $s), ([Drawing.Color]::FromArgb(18, 33, 27)), ([Drawing.Color]::FromArgb(9, 14, 12))
    $g.FillPath($tile, $path)
    if ($s -ge 32) { $g.DrawPath((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(70, 255, 255, 255)), ($s / 128)), $path) }

    # the sprout: stem and two leaves above the soil line
    $green = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.PointF 0, ($s * 0.12)), (New-Object Drawing.PointF 0, ($s * 0.64)), ([Drawing.Color]::FromArgb(110, 231, 183)), ([Drawing.Color]::FromArgb(16, 185, 129))
    $cx = $s * 0.5
    $stem = New-Object Drawing.Pen $green, ([Math]::Max(1.4, $s * 0.075))
    $stem.StartCap = 'Round'; $stem.EndCap = 'Round'
    $g.DrawLine($stem, $cx, ($s * 0.60), $cx, ($s * 0.32))
    foreach ($side in -1, 1) {
        $leaf = New-Object Drawing.Drawing2D.GraphicsPath
        $leaf.AddEllipse((-$s * 0.16), (-$s * 0.07), ($s * 0.32), ($s * 0.14))
        $m = New-Object Drawing.Drawing2D.Matrix
        $m.Translate(($cx + $side * $s * 0.14), ($s * 0.29))
        $m.Rotate(-32 * $side)
        $leaf.Transform($m)
        $g.FillPath($green, $leaf)
    }
    # the soil line
    $soil = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(150, 255, 255, 255)), ([Math]::Max(1, $s * 0.035))
    $soil.StartCap = 'Round'; $soil.EndCap = 'Round'
    $g.DrawLine($soil, ($s * 0.2), ($s * 0.63), ($s * 0.8), ($s * 0.63))
    # the roots, out in the open
    if ($s -ge 20) {
        $root = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(210, 52, 211, 153)), ([Math]::Max(1, $s * 0.045))
        $root.StartCap = 'Round'; $root.EndCap = 'Round'
        $g.DrawLine($root, $cx, ($s * 0.68), $cx, ($s * 0.84))
        $g.DrawBezier($root, $cx, ($s * 0.69), ($cx - $s * 0.05), ($s * 0.75), ($cx - $s * 0.16), ($s * 0.73), ($cx - $s * 0.21), ($s * 0.81))
        $g.DrawBezier($root, $cx, ($s * 0.69), ($cx + $s * 0.05), ($s * 0.75), ($cx + $s * 0.16), ($s * 0.73), ($cx + $s * 0.21), ($s * 0.81))
    }
    $g.Dispose()
    return $bmp
}

# Frames: classic 32-bit DIB up to 64px (what older shells read), PNG for the big ones.
function New-Frame([Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    if ($s -gt 64) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); return , [byte[]]$ms.ToArray() }
    $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32]($s * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    0, 0, 0, 0, 0, 0 | ForEach-Object { $w.Write([uint32]$_) }
    for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
    $stride = [Math]::Ceiling($s / 32) * 4              # AND mask: fully transparent, rows padded to 4 bytes
    for ($y = 0; $y -lt $s; $y++) { $w.Write((New-Object byte[] $stride)) }
    $w.Flush(); return , [byte[]]$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$frames = New-Object "System.Collections.Generic.List[byte[]]"
foreach ($s in $sizes) { $frames.Add([byte[]](New-Frame (New-IconBitmap $s))) }

# ICO container: 6-byte header, 16 bytes per entry, then the frames.
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))   # 256 is stored as 0
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
$target = Join-Path $PSScriptRoot 'Uproot\app.ico'
[IO.File]::WriteAllBytes($target, $out.ToArray())
# a 256 px preview beside it, to look at
(New-IconBitmap 256).Save((Join-Path $PSScriptRoot 'icon-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
"app.ico: $($sizes.Count) sizes, $((Get-Item $target).Length) bytes"
