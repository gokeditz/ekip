using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Uygulama içi tam alan resim görüntüleyici (ayrı pencere değil): arka plan kararır, resim pencereye sığdırılır.
//   Tekerlek: imlecin olduğu yere yakınlaştır/uzaklaştır   Sürükle: kaydır   Çift tık: sığdır ↔ %100
//   Esc / boş yere tık / ✕: kapat   +, -, 0: yakınlaştır, uzaklaştır, sığdır
//   Altta ad, boyut, "İndir" (İndirilenler klasörüne) ve "Klasörde göster". Animasyonlu GIF oynar.
//
//   ResimGoruntuleyici.Goster(form, api, ek);
public sealed class ResimGoruntuleyici : Control
{
    private const int EnBuyukKenar = 3200;   // bellek: daha büyük resimler bu boyuta küçültülür
    private const int GifKenari = 1600;

    private readonly Api _api;
    private readonly Ek _ek;
    private Bitmap? _arka;
    private Image? _resim;
    private AnimasyonluResim? _anim;
    private IDisposable? _kayit;
    private Control? _oncekiOdak;

    private bool _kapandi;
    private double _ilerleme;
    private string? _hata;

    private float _zoom = 1, _fit = 1;
    private PointF _merkez;                  // resmin merkezi (kontrol koordinatında)
    private bool _sigdir = true;
    private bool _basili, _suruklendi, _gorselUstunde;
    private Point _basPoz;
    private PointF _basMerkez;
    private bool _etkilesim;
    private readonly System.Windows.Forms.Timer _kalite = new() { Interval = 160 };

    private string? _kaydedilen;
    private bool _indiriyor, _indirdiktenSonraGoster, _indirmeHatasi;
    private double _indirmeIlerleme;
    private int _uzerinde = -1;              // 0 kapat, 1 klasörde göster, 2 indir

    private int BarH => Tema.S(76);
    private Rectangle Alan => new(0, 0, Width, Math.Max(1, Height - BarH));
    private Rectangle KapatAlani => new(Width - Tema.S(60), Tema.S(18), Tema.S(40), Tema.S(40));

    private ResimGoruntuleyici(Api api, Ek ek)
    {
        _api = api;
        _ek = ek;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        BackColor = Color.FromArgb(12, 13, 17);
        _kalite.Tick += (_, _) => { _kalite.Stop(); _etkilesim = false; Invalidate(Alan); };
    }

    public static ResimGoruntuleyici Goster(Form sahip, Api api, Ek ek)
    {
        var v = new ResimGoruntuleyici(api, ek) { Bounds = sahip.ClientRectangle };
        v._oncekiOdak = sahip.ActiveControl;
        // Arka plan: formun o anki görüntüsü, karartılmış (IcModal ile aynı yöntem).
        try
        {
            using var tam = new Bitmap(sahip.Width, sahip.Height);
            sahip.DrawToBitmap(tam, new Rectangle(Point.Empty, sahip.Size));
            var ofs = sahip.PointToScreen(Point.Empty);
            int dx = ofs.X - sahip.Left, dy = ofs.Y - sahip.Top;
            v._arka = new Bitmap(Math.Max(1, sahip.ClientSize.Width), Math.Max(1, sahip.ClientSize.Height));
            using var g = Graphics.FromImage(v._arka);
            g.DrawImage(tam, new Rectangle(0, 0, v._arka.Width, v._arka.Height), new Rectangle(dx, dy, v._arka.Width, v._arka.Height), GraphicsUnit.Pixel);
            using var karart = new SolidBrush(Color.FromArgb(220, 6, 7, 10));
            g.FillRectangle(karart, 0, 0, v._arka.Width, v._arka.Height);
        }
        catch { v._arka = null; }
        sahip.Controls.Add(v);
        v.BringToFront();
        v.Focus();
        v.Yukle();
        return v;
    }

