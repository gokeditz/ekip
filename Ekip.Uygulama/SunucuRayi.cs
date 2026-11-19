using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// En soldaki dikey sunucu şeridi (~72px): Ana sayfa, sunucu simgeleri, "+" ve Keşfet.
// Hepsi tek kontrolde çizilir; sadece görünen simgeler çizilir. Seçim dışarıdan da verilebilir (Secili / KesfetSecili).
public class SunucuRayi : Control
{
    private List<SunucuOzeti> _sunucular = [];
    private long? _secili;
    private bool _kesfetSecili;
    private int _anaOkunmamis;
    private string? _uzerinde; // "ana", "kesfet", "ekle", "s:<id>"
    private readonly DikeyKaydirma _kaydir;

    private static readonly Color Zemin = ColorTranslator.FromHtml("#0F1013");

    private IkonOnbellek? _ikonlar;

    // İkonlar indikçe şerit kendi kendine yeniden çizilir.
    public IkonOnbellek? Ikonlar
    {
        get => _ikonlar;
        set
        {
            if (_ikonlar != null) _ikonlar.Yuklendi -= Tazele;
            _ikonlar = value;
            if (_ikonlar != null) _ikonlar.Yuklendi += Tazele;
        }
    }

    private void Tazele() { if (!IsDisposed && IsHandleCreated) Invalidate(); }

    public List<SunucuOzeti> Sunucular
    {
        get => _sunucular;
        set { _sunucular = value ?? []; Hesapla(); _uzerinde = null; IpucuyuGizle(); Invalidate(); }
    }

    // null = ana sayfa (ve Keşfet seçili değilse).
    public long? Secili { get => _secili; set { _secili = value; if (value != null) _kesfetSecili = false; Invalidate(); } }
    public bool KesfetSecili { get => _kesfetSecili; set { _kesfetSecili = value; if (value) _secili = null; Invalidate(); } }
    public int AnaSayfaOkunmamis { get => _anaOkunmamis; set { if (_anaOkunmamis != value) { _anaOkunmamis = value; Invalidate(); } } }

    public event Action<long>? SunucuSecildi;
    public event Action? AnaSayfaTik;
    public event Action? KesfetTik;
    public event Action? EkleTik;
    public event Action<long, Rectangle>? SunucuMenuTik; // sağ tık (ekran koordinatında simge alanı)

