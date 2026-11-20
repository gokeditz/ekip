using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ekip.Uygulama;

// %AppData%\Ekip\ayarlar.json. Oturum token'ı Windows'un DPAPI'si ile şifreli saklanır,
// dosya başka bilgisayara kopyalansa bile işe yaramaz.
public class Ayarlar
{
    // Cloudflare'daki sunucu (Ekip.Bulut). Yerel deneme için "Sunucu ayarı"ndan başka adres girilebilir. Bkz. KURULUM.md.
    public const string VarsayilanSunucu = "https://ekip.ekip-bulut.workers.dev";

    // Otomatik güncelleme: son sürüm bilgisinin (son.json) adresi. Boşsa güncelleme kontrolü yapılmaz.
    // GitHub'daki en son Release'in son.json dosyası (surum.yml her sürümde ekler).
    // Test için "--guncelleme <adres>" ile başka adres verilebilir.
    public const string VarsayilanGuncellemeAdresi = "https://github.com/gokeditz/ekip/releases/latest/download/son.json";
    public static string GuncellemeAdresi { get; set; } = VarsayilanGuncellemeAdresi;

    public string SunucuAdresi { get; set; } = VarsayilanSunucu;
    public string? SonKullaniciAdi { get; set; }
    public string? SifreliToken { get; set; }

    // Son giriş yapılan profil: açılışta sunucuya bir anlık ulaşılamazsa uygulama bununla açılır (giriş ekranına düşmez).
    public Ekip.Ortak.Profil? SonProfil { get; set; }

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
            {
                var a = JsonSerializer.Deserialize<Ayarlar>(File.ReadAllText(Dosya)) ?? new Ayarlar();
                // Eski sürümlerin varsayılanı (geliştirme sunucusu) kayıtlıysa yeni sunucuya geç.
                if (a.SunucuAdresi.TrimEnd('/') == "http://localhost:5080") { a.SunucuAdresi = VarsayilanSunucu; a.SifreliToken = null; }
                return a;
            }
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
