using Ekip.Ortak;
using Ekip.Uygulama;

namespace Ekip.Mobil;

// Giriş / kayıt. Kayıtlı oturum varsa şifre sormadan doğrudan girer (bağlantı anlık koparsa birkaç kez dener).
public sealed class GirisSayfasi : ContentPage
{
    private readonly Entry _ad = Tema.Kutu("Kullanıcı adı");
    private readonly Entry _gorunen = Tema.Kutu("Görünen ad");
    private readonly Entry _sifre = Tema.Kutu("Şifre", sifre: true);
    private readonly Entry _kod = Tema.Kutu("Davet kodu (ör. ABCD-EFGH)");
    private readonly Border _gorunenKutu, _kodKutu;
    private readonly Label _baslik = Tema.Yazi("Tekrar hoş geldin!", 26, kalin: true);
    private readonly Label _alt = Tema.Yazi("Arkadaşların seni bekliyor.", 15, Tema.MetinIkincil);
    private readonly Label _hata = new() { FontSize = 14, TextColor = Tema.Hata, IsVisible = false };
    private readonly Button _gonder = Tema.Dugme("Giriş yap");
    private readonly Label _degistir = new() { FontSize = 15, TextColor = Tema.Vurgu, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center };
    private readonly ActivityIndicator _yukleniyor = new() { Color = Tema.Vurgu, IsRunning = false, IsVisible = false };
    private bool _kayit, _otomatikDenendi;

    public GirisSayfasi()
    {
        BackgroundColor = Tema.Arkaplan;
        Shell.SetNavBarIsVisible(this, false);
        _ad.Keyboard = Keyboard.Plain;
        _gorunenKutu = Tema.Cerceve(_gorunen);
        _kodKutu = Tema.Cerceve(_kod);
        _gonder.Clicked += async (_, _) => await Gonder();
        _sifre.Completed += async (_, _) => await Gonder();
        _degistir.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Mod(!_kayit)) });

        var logo = new Image { Source = "splash.png", WidthRequest = 72, HeightRequest = 72, HorizontalOptions = LayoutOptions.Start };
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 48, 24, 24), Spacing = 12,
                Children =
                {
                    new HorizontalStackLayout { Spacing = 12, Children = { logo, new Label { Text = Tema.UygulamaAdi, FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Tema.Metin, VerticalOptions = LayoutOptions.Center } } },
                    new BoxView { HeightRequest = 24, Color = Colors.Transparent },
                    _baslik, _alt,
                    new BoxView { HeightRequest = 8, Color = Colors.Transparent },
                    Tema.Cerceve(_ad), _gorunenKutu, Tema.Cerceve(_sifre), _kodKutu,
                    _hata, _gonder, _yukleniyor, _degistir,
                },
            },
        };
        Mod(false);
    }

    private void Mod(bool kayit)
    {
        _kayit = kayit;
        _baslik.Text = kayit ? "Hesap oluştur" : "Tekrar hoş geldin!";
        _alt.Text = kayit ? "Birkaç saniye sürer, sonra aramıza katıl." : "Arkadaşların seni bekliyor.";
        _gorunenKutu.IsVisible = _kodKutu.IsVisible = kayit;
        _gonder.Text = kayit ? "Kayıt ol" : "Giriş yap";
        _degistir.Text = kayit ? "Zaten hesabın var mı? Giriş yap" : "Hesabın yok mu? Kayıt ol";
        _hata.IsVisible = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_otomatikDenendi) return;
        _otomatikDenendi = true;
        var token = await Oturum.KayitliToken();
        if (token is null) return;

        Mesgul(true);
        var api = new Api(Oturum.SunucuAdresi) { Token = token };
        var s = await api.Ben();
        for (int i = 1; !s.Basarili && s.Durum is null && i <= 4; i++) { await Task.Delay(i * 2000); s = await api.Ben(); }
        Mesgul(false);
        if (s.Basarili) { await Gir(api, s.Veri!, kaydet: false); return; }
        if (s.Durum == System.Net.HttpStatusCode.Unauthorized) SecureStorage.Remove("token");
        Hata(s.Hata);
    }

    private async Task Gonder()
    {
        if (_yukleniyor.IsRunning) return;
        var ad = _ad.Text?.Trim() ?? "";
        var sifre = _sifre.Text ?? "";
        if (ad.Length == 0 || sifre.Length == 0) { Hata("Kullanıcı adı ve şifre gerekli."); return; }

        Mesgul(true);
        var api = new Api(Oturum.SunucuAdresi);
        var s = _kayit
            ? await api.KayitOl(new KayitIstegi(ad, (_gorunen.Text ?? "").Trim(), sifre, _kod.Text))
            : await api.GirisYap(new GirisIstegi(ad, sifre));
        if (!s.Basarili) { Mesgul(false); Hata(s.Hata); return; }
        api.Token = s.Veri!.Token;
        var ben = await api.Ben();
        Mesgul(false);
        if (!ben.Basarili) { Hata(ben.Hata); return; }
        await Gir(api, ben.Veri!, kaydet: true);
    }

    private async Task Gir(Api api, Profil ben, bool kaydet)
    {
        await Oturum.Baslat(api, ben, kaydet);
        if (Window is { } w) w.Page = new AnaSayfa();
    }

    private void Mesgul(bool m)
    {
        _yukleniyor.IsRunning = _yukleniyor.IsVisible = m;
        _gonder.IsEnabled = !m;
        if (m) _hata.IsVisible = false;
    }

    private void Hata(string? m) { _hata.Text = m ?? "Bir sorun oldu."; _hata.IsVisible = true; }
}