    public SunucuRayi()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Zemin;
        _kaydir = new DikeyKaydirma(this);
    }

    public static int GerekenGenislik => Tema.S(72);

    private int Cap => Tema.S(48);
    private int Adim => Tema.S(56);
    private int X => (Width - Cap) / 2;
    private Rectangle AnaAlani => new(X, Tema.S(12), Cap, Cap);
    private Rectangle KesfetAlani => new(X, Height - Tema.S(12) - Cap, Cap, Cap);
    private Rectangle EkleAlani => new(X, KesfetAlani.Y - Tema.S(8) - Cap, Cap, Cap);
    private int ListeUst => AnaAlani.Bottom + Tema.S(8) + Tema.S(2) + Tema.S(10);
    private int ListeAlt => EkleAlani.Y - Tema.S(14);
    private Rectangle ListeAlani => new(0, ListeUst, Width, Math.Max(0, ListeAlt - ListeUst));
    private Rectangle SunucuAlani(int i) => new(X, ListeUst + i * Adim - _kaydir.Konum, Cap, Cap);

    private void Hesapla()
    {
        _kaydir.Ayarla(Math.Max(0, _sunucular.Count * Adim - Tema.S(8)), ListeAlani.Height);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Hesapla(); }

    // ---- Çizim ----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Sunucular (kaydırılan bölge) önce çizilir; TextRenderer Graphics kırpmasına uymadığından taşan baş harfler/rozetler
        // sonra çizilen üst ve alt bölümlerin zemin dolgusuyla örtülür.
        var liste = ListeAlani;
        g.SetClip(liste);
        for (int i = 0; i < _sunucular.Count; i++)
        {
            var r = SunucuAlani(i);
            if (r.Bottom < liste.Top - Tema.S(4) || r.Top > liste.Bottom + Tema.S(4)) continue;
            var s = _sunucular[i];
            bool secili = !_kesfetSecili && _secili == s.Id, uz = _uzerinde == "s:" + s.Id;
            Pil(g, r, secili ? Cap * 3 / 4 : uz ? Cap * 3 / 8 : s.Okunmamis > 0 || s.Bahsedilme > 0 ? Cap / 6 : 0);
            IkonCizim.Sunucu(g, r, s.Ad, s.Renk, Ikonlar?.Sunucu(s.Id, s.IkonSurum), secili || uz ? Tema.S(16) : Cap / 2f);
            if (s.Bahsedilme > 0) Rozet(g, r, s.Bahsedilme);
        }
        g.ResetClip();
        using (var zb = new SolidBrush(Zemin))
        {
            g.FillRectangle(zb, 0, 0, Width, liste.Y);
            g.FillRectangle(zb, 0, liste.Bottom, Width, Height - liste.Bottom);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _kaydir.Ciz(g, new Rectangle(0, liste.Y, Width, liste.Height));

        // Ana sayfa
        var ana = AnaAlani;
        bool anaSecili = _secili is null && !_kesfetSecili;
        bool anaUz = _uzerinde == "ana";
        Pil(g, ana, anaSecili ? Cap * 3 / 4 : anaUz ? Cap * 3 / 8 : _anaOkunmamis > 0 ? Cap / 6 : 0);
        using (var yol = Sekil(ana, anaSecili || anaUz))
        using (var b = new SolidBrush(anaSecili || anaUz ? Tema.Vurgu : Tema.YuzeyUst))
            g.FillPath(b, yol);
        Tema.Logo(g, new RectangleF(ana.X + Tema.S(11), ana.Y + Tema.S(11), Cap - Tema.S(22), Cap - Tema.S(22)), zemin: false);
        if (_anaOkunmamis > 0) Rozet(g, ana, _anaOkunmamis);

        // Ayırıcı
        using (var p = new Pen(Tema.Kenar, Math.Max(1, Tema.S(2)))) g.DrawLine(p, Width / 2 - Tema.S(16), ana.Bottom + Tema.S(9), Width / 2 + Tema.S(16), ana.Bottom + Tema.S(9));

        // Alt: ayırıcı, "+" ve Keşfet
        using (var p = new Pen(Tema.Kenar, Math.Max(1, Tema.S(2)))) g.DrawLine(p, Width / 2 - Tema.S(16), EkleAlani.Y - Tema.S(8), Width / 2 + Tema.S(16), EkleAlani.Y - Tema.S(8));
        var ek = EkleAlani;
        bool ekUz = _uzerinde == "ekle";
        using (var yol = Sekil(ek, ekUz))
        using (var b = new SolidBrush(ekUz ? Tema.Basari : Tema.YuzeyUst))
            g.FillPath(b, yol);
        TextRenderer.DrawText(g, Glif.Arti, Cizim.Ikon(14), ek, ekUz ? Color.White : Tema.Basari, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var ke = KesfetAlani;
        bool keSec = _kesfetSecili, keUz = _uzerinde == "kesfet";
        Pil(g, ke, keSec ? Cap * 3 / 4 : keUz ? Cap * 3 / 8 : 0);
        using (var yol = Sekil(ke, keSec || keUz))
        using (var b = new SolidBrush(keSec || keUz ? Tema.Basari : Tema.YuzeyUst))
            g.FillPath(b, yol);
        Pusula(g, ke, keSec || keUz ? Color.White : Tema.Basari);
    }

    private GraphicsPath Sekil(Rectangle r, bool kare) => Tema.YuvarlakDikdortgen(r, kare ? Tema.S(16) : r.Width / 2f);

    // Sol kenardaki gösterge (seçili: uzun, üzerinde: orta, okunmamış: küçük). Sadece sağ yarısı görünür.
    private void Pil(Graphics g, Rectangle simge, int yukseklik)
    {
        if (yukseklik <= 0) return;
        var r = new RectangleF(-Tema.S(4), simge.Y + (simge.Height - yukseklik) / 2f, Tema.S(8), yukseklik);
        using var yol = Tema.YuvarlakDikdortgen(r, Tema.S(4));
        using var b = new SolidBrush(Tema.Metin);
        g.FillPath(b, yol);
    }

    // Simgenin sağ altında, rayın rengiyle çevrilmiş kırmızı sayı.
    private void Rozet(Graphics g, Rectangle simge, int sayi)
    {
        var yazi = sayi > 99 ? "99+" : sayi.ToString();
        var olcu = TextRenderer.MeasureText(yazi, Tema.Yazi(8, kalin: true), Size.Empty, TextFormatFlags.NoPadding);
        int h = Tema.S(18), w = Math.Max(h, olcu.Width + Tema.S(10));
        var sagOrta = new Point(simge.Right + Tema.S(4), simge.Bottom - Tema.S(5));
        var ring = new Rectangle(sagOrta.X - w - Tema.S(3), sagOrta.Y - h / 2 - Tema.S(3), w + Tema.S(6), h + Tema.S(6));
        Cizim.YuvarlakDoldur(g, ring, ring.Height / 2f, Zemin);
        Cizim.Rozet(g, sagOrta, sayi);
    }

    // Pusula: halka + çapraz iğne.
    internal static void Pusula(Graphics g, Rectangle r, Color renk, float olcek = 1f)
    {
        var merkez = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        float yc = Tema.S(11) * olcek;
        using (var p = new Pen(renk, Math.Max(2f, Tema.S(2))))
            g.DrawEllipse(p, merkez.X - yc, merkez.Y - yc, yc * 2, yc * 2);
        float u = Tema.S(6) * olcek, k = Tema.S(2) * olcek;
        var eski = g.Transform;
        g.TranslateTransform(merkez.X, merkez.Y);
        g.RotateTransform(45);
        using (var b = new SolidBrush(renk))
            g.FillPolygon(b, new PointF[] { new(0, -u - Tema.S(1)), new(k, 0), new(-k, 0) });
        using (var b = new SolidBrush(Color.FromArgb(120, renk)))
            g.FillPolygon(b, new PointF[] { new(0, u + Tema.S(1)), new(k, 0), new(-k, 0) });
        g.Transform = eski;
    }

    // ---- Fare ----

    private string? Bul(Point p)
    {
        if (AnaAlani.Contains(p)) return "ana";
        if (EkleAlani.Contains(p)) return "ekle";
        if (KesfetAlani.Contains(p)) return "kesfet";
        if (ListeAlani.Contains(p))
            for (int i = 0; i < _sunucular.Count; i++)
                if (SunucuAlani(i).Contains(p)) return "s:" + _sunucular[i].Id;
        return null;
    }

    private Rectangle AlanBul(string anahtar) => anahtar switch
    {
        "ana" => AnaAlani,
        "ekle" => EkleAlani,
        "kesfet" => KesfetAlani,
        _ => _sunucular.FindIndex(s => "s:" + s.Id == anahtar) is var i and >= 0 ? SunucuAlani(i) : Rectangle.Empty,
    };

    private string IpucuYazisi(string anahtar) => anahtar switch
    {
        "ana" => "Ana sayfa",
        "ekle" => "Sunucu ekle ya da katıl",
        "kesfet" => "Keşfet",
        _ => _sunucular.FirstOrDefault(s => "s:" + s.Id == anahtar)?.Ad ?? "",
    };

    private void IpucuyuGizle() => IpucuBalonu.Gizle(this);

    private void IpucuyuGoster()
    {
        IpucuyuGizle();
        if (_uzerinde is null) return;
        var a = AlanBul(_uzerinde);
        if (a.IsEmpty) return;
        IpucuBalonu.Goster(this, a, IpucuYazisi(_uzerinde));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var yeni = Bul(e.Location);
        if (yeni == _uzerinde) return;
        _uzerinde = yeni;
        Cursor = yeni != null ? Cursors.Hand : Cursors.Default;
        IpucuyuGoster();
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _uzerinde = null;
        IpucuyuGizle();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_kaydir.Tekerlek(e.Delta, Adim)) return;
        _uzerinde = Bul(e.Location);
        IpucuyuGoster();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var b = Bul(e.Location);
        if (b is null) return;
        if (e.Button == MouseButtons.Right)
        {
            if (b.StartsWith("s:") && long.TryParse(b[2..], out var sid)) SunucuMenuTik?.Invoke(sid, RectangleToScreen(AlanBul(b)));
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        IpucuyuGizle();
        switch (b)
        {
            case "ana": _secili = null; _kesfetSecili = false; Invalidate(); AnaSayfaTik?.Invoke(); break;
            case "kesfet": _secili = null; _kesfetSecili = true; Invalidate(); KesfetTik?.Invoke(); break;
            case "ekle": EkleTik?.Invoke(); break;
            default:
                if (long.TryParse(b[2..], out var id)) { _secili = id; _kesfetSecili = false; Invalidate(); SunucuSecildi?.Invoke(id); }
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { IpucuyuGizle(); Ikonlar = null; }
        base.Dispose(disposing);
    }
}
