param([string]$Cikti)
# 31Hane logosu: mor yuvarlak kare, icinde beyaz ev; evin govdesinde mor "31".
# 24 pikselden kucuk boyutlarda rakam okunmaz: yerine kucuk bir kapi cizilir.
# Uygulama icindeki cizim Tema.Logo ile ayni (256'lik izgara).
Add-Type -AssemblyName System.Drawing

function Ev {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    # Cati + govde tek parca: tepe (128,34), saçaklar govdeden tasar.
    $p.AddPolygon([Drawing.PointF[]]@(
        (New-Object Drawing.PointF 128, 42),
        (New-Object Drawing.PointF 228, 126),
        (New-Object Drawing.PointF 200, 126),
        (New-Object Drawing.PointF 200, 214),
        (New-Object Drawing.PointF 56, 214),
        (New-Object Drawing.PointF 56, 126),
        (New-Object Drawing.PointF 28, 126)))
    return $p
}

function Ciz([int]$n) {
    $bmp = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $k = $n / 256.0
    $g.ScaleTransform($k, $k)

    $yol = New-Object Drawing.Drawing2D.GraphicsPath
    $r = 56; $yol.AddArc(0, 0, $r*2, $r*2, 180, 90); $yol.AddArc(256-$r*2, 0, $r*2, $r*2, 270, 90)
    $yol.AddArc(256-$r*2, 256-$r*2, $r*2, $r*2, 0, 90); $yol.AddArc(0, 256-$r*2, $r*2, $r*2, 90, 90); $yol.CloseFigure()
    $f = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Rectangle 0,0,256,256), [Drawing.ColorTranslator]::FromHtml('#9277FF'), [Drawing.ColorTranslator]::FromHtml('#5B3BE8'), 45)
    $g.FillPath($f, $yol)

    # Ev (koseleri yumusatmak icin ayni renkte kalin kenar)
    $ev = Ev
    $g.FillPath([Drawing.Brushes]::White, $ev)
    $kenar = New-Object Drawing.Pen ([Drawing.Color]::White), 14
    $kenar.LineJoin = 'Round'
    $g.DrawPath($kenar, $ev)
    # Baca
    $g.FillRectangle([Drawing.Brushes]::White, 162, 60, 22, 50)

    $mor = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#5B3BE8'))
    if ($n -ge 24) {
        $yazi = New-Object Drawing.Drawing2D.GraphicsPath
        $fam = New-Object Drawing.FontFamily 'Segoe UI Black'
        $sf = New-Object Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
        $yazi.AddString('31', $fam, 0, 98, (New-Object Drawing.RectangleF 56, 122, 144, 96), $sf)
        $g.FillPath($mor, $yazi)
    } else {
        # Kucuk boyut: kapi
        $g.FillRectangle($mor, 108, 158, 40, 56)
    }
    $g.Dispose()
    return $bmp
}

New-Item -ItemType Directory -Force $Cikti | Out-Null
(Ciz 256).Save("$Cikti\logo-256.png"); (Ciz 512).Save("$Cikti\logo-512.png")
# .ico: 256 PNG olarak, kucukler klasik 32 bit BMP (her programin okuyabildigi bicim).
$boyutlar = 16, 20, 24, 32, 40, 48, 64, 256
$veriler = @()
foreach ($n in $boyutlar) {
    $b = Ciz $n
    if ($n -eq 256) { $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $veriler += ,$ms.ToArray(); $b.Dispose(); continue }
    $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
    $bw.Write([uint32]40); $bw.Write([int32]$n); $bw.Write([int32]($n*2)); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]0); $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
    for ($y = $n - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $n; $x++) { $p = $b.GetPixel($x, $y); $bw.Write([byte]$p.B); $bw.Write([byte]$p.G); $bw.Write([byte]$p.R); $bw.Write([byte]$p.A) } }
    $maske = [int]([math]::Ceiling($n / 32.0) * 4) * $n
    $bw.Write((New-Object byte[] $maske))
    $veriler += ,$ms.ToArray(); $b.Dispose()
}
$ico = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$boyutlar.Count)
$ofs = 6 + 16 * $boyutlar.Count
for ($k = 0; $k -lt $boyutlar.Count; $k++) {
    $n = $boyutlar[$k]
    $w.Write([byte]($n % 256)); $w.Write([byte]($n % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$veriler[$k].Length); $w.Write([uint32]$ofs); $ofs += $veriler[$k].Length
}
foreach ($v in $veriler) { $w.Write($v) }
[IO.File]::WriteAllBytes("$Cikti\31hane.ico", $ico.ToArray())
# Onizleme: tum boyutlar yan yana (kontrol icin)
$on = New-Object Drawing.Bitmap 760, 300
$og = [Drawing.Graphics]::FromImage($on); $og.Clear([Drawing.ColorTranslator]::FromHtml('#1E1F24'))
$x = 10
foreach ($n in 256, 64, 48, 32, 24, 20, 16) { $b = Ciz $n; $og.DrawImage($b, $x, 20, $n, $n); $x += $n + 20; $b.Dispose() }
$og.Dispose(); $on.Save("$Cikti\onizleme.png")
"ok"
