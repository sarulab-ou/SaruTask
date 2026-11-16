$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('SaruTask.IconNative' -as [type])) {
    Add-Type 'namespace SaruTask { public static class IconNative { [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool DestroyIcon(System.IntPtr icon); } }'
}
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot '..\obj') -Force | Out-Null
$bitmap = New-Object System.Drawing.Bitmap 48,48
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(33,99,195))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),4
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.FillEllipse($brush,2,2,44,44)
    $graphics.DrawEllipse($pen,12,10,18,18)
    $graphics.DrawLine($pen,28,28,37,37)
    $handle = $bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($handle)
        $stream = [System.IO.File]::Create((Join-Path $PSScriptRoot '..\obj\SaruTask.ico'))
        try { $icon.Save($stream) } finally { $stream.Dispose() }
    } finally { [SaruTask.IconNative]::DestroyIcon($handle) | Out-Null }
} finally { $pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
