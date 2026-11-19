namespace Ekip.Ortak;

// Sunucu ile uygulama arasında giden veriler. İki taraf da bu dosyayı kullanır.

// Kod: admin'in verdiği davet kodu. Sunucuda hiç kullanıcı yokken (ilk kurulum) gerekmez, o kişi admin olur.
public record KayitIstegi(string KullaniciAdi, string GorunenAd, string Sifre, string? Kod = null);

public record GirisIstegi(string KullaniciAdi, string Sifre);

public record KullaniciBilgisi(long Id, string KullaniciAdi, string GorunenAd);

public record OturumYaniti(string Token, KullaniciBilgisi Kullanici);

// Alan: hatanın ait olduğu form alanı ("kod", "kullaniciAdi"...); uygulama o kutuyu kırmızı yapar.
public record HataYaniti(string Mesaj, string? Alan = null);

// ---- Yönetim (admin) ----

// KalanKullanim -1: sınırsız. SonKullanma: Unix saniye, null = süresiz.
public record DavetKodu(string Kod, int KalanKullanim, int Kullanan, long Olusturma, long? SonKullanma, string Olusturan, bool Iptal);

public record KodOlusturma(int Kullanim, int? Gun); // Kullanim 0 = sınırsız

public record YonetimKullanicisi(long Id, string KullaniciAdi, string GorunenAd, string ProfilRengi, int AvatarSurum,
    long Kayit, bool Admin, bool Yasakli, bool Cevrimici, string? DavetKodu);

public record YonetimOzeti(int Kullanici, int Cevrimici, int AktifKod);

public record DegerAyari(bool Deger);

public enum Durum { Cevrimdisi = 0, Cevrimici = 1, Bosta = 2, RahatsizEtmeyin = 3, Gorunmez = 4 }

// Başkalarına gösterilen profil. AvatarSurum 0 ise profil resmi yok demek.
public record Profil(
    long Id,
    string KullaniciAdi,
    string GorunenAd,
    string Hakkimda,
    string DurumMesaji,
    string ProfilRengi,
    int AvatarSurum,
    Durum Durum);

public record ArkadasListesi(List<Profil> Arkadaslar, List<Profil> Gelen, List<Profil> Giden);

// KullaniciAdi "@ali", "ali" ya da "#12" (kullanıcı numarası) olabilir; Id verilirse o kullanılır (Keşfet'ten ekleme).
public record ArkadasIstegi(string KullaniciAdi, long? Id = null);

public record ArkadasIstegiYaniti(string Mesaj, bool ArkadasOldunuz);

public record ProfilGuncelleme(string GorunenAd, string Hakkimda, string DurumMesaji, string ProfilRengi);

// Zaman: Unix milisaniye (UTC). IstemciId: gönderen uygulamanın verdiği geçici kimlik, çift görünmeyi önler.
// Konuşma türü: KanalId doluysa sunucu kanalı, GrupId doluysa grup, ikisi de null ise birebir (DM; AliciId karşı taraf).
// Kanal/grup mesajında AliciId = 0.
// Duzenlenme: son düzenleme zamanı (ms), null = hiç düzenlenmedi. Silindi: metin ve ekler boşaltılır, yer tutucu gösterilir.
// Bahsedilenler: metindeki @kullaniciadi (ve @herkes) sunucuda çözülür; bu kişilerin kimlikleri.
public record Mesaj(long Id, long GonderenId, long AliciId, string Metin, long Zaman, string? IstemciId = null,
    long? KanalId = null, long? GrupId = null, long? YanitId = null, MesajOzeti? Yanit = null,
    List<Ek>? Ekler = null, List<Tepki>? Tepkiler = null, long? Duzenlenme = null, bool Silindi = false,
    List<long>? Bahsedilenler = null);

// Yanıtlanan mesajın kısa hali (mesajın üstünde alıntı olarak gösterilir). Metin en fazla 120 karakter.
public record MesajOzeti(long Id, long GonderenId, string Metin, bool EkVar, bool Silindi);

