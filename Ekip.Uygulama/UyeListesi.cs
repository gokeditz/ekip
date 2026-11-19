using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Sunucu kanalının sağında açılan üye listesi: role göre gruplu, çevrimiçi olanlar önce, çevrimdışılar soluk.
public class UyeListesi : Control
{
    private abstract record Satir;
    private sealed record Baslik(string Yazi) : Satir;
    private sealed record Uye(SunucuUyesi Kisi) : Satir;

    private List<SunucuUyesi> _uyeler = [];
    private List<Satir> _satirlar = [];
    private int _uzerinde = -1;
    private readonly DikeyKaydirma _kaydir;

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

    public List<SunucuUyesi> Uyeler { get => _uyeler; set { _uyeler = value ?? []; Kur(); } }

    public event Action<Profil, Rectangle>? UyeTik; // (profil, satırın ekran koordinatındaki alanı)

    public UyeListesi()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Yuzey;
        _kaydir = new DikeyKaydirma(this);
    }

    public static int GerekenGenislik => Tema.S(240);

    private int BaslikH => Tema.S(38);
    private int UyeH => Tema.S(46);
    private int SatirYuksekligi(Satir s) => s is Baslik ? BaslikH : UyeH;

    private static bool Cevrimici(Profil p) => p.Durum is not (Durum.Cevrimdisi or Durum.Gorunmez);

    private void Kur()
    {
        var tr = StringComparer.Create(Tema.Turkce, true);
        var liste = new List<Satir>();
        void Grup(string baslik, SunucuRolu rol, bool sayiGoster)
        {
            var grup = _uyeler.Where(u => u.Rol == rol).OrderBy(u => Cevrimici(u.Profil) ? 0 : 1).ThenBy(u => u.Profil.GorunenAd, tr).ToList();
            if (grup.Count == 0) return;
            liste.Add(new Baslik(sayiGoster ? $"{baslik} — {grup.Count}" : baslik));
            liste.AddRange(grup.Select(u => new Uye(u)));
        }
        Grup("SAHİP", SunucuRolu.Sahip, false);
        Grup("YÖNETİCİLER", SunucuRolu.Yonetici, true);
        Grup("ÜYELER", SunucuRolu.Uye, true);
        _satirlar = liste;
        _uzerinde = -1;
        Hesapla();
        Invalidate();
    }

    private int ToplamH => _satirlar.Sum(SatirYuksekligi) + Tema.S(8);
    private void Hesapla() => _kaydir.Ayarla(ToplamH, Height);
    protected override void OnResize(EventArgs e) { base.OnResize(e); Hesapla(); }

    private IEnumerable<(int i, Rectangle r)> Gorunenler()
    {
        int y = Tema.S(4) - _kaydir.Konum;
        for (int i = 0; i < _satirlar.Count; i++)
        {
            int h = SatirYuksekligi(_satirlar[i]);
            var r = new Rectangle(Tema.S(8), y, Width - Tema.S(16) - (_kaydir.Var ? Tema.S(6) : 0), h);
            y += h;
            if (r.Bottom < 0) continue;
            if (r.Top > Height) yield break;
            yield return (i, r);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, 0, 0, Height);

        if (_satirlar.Count == 0)
            TextRenderer.DrawText(g, "Henüz kimse yok.", Tema.Yazi(9.5f), new Rectangle(Tema.S(20), Tema.S(20), Width - Tema.S(40), Tema.S(30)), Tema.MetinSoluk, TextFormatFlags.NoPadding);

        foreach (var (i, r) in Gorunenler())
        {
            if (_satirlar[i] is Baslik b)
            {
                TextRenderer.DrawText(g, b.Yazi, Tema.Yazi(8.5f, kalin: true), new Rectangle(r.X + Tema.S(8), r.Y + Tema.S(12), r.Width - Tema.S(8), Tema.S(20)), Tema.MetinSoluk,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                continue;
            }
            var u = ((Uye)_satirlar[i]).Kisi;
            var p = u.Profil;
            bool cevrimici = Cevrimici(p), uz = i == _uzerinde;
            var zemin = BackColor;
            if (uz) { Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst); zemin = Tema.YuzeyUst; }

            int av = Tema.S(32);
            var avR = new RectangleF(r.X + Tema.S(8), r.Y + (r.Height - av) / 2f, av, av);
            Cizim.Avatar(g, avR, p, Avatarlar?.Al(p), zemin);
            if (!cevrimici)
            {
                // Çevrimdışılar solgun görünsün: avatarın üstüne yarı saydam zemin.
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var sol = new SolidBrush(Color.FromArgb(120, zemin));
                g.FillEllipse(sol, RectangleF.Inflate(avR, Tema.S(1), Tema.S(1)));
            }

            int tx = (int)avR.Right + Tema.S(10), tw = r.Right - tx - Tema.S(8);
            bool durumVar = cevrimici && !string.IsNullOrWhiteSpace(p.DurumMesaji);
            var adRenk = !cevrimici ? Tema.MetinSoluk : uz ? Tema.Metin : Tema.MetinIkincil;
            if (cevrimici && !uz) adRenk = Tema.Metin;
            var f = Tema.Yazi(10, kalin: cevrimici);
            int tac = u.Rol == SunucuRolu.Sahip ? Tema.S(18) : 0;
            var adAlan = durumVar ? new Rectangle(tx, r.Y + Tema.S(5), tw - tac, Tema.S(18)) : new Rectangle(tx, r.Y, tw - tac, r.Height);
            var tf = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | (durumVar ? 0 : TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, p.GorunenAd, f, adAlan, adRenk, tf);
            if (tac > 0)
            {
                int ow = TextRenderer.MeasureText(g, p.GorunenAd, f, Size.Empty, TextFormatFlags.NoPadding).Width;
                Tac(g, new Point(tx + Math.Min(ow, tw - tac) + Tema.S(6), (durumVar ? adAlan.Y + adAlan.Height / 2 : r.Y + r.Height / 2)), cevrimici ? ColorTranslator.FromHtml("#F5B544") : Tema.MetinSoluk);
            }
            if (durumVar)
                TextRenderer.DrawText(g, p.DurumMesaji, Tema.Yazi(8.5f), new Rectangle(tx, r.Y + Tema.S(24), tw, Tema.S(16)), Tema.MetinSoluk, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _kaydir.Ciz(g, ClientRectangle);
    }

    // Sahibin yanında küçük altın taç.
    private static void Tac(Graphics g, Point merkez, Color renk)
    {
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = Tema.S(12), h = Tema.S(9);
        float x = merkez.X, y = merkez.Y - h / 2;
        var pts = new PointF[]
        {
            new(x, y + h), new(x, y + h * 0.25f), new(x + w * 0.25f, y + h * 0.6f), new(x + w * 0.5f, y),
            new(x + w * 0.75f, y + h * 0.6f), new(x + w, y + h * 0.25f), new(x + w, y + h),
        };
        using var b = new SolidBrush(renk);
        g.FillPolygon(b, pts);
        g.SmoothingMode = eski;
    }

    private int Bul(Point p)
    {
        if (_kaydir.Seritte(p, ClientRectangle)) return -1;
        foreach (var (i, r) in Gorunenler())
            if (_satirlar[i] is Uye && r.Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_kaydir.FareHareket(e.Location, ClientRectangle)) return;
        int i = Bul(e.Location);
        if (i == _uzerinde) return;
        _uzerinde = i;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) _kaydir.FareBasla(e.Location, ClientRectangle); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_kaydir.FareBirak() || e.Button != MouseButtons.Left) return;
        int i = Bul(e.Location);
        if (i < 0) return;
        var r = Gorunenler().First(x => x.i == i).r;
        UyeTik?.Invoke(((Uye)_satirlar[i]).Kisi.Profil, RectangleToScreen(r));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_kaydir.Tekerlek(e.Delta, UyeH)) return;
        _uzerinde = Bul(e.Location);
        Invalidate();
    }
}
