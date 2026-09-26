param([string]$Icon, [string]$Shot, [string]$Out)
Add-Type -AssemblyName System.Drawing
$W = 1024; $H = 500
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.PixelOffsetMode = 'HighQuality'

$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $W, $H), ([System.Drawing.Color]::FromArgb(255, 14, 20, 51)), ([System.Drawing.Color]::FromArgb(255, 42, 44, 120))
$g.FillRectangle($bg, 0, 0, $W, $H)
$glow = New-Object System.Drawing.Drawing2D.GraphicsPath
$glow.AddEllipse(520, -120, 700, 700)
$pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
$pgb.CenterColor = [System.Drawing.Color]::FromArgb(110, 91, 91, 214)
$pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 91, 91, 214))
$g.FillPath($pgb, $glow)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

# Phone on the right, cropped by the bottom edge.
$raw = [System.Drawing.Image]::FromFile($Shot)
$top = if ($raw.Height -ge 3000) { 132 } else { 0 }
$crop = New-Object System.Drawing.Rectangle 0, $top, $raw.Width, 2100
$dw = 300; $dh = [int]($dw * $crop.Height / $crop.Width)
$dx = 660; $dy = 70
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 8, 10, 22))), (New-RoundedPath ($dx - 10) ($dy - 10) ($dw + 20) ($dh + 20) 38))
$screen = New-RoundedPath $dx $dy $dw $dh 30
$g.SetClip($screen)
$g.DrawImage($raw, (New-Object System.Drawing.Rectangle $dx, $dy, $dw, $dh), $crop, [System.Drawing.GraphicsUnit]::Pixel)
$g.ResetClip()
$raw.Dispose()

# Icon, name and promise on the left.
$iconImg = [System.Drawing.Image]::FromFile($Icon)
$g.SetClip((New-RoundedPath 64 118 120 120 28)); $g.DrawImage($iconImg, 64, 118, 120, 120); $g.ResetClip()
$iconImg.Dispose()
$title = New-Object System.Drawing.Font 'Segoe UI Semibold', 60, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString('Torrent Client', $title, [System.Drawing.Brushes]::White, 60, 250)
$tag = New-Object System.Drawing.Font 'Segoe UI', 28, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString('No ads. No account. Open source.', $tag, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 201, 201, 255))), 64, 338)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
$Out
