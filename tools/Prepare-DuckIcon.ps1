param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::FromFile($SourcePath)
$resized = New-Object Drawing.Bitmap 64, 64, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    $graphics = [Drawing.Graphics]::FromImage($resized)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($source, 0, 0, 64, 64)
    }
    finally { $graphics.Dispose() }

    for ($y = 0; $y -lt 64; $y++) {
        for ($x = 0; $x -lt 64; $x++) {
            $pixel = $resized.GetPixel($x, $y)
            if ($pixel.A -ge 96) {
                $resized.SetPixel($x, $y, [Drawing.Color]::FromArgb(255, 0, 0, 0))
            }
            else {
                $resized.SetPixel($x, $y, [Drawing.Color]::Transparent)
            }
        }
    }
    $directory = Split-Path -Parent $OutputPath
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $resized.Save($OutputPath, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $resized.Dispose()
    $source.Dispose()
}

Write-Output 'Prepared original 64x64 one-bit duck icon.'
