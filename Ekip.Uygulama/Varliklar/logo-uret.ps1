param([string]$Cikti)
Add-Type -AssemblyName System.Drawing
# Logo: mor yuvarlak kare, iÃ§inde konuÅŸma balonu kuyruklu beyaz "e".
function Ciz([int]$n) {
    $bmp = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $k = $n / 256.0
    $g.ScaleTransform($k, $k)

    $yol = New-Object Drawing.Drawing2D.GraphicsPath
    $r = 56; $yol.AddArc(0, 0, $r*2, $r*2, 180, 90); $yol.AddArc(256-$r*2, 0, $r*2, $r*2, 270, 90)
    $yol.AddArc(256-$r*2, 256-$r*2, $r*2, $r*2, 0, 90); $yol.AddArc(0, 256-$r*2, $r*2, $r*2, 90, 90); $yol.CloseFigure()
    $f = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Rectangle 0,0,256,256), [Drawing.ColorTranslator]::FromHtml('#9277FF'), [Drawing.ColorTranslator]::FromHtml('#5B3BE8'), 45)
    $g.FillPath($f, $yol)

    $cx = 132; $cy = 122; $R = 62
    $kalem = New-Object Drawing.Pen ([Drawing.Color]::White), 34
    $kalem.StartCap = 'Round'; $kalem.EndCap = 'Round'
    # "e" halkasÄ±: saÄŸ altta aÃ§Ä±k (20Â°-55Â° arasÄ± boÅŸ)
    $g.DrawArc($kalem, $cx-$R, $cy-$R, $R*2, $R*2, 78, 318)
    # orta Ã§izgi
    $kalem2 = New-Object Drawing.Pen ([Drawing.Color]::White), 30
    $kalem2.StartCap = 'Flat'; $kalem2.EndCap = 'Round'
    $g.DrawLine($kalem2, $cx-$R+2, $cy, $cx+$R, $cy)
    # konuÅŸma balonu kuyruÄŸu (sol alt)
    $kuyruk = New-Object Drawing.Drawing2D.GraphicsPath
    $kuyruk.AddPolygon([Drawing.PointF[]]@((New-Object Drawing.PointF 66,150), (New-Object Drawing.PointF 46,214), (New-Object Drawing.PointF 112,184)))
    $g.FillPath([Drawing.Brushes]::White, $kuyruk)
    $g.Dispose()
    return $bmp
}
New-Item -ItemType Directory -Force $Cikti | Out-Null
(Ciz 256).Save("$Cikti\logo-256.png"); (Ciz 512).Save("$Cikti\logo-512.png")
# .ico: 256 PNG olarak, küçükler klasik 32 bit BMP (her programın okuyabildiği biçim).
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
[IO.File]::WriteAllBytes("$Cikti\ekip.ico", $ico.ToArray())
"ok"