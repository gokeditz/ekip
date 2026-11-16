using Ekip.Ortak;

namespace Ekip.Uygulama;

// Sol kenar çubuğu: logo, "Arkadaşlar", direkt mesaj listesi, en altta kendi profilin.
public class KenarCubugu : Control
{
    private Profil? _ben;
    private int _bekleyen;
    private List<SohbetOzeti> _sohbetler = [];
    private long? _secili;
    private string? _uzerinde; // "arkadaslar", "kullanici", "ayar", "dm:<index>"
    private int _dmKaydirma;
    private readonly KoyuIpucu _ipucu = new();

    public AvatarOnbellek? Avatarlar { get; set; }
    public Profil? Ben { get => _ben; set { _ben = value; Invalidate(); } }
    public int Bekleyen { get => _bekleyen; set { if (_bekleyen != value) { _bekleyen = value; Invalidate(); } } }
    public List<SohbetOzeti> Sohbetler { get => _sohbetler; set { _sohbetler = value; _dmKaydirma = Math.Clamp(_dmKaydirma, 0, MaksKaydirma); Invalidate(); } }
    public long? Secili { get => _secili; set { _secili = value; Invalidate(); } }
    private PartiBilgisi? _parti;
    public PartiBilgisi? Parti { get => _parti; set { _parti = value; Invalidate(); } }

    private bool _ayarlarAcik;
    public bool AyarlarAcik { get => _ayarlarAcik; set { _ayarlarAcik = value; Invalidate(); } }
    private string _overlayTusu = "Ctrl+Tab";
    public string OverlayTusu { get => _overlayTusu; set { _overlayTusu = value; Invalidate(); } }

    public event Action? PartiKurTik;
    public event Action? PartidenAyrilTik;
    public event Action<Rectangle>? DavetTik;
    public event Action<Rectangle>? KullaniciTik;
    public event Action? AyarTik;
    public event Action? ArkadaslarTik;
    public event Action<long>? SohbetTik;

    private static readonly Color AltZemin = ColorTranslator.FromHtml("#17181C");
    private static readonly Color HoverZemin = ColorTranslator.FromHtml("#232429");

