using Ekip.Ortak;

namespace Ekip.Mobil;

// Masaüstüyle aynı koyu tema (Ekip.Uygulama/Tema.cs ile aynı renkler) ve küçük görünüm yardımcıları.
public static class Tema
{
    public const string UygulamaAdi = "Uygulama";

    public static readonly Color Arkaplan = Color.FromArgb("#15161A");
    public static readonly Color Yuzey = Color.FromArgb("#1C1D22");
    public static readonly Color YuzeyUst = Color.FromArgb("#24252C");
    public static readonly Color Giris = Color.FromArgb("#121317");
    public static readonly Color Kenar = Color.FromArgb("#2E3039");
    public static readonly Color Metin = Color.FromArgb("#ECEDF1");
    public static readonly Color MetinIkincil = Color.FromArgb("#A3A6B4");
    public static readonly Color MetinSoluk = Color.FromArgb("#6C6F7E");
    public static readonly Color Vurgu = Color.FromArgb("#7C5CFF");
    public static readonly Color VurguKoyu = Color.FromArgb("#2B1F66");
    public static readonly Color Hata = Color.FromArgb("#F2637A");
    public static readonly Color Basari = Color.FromArgb("#3DDC97");
    public static readonly Color Bahsetme = Color.FromArgb("#3A3216");

    public static Color DurumRengi(Durum d) => d switch
    {
        Durum.Cevrimici => Basari,
        Durum.Bosta => Color.FromArgb("#F5B544"),
        Durum.RahatsizEtmeyin => Hata,
        _ => MetinSoluk,
    };

    public static string DurumAdi(Durum d) => d switch
    {
        Durum.Cevrimici => "Çevrimiçi",
        Durum.Bosta => "Boşta",
        Durum.RahatsizEtmeyin => "Rahatsız etmeyin",
        Durum.Gorunmez => "Görünmez",
        _ => "Çevrimdışı",
    };

    public static string BasHarfler(string ad)
    {
        var p = ad.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = p.Length switch { 0 => "?", 1 => p[0][..1], _ => p[0][..1] + p[1][..1] };
        return s.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
    }

    public static Label Yazi(string metin, double boyut = 15, Color? renk = null, bool kalin = false) => new()
    {
        Text = metin, FontSize = boyut, TextColor = renk ?? Metin,
        FontAttributes = kalin ? FontAttributes.Bold : FontAttributes.None,
        LineBreakMode = LineBreakMode.TailTruncation,
    };

    public static Button Dugme(string metin, bool birincil = true) => new()
    {
        Text = metin, FontSize = 15, FontAttributes = FontAttributes.Bold, CornerRadius = 10, HeightRequest = 48,
        BackgroundColor = birincil ? Vurgu : YuzeyUst, TextColor = Colors.White, Padding = new Thickness(18, 0),
    };

    public static Entry Kutu(string ipucu, bool sifre = false) => new()
    {
        Placeholder = ipucu, IsPassword = sifre, FontSize = 16, TextColor = Metin, PlaceholderColor = MetinSoluk,
        BackgroundColor = Giris, HeightRequest = 50,
    };

    // Kutuya çerçeve: Entry Android'de alt çizgiyle gelir; yuvarlak koyu kutu içine alınır.
    public static Border Cerceve(View icerik) => new()
    {
        Content = icerik, BackgroundColor = Giris, Stroke = Kenar, StrokeThickness = 1,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Padding = new Thickness(12, 0),
    };
}
