using Ekip.Ortak;

namespace Ekip.Mobil;

// Alt sekmeler: Sohbetler, Sunucular, Keşfet, Profil.
public sealed class AnaSayfa : TabbedPage
{
    public AnaSayfa()
    {
        BackgroundColor = Tema.Arkaplan;
        BarBackgroundColor = Tema.Yuzey;
        SelectedTabColor = Tema.Vurgu;
        UnselectedTabColor = Tema.MetinSoluk;
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetToolbarPlacement(this,
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.ToolbarPlacement.Bottom);
        Children.Add(Sekme(new SohbetlerSayfasi(), "Sohbetler"));
        Children.Add(Sekme(new SunucularSayfasi(), "Sunucular"));
        Children.Add(Sekme(new KesfetSayfasi(), "Keşfet"));
        Children.Add(Sekme(new ProfilSayfasi(), "Profil"));
    }

    private static NavigationPage Sekme(Page p, string baslik) => new(p)
    {
        Title = baslik, BarBackgroundColor = Tema.Yuzey, BarTextColor = Tema.Metin, BackgroundColor = Tema.Arkaplan,
    };
}

// Sohbetler: DM'ler ve gruplar (son mesaja göre), altında henüz yazışmadığın arkadaşlar.
public sealed class SohbetlerSayfasi : ContentPage
{
    private sealed record Oge(KonusmaKimligi K, string Baslik, string? Alt, int Okunmamis, long Zaman, Profil? Kisi, Grup? Grup);

    private readonly CollectionView _liste;
    private readonly RefreshView _yenile;
    private List<Grup> _gruplar = [];

    public SohbetlerSayfasi()
    {
        Title = "Sohbetler";
        BackgroundColor = Tema.Arkaplan;
        _liste = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var s = new ListeSatiri();
                s.BindingContextChanged += (_, _) =>
                {
                    if (s.BindingContext is not Oge o) return;
                    s.Ayarla(o.Baslik, o.Alt, o.Okunmamis);
                    if (o.Kisi != null) s.Avatar.Profil(o.Kisi, durum: true);
                    else if (o.Grup != null) s.Avatar.Ayarla(o.Baslik, o.Grup.Renk);
                };
                s.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { if (s.BindingContext is Oge o) Ac(o); }) });
                return s;
            }),
            EmptyView = new Label { Text = "Henüz sohbet yok. Keşfet'ten arkadaş ekleyebilirsin.", TextColor = Tema.MetinIkincil, HorizontalTextAlignment = TextAlignment.Center, Margin = 32 },
        };
        _yenile = new RefreshView { Content = _liste, RefreshColor = Tema.Vurgu, Command = new Command(async () => await Yenile()) };
        Content = _yenile;

        if (Oturum.Aktif is { } o)
        {
            o.Canli.MesajGeldi += m => { _ = Yenile(); };
            o.Canli.GruplarDegisti += () => _ = Yenile();
            o.Canli.DurumDegisti += (_, _) => _ = Yenile();
            o.ArkadaslarYenilendi += () => _ = Yenile();
        }
    }

    protected override async void OnAppearing() { base.OnAppearing(); await Yenile(); }

    private async Task Yenile()
    {
        if (Oturum.Aktif is not { } o) return;
        var sohbet = o.Api.Sohbetler();
        var grup = o.Api.Gruplar();
        var s = await sohbet;
        var g = await grup;
        if (g.Basarili) _gruplar = g.Veri!;

        var ogeler = new List<Oge>();
        foreach (var x in s.Veri ?? [])
            ogeler.Add(new(new(KonusmaTuru.Dm, x.Karsi.Id), x.Karsi.GorunenAd, Bicim.MesajOzeti(x.SonMesaj), x.Okunmamis, x.SonMesaj?.Zaman ?? 0, x.Karsi, null));
        foreach (var x in _gruplar)
            ogeler.Add(new(new(KonusmaTuru.Grup, x.Id), GrupAdi(x), x.SonMesaj is { } m ? Bicim.MesajOzeti(m) : $"{x.Uyeler.Count} üye", x.Okunmamis, x.SonMesaj?.Zaman ?? 0, null, x));
        ogeler = ogeler.OrderByDescending(x => x.Zaman).ToList();
        // Yazışmadığın arkadaşlar en altta.
        foreach (var p in o.Arkadaslar.Arkadaslar.Where(p => ogeler.All(x => x.Kisi?.Id != p.Id)).OrderBy(p => p.GorunenAd))
            ogeler.Add(new(new(KonusmaTuru.Dm, p.Id), p.GorunenAd, Tema.DurumAdi(p.Durum), 0, 0, p, null));

        _liste.ItemsSource = ogeler;
        _yenile.IsRefreshing = false;
    }

    private static string GrupAdi(Grup g) => string.IsNullOrWhiteSpace(g.Ad) ? string.Join(", ", g.Uyeler.Select(u => u.GorunenAd)) : g.Ad;

    private async void Ac(Oge o)
    {
        var uyeler = o.Grup?.Uyeler ?? (o.Kisi is { } k ? [k] : []);
        await Navigation.PushAsync(new SohbetSayfasi(o.K, o.Baslik, uyeler));
    }
}

