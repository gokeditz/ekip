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

public record ArkadasIstegi(string KullaniciAdi);

public record ArkadasIstegiYaniti(string Mesaj, bool ArkadasOldunuz);

public record ProfilGuncelleme(string GorunenAd, string Hakkimda, string DurumMesaji, string ProfilRengi);

// Zaman: Unix milisaniye (UTC). IstemciId: gönderen uygulamanın verdiği geçici kimlik, çift görünmeyi önler.
public record Mesaj(long Id, long GonderenId, long AliciId, string Metin, long Zaman, string? IstemciId = null);

public record MesajGonderme(string Metin, string? IstemciId);

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
