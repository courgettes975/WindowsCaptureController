<#
.SYNOPSIS
  Generates src\WindowsCaptureController.Plugin\package\metadata\Icon256x256.png (plugin icon in Logi Options+).
  Generic pictogram: white scissors over a dashed selection on blue (no Microsoft artwork).
#>
param([string]$Output = (Join-Path $PSScriptRoot '..\src\WindowsCaptureController.Plugin\package\metadata\Icon256x256.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(0, 103, 192))

$dash = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), 8
$dash.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash
$g.DrawRectangle($dash, 28, 40, 200, 150)

$white = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 16
$white.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$white.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($white, 96, 170, 168, 62)
$g.DrawLine($white, 160, 170, 88, 62)
$ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 14
$g.DrawEllipse($ring, 62, 168, 50, 50)
$g.DrawEllipse($ring, 144, 168, 50, 50)

New-Item -ItemType Directory -Force (Split-Path $Output -Parent) | Out-Null
$bmp.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "Icon written: $Output"
