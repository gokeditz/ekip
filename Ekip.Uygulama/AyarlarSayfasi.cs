namespace Ekip.Uygulama;

// Ana pencerenin içindeki Ayarlar sayfası: üstte sekmeler (Profil, Kısayollar), altında seçili sekmenin içeriği.
public class AyarlarSayfasi : Control
{
    public enum Sekme { Profil, Kisayollar, Hakkinda }

    private Sekme _secili = Sekme.Profil;
    private Sekme? _uzerinde;
    private readonly List<(Sekme s, Rectangle r)> _alanlar = [];
    private readonly Control _profil;
    private readonly KisayolPaneli _kisayollar;
    private readonly HakkindaPaneli _hakkinda = new();

    public KisayolPaneli Kisayollar => _kisayollar;

    public AyarlarSayfasi(Control profilDuzenleyici, KisayolPaneli kisayollar)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
        _profil = profilDuzenleyici;
        _kisayollar = kisayollar;
        Controls.AddRange([_profil, _kisayollar, _hakkinda]);
        Sec(Sekme.Profil);
    }

    public void Sec(Sekme s)
    {
        _secili = s;
        _profil.Visible = s == Sekme.Profil;
        _kisayollar.Visible = s == Sekme.Kisayollar;
        _hakkinda.Visible = s == Sekme.Hakkinda;
        Invalidate();
    }

    private int UstH => Tema.S(56);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_profil is null) return;
        _profil.Bounds = _kisayollar.Bounds = _hakkinda.Bounds = new Rectangle(0, UstH, Width, Height - UstH);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int orta = UstH / 2, x = Tema.S(22);
        TextRenderer.DrawText(g, Glif.Ayarlar, Cizim.Ikon(13), new Rectangle(x, 0, Tema.S(24), UstH), Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += Tema.S(32);
        var bf = Tema.Yazi(11.5f, kalin: true);
        int bw = TextRenderer.MeasureText(g, "Ayarlar", bf, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, "Ayarlar", bf, new Rectangle(x, 0, bw + 4, UstH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += bw + Tema.S(18);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, orta - Tema.S(12), x, orta + Tema.S(12));
        x += Tema.S(16);

        _alanlar.Clear();
        var f = Tema.Yazi(10, kalin: true);
        foreach (var (s, ad) in new[] { (Sekme.Profil, "Profil"), (Sekme.Kisayollar, "Kısayollar"), (Sekme.Hakkinda, "Hakkında") })
        {
            int w = TextRenderer.MeasureText(g, ad, f, Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(24);
            var r = new Rectangle(x, orta - Tema.S(16), w, Tema.S(32));
            _alanlar.Add((s, r));
            if (s == _secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
            else if (s == _uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.Yuzey);
            TextRenderer.DrawText(g, ad, f, r, s == _secili || s == _uzerinde ? Tema.Metin : Tema.MetinIkincil,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x = r.Right + Tema.S(8);
        }
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, UstH - 1, Width, UstH - 1);
    }

    private Sekme? Bul(Point p) => _alanlar.Where(a => a.r.Contains(p)).Select(a => (Sekme?)a.s).FirstOrDefault();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var b = Bul(e.Location);
        if (b == _uzerinde) return;
        _uzerinde = b;
        Cursor = b != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = null; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (Bul(e.Location) is { } s) Sec(s); }
}

// Ayarlar > Hakkında: sürüm ve kullanılan açık kaynak içeriklerin atıfları.
public class HakkindaPaneli : Control
{
    public HakkindaPaneli()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int x = Tema.S(32), y = Tema.S(26), w = Math.Min(Tema.S(760), Width - x * 2);
        TextRenderer.DrawText(g, "Hakkında", Tema.Yazi(16, kalin: true), new Point(x - Tema.S(5), y), Tema.Metin, TextFormatFlags.NoPadding);
        y += Tema.S(36);
        TextRenderer.DrawText(g, "Ekip'i oyun oynarken arkadaşlarınla yazışmak için yaptık: hafif, hızlı, sade.", Tema.Yazi(10), new Rectangle(x - Tema.S(3), y, w, Tema.S(22)), Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        y += Tema.S(34);

        void Satir(string baslik, string metin)
        {
            using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, y, x + w, y);
            TextRenderer.DrawText(g, baslik, Tema.Yazi(10.5f, kalin: true), new Point(x, y + Tema.S(14)), Tema.Metin, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, metin, Tema.Yazi(9.5f), new Rectangle(x, y + Tema.S(38), w, Tema.S(40)), Tema.MetinIkincil, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            y += Tema.S(78);
        }
        Satir("Sürüm", Tema.UygulamaAdi + " " + Guncelleyici.MevcutSurumYazisi);
        Satir("Emoji", "Emoji: Twemoji © Twitter / jdecked, CC-BY 4.0");
    }
}

// Ayarlar > Kısayollar.
public class KisayolPaneli : Panel
{
    private readonly Label _baslik = new() { AutoSize = true, Text = "Kısayollar", Font = Tema.Yazi(16, kalin: true), ForeColor = Tema.Metin };
    private readonly Label _aciklama = new() { AutoSize = true, Text = "Kutuya tıkla, sonra kullanmak istediğin tuşa (ya da Ctrl/Alt/Shift + tuş) bas.", Font = Tema.Yazi(10), ForeColor = Tema.MetinIkincil };
    private readonly KisayolKutusu _overlay = new();
    private readonly KisayolKutusu _mini = new();
    private readonly Label _durum = new() { AutoSize = false, Font = Tema.Yazi(9.5f, kalin: true), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dugme _miniSifirla = new() { Text = "Yerini sıfırla", Tur = Dugme.Turler.Ikincil };
    private readonly BaglantiYazi _overlayVarsayilan = new() { Text = "Varsayılan" };
    private readonly BaglantiYazi _miniVarsayilan = new() { Text = "Varsayılan" };
    private readonly List<(string baslik, string aciklama, Control kontrol, Control? ek)> _satirlar;

    public event Action<Kisayol, Kisayol>? Degisti;   // (overlay, mini)
    public event Action<bool>? YakalamaDurumu;
    public event Action? MiniyiSifirla;

    public KisayolPaneli()
    {
        BackColor = Tema.Arkaplan;
        DoubleBuffered = true;
        _satirlar =
        [
            ("Overlay'i aç / kapat", "Oyundayken arkadaşlar, parti ve sohbetler tam ekran açılır. Sadece tam ekran bir oyun ya da 31Hane öndeyken çalışır.", _overlay, _overlayVarsayilan),
            ("Mini sohbete yaz", "Oyuna sabitlediğin mini sohbette yazma modunu açar. Bu sırada oyun klavye ve fareyi almaz; mini sohbeti taşıyıp boyutlandırabilir, tekerlekle kaydırabilirsin. Enter gönderir, Esc oyuna döner.", _mini, _miniVarsayilan),
            ("Mini sohbetin yeri ve boyutu", "Mini sohbetin yerini ve boyutunu yazma modunda fareyle ayarlarsın, hatırlanır. Ekran dışında kaldıysa buradan sıfırla.", _miniSifirla, null),
        ];
        Controls.AddRange([_baslik, _aciklama, _overlay, _mini, _durum, _miniSifirla, _overlayVarsayilan, _miniVarsayilan]);

        _overlay.Degisti += _ => Bildir();
        _mini.Degisti += _ => Bildir();
        _overlay.YakalamaDurumu += b => YakalamaDurumu?.Invoke(b);
        _mini.YakalamaDurumu += b => YakalamaDurumu?.Invoke(b);
        _overlayVarsayilan.Click += (_, _) => { _overlay.Deger = Kisayol.VarsayilanOverlay; Bildir(); };
        _miniVarsayilan.Click += (_, _) => { _mini.Deger = Kisayol.VarsayilanMini; Bildir(); };
        _miniSifirla.Click += (_, _) => { MiniyiSifirla?.Invoke(); Durum("Mini sohbetin yeri sıfırlandı.", hata: false); };
    }

    public void Yukle(Kisayol overlay, Kisayol mini)
    {
        _overlay.Deger = overlay;
        _mini.Deger = mini;
        _durum.Text = "";
    }

    private void Bildir()
    {
        if (_overlay.Deger == _mini.Deger) { Durum("İki kısayol aynı olamaz.", hata: true); return; }
        Degisti?.Invoke(_overlay.Deger, _mini.Deger);
        Durum("Kaydedildi.", hata: false);
    }

    public void Durum(string mesaj, bool hata)
    {
        _durum.Text = mesaj;
        _durum.ForeColor = hata ? Tema.Hata : Tema.Basari;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_satirlar is null) return;
        int x = Tema.S(32), y = Tema.S(26), w = Math.Min(Tema.S(760), Width - x * 2);
        _baslik.Location = new Point(x - Tema.S(5), y); y = _baslik.Bottom;
        _aciklama.Location = new Point(x - Tema.S(3), y); y = _aciklama.Bottom + Tema.S(22);
        _yerler.Clear();
        foreach (var (baslik, aciklama, kontrol, ek) in _satirlar)
        {
            int kutuW = Tema.S(220), metinW = w - kutuW - Tema.S(110);
            int aciklamaH = TextRenderer.MeasureText(aciklama, Tema.Yazi(9.5f), new Size(metinW, 0), TextFormatFlags.WordBreak).Height;
            int satirH = Math.Max(Tema.S(24) + aciklamaH, Tema.S(44)) + Tema.S(28);
            _yerler.Add((baslik, aciklama, new Rectangle(x, y, metinW, satirH)));
            kontrol.Bounds = new Rectangle(x + metinW + Tema.S(20), y + Tema.S(14), kutuW, Tema.S(40));
            if (ek != null) ek.Location = new Point(kontrol.Right + Tema.S(12), kontrol.Top + (kontrol.Height - ek.Height) / 2);
            y += satirH;
        }
        _durum.Bounds = new Rectangle(x, y + Tema.S(6), w, Tema.S(28));
        Invalidate();
    }

    private readonly List<(string baslik, string aciklama, Rectangle r)> _yerler = [];

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var p = new Pen(Tema.Kenar);
        foreach (var (baslik, aciklama, r) in _yerler)
        {
            g.DrawLine(p, r.X, r.Y, Math.Min(Width - Tema.S(32), r.X + Tema.S(760)), r.Y);
            TextRenderer.DrawText(g, baslik, Tema.Yazi(10.5f, kalin: true), new Point(r.X, r.Y + Tema.S(14)), Tema.Metin, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, aciklama, Tema.Yazi(9.5f), new Rectangle(r.X, r.Y + Tema.S(38), r.Width, r.Height - Tema.S(38)), Tema.MetinIkincil,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }
}
