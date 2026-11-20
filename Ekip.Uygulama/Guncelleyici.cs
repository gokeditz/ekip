using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ekip.Uygulama;

// Otomatik güncelleme. Bir adresteki "son.json" okunur:
//   { "surum": "1.0.1", "indir": "https://.../31Hane-1.0.1.zip", "sha256": "…", "notlar": "…" }
// Daha yeni sürüm varsa zip indirilir, bütünlüğü (sha256) kontrol edilir, exe değiştirilip uygulama yeniden açılır.
// Çalışan bir exe Windows'ta silinemez ama YENİDEN ADLANDIRILABİLİR: eskisini ".eski" yapıp yenisini yerine koyuyoruz.
public static class Guncelleyici
{
    public record SurumBilgisi(string Surum, string Indir, string? Sha256, string? Notlar);

    private static readonly HttpClient Http = Ag.Istemci(TimeSpan.FromMinutes(10));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Version MevcutSurum =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public static string MevcutSurumYazisi => $"{MevcutSurum.Major}.{MevcutSurum.Minor}.{MevcutSurum.Build}";

    // Exe adları: yeni ad önce. Eski ad ("Ekip.Uygulama.exe") yeniden markalamadan önceki kurulumlar için.
    // Paket hangi adla gelirse onu açarız; ad değiştiyse eski exe ".eski" olur ve sonraki açılışta silinir.
    private static readonly string[] ExeAdlari = ["31Hane.exe", "Ekip.Uygulama.exe"];

    // Güncellenebilir kurulum mu? (Geliştirirken "dotnet run" ile açılınca exe'yi değiştirmeye çalışmayalım.)
    private static string? ExeYolu
    {
        get
        {
            var yol = Environment.ProcessPath;
            if (yol is null || !ExeAdlari.Contains(Path.GetFileName(yol), StringComparer.OrdinalIgnoreCase)) return null;
            // Geliştirme derlemesi (bin\Debug): yanında dll'ler var, tek dosyalık kurulum değil. Onu güncellemeyelim.
            if (File.Exists(Path.Combine(Path.GetDirectoryName(yol)!, "Ekip.Uygulama.dll"))) return null;
            // Bu oturumda yeni ada çevrildiyse dosya artık yeni adda.
            var yeni = Path.Combine(Path.GetDirectoryName(yol)!, ExeAdlari[0]);
            return File.Exists(yol) || !File.Exists(yeni) ? yol : yeni;
        }
    }

    private static string GeciciKlasor => Path.Combine(Path.GetTempPath(), "31hane-guncelleme");

    // Önceki güncellemeden kalan eski exe'yi ve indirme klasörünü sil (açılışta çağrılır).
    // Ayrıca eski adla ("Ekip.Uygulama.exe") çalışıyorsak dosyayı "31Hane.exe" yapar: tek dosyalık exe adından
    // bağımsız çalışır, çalışan exe Windows'ta yeniden adlandırılabilir. Bir sonraki açılış yeni adla olur.
    public static void EskileriTemizle()
    {
        if (ExeYolu is not { } exe) return;
        try
        {
            var yeni = Path.Combine(Path.GetDirectoryName(exe)!, ExeAdlari[0]);
            if (!Path.GetFileName(exe).Equals(ExeAdlari[0], StringComparison.OrdinalIgnoreCase) && !File.Exists(yeni))
                File.Move(exe, yeni);
        }
        catch { /* Klasöre yazılamıyorsa eski adla devam. */ }
        try { foreach (var f in Directory.GetFiles(Path.GetDirectoryName(exe)!, "*.eski")) File.Delete(f); }
        catch { /* Kullanımdaysa bir sonraki açılışta silinir. */ }
        try { if (Directory.Exists(GeciciKlasor)) Directory.Delete(GeciciKlasor, true); } catch { }
    }

    // Arka plan denetimi: yeni sürüm varsa bilgisi, yoksa (ya da hata olursa) null.
    public static async Task<SurumBilgisi?> Kontrol(string adres) => (await Denetle(adres)).Yeni;

