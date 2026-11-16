using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Windows 10/11'de hazır gelen "Segoe MDL2 Assets" ikon yazı tipindeki semboller. Resim dosyası gerekmez.
public static class Glif
{
    public const string Insanlar = "";
    public const string ArkadasEkle = "";
    public const string Ayarlar = "";
    public const string Onay = "";
    public const string Kapat = "";
    public const string DahaFazla = "";
    public const string Duzenle = "";
    public const string Kamera = "";
    public const string Ara = "";
    public const string Uyari = "";
    public const string Saat = "";
    public const string Mesaj = "";
    public const string Gonder = "";
    public const string Kopyala = "";
    public const string Oyun = "";
    public const string Arti = "";
    public const string Sabitle = "";
    public const string SabitlemeyiKaldir = "";
}

public static class Cizim
{
    public const string IkonAilesi = "Segoe MDL2 Assets";
    private static readonly Dictionary<float, Font> _ikonYazilari = [];

    public static Font Ikon(float punto)
    {
        if (!_ikonYazilari.TryGetValue(punto, out var f)) _ikonYazilari[punto] = f = new Font(IkonAilesi, punto);
        return f;
    }

    public static Color DurumRengi(Durum d) => d switch
    {
        Durum.Cevrimici => Tema.Basari,
        Durum.Bosta => ColorTranslator.FromHtml("#F5B544"),
        Durum.RahatsizEtmeyin => Tema.Hata,
        _ => Tema.MetinSoluk,
    };

    public static string DurumAdi(Durum d) => d switch
    {
        Durum.Cevrimici => "Çevrimiçi",
        Durum.Bosta => "Boşta",
        Durum.RahatsizEtmeyin => "Rahatsız etmeyin",
        Durum.Gorunmez => "Görünmez",
        _ => "Çevrimdışı",
    };

