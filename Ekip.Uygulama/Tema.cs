using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
#pragma warning disable CA1416

namespace Ekip.Uygulama;

// Tüm renkler, yazı tipleri ve ölçüler tek yerde. Arayüzün tutarlı görünmesi buna bağlı.
public static class Tema
{
    public static readonly Color Arkaplan = Renk("#15161A");
    public static readonly Color Yuzey = Renk("#1C1D22");
    public static readonly Color YuzeyUst = Renk("#24252C");
    public static readonly Color Giris = Renk("#121317");
    public static readonly Color Kenar = Renk("#2E3039");
    public static readonly Color KenarHover = Renk("#3D4050");

    public static readonly Color Metin = Renk("#ECEDF1");
    public static readonly Color MetinIkincil = Renk("#A3A6B4");
    public static readonly Color MetinSoluk = Renk("#6C6F7E");

    public static readonly Color Vurgu = Renk("#7C5CFF");
    public static readonly Color VurguHover = Renk("#8D71FF");
    public static readonly Color VurguBasili = Renk("#6A4AE8");
    public static readonly Color VurguKoyu = Renk("#2B1F66");

    public static readonly Color Hata = Renk("#F2637A");
    public static readonly Color Basari = Renk("#3DDC97");

    // Avatar arka planları için hoş, birbirinden ayrışan renkler.
    private static readonly Color[] AvatarRenkleri =
        new[] { "#7C5CFF", "#3DA9FC", "#2CB67D", "#F59E0B", "#EF4565", "#E862C9", "#14B8A6", "#F97316" }
        .Select(Renk).ToArray();

    public static readonly System.Globalization.CultureInfo Turkce = new("tr-TR");

    // Uygulamanın görünen adı. Her yerde bunu kullan (pencere başlığı, tepsi, bildirimler).
    public const string UygulamaAdi = "31Hane";

    // Uygulama ikonu: Varliklar\31hane.ico (exe'nin içine gömülü). Windows ihtiyaç duyduğu boyutu kendisi seçer.
    private static Icon? _ikon;
    public static Icon Ikon => _ikon ??= new Icon(typeof(Tema).Assembly.GetManifestResourceStream("uygulama.ico")!);

    // Logo: mor zemin üstünde beyaz ev, gövdesinde "31". Varliklar\logo-uret.ps1 ile birebir aynı (256'lık ızgara).
    // Çok küçük çizimlerde (24 px altı) rakam okunmayacağı için kapı çizilir.
    public static void Logo(Graphics g, RectangleF r, bool zemin = true)
    {
        var eskiD = g.Transform;
        var eskiS = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 256f, r.Height / 256f);
        var mor = ColorTranslator.FromHtml("#5B3BE8");
        if (zemin)
        {
            using var yol = YuvarlakDikdortgen(new RectangleF(0, 0, 256, 256), 56);
            using var f = new LinearGradientBrush(new Rectangle(0, 0, 256, 256), ColorTranslator.FromHtml("#9277FF"), mor, 45f);
            g.FillPath(f, yol);
        }
        using (var ev = new GraphicsPath())
        {
            ev.AddPolygon(new PointF[] { new(128, 42), new(228, 126), new(200, 126), new(200, 214), new(56, 214), new(56, 126), new(28, 126) });
            g.FillPath(Brushes.White, ev);
            using var kenar = new Pen(Color.White, 14) { LineJoin = LineJoin.Round };
            g.DrawPath(kenar, ev);
        }
        g.FillRectangle(Brushes.White, 162, 60, 22, 50); // baca
        // Zeminsiz çizimde (koyu arka plan) rakam/kapı arka plan rengini alsın diye de mor kalır.
        using var b = new SolidBrush(mor);
        if (r.Width >= 24)
        {
            using var yazi = new GraphicsPath();
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var aile = new FontFamily("Segoe UI Black");
            yazi.AddString("31", aile, 0, 98, new RectangleF(56, 122, 144, 96), sf);
            g.FillPath(b, yazi);
        }
        else g.FillRectangle(b, 108, 158, 40, 56);
        g.Transform = eskiD;
        g.SmoothingMode = eskiS;
    }

    public const string YaziAilesi = "Segoe UI";
    public const string YaziAilesiKalin = "Segoe UI Semibold";

    private static readonly Dictionary<(string, float, FontStyle), Font> _yazilar = [];

    public static Font Yazi(float punto, bool kalin = false, FontStyle stil = FontStyle.Regular)
    {
        var anahtar = (kalin ? YaziAilesiKalin : YaziAilesi, punto, stil);
        if (!_yazilar.TryGetValue(anahtar, out var f))
            _yazilar[anahtar] = f = new Font(anahtar.Item1, punto, stil);
        return f;
    }

    // Tasarım 96 DPI için yapılıyor, ekran ölçeğine göre büyütülür.
    public static float Olcek { get; private set; } = 1f;
    public static int S(int px) => (int)Math.Round(px * Olcek);

    public static void OlcekBelirle()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        Olcek = g.DpiX / 96f;
    }

    public static Color AvatarRengi(string anahtar)
    {
        uint h = 2166136261;
        foreach (var c in anahtar.ToLowerInvariant()) h = (h ^ c) * 16777619;
        return AvatarRenkleri[h % (uint)AvatarRenkleri.Length];
    }

    public static GraphicsPath YuvarlakDikdortgen(RectangleF r, float yaricap)
    {
        var p = new GraphicsPath();
        var d = Math.Min(yaricap * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static Color Renk(string hex) => ColorTranslator.FromHtml(hex);

    // Pencere başlık çubuğunu koyu yapar (Windows 10 20H1+ ve 11). Desteklenmiyorsa sessizce geçer.
    public static void KoyuBaslik(IntPtr hwnd, Color baslikRengi)
    {
        int evet = 1;
        DwmSetWindowAttribute(hwnd, 20, ref evet, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        int renk = baslikRengi.R | (baslikRengi.G << 8) | (baslikRengi.B << 16);
        DwmSetWindowAttribute(hwnd, 35, ref renk, sizeof(int)); // DWMWA_CAPTION_COLOR (sadece Win11)
    }

    // Pencere odakta değilken görev çubuğunda yanıp söner (yeni mesaj geldiğinde).
    public static void Titret(Form f)
    {
        if (Form.ActiveForm == f || !f.IsHandleCreated) return;
        var bilgi = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = f.Handle, dwFlags = 2 | 12, uCount = 3 };
        FlashWindowEx(ref bilgi);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO bilgi);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

// Tüm pencerelerin ortak tabanı: koyu başlık, tema rengi, titremesiz çizim.
public class TemaliForm : Form
{
    public TemaliForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Tema.Arkaplan;
        ForeColor = Tema.Metin;
        Font = Tema.Yazi(10);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        Icon = Tema.Ikon;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.KoyuBaslik(Handle, Tema.Arkaplan);
    }
}