    // Elle denetim için ayrıntılı sonuç: Yeni dolu = güncelleme var; Hata dolu = denetlenemedi; ikisi de null = güncelsin.
    public static async Task<(SurumBilgisi? Yeni, string? Hata)> Denetle(string adres)
    {
        if (string.IsNullOrWhiteSpace(adres)) return (null, "Güncelleme adresi ayarlı değil.");
        if (ExeYolu is null) return (null, "Bu kurulum otomatik güncellenemiyor (geliştirme sürümü).");
        try
        {
            using var zaman = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var b = await Http.GetFromJsonAsync<SurumBilgisi>(adres + (adres.Contains('?') ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks, Json, zaman.Token);
            if (b is null || !Version.TryParse(b.Surum, out var yeni)) return (null, "Sürüm bilgisi okunamadı.");
            var mevcut = MevcutSurum;
            if (new Version(yeni.Major, yeni.Minor, Math.Max(0, yeni.Build)) <= new Version(mevcut.Major, mevcut.Minor, Math.Max(0, mevcut.Build))) return (null, null);
            // "indir" göreli olabilir (ör. "31Hane-1.0.1.zip"): son.json'un bulunduğu yere göre çözülür.
            if (!Uri.TryCreate(new Uri(adres), b.Indir, out var indir) || indir.Scheme is not ("https" or "http")) return (null, "Sürüm bilgisi okunamadı.");
            return (b with { Indir = indir.ToString() }, null);
        }
        catch { return (null, "Güncelleme sunucusuna ulaşılamadı. İnternetini kontrol edip tekrar dene."); }
    }

    // İndir, doğrula, değiştir, yeniden başlat. Başarılıysa geri dönmez (uygulama kapanır). Hata olursa mesaj döner.
    public static async Task<string?> Uygula(SurumBilgisi b, IProgress<int> ilerleme, Action yenidenBaslamadanOnce)
    {
        var exe = ExeYolu;
        if (exe is null) return "Bu kurulum otomatik güncellenemiyor.";
        var klasor = Path.GetDirectoryName(exe)!;
        var gecici = GeciciKlasor;
        try
        {
            if (Directory.Exists(gecici)) Directory.Delete(gecici, true);
            Directory.CreateDirectory(gecici);
            var zip = Path.Combine(gecici, "guncelleme.zip");

            // İndir (ilerleme yüzdesiyle)
            using (var yanit = await Http.GetAsync(b.Indir, HttpCompletionOption.ResponseHeadersRead))
            {
                yanit.EnsureSuccessStatusCode();
                long toplam = yanit.Content.Headers.ContentLength ?? 0, okunan = 0;
                await using var kaynak = await yanit.Content.ReadAsStreamAsync();
                await using var hedef = File.Create(zip);
                var tampon = new byte[81920];
                int n;
                while ((n = await kaynak.ReadAsync(tampon)) > 0)
                {
                    await hedef.WriteAsync(tampon.AsMemory(0, n));
                    okunan += n;
                    if (toplam > 0) ilerleme.Report((int)(okunan * 100 / toplam));
                }
            }

            // Bütünlük: indirilen dosya yayınlanan dosyanın aynısı mı?
            if (!string.IsNullOrWhiteSpace(b.Sha256))
            {
                await using var f = File.OpenRead(zip);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(f));
                if (!hash.Equals(b.Sha256, StringComparison.OrdinalIgnoreCase)) return "İndirilen dosya bozuk çıktı, güncelleme iptal edildi.";
            }

            var acilan = Path.Combine(gecici, "dosyalar");
            ZipFile.ExtractToDirectory(zip, acilan);
            var yeniAd = ExeAdlari.FirstOrDefault(a => File.Exists(Path.Combine(acilan, a)));
            if (yeniAd is null) return "Güncelleme paketinde uygulama bulunamadı.";
            var yeniExe = Path.Combine(klasor, yeniAd);

            // Değiştir: çalışan exe'yi yeniden adlandır, yenilerini kopyala.
            File.Move(exe, exe + ".eski", overwrite: true);
            foreach (var dosya in Directory.GetFiles(acilan))
            {
                var hedef = Path.Combine(klasor, Path.GetFileName(dosya));
                if (File.Exists(hedef) && !hedef.Equals(exe, StringComparison.OrdinalIgnoreCase))
                    File.Move(hedef, hedef + ".eski", overwrite: true);
                File.Copy(dosya, hedef, overwrite: true);
            }

            yenidenBaslamadanOnce();
            var args = Environment.GetCommandLineArgs().Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a);
            Process.Start(new ProcessStartInfo(yeniExe, string.Join(' ', args)) { UseShellExecute = false, WorkingDirectory = klasor });
            Environment.Exit(0);
            return null;
        }
        catch (Exception e)
        {
            // Yarıda kaldıysa eski exe'yi geri koy.
            try { if (!File.Exists(exe) && File.Exists(exe + ".eski")) File.Move(exe + ".eski", exe); } catch { }
            return "Güncelleme yapılamadı: " + e.Message;
        }
    }
}
