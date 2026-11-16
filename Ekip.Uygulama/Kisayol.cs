using System.Drawing.Drawing2D;

namespace Ekip.Uygulama;

// Bir tuş + Ctrl/Alt/Shift. Ayarlarda "Ctrl+Tab" gibi yazıyla saklanır.
public record Kisayol(Keys Tus, bool Ctrl, bool Alt, bool Shift)
{
    public static readonly Kisayol VarsayilanOverlay = new(Keys.Tab, true, false, false);
    public static readonly Kisayol VarsayilanMini = new(Keys.T, false, false, false);

    public override string ToString()
    {
        var p = new List<string>();
        if (Ctrl) p.Add("Ctrl");
        if (Alt) p.Add("Alt");
        if (Shift) p.Add("Shift");
        p.Add(TusAdi(Tus));
        return string.Join("+", p);
    }

    public static Kisayol Coz(string? yazi, Kisayol varsayilan)
    {
        if (string.IsNullOrWhiteSpace(yazi)) return varsayilan;
        var p = yazi.Split('+');
        bool c = false, a = false, s = false;
        foreach (var x in p[..^1])
        {
            if (x == "Ctrl") c = true;
            else if (x == "Alt") a = true;
            else if (x == "Shift") s = true;
        }
        var son = p[^1];
        Keys tus = son.Length == 1 && char.IsDigit(son[0]) ? Keys.D0 + (son[0] - '0') : Enum.TryParse<Keys>(son, out var k) ? k : Keys.None;
        return tus == Keys.None ? varsayilan : new Kisayol(tus, c, a, s);
    }

    private static string TusAdi(Keys k) => k switch
    {
        >= Keys.D0 and <= Keys.D9 => ((int)(k - Keys.D0)).ToString(),
        Keys.Oemtilde => "Oemtilde",
        _ => k.ToString(),
    };

    // Sadece değiştirici tuş (Ctrl, Shift...) ya da yasak tuş mu? Bunlar tek başına kısayol olamaz.
    public static bool Gecersiz(Keys k) => k is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or Keys.ShiftKey
        or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin
        or Keys.Escape or Keys.Enter or Keys.Back or Keys.None;
}

// Kısayol seçme kutusu: tıkla, istediğin tuşa bas. Esc vazgeçer.
public class KisayolKutusu : Control
{
    private Kisayol _deger = Kisayol.VarsayilanMini;
    private bool _bekliyor, _uzerinde;

    public Kisayol Deger { get => _deger; set { _deger = value; Invalidate(); } }
    public event Action<Kisayol>? Degisti;
    public event Action<bool>? YakalamaDurumu; // true: tuş bekleniyor (ana pencere kendi kısayollarını bu sırada dinlemesin)

    public KisayolKutusu()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Height = Tema.S(40);
    }

    private void Bekle(bool b)
    {
        if (_bekliyor == b) return;
        _bekliyor = b;
        YakalamaDurumu?.Invoke(b);
        Invalidate();
    }

    protected override bool IsInputKey(Keys k) => true; // Tab dahil her tuşu biz alalım.
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); Bekle(!_bekliyor); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Bekle(false); }
    protected override void OnMouseEnter(EventArgs e) { _uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _uzerinde = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.Handled = e.SuppressKeyPress = true;
        if (!_bekliyor) { if (e.KeyCode is Keys.Enter or Keys.Space) Bekle(true); return; }
        if (e.KeyCode == Keys.Escape) { Bekle(false); return; }
        if (Kisayol.Gecersiz(e.KeyCode)) return; // Ctrl'e basıldı, asıl tuşu bekle.
        _deger = new Kisayol(e.KeyCode, e.Control, e.Alt, e.Shift);
        Bekle(false);
        Degisti?.Invoke(_deger);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var yol = Tema.YuvarlakDikdortgen(r, Tema.S(8)))
        {
            using var b = new SolidBrush(Tema.Giris);
            g.FillPath(b, yol);
            using var p = new Pen(_bekliyor ? Tema.Vurgu : _uzerinde ? Tema.KenarHover : Tema.Kenar, _bekliyor ? Tema.S(2) : 1);
            g.DrawPath(p, yol);
        }
        if (_bekliyor)
        {
            TextRenderer.DrawText(g, "Bir tuşa bas…  (Esc: vazgeç)", Tema.Yazi(10), ClientRectangle, Tema.Vurgu,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        // Tuşları klavye tuşu gibi küçük kutular içinde göster.
        var parcalar = _deger.ToString().Split('+');
        var f = Tema.Yazi(9.5f, kalin: true);
        int toplam = parcalar.Sum(p => TextRenderer.MeasureText(p, f).Width + Tema.S(10)) + (parcalar.Length - 1) * Tema.S(18);
        int x = (Width - toplam) / 2, h = Tema.S(26), y = (Height - h) / 2;
        for (int i = 0; i < parcalar.Length; i++)
        {
            int w = TextRenderer.MeasureText(parcalar[i], f).Width + Tema.S(10);
            var k = new Rectangle(x, y, w, h);
            Cizim.YuvarlakDoldur(g, k, Tema.S(5), Tema.YuzeyUst);
            TextRenderer.DrawText(g, parcalar[i], f, k, Tema.Metin, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += w;
            if (i < parcalar.Length - 1)
            {
                TextRenderer.DrawText(g, "+", f, new Rectangle(x, y, Tema.S(18), h), Tema.MetinSoluk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += Tema.S(18);
            }
        }
    }
}
