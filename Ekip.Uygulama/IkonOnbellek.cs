using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Sunucu ve grup ikonları: bir kere indirilir, sürüm değişene kadar bellekte tutulur (AvatarOnbellek gibi).
// tur: "sunucu" | "grup". Sürüm 0 = ikon yok.
public class IkonOnbellek(Api api)
{
    private readonly Dictionary<(string, long), (int surum, Image? resim)> _resimler = [];
    private readonly HashSet<(string, long, int)> _iniyor = [];

    public event Action? Yuklendi;

    public Image? Al(string tur, long id, int surum)
    {
        if (surum == 0) return null;
        if (_resimler.TryGetValue((tur, id), out var k) && k.surum == surum) return k.resim;
        if (_iniyor.Add((tur, id, surum))) _ = Indir(tur, id, surum);
        return _resimler.TryGetValue((tur, id), out k) ? k.resim : null; // Yenisi gelene kadar eskisi görünsün.
    }

    public Image? Sunucu(long id, int surum) => Al("sunucu", id, surum);
    public Image? Grup(long id, int surum) => Al("grup", id, surum);

    // Kendi yüklediğimiz resmi tekrar indirmeye gerek yok.
    public void Koy(string tur, long id, int surum, Image? resim)
    {
        if (_resimler.TryGetValue((tur, id), out var eski) && eski.resim != resim) eski.resim?.Dispose();
        _resimler[(tur, id)] = (surum, resim);
        Yuklendi?.Invoke();
    }

    // Silinen sunucu/grubun ikonunu bellekten at.
    public void Unut(string tur, long id)
    {
        if (_resimler.Remove((tur, id), out var eski)) eski.resim?.Dispose();
    }

    private async Task Indir(string tur, long id, int surum)
    {
        var veri = await api.IkonIndir(tur, id, surum);
        _iniyor.Remove((tur, id, surum));
        // Bozuk/eksik resim olursa null saklarız; baş harfli ikon gösterilir ve tekrar tekrar indirilmez.
        Koy(tur, id, surum, veri is null ? null : Cizim.ResimCoz(veri));
    }
}

// Sunucu ve grup ikonlarının ortak çizimi (ray, kanal listesi, keşfet, önizleme hepsi aynı görünsün diye).
public static class IkonCizim
{
    public static Color RenkCoz(string? hex)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? Tema.Vurgu : ColorTranslator.FromHtml(hex); }
        catch { return Tema.Vurgu; }
    }

    // "Oyun Gecesi" -> "OG", "Ekip" -> "E". En fazla iki baş harf.
    public static string BasHarfler(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad)) return "?";
        var parcalar = ad.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var s = string.Concat(parcalar.Take(2).Select(p => p.TrimStart('#', '@')).Where(p => p.Length > 0).Select(p => p[..1]));
        return s.Length == 0 ? "?" : Tema.Turkce.TextInfo.ToUpper(s);
    }

    // Yuvarlak köşeli kare sunucu ikonu: resim varsa resim (şekle kırpılı), yoksa renkli zemin + baş harfler.
    public static void Sunucu(Graphics g, RectangleF r, string ad, string renk, Image? resim, float yaricap)
    {
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var yol = Tema.YuvarlakDikdortgen(r, yaricap);
        if (resim != null)
        {
            using var firca = new TextureBrush(resim, WrapMode.Clamp);
            firca.TranslateTransform(r.X, r.Y);
            firca.ScaleTransform(r.Width / resim.Width, r.Height / resim.Height);
            g.FillPath(firca, yol);
        }
        else
        {
            using var firca = new SolidBrush(RenkCoz(renk));
            g.FillPath(firca, yol);
            var harfler = BasHarfler(ad);
            float oran = harfler.Length > 1 ? 0.34f : 0.42f;
            using var yazi = new Font(Tema.YaziAilesiKalin, r.Height * oran, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, harfler, yazi, Rectangle.Round(r), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        }
        g.SmoothingMode = eski;
    }

    // Yuvarlak grup ikonu: resim varsa resim, yoksa renkli daire + "insanlar" simgesi.
    public static void Grup(Graphics g, RectangleF r, string renk, Image? resim)
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
            using var firca = new SolidBrush(RenkCoz(renk));
            g.FillEllipse(firca, r);
            using var f = new Font(Cizim.IkonAilesi, r.Height * 0.40f, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, Glif.Insanlar, f, Rectangle.Round(r), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        }
        g.SmoothingMode = eski;
    }

    // Küçük yazı rozeti (SAHİP, YÖNETİCİ...). Sağ kenarı verilen x'e koyar; çizilen alanı döner.
    public static Rectangle YaziRozeti(Graphics g, int x, int yOrta, string yazi, Color renk)
    {
        var f = Tema.Yazi(7.5f, kalin: true);
        int w = TextRenderer.MeasureText(yazi, f, Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(10);
        var r = new Rectangle(x, yOrta - Tema.S(8), w, Tema.S(16));
        Cizim.YuvarlakDoldur(g, r, Tema.S(4), Color.FromArgb(45, renk));
        TextRenderer.DrawText(g, yazi, f, r, renk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        return r;
    }
}
