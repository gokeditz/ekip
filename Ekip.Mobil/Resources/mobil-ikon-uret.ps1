# Mobil simge ve açılış ekranı görselleri (masaüstü logosuyla aynı çizim: mor zeminde beyaz ev, gövdesinde "31").
#   AppIcon\appiconfg.png : sadece ev (şeffaf zemin, Android'in güvenli alanına sığacak kadar küçük)
#   Splash\splash.png     : zeminli logo
# Kullanım: powershell -File Ekip.Mobil\Resources\mobil-ikon-uret.ps1
Add-Type -AssemblyName System.Drawing
$kok = $PSScriptRoot

function Ev([Drawing.Graphics]$g) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.AddPolygon([Drawing.PointF[]]@(
        (New-Object Drawing.PointF 128, 42), (New-Object Drawing.PointF 228, 126), (New-Object Drawing.PointF 200, 126),
        (New-Object Drawing.PointF 200, 214), (New-Object Drawing.PointF 56, 214), (New-Object Drawing.PointF 56, 126),
        (New-Object Drawing.PointF 28, 126)))
    $g.FillPath([Drawing.Brushes]::White, $p)
    $kenar = New-Object Drawing.Pen ([Drawing.Color]::White), 14; $kenar.LineJoin = 'Round'
    $g.DrawPath($kenar, $p)
    $g.FillRectangle([Drawing.Brushes]::White, 162, 60, 22, 50)
    $mor = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#5B3BE8'))
    $yazi = New-Object Drawing.Drawing2D.GraphicsPath
    $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $yazi.AddString('31', (New-Object Drawing.FontFamily 'Segoe UI Black'), 0, 98, (New-Object Drawing.RectangleF 56, 122, 144, 96), $sf)
    $g.FillPath($mor, $yazi)
}

function Ciz([int]$n, [bool]$zemin, [double]$olcek) {
    $bmp = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $k = $n / 256.0
    $g.ScaleTransform($k, $k)
    if ($zemin) {
        $yol = New-Object Drawing.Drawing2D.GraphicsPath
        $r = 56; $yol.AddArc(0, 0, $r*2, $r*2, 180, 90); $yol.AddArc(256-$r*2, 0, $r*2, $r*2, 270, 90)
        $yol.AddArc(256-$r*2, 256-$r*2, $r*2, $r*2, 0, 90); $yol.AddArc(0, 256-$r*2, $r*2, $r*2, 90, 90); $yol.CloseFigure()
        $f = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Rectangle 0,0,256,256), [Drawing.ColorTranslator]::FromHtml('#9277FF'), [Drawing.ColorTranslator]::FromHtml('#5B3BE8'), 45)
        $g.FillPath($f, $yol)
    }
    # Ortadan küçült (Android uyarlanabilir simgede kenarlar kırpılır).
    $g.TranslateTransform(128, 128); $g.ScaleTransform($olcek, $olcek); $g.TranslateTransform(-128, -128)
    Ev $g
    $g.Dispose()
    return $bmp
}

(Ciz 432 $false 0.62).Save("$kok\AppIcon\appiconfg.png")
(Ciz 512 $true 1.0).Save("$kok\Splash\splash.png")
"ok"