// Dosya / fotoğraf / video / GIF eki.
// Tur: "resim" | "video" | "ses" | "dosya" | "gif".
// Url: "/dosya/<anahtar>/<ad>" (sunucuya göre; uygulama başına sunucu adresini ekler) ya da GIF için tam https adresi.
// OnizlemeUrl: resim/GIF için küçük hali (yoksa Url kullanılır). Genislik/Yukseklik: biliniyorsa piksel, yoksa 0.
public record Ek(string Tur, string Ad, string Mime, long Boyut, string Url, string? OnizlemeUrl = null, int Genislik = 0, int Yukseklik = 0);

// Bir emojiye tepki verenler. Sayı = Kullanicilar.Count; "ben de verdim mi" = Kullanicilar.Contains(benimId).
public record Tepki(string Emoji, List<long> Kullanicilar);

// YanitId: yanıtlanan mesaj (aynı konuşmada olmalı). Ekler: önce /api/dosya ile yüklenip dönen Ek'ler ya da GIF.
// Metin boş olabilir ama o zaman en az bir ek olmalı.
public record MesajGonderme(string Metin, string? IstemciId, long? YanitId = null, List<Ek>? Ekler = null);

public record MesajDuzenleme(string Metin);

// Aynı emojiye tekrar basınca tepki geri alınır (aç/kapa).
public record TepkiIstegi(string Emoji);

public enum KonusmaTuru { Dm = 0, Kanal = 1, Grup = 2 }

// URL'de: /api/konusmalar/{dm|kanal|grup}/{id}/... ; DM'de id = karşı kişinin kullanıcı kimliği.
public readonly record struct KonusmaKimligi(KonusmaTuru Tur, long Id)
{
    public string UrlParcasi => Tur switch { KonusmaTuru.Dm => "dm", KonusmaTuru.Kanal => "kanal", _ => "grup" } + "/" + Id;
    public override string ToString() => Tur switch { KonusmaTuru.Dm => "dm:", KonusmaTuru.Kanal => "k:", _ => "g:" } + Id; // Olaylarda kullanılan anahtar
    public static KonusmaKimligi? Coz(string s)
    {
        var p = s.Split(':');
        if (p.Length != 2 || !long.TryParse(p[1], out var id)) return null;
        return p[0] switch { "dm" => new(KonusmaTuru.Dm, id), "k" => new(KonusmaTuru.Kanal, id), "g" => new(KonusmaTuru.Grup, id), _ => null };
    }
    // Bir mesajın ait olduğu konuşma, "ben"im gözümden (DM'de karşı taraf).
    public static KonusmaKimligi Mesajin(Mesaj m, long ben) =>
        m.KanalId is { } k ? new(KonusmaTuru.Kanal, k)
        : m.GrupId is { } g ? new(KonusmaTuru.Grup, g)
        : new(KonusmaTuru.Dm, m.GonderenId == ben ? m.AliciId : m.GonderenId);
}

// ---- Keşfet ----

// Iliski: 0 yok, 1 arkadaş, 2 ben istek gönderdim, 3 o bana istek gönderdi.
// Arkadaş olmayanların durumu gizli: Profil.Durum = Cevrimdisi gelir.
public record KesfetKullanicisi(Profil Profil, int Iliski, long Kayit);

public record KesfetSunucusu(long Id, string Ad, string Aciklama, string Renk, int IkonSurum, int UyeSayisi, int Cevrimici, bool Uyesin);

// ---- Sunucular (Discord tarzı) ----

public enum SunucuRolu { Uye = 0, Yonetici = 1, Sahip = 2 }

// Sol raydaki sunucu simgesi için özet. Okunmamis/Bahsedilme: tüm kanalların toplamı.
public record SunucuOzeti(long Id, string Ad, string Renk, int IkonSurum, int Okunmamis, int Bahsedilme);

// Herkese: Keşfet'te görünür ve kodsuz katılınabilir. DavetKodu: sadece yönetici/sahip görür, diğerlerine null.
public record Sunucu(long Id, string Ad, string Aciklama, string Renk, int IkonSurum, bool Herkese, long SahipId,
    string? DavetKodu, SunucuRolu Rolum, List<Kanal> Kanallar, List<SunucuUyesi> Uyeler);

public record Kanal(long Id, long SunucuId, string Ad, string Konu, int Sira, int Okunmamis, int Bahsedilme);

