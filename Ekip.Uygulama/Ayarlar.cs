using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ekip.Uygulama;

// %AppData%\Ekip\ayarlar.json. Oturum token'ı Windows'un DPAPI'si ile şifreli saklanır,
// dosya başka bilgisayara kopyalansa bile işe yaramaz.
public class Ayarlar
{
    // Arkadaşlara dağıtmadan önce bunu kendi sunucu adresinle değiştir (ör. "https://ekip.duckdns.org").
    // Böylece kimse "Sunucu ayarı"na girmek zorunda kalmaz. Bkz. KURULUM.md.
    public const string VarsayilanSunucu = "http://localhost:5080";

    // Otomatik güncelleme: son sürüm bilgisinin (son.json) adresi. Boşsa güncelleme kontrolü yapılmaz.
    // Cloudflare R2 açılınca ör. "https://pub-xxxx.r2.dev/son.json" olacak. Test için "--guncelleme <adres>" ile de verilebilir.
    public const string VarsayilanGuncellemeAdresi = "";
    public static string GuncellemeAdresi { get; set; } = VarsayilanGuncellemeAdresi;

    public string SunucuAdresi { get; set; } = VarsayilanSunucu;
    public string? SonKullaniciAdi { get; set; }
    public string? SifreliToken { get; set; }

    // Kısayollar (Ayarlar > Kısayollar'dan değişir).
    public string OverlayTusu { get; set; } = Kisayol.VarsayilanOverlay.ToString();
    public string MiniTusu { get; set; } = Kisayol.VarsayilanMini.ToString();

    // Mini sohbetin yeri ve boyutu (fareyle ayarlanır, hatırlanır).
    public int[]? MiniAlan { get; set; } // [x, y, genişlik, yükseklik] ekran pikseli

    // "--profil isim" ile açılınca ayrı klasör kullanılır: aynı bilgisayarda iki hesabı denemek için.
    public static string? ProfilAdi { get; set; }

    private static string Klasor => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProfilAdi is null ? "Ekip" : "Ekip-" + ProfilAdi);
    private static string Dosya => Path.Combine(Klasor, "ayarlar.json");

    public static Ayarlar Yukle()
    {
        try
        {
            if (File.Exists(Dosya))
                return JsonSerializer.Deserialize<Ayarlar>(File.ReadAllText(Dosya)) ?? new Ayarlar();
        }
        catch { /* Bozuk dosya: varsayılanlarla devam. */ }
        return new Ayarlar();
    }

    public void Kaydet()
    {
        Directory.CreateDirectory(Klasor);
        File.WriteAllText(Dosya, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    // Dosyaya YAZILMAZ: sadece şifreli hali (SifreliToken) saklanır.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Token
    {
        get
        {
            if (SifreliToken is null) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(SifreliToken), null, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
        set => SifreliToken = value is null ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }
}
