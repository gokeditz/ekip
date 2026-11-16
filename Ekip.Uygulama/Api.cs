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
    public Task<Sonuc<Profil>> AvatarYukle(byte[] png)
    {
        var icerik = new ByteArrayContent(png);
        icerik.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return Gonder<Profil>(HttpMethod.Put, "api/profil/avatar", icerik);
    }
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
