using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Sunucu / grup / keşfet sayfalarının ortak yapı taşları. Hepsi kendi çizen, hafif kontroller.

// Elle çizilen listeler için ince dikey kaydırma çubuğu mantığı (Kenar.cs / Listeler.cs'deki ile aynı görünüm).
public sealed class DikeyKaydirma(Control sahip)
{
    private int _konum, _surukleBas, _surukleKonum;
    private bool _surukle;

    public int IcerikH { get; private set; }
    public int GorunurH { get; private set; }
    public int Maks => Math.Max(0, IcerikH - GorunurH);
    public bool Var => Maks > 0;
    public bool Surukleniyor => _surukle;
    public int Konum
    {
        get => _konum;
        set { var y = Math.Clamp(value, 0, Maks); if (y != _konum) { _konum = y; sahip.Invalidate(); } }
    }

    public void Ayarla(int icerikH, int gorunurH)
    {
        IcerikH = icerikH;
        GorunurH = gorunurH;
        _konum = Math.Clamp(_konum, 0, Maks);
    }

    public Rectangle Cubuk(Rectangle alan)
    {
        int h = Math.Max(Tema.S(36), (int)((long)alan.Height * GorunurH / Math.Max(1, IcerikH)));
        h = Math.Min(h, alan.Height);
        int y = alan.Y + (Maks == 0 ? 0 : (alan.Height - h) * _konum / Maks);
        return new Rectangle(alan.Right - Tema.S(8), y, Tema.S(6), h);
    }

    public void Ciz(Graphics g, Rectangle alan)
    {
        if (Var) Cizim.YuvarlakDoldur(g, Cubuk(alan), Tema.S(3), _surukle ? Tema.KenarHover : Tema.Kenar);
    }

    // Çubuk şeridinde mi? (tıklama listeye gitmesin)
    public bool Seritte(Point p, Rectangle alan) => Var && p.X >= alan.Right - Tema.S(14) && alan.Contains(p);

    public bool FareBasla(Point p, Rectangle alan)
    {
        if (!Seritte(p, alan)) return false;
        var c = Cubuk(alan);
        if (!c.Contains(p)) { Konum += p.Y < c.Y ? -GorunurH : GorunurH; return true; }
        _surukle = true;
        _surukleBas = p.Y;
        _surukleKonum = _konum;
        sahip.Capture = true;
        return true;
    }

    public bool FareHareket(Point p, Rectangle alan)
    {
        if (!_surukle) return false;
        int yol = Math.Max(1, alan.Height - Cubuk(alan).Height);
        Konum = _surukleKonum + (p.Y - _surukleBas) * Maks / yol;
        return true;
    }

    public bool FareBirak()
    {
        if (!_surukle) return false;
        _surukle = false;
        sahip.Capture = false;
        sahip.Invalidate();
        return true;
    }

    public bool Tekerlek(int delta, int adim)
    {
        if (!Var) return false;
        Konum -= delta * adim / 120;
        return true;
    }
}

// Uygulama içi küçük koyu ipucu balonu: kaynağın sağında belirir (ray simgeleri için). Standart ToolTip yerine
// forma eklenen bir kontrol olduğundan her zaman aynı görünür ve kaynağın dışına taşabilir.
public sealed class IpucuBalonu : Control
{
    private static readonly ConditionalWeakTable<Form, IpucuBalonu> _balonlar = [];
    private string _yazi = "";

