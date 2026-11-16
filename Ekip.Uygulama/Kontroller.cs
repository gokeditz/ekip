using System.Drawing.Drawing2D;

namespace Ekip.Uygulama;

// Kendi çizdiğimiz düğme: yuvarlak köşe, hover/basılı rengi. Animasyon yok, sadece renk değişir.
public class Dugme : Control, IButtonControl
{
    public enum Turler { Birincil, Ikincil, Tehlike }

    private bool _uzerinde, _basili;
    public Turler Tur { get; set; } = Turler.Birincil;
    public int Yaricap { get; set; } = 8;
    public DialogResult DialogResult { get; set; }

    public Dugme()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Font = Tema.Yazi(10, kalin: true);
        Height = Tema.S(44);
    }

    public void NotifyDefault(bool value) { }
    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

    protected override void OnMouseEnter(EventArgs e) { _uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _uzerinde = false; _basili = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _basili = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _basili = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { PerformClick(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (normal, hover, basili, yazi) = Tur switch
        {
            Turler.Ikincil => (Tema.YuzeyUst, Tema.Kenar, Tema.Kenar, Tema.Metin),
            Turler.Tehlike => (ColorTranslator.FromHtml("#DA3B54"), ColorTranslator.FromHtml("#E5546B"), ColorTranslator.FromHtml("#C02E46"), Color.White),
            _ => (Tema.Vurgu, Tema.VurguHover, Tema.VurguBasili, Color.White),
        };
        var dolgu = !Enabled ? Color.FromArgb(110, normal) : _basili ? basili : _uzerinde ? hover : normal;
        if (!Enabled) yazi = Color.FromArgb(170, yazi);

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var yol = Tema.YuvarlakDikdortgen(r, Tema.S(Yaricap)))
        using (var firca = new SolidBrush(dolgu))
        {
            g.FillPath(firca, yol);
            if (Focused && ShowFocusCues)
            {
                using var kalem = new Pen(Color.FromArgb(200, Color.White), Tema.S(2));
                g.DrawPath(kalem, yol);
            }
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, yazi,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// Üstte etiketi olan, yuvarlak köşeli metin kutusu. Hata olunca etiket kırmızıya döner ve mesajı gösterir.
public class GirisKutusu : Control
{
    private readonly TextBox _kutu;
    private string _etiket = "";
    private string? _hata;
    private bool _uzerinde;

    private int EtiketYuksekligi => string.IsNullOrEmpty(_etiket) ? 0 : Tema.S(24);
    private int KutuYuksekligi => _kutu.Multiline ? Height - EtiketYuksekligi : Tema.S(44);

    // Çok satırlı kutu: yüksekliği dışarıdan verilir.
    public bool CokSatir
    {
        get => _kutu.Multiline;
        set { _kutu.Multiline = value; _kutu.ScrollBars = ScrollBars.None; _kutu.WordWrap = true; PerformLayout(); Invalidate(); }
    }

    public TextBox IcKutu => _kutu;

    public GirisKutusu()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _kutu = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Tema.Giris,
            ForeColor = Tema.Metin,
            Font = Tema.Yazi(11),
        };
        _kutu.GotFocus += (_, _) => Invalidate();
        _kutu.LostFocus += (_, _) => Invalidate();
        _kutu.TextChanged += (_, _) => { if (_hata != null) Hata = null; OnTextChanged(EventArgs.Empty); };
        _kutu.MouseEnter += (_, _) => { _uzerinde = true; Invalidate(); };
        _kutu.MouseLeave += (_, _) => { _uzerinde = false; Invalidate(); };
        _kutu.KeyDown += (_, e) => OnKeyDown(e);
        Controls.Add(_kutu);

        Height = EtiketYuksekligi + KutuYuksekligi;
        Cursor = Cursors.IBeam;
    }

    public string Etiket
    {
        get => _etiket;
        set
        {
            _etiket = value;
            if (!_kutu.Multiline) Height = EtiketYuksekligi + KutuYuksekligi; // Etiket eklenince yükseklik değişir.
            PerformLayout();
            Invalidate();
        }
    }
    public string? Hata { get => _hata; set { _hata = value; Invalidate(); } }
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text { get => _kutu.Text; set => _kutu.Text = value; }
    public bool Sifre { get => _kutu.UseSystemPasswordChar; set => _kutu.UseSystemPasswordChar = value; }
    public int MaksUzunluk { get => _kutu.MaxLength; set => _kutu.MaxLength = value; }
    public string IpucuYazisi { get => _kutu.PlaceholderText; set => _kutu.PlaceholderText = value; }
    public new void Focus() { _kutu.Focus(); _kutu.SelectAll(); }

    protected override void OnMouseEnter(EventArgs e) { _uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _uzerinde = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _kutu.Focus(); base.OnMouseDown(e); }
    protected override void OnEnabledChanged(EventArgs e) { _kutu.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int ic = Tema.S(14);
        _kutu.Width = Width - ic * 2;
        if (_kutu.Multiline)
        {
            _kutu.Location = new Point(ic, EtiketYuksekligi + Tema.S(13));
            _kutu.Height = Math.Max(Tema.S(20), KutuYuksekligi - Tema.S(24));
        }
        else _kutu.Location = new Point(ic, EtiketYuksekligi + (KutuYuksekligi - _kutu.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);

        // Etiket
        if (EtiketYuksekligi > 0) EtiketCiz(g);

        // Kutu
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, EtiketYuksekligi + 0.5f, Width - 1.5f, KutuYuksekligi - 1.5f);
        var kenar = _hata != null ? Tema.Hata : _kutu.Focused ? Tema.Vurgu : _uzerinde ? Tema.KenarHover : Tema.Kenar;
        using var yol = Tema.YuvarlakDikdortgen(r, Tema.S(8));
        using var firca = new SolidBrush(Tema.Giris);
        using var kalem = new Pen(kenar, _kutu.Focused || _hata != null ? Tema.S(2) : 1);
        g.FillPath(firca, yol);
        g.DrawPath(kalem, yol);
    }

    private void EtiketCiz(Graphics g)
    {
        var buyuk = Tema.Turkce.TextInfo.ToUpper(_etiket);
        var etiketYazi = _hata is null ? buyuk : $"{buyuk}  ·  {_hata}";
        TextRenderer.DrawText(g, etiketYazi, Tema.Yazi(8.5f, kalin: true),
            new Rectangle(0, 0, Width, EtiketYuksekligi - Tema.S(6)),
            _hata is null ? Tema.MetinIkincil : Tema.Hata,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

// Tıklanabilir yazı (bağlantı görünümünde).
public class BaglantiYazi : Label
{
    public BaglantiYazi()
    {
        AutoSize = true;
        Cursor = Cursors.Hand;
        ForeColor = Tema.Vurgu;
        Font = Tema.Yazi(9.5f, kalin: true);
        // Tıklamayı kendimiz algılıyoruz: Windows'un standart tıklaması fare yakalamaya dayanıyor,
        // pencere öne geçmeden gelen tıklamalarda kaybolabiliyor.
        SetStyle(ControlStyles.StandardClick, false);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) OnClick(EventArgs.Empty);
    }
    protected override void OnMouseEnter(EventArgs e) { ForeColor = Tema.VurguHover; Font = Tema.Yazi(9.5f, true, FontStyle.Underline); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { ForeColor = Tema.Vurgu; Font = Tema.Yazi(9.5f, true); base.OnMouseLeave(e); }
}