    public void Kapat()
    {
        if (_kapandi || IsDisposed) return;
        _kapandi = true;
        var form = FindForm();
        form?.Controls.Remove(this);
        if (form != null && !form.IsDisposed && _oncekiOdak is { IsDisposed: false, Visible: true } o) { try { o.Focus(); } catch { } }
        Dispose();
    }

    // ---------------------------------------------------------------- Yükleme

    private async void Yukle()
    {
        var ilerleme = new Progress<double>(p => { if (!_kapandi) { _ilerleme = p; Invalidate(Alan); } });
        var yol = await DosyaOnbellegi.Indir(_api, _ek, ilerleme);
        if (_kapandi || IsDisposed) return;
        if (yol is null) { Hata("Resim indirilemedi."); return; }

        bool gif = _ek.Mime.Contains("gif", StringComparison.OrdinalIgnoreCase) || yol.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        var (resim, anim) = await Task.Run(() => DosyaOnbellegi.Coz(yol, gif ? GifKenari : EnBuyukKenar, false));
        if (_kapandi || IsDisposed) { anim?.Dispose(); resim?.Dispose(); return; }
        if (resim is null) { Hata("Bu resim açılamadı."); return; }

        _resim = resim;
        _anim = anim;
        Sigdir();
        if (anim != null) _kayit = AnimasyonluResim.Kaydet(this, anim, () => Invalidate(Rectangle.Ceiling(GorselAlani())));
        Invalidate();
    }

    private void Hata(string mesaj) { _hata = mesaj; Invalidate(); }

    // ---------------------------------------------------------------- Yakınlaştırma

    private Image? Gorsel => _anim?.GecerliKare ?? _resim;

    private RectangleF GorselAlani()
    {
        var r = Gorsel;
        if (r is null) return RectangleF.Empty;
        float w = r.Width * _zoom, h = r.Height * _zoom;
        return new RectangleF(_merkez.X - w / 2, _merkez.Y - h / 2, w, h);
    }

    private void FitHesapla()
    {
        var r = Gorsel;
        if (r is null) return;
        var a = Alan;
        int kenar = Tema.S(32);
        _fit = Math.Min(1f, Math.Min((a.Width - kenar * 2) / (float)r.Width, (a.Height - kenar * 2) / (float)r.Height));
        if (_fit <= 0) _fit = 0.05f;
    }

    private void Sigdir()
    {
        FitHesapla();
        _zoom = _fit;
        _sigdir = true;
        _merkez = new PointF(Alan.Width / 2f, Alan.Height / 2f);
        Invalidate(Alan);
    }

    // Resim alandan küçükse ortada kalsın, büyükse alan dışına boşluk bırakacak kadar kaymasın.
    private void Kisitla()
    {
        var r = Gorsel;
        if (r is null) return;
        var a = Alan;
        float w = r.Width * _zoom, h = r.Height * _zoom;
        float x = _merkez.X, y = _merkez.Y;
        x = w <= a.Width ? a.Width / 2f : Math.Clamp(x, a.Width - w / 2, w / 2);
        y = h <= a.Height ? a.Height / 2f : Math.Clamp(y, a.Height - h / 2, h / 2);
        _merkez = new PointF(x, y);
    }

    private void YakinlastirNoktada(float yeniZoom, PointF nokta)
    {
        if (Gorsel is null) return;
        yeniZoom = Math.Clamp(yeniZoom, _fit, Math.Max(12f, _fit));
        if (Math.Abs(yeniZoom - _zoom) < 0.0001f) return;
        // Imlecin altındaki resim noktası yerinde kalsın.
        float u = (nokta.X - _merkez.X) / _zoom, v = (nokta.Y - _merkez.Y) / _zoom;
        _zoom = yeniZoom;
        _merkez = new PointF(nokta.X - u * _zoom, nokta.Y - v * _zoom);
        _sigdir = Math.Abs(_zoom - _fit) < 0.0001f;
        if (_sigdir) _merkez = new PointF(Alan.Width / 2f, Alan.Height / 2f);
        Kisitla();
        Etkilesti();
    }

    private void Etkilesti()
    {
        _etkilesim = true;
        _kalite.Stop();
        _kalite.Start();
        Invalidate(Alan);
    }