    private IpucuBalonu()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = ColorTranslator.FromHtml("#0B0B0E");
        Visible = false;
        Enabled = false; // Fare olaylarını yutmasın.
    }

    // alan: kaynak kontrolün istemci koordinatında; balon alanın sağında, dikeyde ortalı çıkar.
    public static void Goster(Control kaynak, Rectangle alan, string yazi)
    {
        var form = kaynak.FindForm();
        if (form is null || string.IsNullOrEmpty(yazi)) return;
        if (!_balonlar.TryGetValue(form, out var b))
        {
            b = new IpucuBalonu();
            _balonlar.Add(form, b);
        }
        if (b.Parent != form) form.Controls.Add(b);
        b._yazi = yazi;
        var o = TextRenderer.MeasureText(yazi, Tema.Yazi(9.5f, kalin: true), Size.Empty, TextFormatFlags.NoPadding);
        var boyut = new Size(o.Width + Tema.S(20), o.Height + Tema.S(14));
        var nokta = form.PointToClient(kaynak.PointToScreen(new Point(alan.Right + Tema.S(10), alan.Y + alan.Height / 2)));
        b.Bounds = new Rectangle(nokta.X, nokta.Y - boyut.Height / 2, boyut.Width, boyut.Height);
        b.Region?.Dispose();
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(0, 0, boyut.Width, boyut.Height), Tema.S(6)))
            b.Region = new Region(yol);
        b.Visible = true;
        b.BringToFront();
        b.Invalidate();
    }

    public static void Gizle(Control kaynak)
    {
        var form = kaynak.FindForm();
        if (form != null && _balonlar.TryGetValue(form, out var b)) b.Visible = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        TextRenderer.DrawText(e.Graphics, _yazi, Tema.Yazi(9.5f, kalin: true), ClientRectangle, Tema.Metin,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

// Genişliğe göre içeriğini yerleştiren, dikeyde kaydırılan alan (içinde gerçek kontroller olabilir).
// Yerlestir(genislik) çocukları yerleştirir ve içerik yüksekliğini döner. Ayarlar sayfasındaki ince çubuğu kullanır.
public class KaydirmaAlani : Control
{
    private readonly Panel _icerik = new();
    private readonly CubukKontrol _cubuk;
    private readonly MouseEventHandler _tekerlek;
    private int _icerikH;

    public Panel Icerik => _icerik;
    public Func<int, int>? Yerlestir { get; set; }

    public KaydirmaAlani()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
        _icerik.BackColor = Tema.Arkaplan;
        _cubuk = new CubukKontrol(this) { Visible = false };
        Controls.AddRange([_icerik, _cubuk]);
        _tekerlek = (_, e) => Tekerlek(e.Delta);
        _cubuk.KonumDegisti += k => _icerik.Top = -k;
    }

    // İçerik yerleştirildikten sonra çocuklara tekerlek dinleyicisi ekler (metin kutuları tekerleği yutmasın).
    private void TekerlegiBagla(Control ust)
    {
        foreach (Control c in ust.Controls)
        {
            c.MouseWheel -= _tekerlek;
            if (c is not TextBox { Multiline: true }) c.MouseWheel += _tekerlek;
            TekerlegiBagla(c);
        }
    }

    public void YenidenYerlestir()
    {
        if (Yerlestir is null || Width <= 0) return;
        _icerik.SuspendLayout();
        _icerikH = Math.Max(Yerlestir(Width), 0);
        _icerik.Bounds = new Rectangle(0, 0, Width, Math.Max(_icerikH, Height));
        _icerik.ResumeLayout(false);
        TekerlegiBagla(_icerik);
        _cubuk.Kaydirma.Ayarla(_icerikH, Height);
        _cubuk.Visible = _cubuk.Kaydirma.Var;
        _cubuk.Bounds = new Rectangle(Width - Tema.S(14), 0, Tema.S(14), Height);
        _cubuk.BringToFront();
        _icerik.Top = -_cubuk.Kaydirma.Konum;
        _cubuk.Invalidate();
    }

    public void BasaDon() { _cubuk.Kaydirma.Konum = 0; _icerik.Top = 0; }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        YenidenYerlestir();
    }

    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Tekerlek(e.Delta); }

    private void Tekerlek(int delta)
    {
        if (_cubuk.Kaydirma.Tekerlek(delta, Tema.S(64))) { _icerik.Top = -_cubuk.Kaydirma.Konum; _cubuk.Invalidate(); }
    }

    private sealed class CubukKontrol : Control
    {
        public DikeyKaydirma Kaydirma { get; }
        public event Action<int>? KonumDegisti;

        public CubukKontrol(Control sahip)
        {
            Kaydirma = new DikeyKaydirma(this);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Tema.Arkaplan;
            Cursor = Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Kaydirma.Ciz(e.Graphics, ClientRectangle);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (Kaydirma.FareBasla(e.Location, ClientRectangle)) KonumDegisti?.Invoke(Kaydirma.Konum); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Kaydirma.FareHareket(e.Location, ClientRectangle)) KonumDegisti?.Invoke(Kaydirma.Konum); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Kaydirma.FareBirak(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (Kaydirma.Tekerlek(e.Delta, Tema.S(64))) KonumDegisti?.Invoke(Kaydirma.Konum); }
    }
}

// Açma/kapama anahtarı olan kart: başlık + açıklama solda, anahtar sağda.
public class Anahtar : Control
{
    private bool _acik, _uzerinde;
    public event Action? Degisti;

    public string Baslik { get; set; } = "";
    public string Aciklama { get; set; } = "";
    public bool Acik { get => _acik; set { if (_acik != value) { _acik = value; Invalidate(); } } }

