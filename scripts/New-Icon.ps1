$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconRoot = Split-Path $PSScriptRoot -Parent
$iconDestination = Join-Path $iconRoot 'assets/PadDim.ico'
$iconFrames = [System.Collections.Generic.List[byte[]]]::new()
$iconSizes = @(16, 20, 24, 32, 48, 64, 128, 256)
function Fill-Rounded($graphics, $brush, [single]$x, [single]$y, [single]$w, [single]$h, [single]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc(($x+$w-$d), $y, $d, $d, 270, 90)
    $path.AddArc(($x+$w-$d), ($y+$h-$d), $d, $d, 0, 90)
    $path.AddArc($x, ($y+$h-$d), $d, $d, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($brush, $path)
    $path.Dispose()
}
foreach ($size in $iconSizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.ScaleTransform(($size / 256.0), ($size / 256.0))
    $body = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#192c49'))
    $screen = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#274568'))
    $moon = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#ffd88c'))
    Fill-Rounded $graphics $body 12 30 232 158 26
    Fill-Rounded $graphics $screen 28 46 200 120 14
    $moonPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $moonPath.AddEllipse(104, 62, 90, 90)
    $moonRegion = [System.Drawing.Region]::new($moonPath)
    $cutoutPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $cutoutPath.AddEllipse(143, 43, 90, 90)
    $moonRegion.Exclude($cutoutPath)
    $graphics.FillRegion($moon, $moonRegion)
    $moonRegion.Dispose(); $moonPath.Dispose(); $cutoutPath.Dispose()
    Fill-Rounded $graphics $body 116 184 24 28 4
    Fill-Rounded $graphics $body 76 208 104 16 8
    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $iconFrames.Add($memory.ToArray())
    if ($size -eq 256) { $bitmap.Save((Join-Path $iconRoot 'assets/PadDim.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $body.Dispose(); $screen.Dispose(); $moon.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$stream = [System.IO.File]::Create($iconDestination)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconSizes.Count)
    $offset = 6 + 16 * $iconSizes.Count
    for ($i = 0; $i -lt $iconSizes.Count; $i++) {
        $dimension = if ($iconSizes[$i] -eq 256) { 0 } else { $iconSizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$iconFrames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $iconFrames[$i].Length
    }
    foreach ($frame in $iconFrames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Output $iconDestination
