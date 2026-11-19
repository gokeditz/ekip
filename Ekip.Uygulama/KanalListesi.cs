using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Sunucu açıkken kenar çubuğunun yerine geçen kanal listesi (KenarCubugu ile aynı genişlik ve aynı alt kullanıcı alanı).
public class KanalListesi : Control
{
    private Sunucu? _sunucu;
    private long? _seciliKanal;
    private Profil? _ben;
    private bool _ayarlarAcik, _menuAcik;
    private string? _uzerinde; // "baslik", "ekle", "kullanici", "ayar", "k:<id>"
    private List<Kanal> _kanallar = [];
    private readonly DikeyKaydirma _kaydir;

    private static readonly Color AltZemin = ColorTranslator.FromHtml("#17181C");
    private static readonly Color HoverZemin = ColorTranslator.FromHtml("#232429");

    private AvatarOnbellek? _avatarlar;

    // Avatarlar indikçe liste kendi kendine yeniden çizilir.
    public AvatarOnbellek? Avatarlar
    {
        get => _avatarlar;
        set
        {
            if (_avatarlar != null) _avatarlar.Yuklendi -= Tazele;
            _avatarlar = value;
            if (_avatarlar != null) _avatarlar.Yuklendi += Tazele;
        }
    }

    private void Tazele() { if (!IsDisposed && IsHandleCreated) Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Avatarlar = null;
        base.Dispose(disposing);
    }

    public Sunucu? Sunucu
    {
        get => _sunucu;
        set
        {
            _sunucu = value;
            _kanallar = value?.Kanallar.OrderBy(k => k.Sira).ThenBy(k => k.Id).ToList() ?? [];
            _uzerinde = null;
            Hesapla();
            Invalidate();
        }
    }

    public long? SeciliKanal { get => _seciliKanal; set { _seciliKanal = value; Invalidate(); } }
    public Profil? Ben { get => _ben; set { _ben = value; Invalidate(); } }
    public bool AyarlarAcik { get => _ayarlarAcik; set { _ayarlarAcik = value; Invalidate(); } }

    // Sunucu menüsü açıkken başlığın oku yukarı döner; ana form menüyü kapatınca false yapar.
    public bool MenuAcik { get => _menuAcik; set { _menuAcik = value; Invalidate(); } }

    public bool Yonetebilir => _sunucu is { Rolum: >= SunucuRolu.Yonetici };

    public event Action<Rectangle>? MenuTik;        // sunucu adına tıklandı (ekran koordinatında başlık alanı)
    public event Action<long>? KanalSecildi;
    public event Action? KanalEkleTik;
    public event Action<Kanal>? KanalDuzenleTik;    // sağ tık > Düzenle (yönetici+)
    public event Action<Kanal>? KanalSilTik;
    public event Action<Rectangle>? KullaniciTik;
    public event Action? AyarTik;