    public Anahtar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Height = Tema.S(72);
    }

    protected override void OnMouseEnter(EventArgs e) { _uzerinde = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _uzerinde = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !Enabled || !ClientRectangle.Contains(e.Location)) return;
        _acik = !_acik;
        Invalidate();
        Degisti?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var yol = Tema.YuvarlakDikdortgen(r, Tema.S(10)))
        using (var b = new SolidBrush(Tema.Yuzey))
        using (var p = new Pen(_uzerinde && Enabled ? Tema.KenarHover : Tema.Kenar))
        {
            g.FillPath(b, yol);
            g.DrawPath(p, yol);
        }

        int sw = Tema.S(44), sh = Tema.S(24);
        var sr = new Rectangle(Width - Tema.S(16) - sw, (Height - sh) / 2, sw, sh);
        var zemin = _acik ? Tema.Vurgu : Tema.Kenar;
        if (!Enabled) zemin = Color.FromArgb(110, zemin);
        Cizim.YuvarlakDoldur(g, sr, sh / 2f, zemin);
        int top = sh - Tema.S(8);
        int tx = _acik ? sr.Right - Tema.S(4) - top : sr.X + Tema.S(4);
        using (var b = new SolidBrush(Enabled ? Color.White : Color.FromArgb(170, Color.White)))
            g.FillEllipse(b, tx, sr.Y + Tema.S(4), top, top);

        int metinW = sr.X - Tema.S(32);
        var tf = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        var f1 = Tema.Yazi(10.5f, kalin: true);
        var f2 = Tema.Yazi(9.5f);
        var satirli = MetinYardimci.Satirla(Aciklama, f2, metinW, 2);
        int aciklamaH = satirli.Length == 0 ? 0 : TextRenderer.MeasureText(satirli, f2, Size.Empty, TextFormatFlags.NoPadding).Height;
        int toplam = Tema.S(22) + (aciklamaH > 0 ? Tema.S(2) + aciklamaH : 0);
        int y = (Height - toplam) / 2;
        TextRenderer.DrawText(g, Baslik, f1, new Rectangle(Tema.S(16), y, metinW, Tema.S(22)), Enabled ? Tema.Metin : Tema.MetinSoluk, tf);
        if (aciklamaH > 0)
            TextRenderer.DrawText(g, satirli, f2, new Rectangle(Tema.S(16), y + Tema.S(24), metinW, aciklamaH + Tema.S(2)), Tema.MetinIkincil, TextFormatFlags.NoPadding);
    }
}

// Modal içindeki iki üç sekme (SecimGrubu gibi ama değişince olay verir).
public class SekmeSecimi : Control
{
    private readonly string[] _secenekler;
    private int _secili, _uzerinde = -1;
    public event Action<int>? Degisti;
    public int Secili { get => _secili; set { if (_secili != value) { _secili = value; Invalidate(); Degisti?.Invoke(value); } } }