    public KenarCubugu()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Yuzey;
    }

    private int AltH => Tema.S(64);
    private Rectangle ArkadaslarAlani => new(Tema.S(10), Tema.S(72), Width - Tema.S(20), Tema.S(42));
    private Rectangle YonetimAlani => new(Tema.S(10), Tema.S(118), Width - Tema.S(20), Tema.S(42));
    private int Ek => _admin ? Tema.S(46) : 0; // Yönetim satırı varsa aşağısı kayar.
    private Rectangle DmBolumu => new(0, Tema.S(160) + Ek, Width, Height - AltH - PartiH - Tema.S(160) - Ek - Tema.S(4));

    private bool _admin, _yonetimAcik;
    public bool Admin { get => _admin; set { _admin = value; Invalidate(); } }
    public bool YonetimAcik { get => _yonetimAcik; set { _yonetimAcik = value; Invalidate(); } }
    public event Action? YonetimTik;
    private int PartiH => _parti is null ? Tema.S(60) : Tema.S(124);
    private Rectangle PartiAlani => new(Tema.S(10), Height - AltH - PartiH, Width - Tema.S(20), PartiH - Tema.S(10));
    private Rectangle AyrilAlani => new(PartiAlani.Right - Tema.S(60), PartiAlani.Y + Tema.S(6), Tema.S(54), Tema.S(26));
    private Rectangle OyunAlani => new(PartiAlani.X + Tema.S(6), PartiAlani.Y + Tema.S(34), PartiAlani.Width - Tema.S(12), Tema.S(26));
    private Rectangle DavetAlani
    {
        get
        {
            int n = Math.Min(_parti?.Uyeler.Count ?? 0, 6);
            return new Rectangle(PartiAlani.X + Tema.S(12) + n * Tema.S(32), PartiAlani.Y + Tema.S(68), Tema.S(28), Tema.S(28));
        }
    }
    private int DmH => Tema.S(46);
    private int MaksKaydirma => Math.Max(0, _sohbetler.Count * DmH - DmBolumu.Height);
    private Rectangle DmAlani(int i) => new(Tema.S(10), DmBolumu.Y + i * DmH - _dmKaydirma, Width - Tema.S(20), DmH - Tema.S(2));
    private Rectangle KullaniciAlani => new(Tema.S(8), Height - AltH + Tema.S(8), Width - Tema.S(8) - Tema.S(52), AltH - Tema.S(16));
    private Rectangle AyarAlani => new(Width - Tema.S(48), Height - AltH + (AltH - Tema.S(36)) / 2, Tema.S(36), Tema.S(36));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // Logo
        var logo = new Rectangle(Tema.S(18), Tema.S(16), Tema.S(28), Tema.S(28));
        Tema.Logo(g, logo);
        TextRenderer.DrawText(g, "ekip", Tema.Yazi(12.5f, kalin: true), new Rectangle(logo.Right + Tema.S(10), logo.Y, Tema.S(120), logo.Height), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, 0, Tema.S(60), Width, Tema.S(60));

        // Arkadaşlar
        var oge = ArkadaslarAlani;
        bool arkadaslarSecili = _secili is null && !_ayarlarAcik && !_yonetimAcik;
        if (arkadaslarSecili) Cizim.YuvarlakDoldur(g, oge, Tema.S(6), Tema.YuzeyUst);
        else if (_uzerinde == "arkadaslar") Cizim.YuvarlakDoldur(g, oge, Tema.S(6), HoverZemin);
        var renk = arkadaslarSecili || _uzerinde == "arkadaslar" ? Tema.Metin : Tema.MetinIkincil;
        TextRenderer.DrawText(g, Glif.Insanlar, Cizim.Ikon(12), new Rectangle(oge.X + Tema.S(12), oge.Y, Tema.S(24), oge.Height), renk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "Arkadaşlar", Tema.Yazi(10.5f, kalin: true), new Rectangle(oge.X + Tema.S(46), oge.Y, oge.Width, oge.Height), renk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (_bekleyen > 0) Cizim.Rozet(g, new Point(oge.Right - Tema.S(10), oge.Y + oge.Height / 2), _bekleyen);

        // Yönetim (sadece adminlere görünür)
        if (_admin)
        {
            var y = YonetimAlani;
            if (_yonetimAcik) Cizim.YuvarlakDoldur(g, y, Tema.S(6), Tema.YuzeyUst);
            else if (_uzerinde == "yonetim") Cizim.YuvarlakDoldur(g, y, Tema.S(6), HoverZemin);
            var yr = _yonetimAcik || _uzerinde == "yonetim" ? Tema.Metin : Tema.MetinIkincil;
            TextRenderer.DrawText(g, "", Cizim.Ikon(12), new Rectangle(y.X + Tema.S(12), y.Y, Tema.S(24), y.Height), yr, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Yönetim", Tema.Yazi(10.5f, kalin: true), new Rectangle(y.X + Tema.S(46), y.Y, y.Width, y.Height), yr, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Direkt mesajlar
        TextRenderer.DrawText(g, "DİREKT MESAJLAR", Tema.Yazi(8.5f, kalin: true), new Point(Tema.S(20), Tema.S(134) + Ek), Tema.MetinSoluk, TextFormatFlags.NoPadding);
        var bolum = DmBolumu;
        if (_sohbetler.Count == 0)
        {
            TextRenderer.DrawText(g, "Bir arkadaşına mesaj at, sohbetiniz burada görünsün.", Tema.Yazi(9),
                new Rectangle(Tema.S(20), bolum.Y + Tema.S(4), Width - Tema.S(40), Tema.S(40)), Tema.MetinSoluk, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
        g.SetClip(bolum);
        for (int i = 0; i < _sohbetler.Count; i++)
        {
            var r = DmAlani(i);
            if (r.Bottom < bolum.Top || r.Top > bolum.Bottom) continue;
            var s = _sohbetler[i];
            bool secili = s.Karsi.Id == _secili, uzerinde = _uzerinde == "dm:" + i;
            var zemin = BackColor;
            if (secili) { Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst); zemin = Tema.YuzeyUst; }
            else if (uzerinde) { Cizim.YuvarlakDoldur(g, r, Tema.S(6), HoverZemin); zemin = HoverZemin; }

            int av = Tema.S(32);
            Cizim.Avatar(g, new RectangleF(r.X + Tema.S(8), r.Y + (r.Height - av) / 2f, av, av), s.Karsi, Avatarlar?.Al(s.Karsi), zemin);

            bool okunmamis = s.Okunmamis > 0;
            int tx = r.X + Tema.S(50), tw = r.Right - tx - (okunmamis ? Tema.S(36) : Tema.S(8));
            var adRenk = secili || okunmamis || uzerinde ? Tema.Metin : Tema.MetinIkincil;
            var alt = string.IsNullOrWhiteSpace(s.Karsi.DurumMesaji) || s.Karsi.Durum == Durum.Cevrimdisi ? null : s.Karsi.DurumMesaji;
            if (alt is null)
                TextRenderer.DrawText(g, s.Karsi.GorunenAd, Tema.Yazi(10, kalin: okunmamis), new Rectangle(tx, r.Y, tw, r.Height), adRenk,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            else
            {
                TextRenderer.DrawText(g, s.Karsi.GorunenAd, Tema.Yazi(10, kalin: okunmamis), new Rectangle(tx, r.Y + Tema.S(5), tw, Tema.S(18)), adRenk, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, alt, Tema.Yazi(8.5f), new Rectangle(tx, r.Y + Tema.S(24), tw, Tema.S(16)), Tema.MetinSoluk, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            if (okunmamis) Cizim.Rozet(g, new Point(r.Right - Tema.S(8), r.Y + r.Height / 2), s.Okunmamis);
        }
        g.ResetClip();

        PartiCiz(g);

        // Alt: kendi profilim
        var altR = new Rectangle(0, Height - AltH, Width, AltH);
        using (var b = new SolidBrush(AltZemin)) g.FillRectangle(b, altR);
        if (_ben is null) return;

        var ku = KullaniciAlani;
        var kz = AltZemin;
        if (_uzerinde == "kullanici") { Cizim.YuvarlakDoldur(g, ku, Tema.S(6), Tema.YuzeyUst); kz = Tema.YuzeyUst; }
        int a = Tema.S(36);
        var avR = new RectangleF(ku.X + Tema.S(6), ku.Y + (ku.Height - a) / 2f, a, a);
        Cizim.Avatar(g, avR, _ben, Avatarlar?.Al(_ben), kz);

        int kx = (int)avR.Right + Tema.S(10), kw = ku.Right - kx - Tema.S(4);
        TextRenderer.DrawText(g, _ben.GorunenAd, Tema.Yazi(10, kalin: true), new Rectangle(kx, ku.Y + Tema.S(5), kw, Tema.S(20)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var altYazi = string.IsNullOrWhiteSpace(_ben.DurumMesaji) ? Cizim.DurumAdi(_ben.Durum) : _ben.DurumMesaji;
        TextRenderer.DrawText(g, altYazi, Tema.Yazi(9), new Rectangle(kx, ku.Y + Tema.S(26), kw, Tema.S(18)), Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        var ay = AyarAlani;
        if (_uzerinde == "ayar" || _ayarlarAcik) Cizim.YuvarlakDoldur(g, ay, Tema.S(6), Tema.YuzeyUst);
        TextRenderer.DrawText(g, Glif.Ayarlar, Cizim.Ikon(12), ay, _uzerinde == "ayar" || _ayarlarAcik ? Tema.Metin : Tema.MetinIkincil,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void PartiCiz(Graphics g)
    {
        var r = PartiAlani;
        if (_parti is null)
        {
            Cizim.YuvarlakDoldur(g, r, Tema.S(8), _uzerinde == "partikur" ? Tema.Kenar : Tema.YuzeyUst);
            TextRenderer.DrawText(g, Glif.Oyun, Cizim.Ikon(14), new Rectangle(r.X + Tema.S(12), r.Y, Tema.S(28), r.Height), Tema.Vurgu, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Parti kur", Tema.Yazi(10, kalin: true), new Point(r.X + Tema.S(48), r.Y + Tema.S(7)), Tema.Metin, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Arkadaşlarınla oyunda sohbet et", Tema.Yazi(8.5f), new Rectangle(r.X + Tema.S(48), r.Y + Tema.S(27), r.Width - Tema.S(52), Tema.S(16)), Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            return;
        }

        Cizim.YuvarlakDoldur(g, r, Tema.S(8), Tema.YuzeyUst);
        TextRenderer.DrawText(g, Glif.Oyun, Cizim.Ikon(11), new Point(r.X + Tema.S(12), r.Y + Tema.S(12)), Tema.Vurgu, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, $"Parti · {_parti.Uyeler.Count} kişi", Tema.Yazi(9.5f, kalin: true), new Point(r.X + Tema.S(36), r.Y + Tema.S(11)), Tema.Metin, TextFormatFlags.NoPadding);

        var ay = AyrilAlani;
        if (_uzerinde == "ayril") Cizim.YuvarlakDoldur(g, ay, Tema.S(4), Color.FromArgb(40, Tema.Hata));
        TextRenderer.DrawText(g, "Ayrıl", Tema.Yazi(9, kalin: true), ay, Tema.Hata, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var oy = OyunAlani;
        TextRenderer.DrawText(g, $"Oyundayken {OverlayTusu} ile overlay", Tema.Yazi(8.5f), new Rectangle(oy.X + Tema.S(6), oy.Y, oy.Width - Tema.S(12), oy.Height),
            Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        int n = Math.Min(_parti.Uyeler.Count, 6);
        for (int i = 0; i < n; i++)
        {
            var u = _parti.Uyeler[i];
            Cizim.Avatar(g, new RectangleF(r.X + Tema.S(12) + i * Tema.S(32), r.Y + Tema.S(68), Tema.S(28), Tema.S(28)), u, Avatarlar?.Al(u), Tema.YuzeyUst, false);
        }
        if (_parti.Uyeler.Count < Kurallar.PartiUyeMax)
        {
            var d = DavetAlani;
            var eski = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(_uzerinde == "davet" ? Tema.Vurgu : Tema.Kenar)) g.FillEllipse(b, d);
            g.SmoothingMode = eski;
            TextRenderer.DrawText(g, Glif.Arti, Cizim.Ikon(9), d, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private string? Bul(Point p)
    {
        if (_parti is null && PartiAlani.Contains(p)) return "partikur";
        if (_parti != null)
        {
            if (AyrilAlani.Contains(p)) return "ayril";
            if (_parti.Uyeler.Count < Kurallar.PartiUyeMax && DavetAlani.Contains(p)) return "davet";
        }
        if (KullaniciAlani.Contains(p)) return "kullanici";
        if (AyarAlani.Contains(p)) return "ayar";
        if (ArkadaslarAlani.Contains(p)) return "arkadaslar";
        if (_admin && YonetimAlani.Contains(p)) return "yonetim";
        if (DmBolumu.Contains(p))
            for (int i = 0; i < _sohbetler.Count; i++)
                if (DmAlani(i).Contains(p)) return "dm:" + i;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var yeni = Bul(e.Location);
        if (yeni == _uzerinde) return;
        _uzerinde = yeni;
        Cursor = yeni != null ? Cursors.Hand : Cursors.Default;
        _ipucu.Hide(this);
        if (yeni == "ayar") _ipucu.Show("Ayarlar", this, AyarAlani.X - Tema.S(30), AyarAlani.Y - Tema.S(34), 2500);
        if (yeni == "davet") _ipucu.Show("Arkadaş davet et", this, DavetAlani.X - Tema.S(30), DavetAlani.Y - Tema.S(34), 2500);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _uzerinde = null;
        _ipucu.Hide(this);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!DmBolumu.Contains(e.Location)) return;
        _dmKaydirma = Math.Clamp(_dmKaydirma - e.Delta * DmH / 120, 0, MaksKaydirma);
        _uzerinde = Bul(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var b = Bul(e.Location);
        if (b == "kullanici") KullaniciTik?.Invoke(RectangleToScreen(KullaniciAlani));
        else if (b == "ayar") AyarTik?.Invoke();
        else if (b == "arkadaslar") ArkadaslarTik?.Invoke();
        else if (b == "yonetim") YonetimTik?.Invoke();
        else if (b == "partikur") PartiKurTik?.Invoke();
        else if (b == "ayril") PartidenAyrilTik?.Invoke();
        else if (b == "davet") DavetTik?.Invoke(RectangleToScreen(DavetAlani));
        else if (b?.StartsWith("dm:") == true && int.TryParse(b[3..], out var i) && i < _sohbetler.Count) SohbetTik?.Invoke(_sohbetler[i].Karsi.Id);
    }
}
