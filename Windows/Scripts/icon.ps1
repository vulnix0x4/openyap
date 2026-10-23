# Draws the OpenYap waveform icon (mint on a dark rounded square) into a multi-size .ico with PNG frames.
param([Parameter(Mandatory = $true)][string]$Output)
Add-Type -AssemblyName System.Drawing
$sizes = 256, 64, 48, 32, 16
$frames = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = $size * 0.22
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $shape.AddArc($size - 2 * $r - 1, 0, 2 * $r, 2 * $r, 270, 90)
    $shape.AddArc($size - 2 * $r - 1, $size - 2 * $r - 1, 2 * $r, 2 * $r, 0, 90)
    $shape.AddArc(0, $size - 2 * $r - 1, 2 * $r, 2 * $r, 90, 90)
    $shape.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(27, 30, 34))), $shape)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(61, 220, 151)), ([Math]::Max(1.5, $size * 0.075))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $bars = 0.22, 0.5, 0.78, 0.42, 0.62, 0.3
    for ($i = 0; $i -lt $bars.Count; $i++) {
        $x = $size * (0.2 + $i * 0.12)
        $h = $size * 0.62 * $bars[$i]
        $g.DrawLine($pen, [single]$x, [single]($size / 2 - $h / 2), [single]$x, [single]($size / 2 + $h / 2))
    }
    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $w.Write($frame) }
[System.IO.File]::WriteAllBytes($Output, $out.ToArray())