    public SekmeSecimi(params string[] secenekler)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _secenekler = secenekler;
        Height = Tema.S(40);
    }

    private Rectangle Alan(int i)
    {
        int w = (Width - Tema.S(8)) / _secenekler.Length;
        return new Rectangle(Tema.S(4) + i * w, Tema.S(4), w, Height - Tema.S(8));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        Cizim.YuvarlakDoldur(g, ClientRectangle, Tema.S(8), Tema.Giris);
        for (int i = 0; i < _secenekler.Length; i++)
        {
            var r = Alan(i);
            if (i == _secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.Vurgu);
            else if (i == _uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
            TextRenderer.DrawText(g, _secenekler[i], Tema.Yazi(10, kalin: true), r, i == _secili ? Color.White : Tema.MetinIkincil,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private int Bul(Point p) { for (int i = 0; i < _secenekler.Length; i++) if (Alan(i).Contains(p)) return i; return -1; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = Bul(e.Location);
        if (i == _uzerinde) return;
        _uzerinde = i;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = -1; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); int i = Bul(e.Location); if (i >= 0) Secili = i; }
}

// Uygulama içi modal kartlarının tabanı: başlık + kapat çarpısı üstte, altta düğme çubuğu (OnayKarti ile aynı görünüm).
// Kart kendi yüksekliğini belirler; IcModal ortalar. Kapat olayı gelince modal kapatılmalı (Goster bunu yapar).
public abstract class ModalKarti : Panel
{
    private bool _carpiUzerinde;
    protected readonly Panel Alt = new() { BackColor = Tema.YuzeyUst };
    protected readonly Label HataYazisi = new() { AutoSize = false, Font = Tema.Yazi(9.5f, kalin: true), ForeColor = Tema.Hata, BackColor = Tema.YuzeyUst, TextAlign = ContentAlignment.MiddleLeft };
    protected readonly Dugme Vazgec = new() { Text = "Vazgeç", Tur = Dugme.Turler.Ikincil };
    protected readonly Dugme Birincil = new() { Text = "Tamam" };
    protected readonly Dugme SolDugme = new() { Text = "", Tur = Dugme.Turler.Tehlike, Visible = false };

    public string Baslik { get; set; } = "";
    public event Action? Kapat;

    protected int UstH => Tema.S(64);
    protected int AltH => Tema.S(68);
    protected int Ic => Tema.S(20);
    protected void KapatIste() => Kapat?.Invoke();

    protected ModalKarti(int genislik)
    {
        BackColor = Tema.Yuzey;
        ForeColor = Tema.Metin;
        DoubleBuffered = true;
        Width = genislik;
        Alt.Controls.AddRange([HataYazisi, SolDugme, Vazgec, Birincil]);
        Controls.Add(Alt);
        Vazgec.Click += (_, _) => KapatIste();
    }

    public void Hata(string? mesaj) => HataYazisi.Text = mesaj ?? "";

    // Çalışırken içeriği kilitler.
    protected void Mesgul(bool m, string normalYazi, string mesgulYazi, params Control[] kilitler)
    {
        foreach (var c in kilitler) c.Enabled = !m;
        Birincil.Enabled = !m;
        Vazgec.Enabled = !m;
        SolDugme.Enabled = !m;
        Birincil.Text = m ? mesgulYazi : normalYazi;
        UseWaitCursor = m;
    }

    public static IcModal ModalAc(Form sahip, ModalKarti kart)
    {
        var m = IcModal.Goster(sahip, kart);
        kart.Kapat += m.Kapat;
        return m;
    }

    private Rectangle CarpiAlani => new(Width - Ic - Tema.S(28), Tema.S(18), Tema.S(28), Tema.S(28));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (Alt is null) return;
        // Çerçeve çizgisi görünsün diye alt çubuk kartın kenarlarından 1px içeride.
        Alt.Bounds = new Rectangle(1, Height - AltH - 1, Width - 2, AltH);
        int ow = Math.Max(Tema.S(120), TextRenderer.MeasureText(Birincil.Text, Birincil.Font).Width + Tema.S(36));
        Birincil.Bounds = new Rectangle(Width - Ic - 1 - ow, Tema.S(14), ow, Tema.S(40));
        Vazgec.Bounds = new Rectangle(Birincil.Left - Tema.S(10) - Tema.S(100), Tema.S(14), Tema.S(100), Tema.S(40));
        int sw = SolDugme.Visible ? TextRenderer.MeasureText(SolDugme.Text, SolDugme.Font).Width + Tema.S(36) : 0;
        SolDugme.Bounds = new Rectangle(Ic - 1, Tema.S(14), sw, Tema.S(40));
        int hx = SolDugme.Visible ? SolDugme.Right + Tema.S(12) : Ic - 1;
        HataYazisi.Bounds = new Rectangle(hx, 0, Math.Max(0, Vazgec.Left - Tema.S(12) - hx), AltH);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); PerformLayout(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var p = new Pen(Tema.Kenar)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(g, Baslik, Tema.Yazi(13, kalin: true), new Rectangle(Ic, Tema.S(16), Width - Ic * 2 - Tema.S(40), Tema.S(32)), Tema.Metin,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var c = CarpiAlani;
        if (_carpiUzerinde) Cizim.YuvarlakDoldur(g, c, Tema.S(6), Tema.YuzeyUst);
        TextRenderer.DrawText(g, Glif.Kapat, Cizim.Ikon(9.5f), c, _carpiUzerinde ? Tema.Metin : Tema.MetinIkincil,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool u = CarpiAlani.Contains(e.Location);
        if (u == _carpiUzerinde) return;
        _carpiUzerinde = u;
        Cursor = u ? Cursors.Hand : Cursors.Default;
        Invalidate(CarpiAlani);
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_carpiUzerinde) { _carpiUzerinde = false; Cursor = Cursors.Default; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left && CarpiAlani.Contains(e.Location)) KapatIste(); }

    // İçine liste konan koyu, yuvarlak köşeli çerçeve (kart zemininden koyu, metin kutularıyla aynı renk).
    protected static void CerceveCiz(Graphics g, Rectangle r)
    {
        if (r.IsEmpty) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var yol = Tema.YuvarlakDikdortgen(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Tema.S(8));
        using var b = new SolidBrush(Tema.Giris);
        using var p = new Pen(Tema.Kenar);
        g.FillPath(b, yol);
        g.DrawPath(p, yol);
    }

    protected static Label EtiketYap(string s) => new() { AutoSize = true, Text = s, Font = Tema.Yazi(8.5f, kalin: true), ForeColor = Tema.MetinIkincil };
}

// İkon (sunucu: yuvarlak kare, grup: daire) önizlemesi + "Resim yükle" / "Kaldır". Seçilen resim ortadan kare kırpılıp
// 128x128 PNG olur; gönderme işi sahibine bırakılır (YeniResim / Kaldirildi).
public class IkonDuzenleyici : Control
{
    private readonly Dugme _yukle = new() { Text = "Resim yükle", Height = Tema.S(36) };
    private readonly Dugme _kaldir = new() { Text = "Kaldır", Tur = Dugme.Turler.Ikincil, Height = Tema.S(36) };
    private Image? _mevcut, _yeniResim;
    private byte[]? _yeniBayt;
    private bool _kaldirildi;
    private string _ad = "", _renk = Kurallar.ProfilRenkleri[0];

    public bool Yuvarlak { get; set; }              // true: grup (daire)
    public string Ad { get => _ad; set { _ad = value; Invalidate(); } }
    public string Renk { get => _renk; set { _renk = value; Invalidate(); } }
    public byte[]? YeniResim => _yeniBayt;
    public bool Kaldirildi => _kaldirildi;
    public event Action? Degisti;
    public event Action<string>? Hata;

    private int Boyut => Tema.S(76);

    public IkonDuzenleyici()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = Boyut;
        Controls.AddRange([_yukle, _kaldir]);
        _yukle.Click += (_, _) => ResimSec();
        _kaldir.Click += (_, _) =>
        {
            _yeniResim?.Dispose();
            _yeniResim = null;
            _yeniBayt = null;
            _kaldirildi = true;
            Guncelle();
            Degisti?.Invoke();
        };
        Guncelle();
    }

    // Sunucudaki/önbellekteki mevcut ikonu gösterir ve bekleyen değişiklikleri sıfırlar. Resmin sahibi önbelleğidir.
    public void Sifirla(Image? mevcut)
    {
        _yeniResim?.Dispose();
        _yeniResim = null;
        _yeniBayt = null;
        _kaldirildi = false;
        _mevcut = mevcut;
        Guncelle();
    }

    // Mevcut ikon yeni indirilince önizlemeyi tazele (değişiklik beklemiyorsa).
    public void MevcutGuncelle(Image? mevcut)
    {
        if (_yeniBayt != null || _kaldirildi) return;
        _mevcut = mevcut;
        Guncelle();
    }

    public Image? Gosterilen => _yeniResim ?? (_kaldirildi ? null : _mevcut);

    private void Guncelle()
    {
        _yukle.Visible = Enabled;
        _kaldir.Visible = Enabled && Gosterilen != null;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Guncelle(); }

    private void ResimSec()
    {
        using var d = new OpenFileDialog { Title = "İkon seç", Filter = "Resimler|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tüm dosyalar|*.*" };
        if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(d.FileName).Length > 20 * 1024 * 1024) { Hata?.Invoke("Bu resim çok büyük (en fazla 20 MB)."); return; }
            var bayt = Cizim.AvatarHazirla(d.FileName);
            var resim = Cizim.ResimCoz(bayt);
            if (resim is null) { Hata?.Invoke("Bu dosya açılamadı. PNG veya JPG bir resim seç."); return; }
            _yeniResim?.Dispose();
            _yeniResim = resim;
            _yeniBayt = bayt;
            _kaldirildi = false;
            Guncelle();
            Degisti?.Invoke();
        }
        catch { Hata?.Invoke("Bu dosya açılamadı. PNG veya JPG bir resim seç."); }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = Boyut + Tema.S(16);
        _yukle.Bounds = new Rectangle(x, 0, Tema.S(130), Tema.S(36));
        _kaldir.Bounds = new Rectangle(_yukle.Right + Tema.S(10), 0, Tema.S(90), Tema.S(36));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        var r = new RectangleF(0, 0, Boyut, Boyut);
        if (Yuvarlak) IkonCizim.Grup(g, r, _renk, Gosterilen);
        else IkonCizim.Sunucu(g, r, _ad, _renk, Gosterilen, Tema.S(20));
        int x = Boyut + Tema.S(16);
        if (Enabled) TextRenderer.DrawText(g, "PNG ya da JPG, ortadan kare kırpılır.", Tema.Yazi(9),
            new Rectangle(x, Tema.S(46), Math.Max(10, Width - x), Boyut - Tema.S(46)), Tema.MetinSoluk,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _yeniResim?.Dispose();
        base.Dispose(disposing);
    }
}

