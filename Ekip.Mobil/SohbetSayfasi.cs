using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Ekip.Ortak;
using Microsoft.Maui.Controls.Shapes;

namespace Ekip.Mobil;

// DM, grup ve kanal için ortak sohbet ekranı.
// Mesaja dokun: yanıtla / tepki / kopyala / (kendininse) düzenle-sil. Fotoğrafa dokun: tam ekran aç.
public sealed class SohbetSayfasi : ContentPage
{
    private readonly KonusmaKimligi _k;
    private readonly Dictionary<long, Profil> _uyeler;
    private readonly bool _silebilir;
    private readonly ObservableCollection<Mesaj> _mesajlar = [];
    private readonly CollectionView _liste;
    private readonly Editor _yazi;
    private readonly Label _yanitYazi = Tema.Yazi("", 13, Tema.MetinIkincil);
    private readonly Grid _yanitCubugu;
    private readonly Button _eskiler;
    private Mesaj? _yanit, _duzenlenen;
    private bool _tumu, _yukleniyor, _gorunuyor;

    private static readonly string[] HizliTepkiler = ["👍", "❤️", "😂", "😮", "😢", "🔥"];

    public SohbetSayfasi(KonusmaKimligi k, string baslik, IEnumerable<Profil> uyeler, bool silebilir = false)
    {
        _k = k;
        _silebilir = silebilir;
        _uyeler = uyeler.ToDictionary(p => p.Id);
        if (Oturum.Aktif is { } o) _uyeler[o.Ben.Id] = o.Ben;
        Title = baslik;
        BackgroundColor = Tema.Arkaplan;

        _eskiler = new Button { Text = "Daha eski mesajlar", FontSize = 13, BackgroundColor = Colors.Transparent, TextColor = Tema.Vurgu, IsVisible = false };
        _eskiler.Clicked += async (_, _) => await Yukle(eski: true);
        _liste = new CollectionView
        {
            ItemsSource = _mesajlar,
            SelectionMode = SelectionMode.None,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView,
            Header = _eskiler,
            ItemTemplate = new DataTemplate(() => new MesajHucresi(this)),
            EmptyView = new Label { Text = "Henüz mesaj yok. Selam ver! 👋", TextColor = Tema.MetinIkincil, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center },
        };

        _yazi = new Editor
        {
            Placeholder = k.Tur == KonusmaTuru.Kanal ? $"{baslik} kanalına yaz" : "Mesaj yaz", AutoSize = EditorAutoSizeOption.TextChanges,
            FontSize = 16, TextColor = Tema.Metin, PlaceholderColor = Tema.MetinSoluk, BackgroundColor = Colors.Transparent, MaximumHeightRequest = 140,
        };
        _yazi.TextChanged += (_, _) => { if (!string.IsNullOrEmpty(_yazi.Text)) YaziyorBildir(); };
        var gonder = new Button { Text = "➤", FontSize = 18, WidthRequest = 46, HeightRequest = 46, CornerRadius = 23, BackgroundColor = Tema.Vurgu, TextColor = Colors.White, Padding = 0, VerticalOptions = LayoutOptions.End };
        gonder.Clicked += async (_, _) => await Gonder();
        var foto = new Button { Text = "+", FontSize = 22, WidthRequest = 46, HeightRequest = 46, CornerRadius = 23, BackgroundColor = Tema.YuzeyUst, TextColor = Tema.Metin, Padding = 0, VerticalOptions = LayoutOptions.End };
        foto.Clicked += async (_, _) => await FotografGonder();

        var iptal = new Label { Text = "✕", FontSize = 18, TextColor = Tema.MetinIkincil, Padding = new Thickness(12, 0), VerticalOptions = LayoutOptions.Center };
        iptal.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(ModuKapat) });
        _yanitCubugu = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], Padding = new Thickness(16, 8, 4, 0), IsVisible = false };
        _yanitCubugu.Add(_yanitYazi, 0); _yanitCubugu.Add(iptal, 1);

        var kutu = new Border
        {
            Content = _yazi, BackgroundColor = Tema.Giris, Stroke = Tema.Kenar, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 }, Padding = new Thickness(12, 2),
        };
        var alt = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8, Padding = new Thickness(10, 8, 10, 10) };
        alt.Add(foto, 0); alt.Add(kutu, 1); alt.Add(gonder, 2);

        var sayfa = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)], BackgroundColor = Tema.Arkaplan };
        sayfa.Add(_liste, 0, 0);
        sayfa.Add(_yanitCubugu, 0, 1);
        sayfa.Add(alt, 0, 2);
        Content = sayfa;
    }

    // ---- Yaşam döngüsü ----

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _gorunuyor = true;
        if (Oturum.Aktif is { } o)
        {
            o.Canli.MesajGeldi += MesajGeldi;
            o.Canli.MesajGuncellendi += MesajGuncellendi;
        }
        if (_mesajlar.Count == 0) await Yukle(eski: false);
        Okundu();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _gorunuyor = false;
        if (Oturum.Aktif is { } o)
        {
            o.Canli.MesajGeldi -= MesajGeldi;
            o.Canli.MesajGuncellendi -= MesajGuncellendi;
        }
    }

    private async Task Yukle(bool eski)
    {
        if (Oturum.Aktif is not { } o || _yukleniyor || (eski && _tumu)) return;
        _yukleniyor = true;
        long? once = eski ? _mesajlar.FirstOrDefault()?.Id : null;
        var s = await o.Api.KonusmaMesajlari(_k, once);
        _yukleniyor = false;
        if (!s.Basarili) { await DisplayAlertAsync("Mesajlar yüklenemedi", s.Hata, "Tamam"); return; }
        var gelen = s.Veri!;
        _tumu = gelen.Count < Kurallar.MesajSayfasi;
        _eskiler.IsVisible = !_tumu;
        if (eski) for (int i = gelen.Count - 1; i >= 0; i--) { if (_mesajlar.All(m => m.Id != gelen[i].Id)) _mesajlar.Insert(0, gelen[i]); }
        else
        {
            _mesajlar.Clear();
            foreach (var m in gelen) _mesajlar.Add(m);
            if (_mesajlar.Count > 0) _liste.ScrollTo(_mesajlar.Count - 1, position: ScrollToPosition.End, animate: false);
        }
    }

    private void MesajGeldi(Mesaj m)
    {
        if (Oturum.Aktif is not { } o || KonusmaKimligi.Mesajin(m, o.Ben.Id) != _k) return;
        if (_mesajlar.Any(x => x.Id == m.Id)) return;
        _mesajlar.Add(m);
        _liste.ScrollTo(_mesajlar.Count - 1, position: ScrollToPosition.End, animate: true);
        Okundu();
    }

    private void MesajGuncellendi(Mesaj m)
    {
        int i = _mesajlar.ToList().FindIndex(x => x.Id == m.Id);
        if (i >= 0) _mesajlar[i] = m;
    }

    private void Okundu()
    {
        if (_gorunuyor && Oturum.Aktif is { } o) _ = o.Api.KonusmaOkundu(_k);
    }

    private DateTime _sonYaziyor;
    private void YaziyorBildir()
    {
        if (Oturum.Aktif is not { } o || DateTime.Now - _sonYaziyor < TimeSpan.FromSeconds(3)) return;
        _sonYaziyor = DateTime.Now;
        _ = _k.Tur == KonusmaTuru.Dm ? o.Canli.YaziyorBildir(_k.Id) : o.Canli.KonusmaYaziyorBildir(_k);
    }

    // ---- Gönderme ----

    private async Task Gonder()
    {
        if (Oturum.Aktif is not { } o) return;
        var metin = (_yazi.Text ?? "").Trim();
        if (metin.Length == 0) return;
        _yazi.Text = "";
        if (_duzenlenen is { } d)
        {
            ModuKapat();
            var s = await o.Api.MesajDuzenle(d.Id, metin);
            if (!s.Basarili) await DisplayAlertAsync("Düzenlenemedi", s.Hata, "Tamam");
            else MesajGuncellendi(s.Veri!);
            return;
        }
        var yanit = _yanit?.Id;
        ModuKapat();
        var g = await o.Api.KonusmayaGonder(_k, new MesajGonderme(metin, Guid.NewGuid().ToString("N"), yanit));
        if (!g.Basarili) { _yazi.Text = metin; await DisplayAlertAsync("Gönderilemedi", g.Hata, "Tamam"); return; }
        MesajGeldi(g.Veri!);
    }

    private async Task FotografGonder()
    {
        if (Oturum.Aktif is not { } o) return;
        FileResult? dosya;
        try { dosya = (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault(); }
        catch { return; }
        if (dosya is null) return;
        var metin = (_yazi.Text ?? "").Trim();
        await using var akis = await dosya.OpenReadAsync();
        var bellek = new MemoryStream();
        await akis.CopyToAsync(bellek);
        bellek.Position = 0;
        var mime = string.IsNullOrEmpty(dosya.ContentType) ? "image/jpeg" : dosya.ContentType;
        Title = "Yükleniyor…";
        var ek = await o.Api.DosyaYukle(bellek, dosya.FileName, mime);
        Title = (BindingContext as string) ?? Title;
        if (!ek.Basarili) { await DisplayAlertAsync("Fotoğraf yüklenemedi", ek.Hata, "Tamam"); return; }
        _yazi.Text = "";
        var g = await o.Api.KonusmayaGonder(_k, new MesajGonderme(metin, Guid.NewGuid().ToString("N"), _yanit?.Id, [ek.Veri!]));
        ModuKapat();
        if (!g.Basarili) await DisplayAlertAsync("Gönderilemedi", g.Hata, "Tamam");
        else MesajGeldi(g.Veri!);
    }

    private void ModuKapat()
    {
        _yanit = _duzenlenen = null;
        _yanitCubugu.IsVisible = false;
    }

    // ---- Mesaja dokununca ----

    internal async Task MesajMenusu(Mesaj m)
    {
        if (Oturum.Aktif is not { } o || m.Silindi) return;
        bool benim = m.GonderenId == o.Ben.Id;
        var secenekler = new List<string> { "Yanıtla", "Tepki ver" };
        if (!string.IsNullOrEmpty(m.Metin)) secenekler.Add("Metni kopyala");
        if (benim && !string.IsNullOrEmpty(m.Metin)) secenekler.Add("Düzenle");
        string? sil = benim || _silebilir ? "Sil" : null;
        var sec = await DisplayActionSheetAsync(null, "Vazgeç", sil, secenekler.ToArray());
        switch (sec)
        {
            case "Yanıtla":
                _duzenlenen = null;
                _yanit = m;
                _yanitYazi.Text = $"↩ {Ad(m.GonderenId)} kişisine yanıt veriyorsun";
                _yanitCubugu.IsVisible = true;
                _yazi.Focus();
                break;
            case "Tepki ver":
                var e = await DisplayActionSheetAsync("Tepki", "Vazgeç", null, HizliTepkiler);
                if (HizliTepkiler.Contains(e)) await TepkiVer(m, e!);
                break;
            case "Metni kopyala":
                await Clipboard.SetTextAsync(m.Metin);
                break;
            case "Düzenle":
                _yanit = null;
                _duzenlenen = m;
                _yazi.Text = m.Metin;
                _yanitYazi.Text = "✎ Mesajı düzenliyorsun";
                _yanitCubugu.IsVisible = true;
                _yazi.Focus();
                break;
            case "Sil":
                if (!await DisplayAlertAsync("Mesaj silinsin mi?", "Bu geri alınamaz.", "Sil", "Vazgeç")) return;
                var s = await o.Api.MesajSil(m.Id);
                if (!s.Basarili) await DisplayAlertAsync("Silinemedi", s.Hata, "Tamam");
                break;
        }
    }

    internal async Task TepkiVer(Mesaj m, string emoji)
    {
        if (Oturum.Aktif is not { } o) return;
        var s = await o.Api.TepkiVer(m.Id, emoji);
        if (s.Basarili) MesajGuncellendi(s.Veri!);
    }

    internal Profil? Kisi(long id) => _uyeler.GetValueOrDefault(id) ?? Oturum.Aktif?.ProfilBul(id);
    internal string Ad(long id) => Kisi(id)?.GorunenAd ?? "Biri";
    internal long BenId => Oturum.Aktif?.Ben.Id ?? 0;
    internal string? BenAdi => Oturum.Aktif?.Ben.KullaniciAdi;
    internal string TamAdres(string url) => Oturum.Aktif?.Api.TamAdres(url) ?? url;

    // Önceki mesaj aynı kişiden ve 5 dk içindeyse başlık (avatar + ad) tekrar çizilmez.
    internal bool GrupBasi(Mesaj m)
    {
        int i = _mesajlar.IndexOf(m);
        if (i <= 0) return true;
        var o = _mesajlar[i - 1];
        return o.GonderenId != m.GonderenId || m.Zaman - o.Zaman > 5 * 60 * 1000 || m.YanitId != null;
    }
}

