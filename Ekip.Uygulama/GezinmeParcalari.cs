using Ekip.Ortak;

namespace Ekip.Uygulama;

// Sunucu kanalının / grubun sohbet alanının üstündeki başlık çubuğu: "# kanal" + konu (ya da grup ikonu + ad + "n üye"),
// sağda üye listesini açıp kapatan düğme (kanal) ya da grup ayarları dişlisi (grup). Sohbet paneli bunun altına gelir.
public class KonusmaBasligi : Control
{
    private enum Bicim { Kanal, Grup }

    private Bicim _bicim = Bicim.Kanal;
    private string _ad = "", _alt = "";
    private string _renk = "";
    private Image? _resim;
    private bool _uyelerAcik, _uyeDugmesi = true;
    private string? _uzerinde; // "uyeler", "ayar"
    private readonly KoyuIpucu _ipucu = new();

    public event Action? UyelerTik;
    public event Action? AyarTik;

    public KonusmaBasligi()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
    }

    public static int GerekenYukseklik => Tema.S(56);

    public bool UyelerAcik { get => _uyelerAcik; set { if (_uyelerAcik != value) { _uyelerAcik = value; Invalidate(); } } }
    public bool UyeDugmesi { get => _uyeDugmesi; set { if (_uyeDugmesi != value) { _uyeDugmesi = value; Invalidate(); } } }

    public void KanalGoster(string ad, string konu)
    {
        _bicim = Bicim.Kanal; _ad = ad; _alt = konu ?? ""; _resim = null;
        _uzerinde = null;
        Invalidate();
    }

    public void GrupGoster(string ad, int uyeSayisi, string renk, Image? resim)
    {
        _bicim = Bicim.Grup; _ad = ad; _alt = $"{uyeSayisi} üye"; _renk = renk; _resim = resim;
        _uzerinde = null;
        Invalidate();
    }

    private Rectangle DugmeAlani => new(Width - Tema.S(16) - Tema.S(36), (Height - Tema.S(36)) / 2, Tema.S(36), Tema.S(36));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int orta = Height / 2, x = Tema.S(20);
        var d = DugmeAlani;
        bool dugmeVar = _bicim == Bicim.Grup || _uyeDugmesi;
        int sagSinir = (dugmeVar ? d.X : Width) - Tema.S(14);

        if (_bicim == Bicim.Kanal)
        {
            TextRenderer.DrawText(g, "#", Tema.Yazi(16, kalin: true), new Rectangle(x, 0, Tema.S(22), Height - 1), Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += Tema.S(24);
        }
        else
        {
            int av = Tema.S(32);
            IkonCizim.Grup(g, new RectangleF(x, orta - av / 2f, av, av), _renk, _resim);
            x += av + Tema.S(12);
        }

        var af = Tema.Yazi(11.5f, kalin: true);
        int aw = Math.Min(TextRenderer.MeasureText(g, _ad, af, Size.Empty, TextFormatFlags.NoPadding).Width + 2, Math.Max(Tema.S(60), (sagSinir - x) * 3 / 5));
        TextRenderer.DrawText(g, _ad, af, new Rectangle(x, 0, aw, Height - 1), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        x += aw;

        if (!string.IsNullOrWhiteSpace(_alt) && sagSinir - x > Tema.S(60))
        {
            if (_bicim == Bicim.Kanal)
            {
                x += Tema.S(14);
                using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, orta - Tema.S(10), x, orta + Tema.S(10));
                x += Tema.S(14);
                TextRenderer.DrawText(g, _alt, Tema.Yazi(10), new Rectangle(x, 0, sagSinir - x, Height - 1), Tema.MetinIkincil, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            else
            {
                x += Tema.S(12);
                TextRenderer.DrawText(g, _alt, Tema.Yazi(9.5f), new Rectangle(x, 0, sagSinir - x, Height - 1), Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }

        if (dugmeVar)
        {
            bool uz = _uzerinde != null, aktif = _bicim == Bicim.Kanal && _uyelerAcik;
            if (aktif || uz) Cizim.YuvarlakDoldur(g, d, Tema.S(6), aktif ? Tema.YuzeyUst : Tema.Yuzey);
            TextRenderer.DrawText(g, _bicim == Bicim.Kanal ? Glif.Insanlar : Glif.Ayarlar, Cizim.Ikon(12.5f), d, aktif || uz ? Tema.Metin : Tema.MetinIkincil,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
    }

    private string? Bul(Point p)
    {
        if (!(_bicim == Bicim.Grup || _uyeDugmesi) || !DugmeAlani.Contains(p)) return null;
        return _bicim == Bicim.Kanal ? "uyeler" : "ayar";
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var b = Bul(e.Location);
        if (b == _uzerinde) return;
        _uzerinde = b;
        Cursor = b != null ? Cursors.Hand : Cursors.Default;
        _ipucu.Hide(this);
        if (b != null)
        {
            var yazi = b == "uyeler" ? (_uyelerAcik ? "Üye listesini gizle" : "Üye listesini göster") : "Grup ayarları";
            _ipucu.Show(yazi, this, DugmeAlani.X - Tema.S(60), DugmeAlani.Bottom + Tema.S(4), 2500);
        }
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _uzerinde = null;
        _ipucu.Hide(this);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var b = Bul(e.Location);
        _ipucu.Hide(this);
        if (b == "uyeler") UyelerTik?.Invoke();
        else if (b == "ayar") AyarTik?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _ipucu.Dispose();
        base.Dispose(disposing);
    }
}