// Satır satır liste (yönetim listesiyle aynı görünüm): solda isteğe bağlı çizim, başlık, alt yazı, rozetler, sağda yazılı düğmeler.
public class SatirListe : Control
{
    public record Dugme(string Ad, string Yazi, bool Tehlike = false, bool Etkin = true);

    public class Oge
    {
        public object Etiket { get; init; } = "";
        public string Baslik { get; init; } = "";
        public string Alt { get; init; } = "";
        public Action<Graphics, RectangleF, Color>? Sol { get; init; }   // (g, 40px'lik alan, zemin rengi)
        public List<(string yazi, Color renk)> Rozetler { get; init; } = [];
        public List<Dugme> Dugmeler { get; init; } = [];
        public bool Soluk { get; init; }
    }

    private List<Oge> _ogeler = [];
    private int _uzerindeSatir = -1, _uzerindeDugme = -1;
    private readonly DikeyKaydirma _kaydir;

    private AvatarOnbellek? _avatarKaynagi;

    // Satırların "Sol" çizimi avatar kullanıyorsa, avatarlar indikçe liste yeniden çizilsin diye onbellek buraya verilir.
    public AvatarOnbellek? AvatarKaynagi
    {
        get => _avatarKaynagi;
        set
        {
            if (_avatarKaynagi != null) _avatarKaynagi.Yuklendi -= Tazele;
            _avatarKaynagi = value;
            if (_avatarKaynagi != null) _avatarKaynagi.Yuklendi += Tazele;
        }
    }