    public KanalListesi()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Yuzey;
        _kaydir = new DikeyKaydirma(this);
    }

    public static int GerekenGenislik => Tema.S(248);

    private int BaslikH => Tema.S(56);
    private int AltH => Tema.S(64);
    private int SatirH => Tema.S(34);
    private Rectangle BaslikAlani => new(0, 0, Width, BaslikH);
    private Rectangle BolumBasligi => new(Tema.S(18), BaslikH + Tema.S(12), Width - Tema.S(36), Tema.S(24));
    private Rectangle EkleAlani => new(Width - Tema.S(10) - Tema.S(26), BaslikH + Tema.S(11), Tema.S(26), Tema.S(26));
    private Rectangle ListeAlani => new(0, BaslikH + Tema.S(42), Width, Math.Max(0, Height - AltH - BaslikH - Tema.S(42)));
    private Rectangle SatirAlani(int i) => new(Tema.S(10), ListeAlani.Y + i * SatirH - _kaydir.Konum, Width - Tema.S(20) - (_kaydir.Var ? Tema.S(8) : 0), SatirH - Tema.S(2));
    private Rectangle KullaniciAlani => new(Tema.S(8), Height - AltH + Tema.S(8), Width - Tema.S(8) - Tema.S(52), AltH - Tema.S(16));
    private Rectangle AyarAlani => new(Width - Tema.S(48), Height - AltH + (AltH - Tema.S(36)) / 2, Tema.S(36), Tema.S(36));

    private void Hesapla() => _kaydir.Ayarla(_kanallar.Count * SatirH, ListeAlani.Height);
    protected override void OnResize(EventArgs e) { base.OnResize(e); Hesapla(); }

    // ---- Çizim ----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // Liste önce çizilir, başlık ve alt alan üstüne biner (kaydırınca taşan satırlar örtülsün).
        ListeCiz(g);

        // Üst: sunucu adı + ok
        var b = BaslikAlani;
        using (var br = new SolidBrush(BackColor)) g.FillRectangle(br, 0, 0, Width, BaslikH + Tema.S(42));
        if (_uzerinde == "baslik" || _menuAcik) using (var br = new SolidBrush(HoverZemin)) g.FillRectangle(br, 0, 0, Width, BaslikH - 1);
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, BaslikH - 1, Width, BaslikH - 1);
        string ad = _sunucu?.Ad ?? "";
        TextRenderer.DrawText(g, ad, Tema.Yazi(11.5f, kalin: true), new Rectangle(Tema.S(18), 0, Width - Tema.S(18) - Tema.S(48), BaslikH - 1), Tema.Metin,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
        Ok(g, new Point(Width - Tema.S(26), BaslikH / 2), _menuAcik, _uzerinde == "baslik" || _menuAcik ? Tema.Metin : Tema.MetinIkincil);

        // Bölüm başlığı
        var bb = BolumBasligi;
        TextRenderer.DrawText(g, "METİN KANALLARI", Tema.Yazi(8.5f, kalin: true), new Rectangle(bb.X + Tema.S(2), bb.Y, bb.Width - Tema.S(30), bb.Height), Tema.MetinSoluk,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        if (Yonetebilir)
        {
            var ek = EkleAlani;
            if (_uzerinde == "ekle") Cizim.YuvarlakDoldur(g, ek, Tema.S(6), Tema.YuzeyUst);
            TextRenderer.DrawText(g, Glif.Arti, Cizim.Ikon(9.5f), ek, _uzerinde == "ekle" ? Tema.Metin : Tema.MetinSoluk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        }

        AltCiz(g);
    }

    private void ListeCiz(Graphics g)
    {
        var liste = ListeAlani;
        g.SetClip(liste);
        if (_kanallar.Count == 0)
        {
            TextRenderer.DrawText(g, _sunucu is null ? "" : "Bu sunucuda henüz kanal yok.", Tema.Yazi(9),
                new Rectangle(Tema.S(20), liste.Y + Tema.S(4), Width - Tema.S(40), Tema.S(40)), Tema.MetinSoluk, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        }
        for (int i = 0; i < _kanallar.Count; i++)
        {
            var r = SatirAlani(i);
            if (r.Bottom < liste.Top || r.Top > liste.Bottom) continue;
            var k = _kanallar[i];
            bool secili = k.Id == _seciliKanal, uz = _uzerinde == "k:" + k.Id;
            bool okunmamis = k.Okunmamis > 0 || k.Bahsedilme > 0;
            if (secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
            else if (uz) Cizim.YuvarlakDoldur(g, r, Tema.S(6), HoverZemin);

            if (okunmamis && !secili)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var b = new SolidBrush(Tema.Metin);
                int d = Tema.S(6);
                g.FillEllipse(b, r.X - Tema.S(7), r.Y + (r.Height - d) / 2f, d, d);
            }

            var renk = secili || uz || okunmamis ? Tema.Metin : Tema.MetinIkincil;
            TextRenderer.DrawText(g, "#", Tema.Yazi(13, kalin: true), new Rectangle(r.X + Tema.S(10), r.Y, Tema.S(20), r.Height),
                secili || uz ? Tema.MetinIkincil : Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
            int sagBos = k.Bahsedilme > 0 ? Tema.S(40) : Tema.S(10);
            TextRenderer.DrawText(g, k.Ad, Tema.Yazi(10, kalin: okunmamis), new Rectangle(r.X + Tema.S(32), r.Y, r.Width - Tema.S(32) - sagBos, r.Height), renk,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
            if (k.Bahsedilme > 0) Cizim.Rozet(g, new Point(r.Right - Tema.S(8), r.Y + r.Height / 2), k.Bahsedilme);
        }
        g.ResetClip();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _kaydir.Ciz(g, liste);
    }

    // Aşağı / yukarı bakan küçük ok (iki çizgi).
    private static void Ok(Graphics g, Point orta, bool yukari, Color renk)
    {
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = Tema.S(5), h = Tema.S(3);
        int y = yukari ? h : -h;
        using var p = new Pen(renk, Math.Max(1.6f, Tema.S(2) * 0.85f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(p, [new Point(orta.X - w, orta.Y + y), new Point(orta.X, orta.Y - y), new Point(orta.X + w, orta.Y + y)]);
        g.SmoothingMode = eski;
    }

    // Kenar çubuğundakiyle aynı: avatar, ad, durum ve ayar dişlisi.
    private void AltCiz(Graphics g)
    {
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
        TextRenderer.DrawText(g, _ben.GorunenAd, Tema.Yazi(10, kalin: true), new Rectangle(kx, ku.Y + Tema.S(5), kw, Tema.S(20)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
        var altYazi = string.IsNullOrWhiteSpace(_ben.DurumMesaji) ? Cizim.DurumAdi(_ben.Durum) : _ben.DurumMesaji;
        TextRenderer.DrawText(g, altYazi, Tema.Yazi(9), new Rectangle(kx, ku.Y + Tema.S(26), kw, Tema.S(18)), Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);

        var ay = AyarAlani;
        if (_uzerinde == "ayar" || _ayarlarAcik) Cizim.YuvarlakDoldur(g, ay, Tema.S(6), Tema.YuzeyUst);
        TextRenderer.DrawText(g, Glif.Ayarlar, Cizim.Ikon(12), ay, _uzerinde == "ayar" || _ayarlarAcik ? Tema.Metin : Tema.MetinIkincil,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
    }

    // ---- Fare ----

    private string? Bul(Point p)
    {
        if (KullaniciAlani.Contains(p)) return "kullanici";
        if (AyarAlani.Contains(p)) return "ayar";
        if (p.Y < BaslikH) return _sunucu is null ? null : "baslik";
        if (Yonetebilir && Rectangle.Inflate(EkleAlani, Tema.S(2), Tema.S(2)).Contains(p)) return "ekle";
        if (ListeAlani.Contains(p) && !_kaydir.Seritte(p, ListeAlani))
            for (int i = 0; i < _kanallar.Count; i++)
                if (SatirAlani(i).Contains(p)) return "k:" + _kanallar[i].Id;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_kaydir.FareHareket(e.Location, ListeAlani)) return;
        var yeni = Bul(e.Location);
        if (yeni == _uzerinde) return;
        _uzerinde = yeni;
        Cursor = yeni != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _uzerinde = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) _kaydir.FareBasla(e.Location, ListeAlani);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!ListeAlani.Contains(e.Location) || !_kaydir.Tekerlek(e.Delta, SatirH)) return;
        _uzerinde = Bul(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_kaydir.FareBirak()) return;
        var b = Bul(e.Location);
        if (b is null) return;
        if (e.Button == MouseButtons.Right)
        {
            if (b.StartsWith("k:") && Yonetebilir && long.TryParse(b[2..], out var kid) && _kanallar.Find(k => k.Id == kid) is { } kanal) KanalMenusu(kanal, e.Location);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        switch (b)
        {
            case "kullanici": KullaniciTik?.Invoke(RectangleToScreen(KullaniciAlani)); break;
            case "ayar": AyarTik?.Invoke(); break;
            case "baslik": MenuTik?.Invoke(RectangleToScreen(BaslikAlani)); break;
            case "ekle": KanalEkleTik?.Invoke(); break;
            default:
                if (b.StartsWith("k:") && long.TryParse(b[2..], out var id)) { _seciliKanal = id; Invalidate(); KanalSecildi?.Invoke(id); }
                break;
        }
    }

    private void KanalMenusu(Kanal k, Point yer)
    {
        var m = new KoyuMenu();
        m.Ekle("Kanalı düzenle", () => KanalDuzenleTik?.Invoke(k));
        m.Ayirici();
        m.Ekle("Kanalı sil", () => KanalSilTik?.Invoke(k), Tema.Hata);
        m.Closed += (_, _) => BeginInvoke(() => m.Dispose());
        m.Show(this, yer);
    }
}
