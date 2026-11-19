using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Sunucuyla konuşan tek sınıf. Hiçbir çağrı arayüzü dondurmaz, hepsi async.
public class Api
{
    private readonly HttpClient _http;
    private string? _token;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Api(string sunucuAdresi)
    {
        SunucuAdresi = sunucuAdresi.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(SunucuAdresi + "/"), Timeout = TimeSpan.FromSeconds(10) };
    }

    public string SunucuAdresi { get; }

    public string? Token
    {
        get => _token;
        set
        {
            _token = value;
            _http.DefaultRequestHeaders.Authorization = value is null ? null : new AuthenticationHeaderValue("Bearer", value);
        }
    }

    public Task<Sonuc<OturumYaniti>> KayitOl(KayitIstegi i) => Gonder<OturumYaniti>(HttpMethod.Post, "api/kayit", JsonIcerik(i));
    public Task<Sonuc<OturumYaniti>> GirisYap(GirisIstegi i) => Gonder<OturumYaniti>(HttpMethod.Post, "api/giris", JsonIcerik(i));
    public Task<Sonuc<Profil>> Ben() => Gonder<Profil>(HttpMethod.Get, "api/ben");
    public Task<Sonuc<object>> CikisYap() => Gonder<object>(HttpMethod.Post, "api/cikis");

    public Task<Sonuc<ArkadasListesi>> Arkadaslar() => Gonder<ArkadasListesi>(HttpMethod.Get, "api/arkadaslar");
    public Task<Sonuc<ArkadasIstegiYaniti>> IstekGonder(string kullaniciAdi) =>
        Gonder<ArkadasIstegiYaniti>(HttpMethod.Post, "api/arkadaslar/istek", JsonIcerik(new ArkadasIstegi(kullaniciAdi)));
    public Task<Sonuc<object>> IstekKabul(long id) => Gonder<object>(HttpMethod.Post, $"api/arkadaslar/{id}/kabul");
    public Task<Sonuc<object>> IliskiSil(long id) => Gonder<object>(HttpMethod.Delete, $"api/arkadaslar/{id}");

    public Task<Sonuc<Profil>> ProfilGuncelle(ProfilGuncelleme p) => Gonder<Profil>(HttpMethod.Put, "api/profil", JsonIcerik(p));
    public Task<Sonuc<Profil>> AvatarYukle(byte[] png) => Gonder<Profil>(HttpMethod.Put, "api/profil/avatar", ResimIcerik(png));
    public Task<Sonuc<Profil>> AvatarSil() => Gonder<Profil>(HttpMethod.Delete, "api/profil/avatar");

    // ---- Yönetim (sadece admin) ----
    public Task<Sonuc<YonetimOzeti>> YonetimOzeti() => Gonder<YonetimOzeti>(HttpMethod.Get, "api/admin/ozet");
    public Task<Sonuc<List<DavetKodu>>> Kodlar() => Gonder<List<DavetKodu>>(HttpMethod.Get, "api/admin/kodlar");
    public Task<Sonuc<DavetKodu>> KodOlustur(int kullanim, int? gun) => Gonder<DavetKodu>(HttpMethod.Post, "api/admin/kodlar", JsonIcerik(new KodOlusturma(kullanim, gun)));
    public Task<Sonuc<object>> KodIptal(string kod) => Gonder<object>(HttpMethod.Delete, $"api/admin/kodlar/{Uri.EscapeDataString(kod)}");
    public Task<Sonuc<List<YonetimKullanicisi>>> YonetimKullanicilari() => Gonder<List<YonetimKullanicisi>>(HttpMethod.Get, "api/admin/kullanicilar");
    public Task<Sonuc<object>> AdminAyarla(long id, bool deger) => Gonder<object>(HttpMethod.Post, $"api/admin/kullanicilar/{id}/admin", JsonIcerik(new DegerAyari(deger)));
    public Task<Sonuc<object>> YasakAyarla(long id, bool deger) => Gonder<object>(HttpMethod.Post, $"api/admin/kullanicilar/{id}/yasak", JsonIcerik(new DegerAyari(deger)));

    public Task<Sonuc<List<SohbetOzeti>>> Sohbetler() => Gonder<List<SohbetOzeti>>(HttpMethod.Get, "api/sohbetler");
    public Task<Sonuc<List<Mesaj>>> Mesajlar(long karsi, long? once) =>
        Gonder<List<Mesaj>>(HttpMethod.Get, once is null ? $"api/mesajlar/{karsi}" : $"api/mesajlar/{karsi}?once={once}");
    public Task<Sonuc<Mesaj>> MesajGonder(long karsi, string metin, string istemciId) =>
        Gonder<Mesaj>(HttpMethod.Post, $"api/mesajlar/{karsi}", JsonIcerik(new MesajGonderme(metin, istemciId)));
    public Task<Sonuc<object>> Okundu(long karsi) => Gonder<object>(HttpMethod.Post, $"api/mesajlar/{karsi}/okundu");

    public async Task<byte[]?> AvatarIndir(long id, int surum)
    {
        try { return await _http.GetByteArrayAsync($"api/avatar/{id}?v={surum}"); }
        catch { return null; }
    }

    // ---- Konuşmalar (DM / kanal / grup için ortak) ----
    public Task<Sonuc<List<Mesaj>>> KonusmaMesajlari(KonusmaKimligi k, long? once) =>
        Gonder<List<Mesaj>>(HttpMethod.Get, $"api/konusmalar/{k.UrlParcasi}/mesajlar" + (once is null ? "" : $"?once={once}"));
    public Task<Sonuc<Mesaj>> KonusmayaGonder(KonusmaKimligi k, MesajGonderme m) =>
        Gonder<Mesaj>(HttpMethod.Post, $"api/konusmalar/{k.UrlParcasi}/mesajlar", JsonIcerik(m));
    public Task<Sonuc<object>> KonusmaOkundu(KonusmaKimligi k) => Gonder<object>(HttpMethod.Post, $"api/konusmalar/{k.UrlParcasi}/okundu");
    public Task<Sonuc<Mesaj>> MesajDuzenle(long mesajId, string metin) =>
        Gonder<Mesaj>(HttpMethod.Put, $"api/mesaj/{mesajId}", JsonIcerik(new MesajDuzenleme(metin)));
    public Task<Sonuc<object>> MesajSil(long mesajId) => Gonder<object>(HttpMethod.Delete, $"api/mesaj/{mesajId}");
    public Task<Sonuc<Mesaj>> TepkiVer(long mesajId, string emoji) =>
        Gonder<Mesaj>(HttpMethod.Post, $"api/mesaj/{mesajId}/tepki", JsonIcerik(new TepkiIstegi(emoji)));

    // ---- Keşfet ----
    public Task<Sonuc<List<KesfetKullanicisi>>> KesfetKullanicilar(string? ara = null) =>
        Gonder<List<KesfetKullanicisi>>(HttpMethod.Get, "api/kesfet/kullanicilar" + Sorgu(ara));
    public Task<Sonuc<List<KesfetSunucusu>>> KesfetSunucular(string? ara = null) =>
        Gonder<List<KesfetSunucusu>>(HttpMethod.Get, "api/kesfet/sunucular" + Sorgu(ara));
    public Task<Sonuc<ArkadasIstegiYaniti>> IstekGonderId(long id) =>
        Gonder<ArkadasIstegiYaniti>(HttpMethod.Post, "api/arkadaslar/istek", JsonIcerik(new ArkadasIstegi("", id)));
    private static string Sorgu(string? ara) => string.IsNullOrWhiteSpace(ara) ? "" : "?q=" + Uri.EscapeDataString(ara.Trim());

    // ---- Sunucular ----
    public Task<Sonuc<List<SunucuOzeti>>> Sunucular() => Gonder<List<SunucuOzeti>>(HttpMethod.Get, "api/sunucular");
    public Task<Sonuc<Sunucu>> SunucuGetir(long id) => Gonder<Sunucu>(HttpMethod.Get, $"api/sunucular/{id}");
    public Task<Sonuc<Sunucu>> SunucuOlustur(SunucuAyari a) => Gonder<Sunucu>(HttpMethod.Post, "api/sunucular", JsonIcerik(a));
    public Task<Sonuc<Sunucu>> SunucuGuncelle(long id, SunucuAyari a) => Gonder<Sunucu>(HttpMethod.Put, $"api/sunucular/{id}", JsonIcerik(a));
    public Task<Sonuc<object>> SunucuSil(long id) => Gonder<object>(HttpMethod.Delete, $"api/sunucular/{id}");
    public Task<Sonuc<Sunucu>> SunucuIkonYukle(long id, byte[] png) => Gonder<Sunucu>(HttpMethod.Put, $"api/sunucular/{id}/ikon", ResimIcerik(png));
    public Task<Sonuc<Sunucu>> SunucuIkonSil(long id) => Gonder<Sunucu>(HttpMethod.Delete, $"api/sunucular/{id}/ikon");
    public Task<Sonuc<Kanal>> KanalOlustur(long sunucuId, KanalAyari a) => Gonder<Kanal>(HttpMethod.Post, $"api/sunucular/{sunucuId}/kanallar", JsonIcerik(a));
    public Task<Sonuc<Kanal>> KanalGuncelle(long kanalId, KanalAyari a) => Gonder<Kanal>(HttpMethod.Put, $"api/kanallar/{kanalId}", JsonIcerik(a));
    public Task<Sonuc<object>> KanalSil(long kanalId) => Gonder<object>(HttpMethod.Delete, $"api/kanallar/{kanalId}");
    public Task<Sonuc<Sunucu>> SunucuyaKatil(long id) => Gonder<Sunucu>(HttpMethod.Post, $"api/sunucular/{id}/katil");
    public Task<Sonuc<Sunucu>> KodlaKatil(string kod) => Gonder<Sunucu>(HttpMethod.Post, "api/sunucular/katil-kod", JsonIcerik(new SunucuKoduIstegi(kod)));
    public Task<Sonuc<object>> SunucudanAyril(long id) => Gonder<object>(HttpMethod.Post, $"api/sunucular/{id}/ayril");
    public Task<Sonuc<SunucuKoduIstegi>> DavetKoduYenile(long id) => Gonder<SunucuKoduIstegi>(HttpMethod.Post, $"api/sunucular/{id}/davet-kodu");
    public Task<Sonuc<object>> RolAyarla(long sunucuId, long uyeId, SunucuRolu rol) =>
        Gonder<object>(HttpMethod.Put, $"api/sunucular/{sunucuId}/uyeler/{uyeId}/rol", JsonIcerik(new RolAyari(rol)));
    public Task<Sonuc<object>> UyeAt(long sunucuId, long uyeId) => Gonder<object>(HttpMethod.Delete, $"api/sunucular/{sunucuId}/uyeler/{uyeId}");
    public async Task<byte[]?> IkonIndir(string tur /* "sunucu" | "grup" */, long id, int surum)
    {
        try { return await _http.GetByteArrayAsync($"api/ikon/{tur}/{id}?v={surum}"); }
        catch { return null; }
    }

    // ---- Gruplar ----
    public Task<Sonuc<List<Grup>>> Gruplar() => Gonder<List<Grup>>(HttpMethod.Get, "api/gruplar");
    public Task<Sonuc<Grup>> GrupOlustur(GrupOlusturma g) => Gonder<Grup>(HttpMethod.Post, "api/gruplar", JsonIcerik(g));
    public Task<Sonuc<Grup>> GrupGuncelle(long id, GrupAyari a) => Gonder<Grup>(HttpMethod.Put, $"api/gruplar/{id}", JsonIcerik(a));
    public Task<Sonuc<Grup>> GrupIkonYukle(long id, byte[] png) => Gonder<Grup>(HttpMethod.Put, $"api/gruplar/{id}/ikon", ResimIcerik(png));
    public Task<Sonuc<Grup>> GrupIkonSil(long id) => Gonder<Grup>(HttpMethod.Delete, $"api/gruplar/{id}/ikon");
    public Task<Sonuc<Grup>> GrubaEkle(long id, long uyeId) => Gonder<Grup>(HttpMethod.Post, $"api/gruplar/{id}/uyeler", JsonIcerik(new UyeEkleme(uyeId)));
    // Kendini çıkarmak = gruptan ayrılmak. Başkasını sadece kurucu çıkarabilir.
    public Task<Sonuc<object>> GruptanCikar(long id, long uyeId) => Gonder<object>(HttpMethod.Delete, $"api/gruplar/{id}/uyeler/{uyeId}");

    // ---- GIF ----
    // Sunucuda GIF anahtarı ayarlı değilse 503 ve açıklayıcı bir Hata döner.
    public Task<Sonuc<GifSayfasi>> GifAra(string? ara, string? sonraki = null) =>
        Gonder<GifSayfasi>(HttpMethod.Get, "api/gif" + Sorgu(ara) + (sonraki is null ? "" : (string.IsNullOrWhiteSpace(ara) ? "?" : "&") + "sonraki=" + Uri.EscapeDataString(sonraki)));

    // ---- Dosyalar ----
    // Dosyalar sunucunun veritabanına değil, Cloudflare R2'ye gider (Worker doğrudan aktarır). İndirme adresi
    // tahmin edilemez bir anahtar içerir; oturum gerektirmez.

    // Ek.Url "/dosya/..." gibi göreliyse sunucu adresini ekler; GIF gibi tam adresleri olduğu gibi döner.
    public string TamAdres(string url) => url.StartsWith('/') ? SunucuAdresi + url : url;

    // Uzun süren aktarımlar için ayrı istemci (normal istekler 10 sn'de zaman aşımına uğrar).
    private static readonly HttpClient Aktarim = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async Task<Sonuc<Ek>> DosyaYukle(string yol, string mime, IProgress<double>? ilerleme = null, CancellationToken iptal = default)
    {
        try
        {
            await using var dosya = File.OpenRead(yol);
            return await DosyaYukle(dosya, Path.GetFileName(yol), mime, ilerleme, iptal);
        }
        catch (IOException e) { return new Sonuc<Ek>(default, "Dosya okunamadı: " + e.Message, null); }
    }

    public async Task<Sonuc<Ek>> DosyaYukle(Stream veri, string ad, string mime, IProgress<double>? ilerleme = null, CancellationToken iptal = default)
    {
        if (veri.CanSeek && veri.Length > Kurallar.DosyaMaxBayt)
            return new Sonuc<Ek>(default, $"Dosya çok büyük (en fazla {Kurallar.DosyaMaxBayt / 1024 / 1024} MB).", null);
        try
        {
            using var istek = new HttpRequestMessage(HttpMethod.Post,
                $"{SunucuAdresi}/api/dosya?ad={Uri.EscapeDataString(ad)}&mime={Uri.EscapeDataString(mime)}")
            { Content = new IlerlemeliIcerik(veri, ilerleme) };
            istek.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            istek.Headers.Authorization = _http.DefaultRequestHeaders.Authorization;
            using var yanit = await Aktarim.SendAsync(istek, iptal);
            if (yanit.IsSuccessStatusCode) return new Sonuc<Ek>(await yanit.Content.ReadFromJsonAsync<Ek>(Json, iptal), null, yanit.StatusCode);
            string mesaj = yanit.StatusCode == HttpStatusCode.RequestEntityTooLarge ? "Dosya çok büyük." : "Dosya yüklenemedi.";
            try { if ((await yanit.Content.ReadFromJsonAsync<HataYaniti>(Json, iptal))?.Mesaj is { Length: > 0 } m) mesaj = m; } catch { }
            return new Sonuc<Ek>(default, mesaj, yanit.StatusCode);
        }
        catch (OperationCanceledException) when (iptal.IsCancellationRequested) { return new Sonuc<Ek>(default, "İptal edildi.", null); }
        catch (Exception) { return new Sonuc<Ek>(default, "Dosya yüklenemedi. İnternetini kontrol et.", null); }
    }

    // İndirir; hedef null ise belleğe (küçük dosyalar/resimler için). Başarısızsa null.
    public async Task<byte[]?> DosyaIndir(string url, IProgress<double>? ilerleme = null, CancellationToken iptal = default)
    {
        using var bellek = new MemoryStream();
        return await DosyaIndir(url, bellek, ilerleme, iptal) ? bellek.ToArray() : null;
    }

    public async Task<bool> DosyaIndir(string url, Stream hedef, IProgress<double>? ilerleme = null, CancellationToken iptal = default)
    {
        try
        {
            using var yanit = await Aktarim.GetAsync(TamAdres(url), HttpCompletionOption.ResponseHeadersRead, iptal);
            if (!yanit.IsSuccessStatusCode) return false;
            long toplam = yanit.Content.Headers.ContentLength ?? 0, okunan = 0;
            await using var kaynak = await yanit.Content.ReadAsStreamAsync(iptal);
            var tampon = new byte[81920];
            int n;
            while ((n = await kaynak.ReadAsync(tampon, iptal)) > 0)
            {
                await hedef.WriteAsync(tampon.AsMemory(0, n), iptal);
                okunan += n;
                if (toplam > 0) ilerleme?.Report((double)okunan / toplam);
            }
            return true;
        }
        catch { return false; }
    }

    private static ByteArrayContent ResimIcerik(byte[] png)
    {
        var icerik = new ByteArrayContent(png);
        icerik.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return icerik;
    }

    // Yüklerken ilerleme bildiren içerik (Content-Length gönderir; Worker akışı doğrudan R2'ye yazar).
    private sealed class IlerlemeliIcerik(Stream veri, IProgress<double>? ilerleme) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream hedef, System.Net.TransportContext? c)
        {
            long toplam = veri.CanSeek ? veri.Length : 0, giden = 0;
            var tampon = new byte[81920];
            int n;
            while ((n = await veri.ReadAsync(tampon)) > 0)
            {
                await hedef.WriteAsync(tampon.AsMemory(0, n));
                giden += n;
                if (toplam > 0) ilerleme?.Report((double)giden / toplam);
            }
        }
        protected override bool TryComputeLength(out long length)
        {
            length = veri.CanSeek ? veri.Length - veri.Position : -1;
            return veri.CanSeek;
        }
    }

    private static HttpContent JsonIcerik(object o) => JsonContent.Create(o, o.GetType(), options: Json);

    private async Task<Sonuc<T>> Gonder<T>(HttpMethod yontem, string yol, HttpContent? govde = null)
    {
        try
        {
            using var istek = new HttpRequestMessage(yontem, yol) { Content = govde };
            using var yanit = await _http.SendAsync(istek);

            if (yanit.IsSuccessStatusCode)
            {
                var veri = typeof(T) == typeof(object) ? default : await yanit.Content.ReadFromJsonAsync<T>(Json);
                return new Sonuc<T>(veri, null, yanit.StatusCode);
            }

            string mesaj = yanit.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => "Çok hızlı gidiyorsun, bir dakika bekle.",
                HttpStatusCode.Unauthorized => "Oturumun sona ermiş, tekrar giriş yap.",
                HttpStatusCode.Forbidden => "Bunun için yetkin yok.",
                _ => "Sunucuda bir sorun oldu.",
            };
            string? alan = null;
            try
            {
                var hata = await yanit.Content.ReadFromJsonAsync<HataYaniti>(Json);
                if (!string.IsNullOrWhiteSpace(hata?.Mesaj)) mesaj = hata.Mesaj;
                alan = hata?.Alan;
            }
            catch { /* Gövde JSON değilse varsayılan mesaj kalır. */ }
            return new Sonuc<T>(default, mesaj, yanit.StatusCode, alan);
        }
        catch (TaskCanceledException)
        {
            return new Sonuc<T>(default, "Sunucu cevap vermedi. Biraz sonra tekrar dene.", null);
        }
        catch (HttpRequestException)
        {
            return new Sonuc<T>(default, "Sunucuya ulaşılamadı. İnternetini ya da sunucu adresini kontrol et.", null);
        }
    }
}

// Alan: hata hangi form alanıyla ilgili ("kod", "kullaniciAdi"), sunucu söylerse.
public record Sonuc<T>(T? Veri, string? Hata, HttpStatusCode? Durum, string? Alan = null)
{
    public bool Basarili => Hata is null;
}
