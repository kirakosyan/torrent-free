param(
    [string]$RawDir,
    [string]$OutDir,
    [string]$CaptionsJson,
    [string[]]$Locales,
    [string[]]$Shots,          # raw file names (without .png)
    [int[]]$CaptionIndex       # caption index for each shot
)
Add-Type -AssemblyName System.Drawing

$W = 1440; $H = 2560
$captions = Get-Content $CaptionsJson -Raw -Encoding UTF8 | ConvertFrom-Json

function Get-FontName([string]$locale) {
    switch -Regex ($locale) {
        '^ja' { 'Yu Gothic UI' }
        '^ko' { 'Malgun Gothic' }
        '^zh' { 'Microsoft YaHei UI' }
        '^th' { 'Leelawadee UI' }
        '^hi' { 'Nirmala UI' }
        default { 'Segoe UI' }
    }
}

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

foreach ($locale in $Locales) {
    $key = $locale
    $list = $captions.$key
    if (-not $list) { throw "No captions for $locale" }
    $dir = Join-Path $OutDir $locale
    New-Item -ItemType Directory -Force $dir | Out-Null
    for ($i = 0; $i -lt $Shots.Count; $i++) {
        $raw = [System.Drawing.Image]::FromFile((Join-Path $RawDir ($Shots[$i] + '.png')))
        # Raw captures from a 1440x3120 phone still include the status and navigation bars; drop them.
        $crop = if ($raw.Height -ge 3000) { New-Object System.Drawing.Rectangle 0, 132, $raw.Width, (2938 - 132) } else { New-Object System.Drawing.Rectangle 0, 0, $raw.Width, $raw.Height }
        $bmp = New-Object System.Drawing.Bitmap $W, $H
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.PixelOffsetMode = 'HighQuality'

        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $W, $H), ([System.Drawing.Color]::FromArgb(255, 14, 20, 51)), ([System.Drawing.Color]::FromArgb(255, 42, 44, 120))
        $g.FillRectangle($bg, 0, 0, $W, $H)
        $glow = New-Object System.Drawing.Drawing2D.GraphicsPath
        $glow.AddEllipse(-300, 1400, 2040, 1600)
        $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
        $pgb.CenterColor = [System.Drawing.Color]::FromArgb(90, 91, 91, 214)
        $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 91, 91, 214))
        $g.FillPath($pgb, $glow)

        # Caption
        $text = $list[$CaptionIndex[$i]]
        $fontName = Get-FontName $locale
        $size = 86
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
        if ($locale -match '^(ar|fa)') { $fmt.FormatFlags = [System.Drawing.StringFormatFlags]::DirectionRightToLeft }
        $area = New-Object System.Drawing.RectangleF 100, 90, ($W - 200), 380
        do {
            $font = New-Object System.Drawing.Font $fontName, $size, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
            $measured = $g.MeasureString($text, $font, [int]$area.Width, $fmt)
            if ($measured.Height -le $area.Height) { break }
            $font.Dispose(); $size -= 4
        } while ($size -gt 40)
        $g.DrawString($text, $font, [System.Drawing.Brushes]::White, $area, $fmt)
        $font.Dispose()

        # Device
        $top = 520
        $dh = $H - $top - 90
        $dw = [int]($dh * $crop.Width / $crop.Height)
        $dx = [int](($W - $dw) / 2)
        $shadow = New-RoundedPath ($dx - 6) ($top + 18) ($dw + 12) ($dh + 12) 70
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 0, 0, 0))), $shadow)
        $bezel = New-RoundedPath ($dx - 22) ($top - 22) ($dw + 44) ($dh + 44) 78
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 8, 10, 22))), $bezel)
        $screen = New-RoundedPath $dx $top $dw $dh 56
        $g.SetClip($screen)
        $g.DrawImage($raw, (New-Object System.Drawing.Rectangle $dx, $top, $dw, $dh), $crop, [System.Drawing.GraphicsUnit]::Pixel)
        $g.ResetClip()

        $out = Join-Path $dir ('{0:D2}.png' -f ($i + 1))
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose(); $raw.Dispose()
    }
}
"framed $($Locales.Count) locale(s)"