public record SunucuUyesi(Profil Profil, SunucuRolu Rol, long Katilma);

public record SunucuAyari(string Ad, string Aciklama, string Renk, bool Herkese);

public record KanalAyari(string Ad, string Konu);

public record SunucuKoduIstegi(string Kod);

public record RolAyari(SunucuRolu Rol);

// ---- Gruplar (çok kişilik DM) ----

public record Grup(long Id, string Ad, string Aciklama, string Renk, int IkonSurum, long KurucuId,
    List<Profil> Uyeler, Mesaj? SonMesaj, int Okunmamis, int Bahsedilme);

// Ad boşsa üyelerin adlarından oluşturulur. Uyeler: benim dışımdaki arkadaşlarım.
public record GrupOlusturma(string Ad, List<long> Uyeler);

public record GrupAyari(string Ad, string Aciklama, string Renk);

public record UyeEkleme(long Id);

// ---- GIF (sunucu, ayarlı sağlayıcıya aracılık eder; anahtar uygulamada durmaz) ----

public record GifSonucu(string Id, string Baslik, string Url, string OnizlemeUrl, int Genislik, int Yukseklik);

// Sonraki: bir sonraki sayfa için sunucuya geri verilecek değer (yoksa son sayfa).
public record GifSayfasi(List<GifSonucu> Sonuclar, string? Sonraki);

public record SohbetOzeti(Profil Karsi, Mesaj? SonMesaj, int Okunmamis);

// Parti: birlikte oyun oynayan grup. Sunucu bellekte tutar, kalıcı değil.
// Oyun: "surec.exe|Pencere başlığı". Herkes kendi bilgisayarında aynı süreç adına sahip pencereyi arar.
public record PartiBilgisi(string Id, long LiderId, List<Profil> Uyeler, string? Oyun);

public record PartiMesaji(long GonderenId, string GonderenAd, string GonderenRenk, string Metin, long Zaman);

public record PartiDurumu(PartiBilgisi Bilgi, List<PartiMesaji> Mesajlar);

// SignalR üzerinden sunucunun uygulamaya gönderdiği olayların adları.
public static class HubOlaylari
{
    public const string ArkadaslarDegisti = "ArkadaslarDegisti";   // (parametre yok) listeyi yeniden çek
    public const string ProfilDegisti = "ProfilDegisti";           // (Profil)
    public const string DurumDegisti = "DurumDegisti";             // (long id, Durum durum)
    public const string MesajGeldi = "MesajGeldi";                 // (Mesaj) hem alıcıya hem gönderenin diğer cihazlarına
    public const string Yaziyor = "Yaziyor";                       // (long kimYaziyor)
    public const string SohbetOkundu = "SohbetOkundu";             // (long karsiId) başka cihazımda okudum
    public const string PartiDaveti = "PartiDaveti";               // (string partiId, Profil davetEden)
    public const string PartiGuncellendi = "PartiGuncellendi";     // (PartiBilgisi? bilgi) null: artık partide değilsin
    public const string PartiMesaji = "PartiMesaji";               // (PartiMesaji)
    public const string OturumSonlandi = "OturumSonlandi";         // (string sebep) yasaklandın: uygulama çıkış yapar

    // MesajGeldi artık her konuşma türü için gelir (Mesaj.KanalId/GrupId'ye bak).
    public const string MesajGuncellendi = "MesajGuncellendi";     // (Mesaj) düzenlendi / silindi / tepkisi değişti
    public const string KonusmaYaziyor = "KonusmaYaziyor";         // (string konusma, long kim) kanal/grupta yazıyor; konusma = KonusmaKimligi.ToString()
    public const string KonusmaOkundu = "KonusmaOkundu";           // (string konusma) başka cihazımda okudum (kanal/grup)
    public const string SunucularDegisti = "SunucularDegisti";     // (parametre yok) sunucu listemi yeniden çek
    public const string SunucuGuncellendi = "SunucuGuncellendi";   // (long sunucuId) o sunucunun ayrıntısını yeniden çek
    public const string GruplarDegisti = "GruplarDegisti";         // (parametre yok) grup listemi yeniden çek
}