    private void Tazele() { if (!IsDisposed && IsHandleCreated) Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) AvatarKaynagi = null;
        base.Dispose(disposing);
    }

    public string BosYazi { get; set; } = "";
    public int SatirYuksekligi { get; set; } = Tema.S(64);
    public event Action<object, string, Rectangle>? Eylem;   // (etiket, düğme adı, düğmenin ekran koordinatındaki alanı)

    public SatirListe()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Tema.Arkaplan;
        _kaydir = new DikeyKaydirma(this);
    }

    public void Ayarla(List<Oge> ogeler)
    {
        _ogeler = ogeler;
        _kaydir.Ayarla(_ogeler.Count * SatirYuksekligi, Height);
        _uzerindeSatir = _uzerindeDugme = -1;
        Invalidate();
    }

    private int SatirW => Width - (_kaydir.Var ? Tema.S(12) : 0);
    private Rectangle SatirAlani(int i) => new(0, i * SatirYuksekligi - _kaydir.Konum, SatirW, SatirYuksekligi);

    private List<Rectangle> DugmeAlanlari(int i)
    {
        var r = SatirAlani(i);
        var list = new List<Rectangle>();
        int x = r.Right - Tema.S(12);
        var f = Tema.Yazi(9.5f, kalin: true);
        var d = _ogeler[i].Dugmeler;
        for (int j = d.Count - 1; j >= 0; j--)
        {
            int w = TextRenderer.MeasureText(d[j].Yazi, f, Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(24);
            x -= w;
            list.Insert(0, new Rectangle(x, r.Y + (r.Height - Tema.S(34)) / 2, w, Tema.S(34)));
            x -= Tema.S(8);
        }
        return list;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_ogeler.Count == 0 && BosYazi.Length > 0)
        {
            TextRenderer.DrawText(g, BosYazi, Tema.Yazi(10), new Rectangle(Tema.S(20), Tema.S(20), Width - Tema.S(40), Height - Tema.S(40)), Tema.MetinSoluk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return;
        }
        for (int i = 0; i < _ogeler.Count; i++)
        {
            var r = SatirAlani(i);
            if (r.Bottom < 0 || r.Top > Height) continue;
            var o = _ogeler[i];
            bool uzerinde = i == _uzerindeSatir;
            if (uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(8), Tema.Yuzey);
            else if (i > 0 && i - 1 != _uzerindeSatir) using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, r.X + Tema.S(10), r.Y, r.Right - Tema.S(10), r.Y);

            var zemin = uzerinde ? Tema.Yuzey : BackColor;
            int av = Tema.S(40);
            var avR = new RectangleF(r.X + Tema.S(12), r.Y + (r.Height - av) / 2f, av, av);
            o.Sol?.Invoke(g, avR, zemin);

            var dugmeler = DugmeAlanlari(i);
            int tx = (int)avR.Right + Tema.S(14), sag = (dugmeler.Count > 0 ? dugmeler[0].X : r.Right) - Tema.S(16);
            bool altVar = !string.IsNullOrEmpty(o.Alt);
            int ust = altVar ? r.Y + Tema.S(13) : r.Y + (r.Height - Tema.S(20)) / 2;
            var bf = Tema.Yazi(10.5f, kalin: true);
            int bw = TextRenderer.MeasureText(g, o.Baslik, bf, Size.Empty, TextFormatFlags.NoPadding).Width;
            // Rozetlere yer bırak: başlık gerekirse kısalır.
            int rozetW = 0;
            foreach (var (yazi, _) in o.Rozetler) rozetW += TextRenderer.MeasureText(yazi, Tema.Yazi(7.5f, kalin: true), Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(16);
            int bGenislik = Math.Max(Tema.S(40), Math.Min(bw, sag - tx - rozetW - Tema.S(4)));
            TextRenderer.DrawText(g, o.Baslik, bf, new Rectangle(tx, ust, bGenislik, Tema.S(20)), o.Soluk ? Tema.MetinIkincil : Tema.Metin,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            int rx = tx + bGenislik + Tema.S(8);
            foreach (var (yazi, renk) in o.Rozetler)
            {
                if (rx + Tema.S(30) > sag) break;
                rx = IkonCizim.YaziRozeti(g, rx, ust + Tema.S(10), yazi, renk).Right + Tema.S(6);
            }
            if (altVar)
                TextRenderer.DrawText(g, o.Alt, Tema.Yazi(9), new Rectangle(tx, r.Y + Tema.S(36), Math.Max(10, sag - tx), Tema.S(18)), Tema.MetinIkincil,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            for (int j = 0; j < dugmeler.Count; j++)
            {
                var d = o.Dugmeler[j];
                bool du = uzerinde && j == _uzerindeDugme && d.Etkin;
                var dz = d.Tehlike ? (du ? Color.FromArgb(70, Tema.Hata) : Color.FromArgb(35, Tema.Hata)) : (du ? Tema.Kenar : Tema.YuzeyUst);
                Cizim.YuvarlakDoldur(g, dugmeler[j], Tema.S(6), dz);
                var yr = d.Tehlike ? Tema.Hata : Tema.Metin;
                if (!d.Etkin) yr = Tema.MetinSoluk;
                TextRenderer.DrawText(g, d.Yazi, Tema.Yazi(9.5f, kalin: true), dugmeler[j], yr,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _kaydir.Ciz(g, ClientRectangle);
    }

    private (int satir, int dugme) Bul(Point p)
    {
        if (_kaydir.Seritte(p, ClientRectangle)) return (-1, -1);
        int i = (p.Y + _kaydir.Konum) / SatirYuksekligi;
        if (p.Y < 0 || i < 0 || i >= _ogeler.Count) return (-1, -1);
        var d = DugmeAlanlari(i);
        for (int j = 0; j < d.Count; j++) if (d[j].Contains(p)) return (i, j);
        return (i, -1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_kaydir.FareHareket(e.Location, ClientRectangle)) return;
        var (s, d) = Bul(e.Location);
        if (s == _uzerindeSatir && d == _uzerindeDugme) return;
        _uzerindeSatir = s;
        _uzerindeDugme = d;
        Cursor = d >= 0 && _ogeler[s].Dugmeler[d].Etkin ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerindeSatir = _uzerindeDugme = -1; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) _kaydir.FareBasla(e.Location, ClientRectangle); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_kaydir.FareBirak()) return;
        if (e.Button != MouseButtons.Left) return;
        var (s, d) = Bul(e.Location);
        if (s < 0 || d < 0 || !_ogeler[s].Dugmeler[d].Etkin) return;
        Eylem?.Invoke(_ogeler[s].Etiket, _ogeler[s].Dugmeler[d].Ad, RectangleToScreen(DugmeAlanlari(s)[d]));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_kaydir.Tekerlek(e.Delta, SatirYuksekligi)) OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, e.X, e.Y, 0));
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); _kaydir.Ayarla(_ogeler.Count * SatirYuksekligi, Height); }
}

