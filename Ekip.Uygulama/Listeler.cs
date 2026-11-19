using Ekip.Ortak;

namespace Ekip.Uygulama;

public enum SatirTuru { Arkadas, Gelen, Giden }
public record Satir(Profil Profil, SatirTuru Tur);

// Arkadaş listesi. Tek bir kontrol, her satırı kendisi çizer: 500 arkadaş olsa da sadece görünen satırlar çizilir.
public class ArkadasListe : Control
{
    private List<Satir> _satirlar = [];
    private int _kaydirma, _uzerindeSatir = -1, _uzerindeDugme = -1;
    private int? _surukleBaslangic;
    private int _surukleKaydirma;
    private readonly KoyuIpucu _ipucu = new();
    private string? _ipucuYazisi;

    public AvatarOnbellek? Avatarlar { get; set; }

    // eylem: "profil", "kabul", "reddet", "iptal", "menu". Rectangle ekran koordinatında.
    public event Action<Satir, string, Rectangle>? Eylem;

    public ArkadasListe()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Tema.Arkaplan;
    }

    private int SatirH => Tema.S(64);
    private int IcerikH => _satirlar.Count * SatirH;
    private int MaksKaydirma => Math.Max(0, IcerikH - Height);
    private bool KaydirmaVar => IcerikH > Height;

    public void Ayarla(List<Satir> satirlar)
    {
        _satirlar = satirlar;
        _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
        Invalidate();
    }

    public void BasaDon() { _kaydirma = 0; Invalidate(); }

    private static (string ad, string glif, string ipucu)[] Dugmeler(Satir s) => s.Tur switch
    {
        SatirTuru.Gelen => [("kabul", Glif.Onay, "Kabul et"), ("reddet", Glif.Kapat, "Reddet")],
        SatirTuru.Giden => [("iptal", Glif.Kapat, "İsteği geri çek")],
        _ => [("mesaj", Glif.Mesaj, "Mesaj gönder"), ("menu", Glif.DahaFazla, "Diğer")],
    };

    private Rectangle SatirAlani(int i) =>
        new(0, i * SatirH - _kaydirma, Width - (KaydirmaVar ? Tema.S(14) : 0), SatirH);

    private Rectangle DugmeAlani(Rectangle satir, int j, int adet)
    {
        int cap = Tema.S(36), ara = Tema.S(10);
        int x = satir.Right - Tema.S(12) - (adet - j) * cap - (adet - j - 1) * ara;
        return new Rectangle(x, satir.Y + (satir.Height - cap) / 2, cap, cap);
    }

    private Rectangle KaydirmaCubugu()
    {
        int h = Math.Max(Tema.S(40), Height * Height / Math.Max(1, IcerikH));
        int y = MaksKaydirma == 0 ? 0 : (Height - h) * _kaydirma / MaksKaydirma;
        return new Rectangle(Width - Tema.S(8), y, Tema.S(6), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int ilk = Math.Max(0, _kaydirma / SatirH), son = Math.Min(_satirlar.Count - 1, (_kaydirma + Height) / SatirH);

        for (int i = ilk; i <= son; i++)
        {
            var s = _satirlar[i];
            var r = SatirAlani(i);
            bool uzerinde = i == _uzerindeSatir;

            if (uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(8), Tema.YuzeyUst);
            else if (i > 0 && i - 1 != _uzerindeSatir)
            {
                using var p = new Pen(Tema.Kenar);
                g.DrawLine(p, r.X + Tema.S(10), r.Y, r.Right - Tema.S(10), r.Y);
            }

            int av = Tema.S(40);
            var avR = new RectangleF(r.X + Tema.S(12), r.Y + (r.Height - av) / 2f, av, av);
            Cizim.Avatar(g, avR, s.Profil, Avatarlar?.Al(s.Profil), uzerinde ? Tema.YuzeyUst : BackColor, s.Tur == SatirTuru.Arkadas);

            var dugmeler = Dugmeler(s);
            int yaziX = (int)avR.Right + Tema.S(12);
            int yaziSag = DugmeAlani(r, 0, dugmeler.Length).X - Tema.S(12);
            var adFont = Tema.Yazi(10.5f, kalin: true);
            var adOlcu = TextRenderer.MeasureText(g, s.Profil.GorunenAd, adFont, Size.Empty, TextFormatFlags.NoPadding);
            int ustY = r.Y + r.Height / 2 - adOlcu.Height - Tema.S(1);
            TextRenderer.DrawText(g, s.Profil.GorunenAd, adFont, new Rectangle(yaziX, ustY, yaziSag - yaziX, adOlcu.Height),
                Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            int kullaniciX = yaziX + Math.Min(adOlcu.Width, yaziSag - yaziX) + Tema.S(6);
            if (uzerinde && kullaniciX < yaziSag)
                TextRenderer.DrawText(g, "@" + s.Profil.KullaniciAdi, Tema.Yazi(9.5f), new Rectangle(kullaniciX, ustY + Tema.S(1), yaziSag - kullaniciX, adOlcu.Height),
                    Tema.MetinSoluk, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            var alt = s.Tur switch
            {
                SatirTuru.Gelen => "Sana arkadaşlık isteği gönderdi",
                SatirTuru.Giden => "Gönderdiğin istek bekliyor",
                _ => string.IsNullOrWhiteSpace(s.Profil.DurumMesaji) || s.Profil.Durum == Durum.Cevrimdisi
                    ? Cizim.DurumAdi(s.Profil.Durum) : s.Profil.DurumMesaji,
            };
            TextRenderer.DrawText(g, alt, Tema.Yazi(9.5f), new Rectangle(yaziX, r.Y + r.Height / 2 + Tema.S(2), yaziSag - yaziX, Tema.S(20)),
                Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            for (int j = 0; j < dugmeler.Length; j++)
            {
                var d = DugmeAlani(r, j, dugmeler.Length);
                bool dUzerinde = uzerinde && j == _uzerindeDugme;
                var zemin = uzerinde ? Tema.Arkaplan : Tema.Yuzey;
                var eski = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(zemin)) g.FillEllipse(b, d);
                g.SmoothingMode = eski;
                var renk = !dUzerinde ? Tema.MetinIkincil : dugmeler[j].ad switch
                {
                    "kabul" => Tema.Basari,
                    "reddet" or "iptal" => Tema.Hata,
                    _ => Tema.Metin,
                };
                TextRenderer.DrawText(g, dugmeler[j].glif, Cizim.Ikon(10.5f), d, renk,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        if (KaydirmaVar)
            Cizim.YuvarlakDoldur(g, KaydirmaCubugu(), Tema.S(3), _surukleBaslangic != null ? Tema.KenarHover : Tema.Kenar);
    }

    private (int satir, int dugme) Bul(Point p)
    {
        if (p.X > Width - Tema.S(14) && KaydirmaVar) return (-1, -1);
        int i = (p.Y + _kaydirma) / SatirH;
        if (p.Y < 0 || i < 0 || i >= _satirlar.Count) return (-1, -1);
        var r = SatirAlani(i);
        var dugmeler = Dugmeler(_satirlar[i]);
        for (int j = 0; j < dugmeler.Length; j++)
        {
            var d = DugmeAlani(r, j, dugmeler.Length);
            d.Inflate(Tema.S(2), Tema.S(2));
            if (d.Contains(p)) return (i, j);
        }
        return (i, -1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_surukleBaslangic is { } bas)
        {
            int cubukH = KaydirmaCubugu().Height;
            int yol = Math.Max(1, Height - cubukH);
            _kaydirma = Math.Clamp(_surukleKaydirma + (e.Y - bas) * MaksKaydirma / yol, 0, MaksKaydirma);
            Invalidate();
            return;
        }

        var (satir, dugme) = Bul(e.Location);
        if (satir != _uzerindeSatir || dugme != _uzerindeDugme)
        {
            _uzerindeSatir = satir;
            _uzerindeDugme = dugme;
            Cursor = satir >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();

            string? ipucu = dugme >= 0 ? Dugmeler(_satirlar[satir])[dugme].ipucu : null;
            if (ipucu != _ipucuYazisi)
            {
                _ipucuYazisi = ipucu;
                _ipucu.Hide(this);
                if (ipucu != null)
                {
                    var d = DugmeAlani(SatirAlani(satir), dugme, Dugmeler(_satirlar[satir]).Length);
                    _ipucu.Show(ipucu, this, d.X - Tema.S(10), d.Y - Tema.S(34), 2500);
                }
            }
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _uzerindeSatir = _uzerindeDugme = -1;
        _ipucuYazisi = null;
        _ipucu.Hide(this);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && KaydirmaVar && e.X > Width - Tema.S(14))
        {
            var c = KaydirmaCubugu();
            if (!c.Contains(e.Location)) // Çubuğun dışına tıklandı: o tarafa bir sayfa kaydır.
            {
                _kaydirma = Math.Clamp(_kaydirma + (e.Y < c.Y ? -Height : Height), 0, MaksKaydirma);
                Invalidate();
                return;
            }
            _surukleBaslangic = e.Y;
            _surukleKaydirma = _kaydirma;
            Capture = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_surukleBaslangic != null) { _surukleBaslangic = null; Capture = false; Invalidate(); return; }
        if (e.Button != MouseButtons.Left) return;

        var (satir, dugme) = Bul(e.Location);
        if (satir < 0) return;
        var s = _satirlar[satir];
        var r = SatirAlani(satir);
        if (dugme >= 0)
        {
            var dugmeler = Dugmeler(s);
            Eylem?.Invoke(s, dugmeler[dugme].ad, RectangleToScreen(DugmeAlani(r, dugme, dugmeler.Length)));
        }
        else Eylem?.Invoke(s, s.Tur == SatirTuru.Arkadas ? "mesaj" : "profil", RectangleToScreen(r));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _kaydirma = Math.Clamp(_kaydirma - e.Delta * SatirH / 120, 0, MaksKaydirma);
        OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, e.X, e.Y, 0));
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
    }
}

// Profil kartı: üstte renkli bant, avatar, ad, durum ve hakkımda yazısı. Hem önizlemede hem açılır pencerede kullanılır.
public class ProfilKarti : Control
{
    private Profil? _profil;
    private Image? _resim;

    public ProfilKarti()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
    }

    public Profil? Profil { get => _profil; set { _profil = value; Invalidate(); } }
    public Image? Resim { get => _resim; set { _resim = value; Invalidate(); } }
    public bool Cerceve { get; set; }

    // "Kopyala" bağlantısı: kullanıcı numarasını (#12) panoya koyar; kısa süre "Kopyalandı" yazar.
    private Rectangle _kopyaAlani;
    private bool _kopyaUzerinde, _kopyalandi;
    private System.Windows.Forms.Timer? _kopyaZamanlayici;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool u = _profil != null && _kopyaAlani.Contains(e.Location);
        if (u == _kopyaUzerinde) return;
        _kopyaUzerinde = u;
        Cursor = u ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_kopyaUzerinde) return;
        _kopyaUzerinde = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _profil is null || !_kopyaAlani.Contains(e.Location)) return;
        try { Clipboard.SetText("#" + _profil.Id); } catch { return; }
        _kopyalandi = true;
        Invalidate();
        _kopyaZamanlayici ??= new System.Windows.Forms.Timer { Interval = 1600 };
        _kopyaZamanlayici.Tick -= KopyaBitti;
        _kopyaZamanlayici.Tick += KopyaBitti;
        _kopyaZamanlayici.Stop();
        _kopyaZamanlayici.Start();
    }

    private void KopyaBitti(object? s, EventArgs e)
    {
        _kopyaZamanlayici?.Stop();
        _kopyalandi = false;
        if (!IsDisposed) Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _kopyaZamanlayici?.Dispose();
        base.Dispose(disposing);
    }

    private int BantH => Tema.S(84);
    private int AvatarCap => Tema.S(84);
    private int Ic => Tema.S(18);

    public int GerekenYukseklik(int genislik)
    {
        if (_profil is null) return Tema.S(300);
        int y = BantH + AvatarCap / 2 + Tema.S(14);
        y += Tema.S(12) + Tema.S(28) + Tema.S(22) + Tema.S(6);
        if (!string.IsNullOrWhiteSpace(_profil.DurumMesaji)) y += Tema.S(24);
        y += Tema.S(18) + Tema.S(22);
        var hakkimda = string.IsNullOrWhiteSpace(_profil.Hakkimda) ? "Henüz bir şey yazmamış." : _profil.Hakkimda;
        y += TextRenderer.MeasureText(hakkimda, Tema.Yazi(10), new Size(genislik - Ic * 4, 0), TextFormatFlags.WordBreak).Height;
        return y + Ic * 2;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        if (_profil is null) return;
        var p = _profil;

        var kart = new Rectangle(0, 0, Width - 1, Height - 1);
        Cizim.YuvarlakDoldur(g, kart, Tema.S(12), Tema.Yuzey);

        // Bant: üst köşeleri yuvarlak, alt düz.
        var eski = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(0, 0, kart.Width, BantH + Tema.S(14)), Tema.S(12)))
        using (var b = new SolidBrush(ColorTranslator.FromHtml(p.ProfilRengi)))
        {
            g.SetClip(new Rectangle(0, 0, Width, BantH));
            g.FillPath(b, yol);
            g.ResetClip();
        }

        // Avatar, kart rengiyle bir halka içinde banda taşar.
        int halka = Tema.S(6);
        var avDis = new RectangleF(Ic - halka, BantH - AvatarCap / 2f - halka, AvatarCap + halka * 2, AvatarCap + halka * 2);
        using (var b = new SolidBrush(Tema.Yuzey)) g.FillEllipse(b, avDis);
        g.SmoothingMode = eski;
        Cizim.Avatar(g, new RectangleF(Ic, BantH - AvatarCap / 2f, AvatarCap, AvatarCap), p, _resim, Tema.Yuzey);

        // Bilgi kutusu
        int y = BantH + AvatarCap / 2 + Tema.S(14);
        var bilgi = new Rectangle(Ic, y, kart.Width - Ic * 2, kart.Height - y - Ic);
        Cizim.YuvarlakDoldur(g, bilgi, Tema.S(8), Tema.Arkaplan);

        int x = bilgi.X + Ic, w = bilgi.Width - Ic * 2;
        y = bilgi.Y + Tema.S(12);
        TextRenderer.DrawText(g, p.GorunenAd, Tema.Yazi(14, kalin: true), new Rectangle(x, y, w, Tema.S(28)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        y += Tema.S(28);
        // @kullaniciadi · #id  ve sağda küçük "Kopyala" (arkadaş eklerken #numara da kabul edilir).
        _kopyaAlani = new Rectangle(x + w - Tema.S(84), y - Tema.S(1), Tema.S(84), Tema.S(22));
        TextRenderer.DrawText(g, "@" + p.KullaniciAdi + "  ·  #" + p.Id, Tema.Yazi(10), new Rectangle(x, y, w - _kopyaAlani.Width - Tema.S(6), Tema.S(20)), Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        if (_kopyaUzerinde || _kopyalandi) Cizim.YuvarlakDoldur(g, _kopyaAlani, Tema.S(6), Tema.YuzeyUst);
        var kopyaRenk = _kopyalandi ? Tema.Basari : _kopyaUzerinde ? Tema.Metin : Tema.MetinIkincil;
        TextRenderer.DrawText(g, _kopyalandi ? Glif.Onay : Glif.Kopyala, Cizim.Ikon(9), new Rectangle(_kopyaAlani.X + Tema.S(8), _kopyaAlani.Y, Tema.S(18), _kopyaAlani.Height), kopyaRenk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _kopyalandi ? "Kopyalandı" : "Kopyala", Tema.Yazi(9, kalin: true), new Rectangle(_kopyaAlani.X + Tema.S(28), _kopyaAlani.Y, _kopyaAlani.Width - Tema.S(30), _kopyaAlani.Height), kopyaRenk,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        y += Tema.S(22);
        if (!string.IsNullOrWhiteSpace(p.DurumMesaji))
        {
            TextRenderer.DrawText(g, p.DurumMesaji, Tema.Yazi(10), new Rectangle(x, y, w, Tema.S(20)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            y += Tema.S(24);
        }

        y += Tema.S(8);
        using (var pen = new Pen(Tema.Kenar)) g.DrawLine(pen, x, y, x + w, y);
        y += Tema.S(10);

        TextRenderer.DrawText(g, "HAKKIMDA", Tema.Yazi(8.5f, kalin: true), new Point(x, y), Tema.MetinIkincil, TextFormatFlags.NoPadding);
        y += Tema.S(22);
        bool bos = string.IsNullOrWhiteSpace(p.Hakkimda);
        TextRenderer.DrawText(g, bos ? "Henüz bir şey yazmamış." : p.Hakkimda, Tema.Yazi(10),
            new Rectangle(x, y, w, bilgi.Bottom - y - Tema.S(8)), bos ? Tema.MetinSoluk : Tema.Metin,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        if (Cerceve)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var yol = Tema.YuvarlakDikdortgen(kart, Tema.S(12));
            using var pen = new Pen(Tema.Kenar);
            g.DrawPath(pen, yol);
        }
    }
}

// Liste boşken ortada gösterilen ikon + yazı.
public class BosDurum : Control
{
    public BosDurum()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
    }

    public string Glif { get; set; } = Uygulama.Glif.Insanlar;
    public string Baslik { get; set; } = "";
    public string Aciklama { get; set; } = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int daire = Tema.S(96);
        int toplam = daire + Tema.S(20) + Tema.S(26) + Tema.S(44);
        int y = Math.Max(0, (Height - toplam) / 2 - Tema.S(20));
        var d = new Rectangle((Width - daire) / 2, y, daire, daire);
        var eski = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Tema.Yuzey)) g.FillEllipse(b, d);
        g.SmoothingMode = eski;
        TextRenderer.DrawText(g, Glif, Cizim.Ikon(26), d, Tema.MetinSoluk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        y = d.Bottom + Tema.S(20);
        TextRenderer.DrawText(g, Baslik, Tema.Yazi(12, kalin: true), new Rectangle(0, y, Width, Tema.S(26)), Tema.Metin, TextFormatFlags.HorizontalCenter);
        y += Tema.S(28);
        TextRenderer.DrawText(g, Aciklama, Tema.Yazi(10), new Rectangle(Tema.S(40), y, Width - Tema.S(80), Tema.S(44)), Tema.MetinIkincil,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
    }
}
