using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ekip.Uygulama;

// Otomatik güncelleme. Bir adresteki "son.json" okunur:
//   { "surum": "1.0.1", "indir": "https://.../Ekip-1.0.1.zip", "sha256": "…", "notlar": "…" }
// Daha yeni sürüm varsa zip indirilir, bütünlüğü (sha256) kontrol edilir, exe değiştirilip uygulama yeniden açılır.
// Çalışan bir exe Windows'ta silinemez ama YENİDEN ADLANDIRILABİLİR: eskisini ".eski" yapıp yenisini yerine koyuyoruz.
public static class Guncelleyici
{
    public record SurumBilgisi(string Surum, string Indir, string? Sha256, string? Notlar);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Version MevcutSurum =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public static string MevcutSurumYazisi => $"{MevcutSurum.Major}.{MevcutSurum.Minor}.{MevcutSurum.Build}";

    // Güncellenebilir kurulum mu? (Geliştirirken "dotnet run" ile açılınca exe'yi değiştirmeye çalışmayalım.)
    private static string? ExeYolu
    {
        get
        {
            var yol = Environment.ProcessPath;
            return yol != null && Path.GetFileName(yol).Equals("Ekip.Uygulama.exe", StringComparison.OrdinalIgnoreCase) ? yol : null;
        }
    }

    private static string GeciciKlasor => Path.Combine(Path.GetTempPath(), "ekip-guncelleme");

    // Önceki güncellemeden kalan eski exe'yi ve indirme klasörünü sil (açılışta çağrılır).
    public static void EskileriTemizle()
    {
        if (ExeYolu is not { } exe) return;
        try { foreach (var f in Directory.GetFiles(Path.GetDirectoryName(exe)!, "*.eski")) File.Delete(f); }
        catch { /* Kullanımdaysa bir sonraki açılışta silinir. */ }
        try { if (Directory.Exists(GeciciKlasor)) Directory.Delete(GeciciKlasor, true); } catch { }
    }

    public static async Task<SurumBilgisi?> Kontrol(string adres)
    {
        if (string.IsNullOrWhiteSpace(adres) || ExeYolu is null) return null;
        try
        {
            using var zaman = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var b = await Http.GetFromJsonAsync<SurumBilgisi>(adres + (adres.Contains('?') ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks, Json, zaman.Token);
            if (b is null || !Version.TryParse(b.Surum, out var yeni)) return null;
            var mevcut = MevcutSurum;
            if (new Version(yeni.Major, yeni.Minor, Math.Max(0, yeni.Build)) <= new Version(mevcut.Major, mevcut.Minor, Math.Max(0, mevcut.Build))) return null;
            // "indir" göreli olabilir (ör. "Ekip-1.0.1.zip"): son.json'un bulunduğu yere göre çözülür.
            if (!Uri.TryCreate(new Uri(adres), b.Indir, out var indir) || indir.Scheme is not ("https" or "http")) return null;
            return b with { Indir = indir.ToString() };
        }
        catch { return null; } // İnternet yoksa ya da dosya bozuksa sessizce geç.
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
            if (!File.Exists(Path.Combine(acilan, "Ekip.Uygulama.exe"))) return "Güncelleme paketinde uygulama bulunamadı.";

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
            Process.Start(new ProcessStartInfo(exe, string.Join(' ', args)) { UseShellExecute = false, WorkingDirectory = klasor });
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