// Arkadaş seçme listesi (grup oluştur / üye ekle). Çoklu modda onay kutulu, tek tıkla modda satıra tıklayınca "Tikla" olayı gelir.
public class ArkadasSecimListesi : Control
{
    private List<Profil> _hepsi = [];
    private List<Profil> _gorunen = [];
    private string _filtre = "";
    private int _uzerinde = -1;
    private readonly DikeyKaydirma _kaydir;

    private AvatarOnbellek? _avatarlar;

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

    public bool Coklu { get; set; } = true;
    public int Maks { get; set; } = int.MaxValue;
    public HashSet<long> Secililer { get; } = [];
    public string BosYazi { get; set; } = "Burada gösterilecek kimse yok.";
    public event Action? SecimDegisti;
    public event Action<Profil>? Tikla;

    private int SatirH => Tema.S(52);

    public ArkadasSecimListesi()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Tema.Giris;
        _kaydir = new DikeyKaydirma(this);
    }

    public void Ayarla(IEnumerable<Profil> liste)
    {
        var tr = StringComparer.Create(Tema.Turkce, true);
        _hepsi = liste.OrderBy(p => p.Durum is Durum.Cevrimdisi or Durum.Gorunmez ? 1 : 0).ThenBy(p => p.GorunenAd, tr).ToList();
        Secililer.IntersectWith(_hepsi.Select(p => p.Id));
        Filtrele();
    }

    public string Filtre { get => _filtre; set { _filtre = value ?? ""; Filtrele(); } }

    private void Filtrele()
    {
        var f = _filtre.Trim().TrimStart('@');
        _gorunen = f.Length == 0 ? _hepsi
            : _hepsi.Where(p => p.GorunenAd.Contains(f, StringComparison.CurrentCultureIgnoreCase) || p.KullaniciAdi.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        _kaydir.Ayarla(_gorunen.Count * SatirH, Height);
        _uzerinde = -1;
        Invalidate();
    }

    public bool Dolu => Secililer.Count >= Maks;
    private int SatirW => Width - (_kaydir.Var ? Tema.S(12) : 0);
    private Rectangle SatirAlani(int i) => new(Tema.S(4), i * SatirH - _kaydir.Konum, SatirW - Tema.S(8), SatirH);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_gorunen.Count == 0)
        {
            TextRenderer.DrawText(g, _hepsi.Count == 0 ? BosYazi : "Eşleşen arkadaş bulunamadı.", Tema.Yazi(10),
                new Rectangle(Tema.S(20), Tema.S(24), Width - Tema.S(40), Height - Tema.S(24)), Tema.MetinSoluk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            return;
        }
        for (int i = 0; i < _gorunen.Count; i++)
        {
            var r = SatirAlani(i);
            if (r.Bottom < 0 || r.Top > Height) continue;
            var p = _gorunen[i];
            bool secili = Secililer.Contains(p.Id);
            bool kilitli = Coklu && !secili && Dolu;
            bool uzerinde = i == _uzerinde && !kilitli;
            var zemin = BackColor;
            if (uzerinde) { Cizim.YuvarlakDoldur(g, r, Tema.S(8), Tema.YuzeyUst); zemin = Tema.YuzeyUst; }

            int x = r.X + Tema.S(10);
            if (Coklu)
            {
                var kutu = new Rectangle(x, r.Y + (r.Height - Tema.S(22)) / 2, Tema.S(22), Tema.S(22));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var yol = Tema.YuvarlakDikdortgen(kutu, Tema.S(6)))
                {
                    if (secili) { using var b = new SolidBrush(Tema.Vurgu); g.FillPath(b, yol); }
                    else { using var pen = new Pen(kilitli ? Tema.Kenar : Tema.KenarHover, Tema.S(2)); g.DrawPath(pen, yol); }
                }
                if (secili)
                    TextRenderer.DrawText(g, Glif.Onay, Cizim.Ikon(9), kutu, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x = kutu.Right + Tema.S(12);
            }
            int av = Tema.S(34);
            Cizim.Avatar(g, new RectangleF(x, r.Y + (r.Height - av) / 2f, av, av), p, Avatarlar?.Al(p), zemin);
            if (kilitli)
            {
                using var b = new SolidBrush(Color.FromArgb(130, BackColor));
                g.FillEllipse(b, x, r.Y + (r.Height - av) / 2f, av, av);
            }
            int tx = x + av + Tema.S(12);
            int sagBos = Coklu ? Tema.S(10) : Tema.S(48);
            var ad = kilitli ? Tema.MetinSoluk : Tema.Metin;
            TextRenderer.DrawText(g, p.GorunenAd, Tema.Yazi(10, kalin: true), new Rectangle(tx, r.Y + Tema.S(8), r.Right - tx - sagBos, Tema.S(18)), ad, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "@" + p.KullaniciAdi, Tema.Yazi(9), new Rectangle(tx, r.Y + Tema.S(27), r.Right - tx - sagBos, Tema.S(16)), Tema.MetinSoluk, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (!Coklu)
            {
                var d = new Rectangle(r.Right - Tema.S(40), r.Y + (r.Height - Tema.S(30)) / 2, Tema.S(30), Tema.S(30));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(uzerinde ? Tema.Vurgu : Tema.YuzeyUst)) g.FillEllipse(b, d);
                TextRenderer.DrawText(g, Glif.Arti, Cizim.Ikon(9), d, uzerinde ? Color.White : Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _kaydir.Ciz(g, ClientRectangle);
    }

    private int Bul(Point p)
    {
        if (_kaydir.Seritte(p, ClientRectangle)) return -1;
        int i = (p.Y + _kaydir.Konum) / SatirH;
        return p.Y < 0 || i < 0 || i >= _gorunen.Count ? -1 : i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_kaydir.FareHareket(e.Location, ClientRectangle)) return;
        int i = Bul(e.Location);
        if (i == _uzerinde) return;
        _uzerinde = i;
        Cursor = i >= 0 && !(Coklu && Dolu && !Secililer.Contains(_gorunen[i].Id)) ? Cursors.Hand : Cursors.Default;
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
        var p = _gorunen[i];
        if (!Coklu) { Tikla?.Invoke(p); return; }
        if (!Secililer.Remove(p.Id))
        {
            if (Dolu) return;
            Secililer.Add(p.Id);
        }
        Invalidate();
        SecimDegisti?.Invoke();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_kaydir.Tekerlek(e.Delta, SatirH)) OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, e.X, e.Y, 0));
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); _kaydir.Ayarla(_gorunen.Count * SatirH, Height); }
}
