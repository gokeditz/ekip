namespace Ekip.Uygulama;

// Uygulamanın İÇİNDE açılan pencere (ayrı Windows penceresi değil): arka plan kararır, ortada kart durur.
// Dışarı tıklayınca ya da Esc'e basınca kapanır.
public class IcModal : Control
{
    private Bitmap? _arka;
    private Control _icerik = null!;
    public event Action? Kapandi;

    private IcModal()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        BackColor = Color.FromArgb(12, 13, 17); // Kartın yuvarlak köşelerinin arkası karartılmış zeminle uyumlu olsun.
    }

    public static IcModal Goster(Form sahip, Control icerik)
    {
        var m = new IcModal { _icerik = icerik, Bounds = sahip.ClientRectangle };
        // Arka plan: formun o anki görüntüsü, karartılmış (WinForms'ta gerçek saydamlık yerine ucuz ve düzgün bir yol).
        try
        {
            using var tam = new Bitmap(sahip.Width, sahip.Height);
            sahip.DrawToBitmap(tam, new Rectangle(Point.Empty, sahip.Size));
            var ofs = sahip.PointToScreen(Point.Empty);
            int dx = ofs.X - sahip.Left, dy = ofs.Y - sahip.Top;
            m._arka = new Bitmap(Math.Max(1, sahip.ClientSize.Width), Math.Max(1, sahip.ClientSize.Height));
            using var g = Graphics.FromImage(m._arka);
            g.DrawImage(tam, new Rectangle(0, 0, m._arka.Width, m._arka.Height), new Rectangle(dx, dy, m._arka.Width, m._arka.Height), GraphicsUnit.Pixel);
            using var karart = new SolidBrush(Color.FromArgb(170, 5, 6, 10));
            g.FillRectangle(karart, 0, 0, m._arka.Width, m._arka.Height);
        }
        catch { m._arka = null; }
        m.Controls.Add(icerik);
        sahip.Controls.Add(m);
        m.BringToFront();
        m.Focus();
        return m;
    }

    public void Kapat()
    {
        if (IsDisposed) return;
        Parent?.Controls.Remove(this);
        Kapandi?.Invoke();
        Dispose();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_icerik is null) return;
        _icerik.Location = new Point((Width - _icerik.Width) / 2, Math.Max(Tema.S(20), (Height - _icerik.Height) / 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_arka != null) e.Graphics.DrawImage(_arka, ClientRectangle);
        else e.Graphics.Clear(Color.FromArgb(12, 13, 17));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_icerik.Bounds.Contains(e.Location)) Kapat();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Kapat(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _arka?.Dispose();
        base.Dispose(disposing);
    }
}

// Altta yüzen kısa bildirim hapı (ör. "Kod oluşturuldu"). Birkaç saniye görünür, içeriği itmez.
public class BildirimHapi : Control
{
    private string _yazi = "";
    private bool _hata;

    public BildirimHapi()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
    }

    public void Ayarla(string yazi, bool hata) { _yazi = yazi; _hata = hata; Invalidate(); }

    public Size GerekenBoyut(int maksGenislik)
    {
        var o = TextRenderer.MeasureText(_yazi, Tema.Yazi(10, kalin: true), new Size(maksGenislik - Tema.S(56), 0), TextFormatFlags.WordBreak);
        return new Size(Math.Min(maksGenislik, o.Width + Tema.S(56)), Math.Max(Tema.S(44), o.Height + Tema.S(22)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        var zemin = _hata ? ColorTranslator.FromHtml("#3A1219") : ColorTranslator.FromHtml("#0E3326");
        var cizgi = _hata ? ColorTranslator.FromHtml("#7A2A38") : ColorTranslator.FromHtml("#1F6B4D");
        var yazi = _hata ? ColorTranslator.FromHtml("#FFB3BF") : ColorTranslator.FromHtml("#9EF0C9");
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(r, Tema.S(10)))
        {
            using var b = new SolidBrush(zemin);
            g.FillPath(b, yol);
            using var p = new Pen(cizgi);
            g.DrawPath(p, yol);
        }
        TextRenderer.DrawText(g, _hata ? Glif.Uyari : Glif.Onay, Cizim.Ikon(10), new Rectangle(Tema.S(14), 0, Tema.S(20), Height), yazi, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _yazi, Tema.Yazi(10, kalin: true), new Rectangle(Tema.S(42), 0, Width - Tema.S(56), Height), yazi,
            TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Visible = false; } // Tıklayınca kapanır.
}

// Onay kartı (ör. "Arkadaşlıktan çıkarılsın mı?"). Modal içinde gösterilir.
public class OnayKarti : Panel
{
    public event Action<bool>? Cevap;

    public OnayKarti(string baslik, string mesaj, string onayYazisi, bool tehlike = true)
    {
        BackColor = Tema.Yuzey;
        int w = Tema.S(440);
        var b = new Label { Text = baslik, Font = Tema.Yazi(13, kalin: true), ForeColor = Tema.Metin, BackColor = Tema.Yuzey, AutoSize = false, Location = new Point(Tema.S(20), Tema.S(20)) };
        b.Size = new Size(w - Tema.S(40), TextRenderer.MeasureText(baslik, b.Font, new Size(w - Tema.S(40), 0), TextFormatFlags.WordBreak).Height + Tema.S(4));
        var m = new Label { Text = mesaj, Font = Tema.Yazi(10), ForeColor = Tema.MetinIkincil, BackColor = Tema.Yuzey, AutoSize = false, Location = new Point(Tema.S(20), b.Bottom + Tema.S(8)) };
        m.Size = new Size(w - Tema.S(40), TextRenderer.MeasureText(mesaj, m.Font, new Size(w - Tema.S(40), 0), TextFormatFlags.WordBreak).Height + Tema.S(4));

        var alt = new Panel { BackColor = Tema.YuzeyUst, Bounds = new Rectangle(0, m.Bottom + Tema.S(24), w, Tema.S(68)) };
        var iptal = new Dugme { Text = "Vazgeç", Tur = Dugme.Turler.Ikincil };
        var onay = new Dugme { Text = onayYazisi, Tur = tehlike ? Dugme.Turler.Tehlike : Dugme.Turler.Birincil };
        int ow = TextRenderer.MeasureText(onayYazisi, onay.Font).Width + Tema.S(36);
        onay.Bounds = new Rectangle(w - Tema.S(20) - ow, Tema.S(14), ow, Tema.S(40));
        iptal.Bounds = new Rectangle(onay.Left - Tema.S(10) - Tema.S(100), Tema.S(14), Tema.S(100), Tema.S(40));
        iptal.Click += (_, _) => Cevap?.Invoke(false);
        onay.Click += (_, _) => Cevap?.Invoke(true);
        alt.Controls.AddRange([iptal, onay]);

        Controls.AddRange([b, m, alt]);
        Size = new Size(w, alt.Bottom);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Tema.Kenar);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    // Kartı form içinde gösterir, cevabı bekler. Dışarı tıklamak / Esc = vazgeç.
    public static Task<bool> Sor(Form sahip, string baslik, string mesaj, string onayYazisi, bool tehlike = true)
    {
        var tcs = new TaskCompletionSource<bool>();
        var kart = new OnayKarti(baslik, mesaj, onayYazisi, tehlike);
        var modal = IcModal.Goster(sahip, kart);
        kart.Cevap += c => { tcs.TrySetResult(c); modal.Kapat(); };
        modal.Kapandi += () => tcs.TrySetResult(false);
        return tcs.Task;
    }
}