    // Avatar: resim varsa yuvarlak kırpılmış resim, yoksa renkli daire + baş harf. İstenirse sağ altta durum noktası.
    public static void Avatar(Graphics g, RectangleF r, Profil p, Image? resim, Color zemin, bool durumGoster = true)
    {
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (resim != null)
        {
            using var firca = new TextureBrush(resim, WrapMode.Clamp);
            firca.TranslateTransform(r.X, r.Y);
            firca.ScaleTransform(r.Width / resim.Width, r.Height / resim.Height);
            g.FillEllipse(firca, r);
        }
        else
        {
            using var firca = new SolidBrush(ColorTranslator.FromHtml(p.ProfilRengi));
            g.FillEllipse(firca, r);
            var harf = string.IsNullOrWhiteSpace(p.GorunenAd) ? "?" : Tema.Turkce.TextInfo.ToUpper(p.GorunenAd.Trim()[..1]);
            using var yazi = new Font(Tema.YaziAilesiKalin, r.Height * 0.40f, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, harf, yazi, Rectangle.Round(r), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        if (durumGoster)
        {
            float cap = Math.Max(10, r.Width * 0.32f), halka = Math.Max(2.5f, cap * 0.22f);
            var nokta = new RectangleF(r.Right - cap + halka * 0.4f, r.Bottom - cap + halka * 0.4f, cap, cap);
            using (var z = new SolidBrush(zemin)) g.FillEllipse(z, nokta);
            var ic = RectangleF.Inflate(nokta, -halka, -halka);
            using var d = new SolidBrush(DurumRengi(p.Durum));
            g.FillEllipse(d, ic);
            if (p.Durum is Durum.Cevrimdisi or Durum.Gorunmez)
            {
                var delik = RectangleF.Inflate(ic, -ic.Width * 0.27f, -ic.Height * 0.27f);
                using var z2 = new SolidBrush(zemin);
                g.FillEllipse(z2, delik);
            }
        }
        g.SmoothingMode = eski;
    }

    // Kırmızı sayı rozeti (bekleyen istekler için).
    public static void Rozet(Graphics g, Point merkezSag, int sayi)
    {
        var yazi = sayi > 99 ? "99+" : sayi.ToString();
        var f = Tema.Yazi(8, kalin: true);
        var olcu = TextRenderer.MeasureText(yazi, f, Size.Empty, TextFormatFlags.NoPadding);
        int h = Tema.S(18), w = Math.Max(h, olcu.Width + Tema.S(10));
        var r = new Rectangle(merkezSag.X - w, merkezSag.Y - h / 2, w, h);
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(r, h / 2f))
        using (var b = new SolidBrush(Tema.Hata))
            g.FillPath(b, yol);
        g.SmoothingMode = eski;
        TextRenderer.DrawText(g, yazi, f, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    public static void YuvarlakDoldur(Graphics g, RectangleF r, float yaricap, Color renk)
    {
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var yol = Tema.YuvarlakDikdortgen(r, yaricap);
        using var b = new SolidBrush(renk);
        g.FillPath(b, yol);
        g.SmoothingMode = eski;
    }

    // Seçilen resmi ortadan kare kırpıp 128x128 PNG yapar. Sunucuya giden her zaman küçük ve tek tip.
    public static byte[] AvatarHazirla(string dosya)
    {
        using var kaynak = Image.FromFile(dosya);
        int kenar = Math.Min(kaynak.Width, kaynak.Height);
        var kirp = new Rectangle((kaynak.Width - kenar) / 2, (kaynak.Height - kenar) / 2, kenar, kenar);
        using var hedef = new Bitmap(128, 128);
        using (var g = Graphics.FromImage(hedef))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(kaynak, new Rectangle(0, 0, 128, 128), kirp, GraphicsUnit.Pixel);
        }
        using var ms = new MemoryStream();
        hedef.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    public static Image? ResimCoz(byte[] veri)
    {
        try
        {
            using var ms = new MemoryStream(veri);
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return null; }
    }
}

// Sağ tık / açılır menüler için koyu tema.
public class KoyuMenu : ContextMenuStrip
{
    public KoyuMenu()
    {
        Renderer = new Cizici();
        BackColor = Tema.YuzeyUst;
        ForeColor = Tema.Metin;
        Font = Tema.Yazi(10);
        ShowImageMargin = true;
        Padding = new Padding(Tema.S(4));
        Opened += (_, _) => AcikMenu++;
        Closed += (_, _) => AcikMenu = Math.Max(0, AcikMenu - 1);
    }

    // Şu an açık menü sayısı: Esc önce menüyü kapatsın diye overlay buna bakar.
    public static int AcikMenu { get; private set; }

    public ToolStripMenuItem Ekle(string yazi, Action tik, Color? renk = null, Image? resim = null)
    {
        var o = new ToolStripMenuItem(yazi, resim, (_, _) => tik())
        {
            ForeColor = renk ?? Tema.Metin,
            Padding = new Padding(0, Tema.S(5), 0, Tema.S(5)),
        };
        Items.Add(o);
        return o;
    }

    public void Ayirici() => Items.Add(new ToolStripSeparator());

    public static Image Nokta(Color renk)
    {
        int s = Tema.S(12);
        var b = new Bitmap(s, s);
        using var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var f = new SolidBrush(renk);
        g.FillEllipse(f, 1, 1, s - 2, s - 2);
        return b;
    }

    private class Cizici() : ToolStripProfessionalRenderer(new Renkler())
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new Rectangle(Tema.S(2), 0, e.Item.Width - Tema.S(4), e.Item.Height);
            Cizim.YuvarlakDoldur(e.Graphics, r, Tema.S(4), e.Item.ForeColor == Tema.Hata ? Color.FromArgb(50, Tema.Hata) : Tema.Vurgu);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.ForeColor != Tema.Hata) e.TextColor = Color.White;
            else e.TextColor = e.Item.ForeColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var p = new Pen(Tema.Kenar);
            e.Graphics.DrawLine(p, Tema.S(8), y, e.Item.Width - Tema.S(8), y);
        }
    }

    private class Renkler : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Tema.YuzeyUst;
        public override Color ImageMarginGradientBegin => Tema.YuzeyUst;
        public override Color ImageMarginGradientMiddle => Tema.YuzeyUst;
        public override Color ImageMarginGradientEnd => Tema.YuzeyUst;
        public override Color MenuBorder => Tema.Kenar;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Tema.Vurgu;
    }
}

// Koyu ipucu balonu.
public class KoyuIpucu : ToolTip
{
    public KoyuIpucu()
    {
        OwnerDraw = true;
        InitialDelay = 400;
        Popup += (_, e) =>
        {
            var o = TextRenderer.MeasureText(GetToolTip(e.AssociatedControl), Tema.Yazi(9.5f, kalin: true));
            e.ToolTipSize = new Size(o.Width + Tema.S(16), o.Height + Tema.S(10));
        };
        Draw += (_, e) =>
        {
            e.Graphics.Clear(ColorTranslator.FromHtml("#0B0B0E"));
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Tema.Yazi(9.5f, kalin: true), e.Bounds, Tema.Metin,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }
}