// Uygulamanın sunucuda çağırdığı hub metotları (SignalR).
public static class HubMetotlari
{
    public const string KonusmaYaziyor = "KonusmaYaziyor";         // (string konusma) kanal/grupta "yazıyor" bildir
}

public static class Kurallar
{
    public const int KullaniciAdiMin = 3;
    public const int KullaniciAdiMax = 20;
    public const int GorunenAdMax = 32;
    public const int SifreMin = 6;
    public const int SifreMax = 128;

    // Hata yoksa null döner.
    public static string? KullaniciAdiKontrol(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad)) return "Kullanıcı adı boş olamaz.";
        if (ad.Length < KullaniciAdiMin || ad.Length > KullaniciAdiMax)
            return $"Kullanıcı adı {KullaniciAdiMin}-{KullaniciAdiMax} karakter olmalı.";
        foreach (var c in ad)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '.'))
                return "Kullanıcı adında sadece harf, rakam, _ ve . olabilir.";
        return null;
    }

    public static string? GorunenAdKontrol(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad)) return "Görünen ad boş olamaz.";
        if (ad.Trim().Length > GorunenAdMax) return $"Görünen ad en fazla {GorunenAdMax} karakter olabilir.";
        return null;
    }

    public const int HakkimdaMax = 190;
    public const int DurumMesajiMax = 60;
    public const int AvatarMaxBayt = 512 * 1024;
    public const int MesajMax = 2000;
    public const int MesajSayfasi = 50;
    public const int PartiMesajMax = 300;
    public const int PartiUyeMax = 10;

    public const int MesajEkMax = 10;                    // bir mesajda en fazla ek
    public const long DosyaMaxBayt = 95L * 1024 * 1024;  // tek dosya (Cloudflare bedava planı istek sınırı 100 MB)
    public const long GunlukYuklemeBayt = 1024L * 1024 * 1024; // kişi başı günlük yükleme
    public const int DosyaSaklamaGun = 90;               // bu kadar günden eski dosyalar silinir
    public const int TepkiMax = 20;                      // bir mesajda en fazla farklı emoji
    public const int SunucuAdMax = 40;
    public const int SunucuAciklamaMax = 300;
    public const int KanalAdMax = 32;
    public const int KanalKonuMax = 120;
    public const int SunucuKanalMax = 50;
    public const int SunucuMax = 30;                     // bir kişinin üye olabileceği sunucu
    public const int GrupUyeMax = 10;                    // ben dahil
    public const int GrupAdMax = 40;

    // Kanal adı: küçük harf, rakam, tire ("genel", "oyun-sohbeti"). Uygulama yazarken boşluğu tireye çevirir.
    public static string? KanalAdKontrol(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad)) return "Kanal adı boş olamaz.";
        if (ad.Length > KanalAdMax) return $"Kanal adı en fazla {KanalAdMax} karakter olabilir.";
        foreach (var c in ad)
            if (!(char.IsLetterOrDigit(c) && !char.IsUpper(c) || c is '-' or '_'))
                return "Kanal adında küçük harf, rakam, - ve _ olabilir.";
        return null;
    }

    public static readonly string[] ProfilRenkleri =
        ["#7C5CFF", "#3DA9FC", "#2CB67D", "#F59E0B", "#EF4565", "#E862C9", "#14B8A6", "#F97316", "#64748B"];

    public static string? ProfilKontrol(ProfilGuncelleme p) =>
        GorunenAdKontrol(p.GorunenAd)
        ?? ((p.Hakkimda?.Length ?? 0) > HakkimdaMax ? $"Hakkımda en fazla {HakkimdaMax} karakter olabilir." : null)
        ?? ((p.DurumMesaji?.Length ?? 0) > DurumMesajiMax ? $"Durum mesajı en fazla {DurumMesajiMax} karakter olabilir." : null)
        ?? (Array.IndexOf(ProfilRenkleri, p.ProfilRengi) < 0 ? "Geçersiz profil rengi." : null);

    public static string? SifreKontrol(string? sifre)
    {
        if (string.IsNullOrEmpty(sifre) || sifre.Length < SifreMin) return $"Şifre en az {SifreMin} karakter olmalı.";
        if (sifre.Length > SifreMax) return "Şifre çok uzun.";
        return null;
    }
}