    // ---------------------------------------------------------------- Çizim

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_arka != null) g.DrawImage(_arka, ClientRectangle);
        else g.Clear(Color.FromArgb(12, 13, 17));

        var alan = Alan;
        var img = Gorsel;
        if (img != null)
        {
            var eski = g.Clip;
            g.SetClip(alan);
            var r = GorselAlani();
            g.InterpolationMode = _zoom >= 4f ? InterpolationMode.NearestNeighbor : _etkilesim || _anim != null ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.DrawImage(img, r);
            g.Clip = eski;
            if (_zoom < 0.999f || _zoom > 1.001f) ZoomRozeti(g, alan);
        }
        else DurumCiz(g, alan);

        BarCiz(g);
        KapatCiz(g);
    }

    private void DurumCiz(Graphics g, Rectangle alan)
    {
        if (_hata != null)
        {
            TextRenderer.DrawText(g, Glif.Uyari, Cizim.Ikon(28), new Rectangle(0, alan.Height / 2 - Tema.S(50), Width, Tema.S(50)), Tema.MetinSoluk, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _hata, Tema.Yazi(12, true), new Rectangle(0, alan.Height / 2 + Tema.S(10), Width, Tema.S(28)), Tema.Metin, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            return;
        }
        TextRenderer.DrawText(g, _ilerleme > 0 && _ilerleme < 1 ? $"Yükleniyor… %{(int)(_ilerleme * 100)}" : "Yükleniyor…", Tema.Yazi(11, true),
            new Rectangle(0, alan.Height / 2 - Tema.S(24), Width, Tema.S(28)), Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        int bw = Tema.S(180), bh = Tema.S(4);
        var cubuk = new RectangleF((Width - bw) / 2f, alan.Height / 2f + Tema.S(14), bw, bh);
        Cizim.YuvarlakDoldur(g, cubuk, bh / 2f, Tema.Kenar);
        if (_ilerleme > 0) Cizim.YuvarlakDoldur(g, new RectangleF(cubuk.X, cubuk.Y, (float)(bw * Math.Clamp(_ilerleme, 0.03, 1)), bh), bh / 2f, Tema.Vurgu);
    }

    private void ZoomRozeti(Graphics g, Rectangle alan)
    {
        var yazi = $"%{(int)Math.Round(_zoom * 100)}";
        var f = Tema.Yazi(9, true);
        int w = TextRenderer.MeasureText(yazi, f, Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(20), h = Tema.S(26);
        var r = new Rectangle(Width - w - Tema.S(20), alan.Bottom - h - Tema.S(14), w, h);
        Cizim.YuvarlakDoldur(g, r, h / 2f, Color.FromArgb(210, 10, 11, 14));
        TextRenderer.DrawText(g, yazi, f, r, Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void KapatCiz(Graphics g)
    {
        var k = KapatAlani;
        Cizim.YuvarlakDoldur(g, k, k.Width / 2f, _uzerinde == 0 ? Tema.Kenar : Color.FromArgb(200, 10, 11, 14));
        TextRenderer.DrawText(g, Glif.Kapat, Cizim.Ikon(10), k, Tema.Metin, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // Alt çubuktaki düğmelerin yerleri (sağdan sola: İndir, Klasörde göster).
    private (Rectangle Indir, Rectangle Klasor) Dugmeler()
    {
        int h = Tema.S(38), y = Height - BarH + (BarH - h) / 2;
        int wi = DugmeGenisligi(IndirYazisi()), wk = DugmeGenisligi("Klasörde göster");
        var indir = new Rectangle(Width - Tema.S(24) - wi, y, wi, h);
        var klasor = new Rectangle(indir.Left - Tema.S(10) - wk, y, wk, h);
        return (indir, klasor);
    }

    private static int DugmeGenisligi(string yazi) =>
        Math.Max(Tema.S(110), TextRenderer.MeasureText(yazi, Tema.Yazi(10, true), Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(40));

    private string IndirYazisi() =>
        _kaydedilen != null ? "İndirildi" : _indiriyor ? $"İndiriliyor %{(int)(_indirmeIlerleme * 100)}" : _indirmeHatasi ? "Tekrar dene" : "İndir";

    private void BarCiz(Graphics g)
    {
        var bar = new Rectangle(0, Height - BarH, Width, BarH);
        using (var f = new SolidBrush(Tema.Arkaplan)) g.FillRectangle(f, bar);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, 0, bar.Top, Width, bar.Top);

        var (indir, klasor) = Dugmeler();
        int sol = Tema.S(24), sag = klasor.Left - Tema.S(20);
        TextRenderer.DrawText(g, _ek.Ad, Tema.Yazi(11, true), new Rectangle(sol, bar.Top + Tema.S(15), Math.Max(10, sag - sol), Tema.S(22)), Tema.Metin,
            TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, BilgiSatiri(), Tema.Yazi(9.5f), new Rectangle(sol, bar.Top + Tema.S(40), Math.Max(10, sag - sol), Tema.S(18)), Tema.MetinSoluk,
            TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        DugmeCiz(g, klasor, "Klasörde göster", false, _uzerinde == 1);
        DugmeCiz(g, indir, IndirYazisi(), true, _uzerinde == 2, _kaydedilen != null, _indirmeHatasi);
    }

    private void DugmeCiz(Graphics g, Rectangle r, string yazi, bool birincil, bool uzerinde, bool tamam = false, bool hata = false)
    {
        Color zemin = tamam ? ColorTranslator.FromHtml("#0E3326")
            : birincil ? (uzerinde ? Tema.VurguHover : Tema.Vurgu)
            : uzerinde ? Tema.Kenar : Tema.YuzeyUst;
        if (hata) zemin = uzerinde ? ColorTranslator.FromHtml("#E5546B") : ColorTranslator.FromHtml("#DA3B54");
        Cizim.YuvarlakDoldur(g, r, Tema.S(8), zemin);
        var renk = tamam ? ColorTranslator.FromHtml("#9EF0C9") : birincil ? Color.White : Tema.Metin;
        TextRenderer.DrawText(g, yazi, Tema.Yazi(10, true), r, renk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private string BilgiSatiri()
    {
        var parcalar = new List<string>();
        int w = _ek.Genislik, h = _ek.Yukseklik;
        if (w <= 0 && _resim != null && Math.Max(_resim.Width, _resim.Height) < EnBuyukKenar) { w = _resim.Width; h = _resim.Height; }
        if (w > 0 && h > 0) parcalar.Add($"{w} × {h}");
        if (_ek.Boyut > 0) parcalar.Add(BoyutYazisi(_ek.Boyut));
        return string.Join(" · ", parcalar);
    }

    public static string BoyutYazisi(long bayt) => bayt switch
    {
        < 1024 => $"{bayt} B",
        < 1024 * 1024 => $"{bayt / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bayt / 1048576.0:0.#} MB",
        _ => $"{bayt / 1073741824.0:0.##} GB",
    };

    // ---------------------------------------------------------------- Etkileşim

    private int DugmeBul(Point p)
    {
        if (KapatAlani.Contains(p)) return 0;
        var (indir, klasor) = Dugmeler();
        if (klasor.Contains(p)) return 1;
        if (indir.Contains(p)) return 2;
        return -1;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Gorsel is null || !Alan.Contains(e.Location)) return;
        YakinlastirNoktada(_zoom * (float)Math.Pow(1.2, e.Delta / 120.0), e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        int d = DugmeBul(e.Location);
        if (d >= 0) { DugmeTikla(d); return; }
        _basili = true;
        _suruklendi = false;
        _basPoz = e.Location;
        _basMerkez = _merkez;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_basili && Gorsel != null)
        {
            int dx = e.X - _basPoz.X, dy = e.Y - _basPoz.Y;
            if (!_suruklendi && dx * dx + dy * dy > Tema.S(5) * Tema.S(5)) _suruklendi = true;
            if (_suruklendi)
            {
                _merkez = new PointF(_basMerkez.X + dx, _basMerkez.Y + dy);
                Kisitla();
                Cursor = Cursors.SizeAll;
                Etkilesti();
            }
            return;
        }
        int u = DugmeBul(e.Location);
        if (u != _uzerinde) { _uzerinde = u; Invalidate(new Rectangle(0, Height - BarH, Width, BarH)); Invalidate(KapatAlani); }
        bool gorselUstu = Gorsel != null && GorselAlani().Contains(e.Location);
        Cursor = u >= 0 ? Cursors.Hand : gorselUstu ? Cursors.Hand : Cursors.Default;
        _gorselUstunde = gorselUstu;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_basili) return;
        _basili = false;
        Cursor = _gorselUstunde ? Cursors.Hand : Cursors.Default;
        // Sürüklenmeden, resmin ve çubuğun dışına tıklandıysa kapat.
        if (!_suruklendi && Alan.Contains(e.Location) && !(Gorsel != null && GorselAlani().Contains(e.Location))) Kapat();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_uzerinde != -1) { _uzerinde = -1; Invalidate(); }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (Gorsel is null || !Alan.Contains(e.Location) || !GorselAlani().Contains(e.Location)) return;
        // Sığdırılmışsa %100'e (sığdırma zaten %100 ise %200'e), değilse sığdır.
        if (_sigdir) YakinlastirNoktada(_fit < 0.999f ? 1f : 2f, e.Location);
        else { Sigdir(); Etkilesti(); }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Gorsel is null) return;
        if (_sigdir) Sigdir();
        else { FitHesapla(); _zoom = Math.Max(_zoom, _fit); Kisitla(); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape: Kapat(); return true;
            case Keys.Oemplus or Keys.Add or Keys.Oemplus | Keys.Shift:
                YakinlastirNoktada(_zoom * 1.25f, new PointF(Alan.Width / 2f, Alan.Height / 2f)); return true;
            case Keys.OemMinus or Keys.Subtract:
                YakinlastirNoktada(_zoom / 1.25f, new PointF(Alan.Width / 2f, Alan.Height / 2f)); return true;
            case Keys.D0 or Keys.NumPad0:
                if (Gorsel != null) { Sigdir(); Etkilesti(); }
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void DugmeTikla(int d)
    {
        switch (d)
        {
            case 0: Kapat(); break;
            case 1: Indir(true); break;
            case 2: Indir(false); break;
        }
    }

    private async void Indir(bool klasordeGoster)
    {
        if (_kaydedilen != null && File.Exists(_kaydedilen))
        {
            if (klasordeGoster) DosyaOnbellegi.KlasordeGoster(_kaydedilen);
            return;
        }
        _kaydedilen = null;
        if (_indiriyor) { _indirdiktenSonraGoster |= klasordeGoster; return; }
        _indiriyor = true;
        _indirmeHatasi = false;
        _indirmeIlerleme = 0;
        _indirdiktenSonraGoster = klasordeGoster;
        Invalidate();
        var ilerleme = new Progress<double>(p => { if (!_kapandi) { _indirmeIlerleme = p; Invalidate(new Rectangle(0, Height - BarH, Width, BarH)); } });
        var yol = await DosyaOnbellegi.IndirilenlereKaydet(_api, _ek, ilerleme);
        if (_kapandi || IsDisposed) return;
        _indiriyor = false;
        if (yol is null) _indirmeHatasi = true;
        else
        {
            _kaydedilen = yol;
            if (_indirdiktenSonraGoster) DosyaOnbellegi.KlasordeGoster(yol);
        }
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _kapandi = true;
            _kalite.Dispose();
            _kayit?.Dispose();
            _kayit = null;
            // Çizim bitmeden atmamak için önce referansları bırak.
            var anim = _anim; var resim = _resim;
            _anim = null; _resim = null;
            anim?.Dispose();
            if (anim is null) resim?.Dispose();
            _arka?.Dispose();
        }
        base.Dispose(disposing);
    }
}
