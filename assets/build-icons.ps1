$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = $PSScriptRoot
$webDirectory = Join-Path (Split-Path -Parent $assetDirectory) 'web\dist'
$svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><rect width="32" height="32" rx="4" fill="#008080"/><rect x="7" y="7" width="18" height="18" rx="1" fill="none" stroke="#fff" stroke-width="2.4"/><path d="m11 16 3.5 3.5 7-7" fill="none" stroke="#fff" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"/></svg>'
[System.IO.File]::WriteAllText((Join-Path $assetDirectory 'chooser.svg'),$svg,(New-Object System.Text.UTF8Encoding $false))
$frames = @()
foreach ($iconSize in @(16,32,48,64,192,256,512)) {
    $bitmap = New-Object System.Drawing.Bitmap $iconSize,$iconSize
    $drawing = [System.Drawing.Graphics]::FromImage($bitmap)
    $drawing.SmoothingMode = 'AntiAlias'
    $drawing.Clear([System.Drawing.Color]::Transparent)
    $scale = [single]($iconSize / 32.0)
    $drawing.ScaleTransform($scale,$scale)
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0,128,128))
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(0,0,8,8,180,90); $shape.AddArc(24,0,8,8,270,90); $shape.AddArc(24,24,8,8,0,90); $shape.AddArc(0,24,8,8,90,90); $shape.CloseFigure()
    $drawing.FillPath($fill,$shape)
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),2.4
    $outline.LineJoin = 'Round'
    $drawing.DrawRectangle($outline,7,7,18,18)
    $check = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),2.8
    $check.StartCap = 'Round'; $check.EndCap = 'Round'; $check.LineJoin = 'Round'
    $drawing.DrawLine($check,[single]11,[single]16,[single]14.5,[single]19.5)
    $drawing.DrawLine($check,[single]14.5,[single]19.5,[single]21.5,[single]12.5)
    $pngPath = Join-Path $assetDirectory ('chooser-' + $iconSize + '.png')
    $bitmap.Save($pngPath,[System.Drawing.Imaging.ImageFormat]::Png)
    if ($iconSize -in @(16,32,48,64,256)) { $frames += ,([System.IO.File]::ReadAllBytes($pngPath)) }
    $check.Dispose(); $outline.Dispose(); $shape.Dispose(); $fill.Dispose(); $drawing.Dispose(); $bitmap.Dispose()
}
$ico = [System.IO.File]::Create((Join-Path $assetDirectory 'chooser.ico'))
$writer = New-Object System.IO.BinaryWriter $ico
try {
    $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]5)
    $offset = [UInt32](6 + 16 * 5)
    $sizes = @(16,32,48,64,256)
    for ($index=0; $index -lt 5; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$frames[$index].Length); $writer.Write($offset)
        $offset += [UInt32]$frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
Copy-Item -LiteralPath (Join-Path $assetDirectory 'chooser.svg') -Destination (Join-Path $webDirectory 'icon.svg')
Copy-Item -LiteralPath (Join-Path $assetDirectory 'chooser.ico') -Destination (Join-Path $webDirectory 'icon.ico')
foreach ($iconSize in @(192,512)) { Copy-Item -LiteralPath (Join-Path $assetDirectory ('chooser-' + $iconSize + '.png')) -Destination (Join-Path $webDirectory ('icon-' + $iconSize + '.png')) }
Get-Item -LiteralPath (Join-Path $assetDirectory 'chooser.ico'),(Join-Path $assetDirectory 'chooser-64.png') | Select-Object Name,Length
