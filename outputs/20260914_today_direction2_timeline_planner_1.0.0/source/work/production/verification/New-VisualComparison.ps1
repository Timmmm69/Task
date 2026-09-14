[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ReferencePath,
    [Parameter(Mandatory)] [string]$ImplementationPath,
    [Parameter(Mandatory)] [string]$OutputPath,
    [int]$CropHeight = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$reference = [Drawing.Bitmap]::FromFile([IO.Path]::GetFullPath($ReferencePath))
$implementation = [Drawing.Bitmap]::FromFile([IO.Path]::GetFullPath($ImplementationPath))
try {
    $height = if ($CropHeight -gt 0) {
        [Math]::Min($CropHeight, [Math]::Min($reference.Height, $implementation.Height))
    } else {
        [Math]::Max($reference.Height, $implementation.Height)
    }
    $labelHeight = 42
    $canvas = [Drawing.Bitmap]::new($reference.Width + $implementation.Width, $height + $labelHeight)
    try {
        $graphics = [Drawing.Graphics]::FromImage($canvas)
        try {
            $graphics.Clear([Drawing.Color]::White)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $font = [Drawing.Font]::new('Segoe UI', 15, [Drawing.FontStyle]::Bold)
            $brush = [Drawing.Brushes]::Black
            try {
                $graphics.DrawString('REFERENCE', $font, $brush, 14, 8)
                $graphics.DrawString('IMPLEMENTATION', $font, $brush, $reference.Width + 14, 8)
                $referenceSource = [Drawing.Rectangle]::new(0, 0, $reference.Width, [Math]::Min($height, $reference.Height))
                $implementationSource = [Drawing.Rectangle]::new(0, 0, $implementation.Width, [Math]::Min($height, $implementation.Height))
                $graphics.DrawImage($reference, [Drawing.Rectangle]::new(0, $labelHeight, $reference.Width, $referenceSource.Height), $referenceSource, [Drawing.GraphicsUnit]::Pixel)
                $graphics.DrawImage($implementation, [Drawing.Rectangle]::new($reference.Width, $labelHeight, $implementation.Width, $implementationSource.Height), $implementationSource, [Drawing.GraphicsUnit]::Pixel)
            }
            finally { $font.Dispose() }
        }
        finally { $graphics.Dispose() }

        $output = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
        $canvas.Save($output, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $canvas.Dispose() }
}
finally {
    $reference.Dispose()
    $implementation.Dispose()
}
