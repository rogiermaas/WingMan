$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $PSScriptRoot '..\assets\branding'
$master = [System.Drawing.Image]::FromFile((Join-Path $assetRoot 'wingman-icon-master.png'))
$sizes = @(16,20,24,32,40,48,64,128,256)
$images = New-Object 'System.Collections.Generic.List[byte[]]'
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($master,0,0,$size,$size)
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
        $images.Add($stream.ToArray())
        [System.IO.File]::WriteAllBytes((Join-Path $assetRoot "wingman-$size.png"),$stream.ToArray())
        $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
    $file = [System.IO.File]::Create((Join-Path $assetRoot 'WingMan.ico'))
    $writer = New-Object System.IO.BinaryWriter($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i=0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) {0} else {$sizes[$i]}
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
    } finally { $writer.Dispose(); $file.Dispose() }
} finally { $master.Dispose() }
