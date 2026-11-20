using Ekip.Ortak;
using Microsoft.Maui.Controls.Shapes;

namespace Ekip.Mobil;

// Keşfet: uygulamaya kayıtlı herkes; arkadaş ekle / kabul et / mesaj gönder.
public sealed class KesfetSayfasi : ContentPage
{
    private readonly SearchBar _ara = new() { Placeholder = "Ad, @kullanıcıadı ya da #numara", TextColor = Tema.Metin, PlaceholderColor = Tema.MetinSoluk, BackgroundColor = Tema.Yuzey, CancelButtonColor = Tema.MetinIkincil };
    private readonly CollectionView _liste;
    private CancellationTokenSource? _bekle;

    public KesfetSayfasi()
    {
        Title = "Keşfet";
        BackgroundColor = Tema.Arkaplan;
        _liste = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var s = new ListeSatiri();
                var dugme = new Button { FontSize = 13, FontAttributes = FontAttributes.Bold, CornerRadius = 8, HeightRequest = 36, Padding = new Thickness(12, 0), TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
                var g = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], Padding = new Thickness(0, 0, 16, 0) };
                g.Add(s, 0); g.Add(dugme, 1);
                g.BindingContextChanged += (_, _) =>
                {
                    if (g.BindingContext is not KesfetKullanicisi k) return;
                    s.Ayarla(k.Profil.GorunenAd, $"@{k.Profil.KullaniciAdi} · #{k.Profil.Id}", 0);
                    s.Avatar.Profil(k.Profil, durum: k.Iliski == 1);
                    (dugme.Text, dugme.BackgroundColor, dugme.IsEnabled) = k.Iliski switch
                    {
                        1 => ("Mesaj", Tema.YuzeyUst, true),
                        2 => ("İstek gitti", Tema.Yuzey, false),
                        3 => ("Kabul et", Color.FromArgb("#1F6B4D"), true),
                        _ => ("Ekle", Tema.Vurgu, true),
                    };
                };
                dugme.Clicked += async (_, _) => { if (g.BindingContext is KesfetKullanicisi k) await Eylem(k); };
                return g;
            }),
        };
        var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)], Padding = new Thickness(0, 8, 0, 0) };
        grid.Add(_ara, 0, 0);
        grid.Add(_liste, 0, 1);
        Content = grid;
        _ara.TextChanged += async (_, _) =>
        {
            _bekle?.Cancel();
            _bekle = new CancellationTokenSource();
            var t = _bekle.Token;
            try { await Task.Delay(350, t); } catch { return; }
            await Yenile();
        };
    }

    protected override async void OnAppearing() { base.OnAppearing(); await Yenile(); }

    private async Task Yenile()
    {
        if (Oturum.Aktif is not { } o) return;
        var s = await o.Api.KesfetKullanicilar(_ara.Text);
        if (s.Basarili) _liste.ItemsSource = s.Veri;
    }

    private async Task Eylem(KesfetKullanicisi k)
    {
        if (Oturum.Aktif is not { } o) return;
        switch (k.Iliski)
        {
            case 1:
                await Navigation.PushAsync(new SohbetSayfasi(new(KonusmaTuru.Dm, k.Profil.Id), k.Profil.GorunenAd, [k.Profil]));
                return;
            case 3:
                var kabul = await o.Api.IstekKabul(k.Profil.Id);
                if (!kabul.Basarili) await DisplayAlertAsync("Olmadı", kabul.Hata, "Tamam");
                break;
            default:
                var istek = await o.Api.IstekGonderId(k.Profil.Id);
                if (!istek.Basarili) await DisplayAlertAsync("Olmadı", istek.Hata, "Tamam");
                break;
        }
        await o.ArkadaslariYenile();
        await Yenile();
    }
}

// Profil: ben, kimliğim (kopyalanabilir), durum, çıkış.
public sealed class ProfilSayfasi : ContentPage
{
    private readonly Avatar _avatar = new(88, durumGoster: true);
    private readonly Label _ad = Tema.Yazi("", 24, kalin: true);
    private readonly Label _kimlik = Tema.Yazi("", 15, Tema.MetinIkincil);
    private readonly Label _durum = Tema.Yazi("", 15, Tema.MetinIkincil);

    public ProfilSayfasi()
    {
        Title = "Profil";
        BackgroundColor = Tema.Arkaplan;
        var kopyala = Tema.Dugme("Kimliğimi kopyala", birincil: false);
        kopyala.Clicked += async (_, _) =>
        {
            if (Oturum.Aktif is not { } o) return;
            await Clipboard.SetTextAsync("#" + o.Ben.Id);
            await DisplayAlertAsync("Kopyalandı", $"#{o.Ben.Id} — arkadaşların bunu \"Arkadaş ekle\"ye yazabilir.", "Tamam");
        };
        var durumDegistir = Tema.Dugme("Durumu değiştir", birincil: false);
        durumDegistir.Clicked += async (_, _) => await DurumSec();
        var cikis = Tema.Dugme("Çıkış yap", birincil: false);
        cikis.TextColor = Tema.Hata;
        cikis.Clicked += async (_, _) =>
        {
            if (Oturum.Aktif is { } o && await DisplayAlertAsync("Çıkış yap", "Bu telefondaki oturum kapanacak.", "Çıkış yap", "Vazgeç")) await o.Cikis();
        };
        var surum = Tema.Yazi($"{Tema.UygulamaAdi} {AppInfo.Current.VersionString} · Android", 13, Tema.MetinSoluk);
        surum.HorizontalOptions = LayoutOptions.Center;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24, Spacing = 12,
                Children =
                {
                    _avatar, _ad, _kimlik, _durum,
                    new BoxView { HeightRequest = 12, Color = Colors.Transparent },
                    kopyala, durumDegistir, cikis,
                    new BoxView { HeightRequest = 24, Color = Colors.Transparent },
                    surum,
                },
            },
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Oturum.Aktif is not { } o) return;
        _avatar.Profil(o.Ben, durum: true);
        _ad.Text = o.Ben.GorunenAd;
        _kimlik.Text = $"@{o.Ben.KullaniciAdi} · #{o.Ben.Id}";
        _durum.Text = string.IsNullOrWhiteSpace(o.Ben.DurumMesaji) ? Tema.DurumAdi(o.Ben.Durum) : o.Ben.DurumMesaji;
    }

    private async Task DurumSec()
    {
        if (Oturum.Aktif is not { } o) return;
        var secenekler = new[] { Durum.Cevrimici, Durum.Bosta, Durum.RahatsizEtmeyin, Durum.Gorunmez };
        var sec = await DisplayActionSheetAsync("Durumun", "Vazgeç", null, secenekler.Select(Tema.DurumAdi).ToArray());
        var d = secenekler.FirstOrDefault(x => Tema.DurumAdi(x) == sec, Durum.Cevrimdisi);
        if (d == Durum.Cevrimdisi) return;
        await o.Canli.DurumAyarla(d);
        o.Ben = o.Ben with { Durum = d };
        OnAppearing();
    }
}
