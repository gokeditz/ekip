using Ekip.Ortak;
using Microsoft.Maui.Controls.Shapes;

namespace Ekip.Mobil;

// Yuvarlak avatar: profil resmi varsa o, yoksa baş harfler + profil rengi. İsteğe bağlı durum noktası.
public sealed class Avatar : Grid
{
    private readonly Border _daire;
    private readonly Label _harf;
    private readonly Image _resim;
    private readonly Border _nokta;

    public Avatar(double boyut, bool durumGoster = false)
    {
        WidthRequest = HeightRequest = boyut;
        _harf = new Label { FontSize = boyut * 0.4, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _resim = new Image { Aspect = Aspect.AspectFill, IsVisible = false };
        _daire = new Border
        {
            StrokeThickness = 0, StrokeShape = new Ellipse(), WidthRequest = boyut, HeightRequest = boyut,
            Content = new Grid { Children = { _harf, _resim } },
        };
        double n = Math.Max(10, boyut * 0.3);
        _nokta = new Border
        {
            WidthRequest = n, HeightRequest = n, StrokeShape = new Ellipse(), Stroke = Tema.Arkaplan, StrokeThickness = 2.5,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, IsVisible = durumGoster,
        };
        Children.Add(_daire);
        Children.Add(_nokta);
    }

    public void Ayarla(string ad, string renk, Durum? durum = null)
    {
        _harf.Text = Tema.BasHarfler(ad);
        _daire.BackgroundColor = Color.FromArgb(renk);
        if (durum is { } d) _nokta.BackgroundColor = Tema.DurumRengi(d);
    }

    public async void Profil(Profil p, bool durum = false)
    {
        Ayarla(p.GorunenAd, p.ProfilRengi, durum ? p.Durum : null);
        _nokta.IsVisible = durum;
        _resim.IsVisible = false;
        if (Oturum.Aktif is not { } o) return;
        var r = await o.Avatar(p);
        if (r != null) { _resim.Source = r; _resim.IsVisible = true; }
    }
}

// Liste satırı: avatar + başlık + alt yazı + sağda okunmamış rozeti.
public sealed class ListeSatiri : Grid
{
    public Avatar Avatar { get; } = new(44, durumGoster: true);
    private readonly Label _baslik = Tema.Yazi("", 16, kalin: true);
    private readonly Label _alt = Tema.Yazi("", 13.5, Tema.MetinIkincil);
    private readonly Border _rozet;
    private readonly Label _rozetYazi = new() { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };

    public ListeSatiri()
    {
        Padding = new Thickness(16, 10);
        ColumnSpacing = 12;
        ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)];
        _rozet = new Border { BackgroundColor = Tema.Hata, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 11 }, HeightRequest = 22, MinimumWidthRequest = 22, Padding = new Thickness(6, 0), Content = _rozetYazi, VerticalOptions = LayoutOptions.Center, IsVisible = false };
        var metin = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { _baslik, _alt } };
        this.Add(Avatar, 0);
        this.Add(metin, 1);
        this.Add(_rozet, 2);
    }

    public void Ayarla(string baslik, string? alt, int okunmamis)
    {
        _baslik.Text = baslik;
        _alt.Text = alt ?? "";
        _alt.IsVisible = !string.IsNullOrEmpty(alt);
        _baslik.TextColor = okunmamis > 0 ? Tema.Metin : Color.FromArgb("#D5D7DE");
        _rozet.IsVisible = okunmamis > 0;
        _rozetYazi.Text = okunmamis > 99 ? "99+" : okunmamis.ToString();
    }
}

public static class Bicim
{
    public static string MesajOzeti(Mesaj? m)
    {
        if (m is null) return "";
        if (m.Silindi) return "Bu mesaj silindi.";
        if (!string.IsNullOrWhiteSpace(m.Metin)) return m.Metin.Replace('\n', ' ');
        var ek = m.Ekler?.FirstOrDefault();
        return ek is null ? "" : ek.Tur switch { "resim" => "📷 Fotoğraf", "gif" => "🎞️ GIF", "video" => "🎬 Video", "ses" => "🎵 Ses", _ => "📎 " + ek.Ad };
    }

    public static string Zaman(long ms)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
        if (t.Date == DateTime.Today) return t.ToString("HH:mm");
        if (t.Date == DateTime.Today.AddDays(-1)) return "Dün " + t.ToString("HH:mm");
        return t.ToString("dd.MM.yyyy HH:mm");
    }
}