// Tek bir mesaj satırı. Bağlam (Mesaj) değişince içeriği yeniden kurulur.
internal sealed partial class MesajHucresi : ContentView
{
    private readonly SohbetSayfasi _s;

    public MesajHucresi(SohbetSayfasi s)
    {
        _s = s;
        GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => { if (BindingContext is Mesaj m) await _s.MesajMenusu(m); }) });
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is not Mesaj m) { Content = null; return; }
        bool basi = _s.GrupBasi(m);
        bool bana = m.Bahsedilenler?.Contains(_s.BenId) == true && m.GonderenId != _s.BenId;

        var sutun = new VerticalStackLayout { Spacing = 3 };
        if (m.Yanit is { } y)
            sutun.Add(new Label { Text = $"↩ {_s.Ad(y.GonderenId)}: {(y.Silindi ? "silinmiş mesaj" : y.Metin)}", FontSize = 12.5, TextColor = Tema.MetinSoluk, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
        if (basi)
        {
            var ust = new HorizontalStackLayout { Spacing = 8 };
            ust.Add(new Label { Text = _s.Ad(m.GonderenId), FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Tema.Metin });
            ust.Add(new Label { Text = Bicim.Zaman(m.Zaman), FontSize = 12, TextColor = Tema.MetinSoluk, VerticalOptions = LayoutOptions.Center });
            sutun.Add(ust);
        }
        if (m.Silindi)
            sutun.Add(new Label { Text = "Bu mesaj silindi.", FontSize = 15, FontAttributes = FontAttributes.Italic, TextColor = Tema.MetinSoluk });
        else
        {
            if (!string.IsNullOrEmpty(m.Metin))
            {
                var metin = new Label { FormattedText = Zengin(m.Metin), FontSize = 15.5, TextColor = Color.FromArgb("#DCDDE3") };
                if (m.Duzenlenme != null) metin.FormattedText.Spans.Add(new Span { Text = " (düzenlendi)", FontSize = 11.5, TextColor = Tema.MetinSoluk });
                sutun.Add(metin);
            }
            foreach (var ek in m.Ekler ?? []) sutun.Add(EkGorunumu(ek));
            if (m.Tepkiler is { Count: > 0 } t) sutun.Add(Tepkiler(m, t));
        }

        var satir = new Grid
        {
            ColumnDefinitions = [new(48), new(GridLength.Star)],
            Padding = new Thickness(12, basi ? 10 : 1, 14, 2),
            BackgroundColor = bana ? Tema.Bahsetme : Colors.Transparent,
        };
        if (basi && _s.Kisi(m.GonderenId) is { } p)
        {
            var av = new Avatar(38) { VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 2, 0, 0) };
            av.Profil(p);
            satir.Add(av, 0);
        }
        satir.Add(sutun, 1);
        Content = satir;
    }

    private View EkGorunumu(Ek ek)
    {
        var url = _s.TamAdres(ek.Url);
        if (ek.Tur is "resim" or "gif")
        {
            double w = 240, h = 180;
            if (ek.Genislik > 0 && ek.Yukseklik > 0) { h = Math.Min(260, w * ek.Yukseklik / ek.Genislik); }
            var resim = new Image
            {
                Source = ImageSource.FromUri(new Uri(_s.TamAdres(ek.OnizlemeUrl ?? ek.Url))), Aspect = Aspect.AspectFit,
                WidthRequest = w, HeightRequest = h, HorizontalOptions = LayoutOptions.Start, IsAnimationPlaying = ek.Tur == "gif",
            };
            var kutu = new Border { Content = resim, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 }, HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(0, 4) };
            kutu.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Launcher.Default.OpenAsync(new Uri(url))) });
            return kutu;
        }
        var simge = ek.Tur switch { "video" => "🎬", "ses" => "🎵", _ => "📎" };
        var kart = new Border
        {
            BackgroundColor = Tema.Yuzey, Stroke = Tema.Kenar, StrokeShape = new RoundRectangle { CornerRadius = 10 }, Padding = 12,
            HorizontalOptions = LayoutOptions.Start, Margin = new Thickness(0, 4),
            Content = new Label { Text = $"{simge}  {ek.Ad}  ·  {Boyut(ek.Boyut)}", TextColor = Tema.Vurgu, FontSize = 14, LineBreakMode = LineBreakMode.MiddleTruncation },
        };
        kart.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Launcher.Default.OpenAsync(new Uri(url))) });
        return kart;
    }

    private View Tepkiler(Mesaj m, List<Tepki> tepkiler)
    {
        var sira = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var t in tepkiler)
        {
            bool benim = t.Kullanicilar.Contains(_s.BenId);
            var hap = new Border
            {
                BackgroundColor = benim ? Tema.VurguKoyu : Tema.Yuzey, Stroke = benim ? Tema.Vurgu : Tema.Kenar,
                StrokeShape = new RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(8, 3), Margin = new Thickness(0, 0, 6, 4),
                Content = new Label { Text = $"{t.Emoji} {t.Kullanicilar.Count}", FontSize = 13.5, TextColor = Tema.Metin },
            };
            var emoji = t.Emoji;
            hap.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await _s.TepkiVer(m, emoji)) });
            sira.Add(hap);
        }
        return sira;
    }

    private static string Boyut(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        _ => $"{b / 1024.0 / 1024.0:0.#} MB",
    };

    // **kalın**, *italik*, `kod`, ~~çizili~~, @bahsetme.
    private FormattedString Zengin(string metin)
    {
        var fs = new FormattedString();
        int i = 0;
        foreach (Match x in Desen().Matches(metin))
        {
            if (x.Index > i) fs.Spans.Add(new Span { Text = metin[i..x.Index] });
            var s = new Span();
            if (x.Groups["k"].Success) { s.Text = x.Groups["k"].Value; s.FontAttributes = FontAttributes.Bold; }
            else if (x.Groups["c"].Success) { s.Text = x.Groups["c"].Value; s.TextDecorations = TextDecorations.Strikethrough; }
            else if (x.Groups["kod"].Success) { s.Text = x.Groups["kod"].Value; s.FontFamily = "monospace"; s.BackgroundColor = Tema.YuzeyUst; }
            else if (x.Groups["i"].Success) { s.Text = x.Groups["i"].Value; s.FontAttributes = FontAttributes.Italic; }
            else
            {
                s.Text = x.Value;
                bool ben = x.Groups["b"].Value.Equals(_s.BenAdi, StringComparison.OrdinalIgnoreCase) || x.Groups["b"].Value == "herkes";
                s.TextColor = ben ? Color.FromArgb("#F5C451") : Color.FromArgb("#B9A8FF");
                s.FontAttributes = FontAttributes.Bold;
            }
            fs.Spans.Add(s);
            i = x.Index + x.Length;
        }
        if (i < metin.Length) fs.Spans.Add(new Span { Text = metin[i..] });
        return fs;
    }

    [GeneratedRegex(@"\*\*(?<k>.+?)\*\*|~~(?<c>.+?)~~|`(?<kod>[^`]+)`|(?<![\w*])\*(?!\s)(?<i>.+?)(?<!\s)\*(?![\w*])|@(?<b>[A-Za-z0-9_.]{2,20})")]
    private static partial Regex Desen();
}