// Sunucular → kanallar → kanal sohbeti.
public sealed class SunucularSayfasi : ContentPage
{
    private readonly CollectionView _liste;

    public SunucularSayfasi()
    {
        Title = "Sunucular";
        BackgroundColor = Tema.Arkaplan;
        _liste = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var s = new ListeSatiri();
                s.BindingContextChanged += (_, _) =>
                {
                    if (s.BindingContext is not SunucuOzeti o) return;
                    s.Ayarla(o.Ad, o.Bahsedilme > 0 ? $"{o.Bahsedilme} bahsetme" : null, o.Okunmamis);
                    s.Avatar.Ayarla(o.Ad, o.Renk);
                };
                s.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () =>
                {
                    if (s.BindingContext is SunucuOzeti o) await Navigation.PushAsync(new KanallarSayfasi(o.Id, o.Ad));
                }) });
                return s;
            }),
            EmptyView = new Label { Text = "Henüz bir sunucuda değilsin. Keşfet'ten katılabilir ya da bilgisayardan sunucu kurabilirsin.", TextColor = Tema.MetinIkincil, HorizontalTextAlignment = TextAlignment.Center, Margin = 32 },
        };
        Content = new RefreshView { Content = _liste, RefreshColor = Tema.Vurgu, Command = new Command(async () => await Yenile()) };
        ToolbarItems.Add(new ToolbarItem("Kodla katıl", null, async () => await KodlaKatil()));
        if (Oturum.Aktif is { } o) o.Canli.SunucularDegisti += () => _ = Yenile();
    }

    protected override async void OnAppearing() { base.OnAppearing(); await Yenile(); }

    private async Task Yenile()
    {
        if (Oturum.Aktif is not { } o) return;
        var s = await o.Api.Sunucular();
        if (s.Basarili) _liste.ItemsSource = s.Veri;
        if (Content is RefreshView r) r.IsRefreshing = false;
    }

    private async Task KodlaKatil()
    {
        if (Oturum.Aktif is not { } o) return;
        var kod = await DisplayPromptAsync("Sunucuya katıl", "Davet kodunu yaz", "Katıl", "Vazgeç", "ABCD-EFGH", 12);
        if (string.IsNullOrWhiteSpace(kod)) return;
        var s = await o.Api.KodlaKatil(kod);
        if (!s.Basarili) { await DisplayAlertAsync("Katılamadın", s.Hata, "Tamam"); return; }
        await Yenile();
        await Navigation.PushAsync(new KanallarSayfasi(s.Veri!.Id, s.Veri.Ad));
    }
}

public sealed class KanallarSayfasi : ContentPage
{
    private readonly long _id;
    private Sunucu? _sunucu;
    private readonly CollectionView _liste;

    public KanallarSayfasi(long id, string ad)
    {
        _id = id;
        Title = ad;
        BackgroundColor = Tema.Arkaplan;
        _liste = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var ad = Tema.Yazi("", 16);
                var rozet = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Tema.Hata, VerticalOptions = LayoutOptions.Center };
                var satir = new Grid { Padding = new Thickness(20, 14), ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 10 };
                satir.Add(new Label { Text = "#", FontSize = 20, TextColor = Tema.MetinSoluk, VerticalOptions = LayoutOptions.Center }, 0);
                satir.Add(ad, 1);
                satir.Add(rozet, 2);
                satir.BindingContextChanged += (_, _) =>
                {
                    if (satir.BindingContext is not Kanal k) return;
                    ad.Text = k.Ad;
                    ad.FontAttributes = k.Okunmamis > 0 ? FontAttributes.Bold : FontAttributes.None;
                    ad.TextColor = k.Okunmamis > 0 ? Tema.Metin : Tema.MetinIkincil;
                    rozet.Text = k.Bahsedilme > 0 ? "@" + k.Bahsedilme : "";
                };
                satir.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () =>
                {
                    if (satir.BindingContext is Kanal k && _sunucu is { } s)
                        await Navigation.PushAsync(new SohbetSayfasi(new(KonusmaTuru.Kanal, k.Id), "#" + k.Ad, s.Uyeler.Select(u => u.Profil).ToList(),
                            silebilir: s.Rolum >= SunucuRolu.Yonetici));
                }) });
                return satir;
            }),
        };
        Content = _liste;
        if (Oturum.Aktif is { } o) o.Canli.SunucuGuncellendi += sid => { if (sid == _id) _ = Yenile(); };
    }

    protected override async void OnAppearing() { base.OnAppearing(); await Yenile(); }

    private async Task Yenile()
    {
        if (Oturum.Aktif is not { } o) return;
        var s = await o.Api.SunucuGetir(_id);
        if (!s.Basarili) { await DisplayAlertAsync("Açılamadı", s.Hata, "Tamam"); return; }
        _sunucu = s.Veri!;
        Title = _sunucu.Ad;
        _liste.ItemsSource = _sunucu.Kanallar.OrderBy(k => k.Sira).ThenBy(k => k.Id).ToList();
    }
}
