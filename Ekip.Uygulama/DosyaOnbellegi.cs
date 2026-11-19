using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Ağdan inen resim/dosyaların disk + bellek önbelleği.
//
// Disk: %LocalAppData%\Ekip[-profil]\onbellek\<adres özeti>\<dosya adı>   (en fazla ~300 MB, en eski kullanılan silinir).
//   Dosya adı korunur: "Aç" dediğinde varsayılan program gerçek adı görür.
// Bellek: küçültülmüş resimler için LRU (~96 MB). Animasyonlu GIF'in kareleri de burada durur.
//
// ÖNEMLİ: ResimAl'ın döndürdüğü resmi Dispose ETME; önbellek paylaşıyor. (Önbellekten atılan resim çöp toplayıcıya kalır,
// hâlâ çizen biri varsa bozulmaz.)
public static class DosyaOnbellegi
{
    private const long DiskEnCok = 300L * 1024 * 1024;
    private const long DiskHedef = 240L * 1024 * 1024; // sınır aşılınca buraya kadar sil
    private const long BellekButcesi = 96L * 1024 * 1024;
    private const long EnBuyukPiksel = 120_000_000; // bundan büyük resim çözülmez (bellek)

    private sealed class DiskGirisi(string yol, long boyut, DateTime son)
    {
        public string Yol = yol;
        public long Boyut = boyut;
        public DateTime SonKullanim = son;
    }

    private sealed class BellekGirisi(string anahtar, Image resim, long maliyet)
    {
        public string Anahtar = anahtar;
        public Image Resim = resim;
        public long Maliyet = maliyet;
    }

    private static readonly object _kilit = new();
    private static Dictionary<string, DiskGirisi>? _indeks;
    private static long _diskToplam;
    private static readonly Dictionary<string, Task<string?>> _indirmeler = [];

    private static readonly LinkedList<BellekGirisi> _lru = new();
    private static readonly Dictionary<string, LinkedListNode<BellekGirisi>> _bellek = [];
    private static readonly Dictionary<string, Task<Image?>> _cozulenler = [];
    private static long _bellekBayti;

    // Önbellek klasörü. Profil adı ("--profil") varsa ayrı klasör.
    public static string Klasor => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Ayarlar.ProfilAdi is null ? "Ekip" : "Ekip-" + Ayarlar.ProfilAdi, "onbellek");

    // ------------------------------------------------------------------ Resim

    // Resmi indirir (gerekirse), en uzun kenarı maksKenar piksel olacak şekilde KÜÇÜLTÜR (büyütmez) ve belleğe alır.
    // Animasyonlu GIF ise dönen resim ilk karedir; AnimasyonluResim.Bul(resim) ile kareleri alınır.
    // Aynı anda aynı resim istenirse tek indirme yapılır. Başarısızsa null.
    public static Task<Image?> ResimAl(Api api, string url, int maksKenar)
    {
        maksKenar = Math.Clamp(maksKenar, 16, 8192);
        var tam = api.TamAdres(url);
        var anahtar = Ozet(tam) + "@" + maksKenar;
        lock (_kilit)
        {
            if (_bellek.TryGetValue(anahtar, out var dugum))
            {
                _lru.Remove(dugum);
                _lru.AddFirst(dugum);
                return Task.FromResult<Image?>(dugum.Value.Resim);
            }
            if (_cozulenler.TryGetValue(anahtar, out var bekleyen)) return bekleyen;
            var gorev = ResimYukle(api, url, tam, anahtar, maksKenar);
            _cozulenler[anahtar] = gorev;
            return gorev;
        }
    }

    // Bellekte hazırsa hemen verir, yoksa null (indirme başlatmaz). Çizim sırasında hızlı bakmak için.
    public static Image? BellektenAl(Api api, string url, int maksKenar)
    {
        var anahtar = Ozet(api.TamAdres(url)) + "@" + Math.Clamp(maksKenar, 16, 8192);
        lock (_kilit)
        {
            if (!_bellek.TryGetValue(anahtar, out var dugum)) return null;
            _lru.Remove(dugum);
            _lru.AddFirst(dugum);
            return dugum.Value.Resim;
        }
    }

    private static async Task<Image?> ResimYukle(Api api, string url, string tam, string anahtar, int maksKenar)
    {
        try
        {
            var yol = await Getir(api, url, AdOnerisi(tam), null).ConfigureAwait(false);
            if (yol is null) return null;
            var resim = await Task.Run(() => Coz(yol, maksKenar, onbellekliAnim: true).Resim).ConfigureAwait(false);
            if (resim is null) return null;
            long maliyet = AnimasyonluResim.Bul(resim)?.Maliyet ?? (long)resim.Width * resim.Height * 4;
            lock (_kilit) BellegeEkle(anahtar, resim, maliyet);
            return resim;
        }
        catch { return null; }
        finally { lock (_kilit) _cozulenler.Remove(anahtar); }
    }

    private static void BellegeEkle(string anahtar, Image resim, long maliyet)
    {
        if (_bellek.TryGetValue(anahtar, out var eski)) { _lru.Remove(eski); _bellek.Remove(anahtar); _bellekBayti -= eski.Value.Maliyet; }
        _bellek[anahtar] = _lru.AddFirst(new BellekGirisi(anahtar, resim, maliyet));
        _bellekBayti += maliyet;
        // Bütçeyi aşınca en az kullanılandan at (en az bir resim hep kalır). Dispose edilmez: çizen biri olabilir.
        while (_bellekBayti > BellekButcesi && _lru.Count > 1)
        {
            var son = _lru.Last!;
            _lru.RemoveLast();
            _bellek.Remove(son.Value.Anahtar);
            _bellekBayti -= son.Value.Maliyet;
        }
    }

    // Bellek önbelleğini boşaltır (ör. bellek baskısında). Disk dokunulmaz.
    public static void BellegiBosalt()
    {
        lock (_kilit) { _lru.Clear(); _bellek.Clear(); _bellekBayti = 0; }
    }

    // Dosyayı çözer: tek kare resim ya da animasyonlu GIF. Arka planda çağır.
    // onbellekliAnim: animasyonu ilk kareye bağlar (ResimAl'dan gelen paylaşımlı resimler için).
    // Dönen Resim çağıranındır (paylaşımlı değilse Dispose etmeli; Anim varsa Anim.Dispose kareleri de atar).
    internal static (Image? Resim, AnimasyonluResim? Anim) Coz(string yol, int maksKenar, bool onbellekliAnim)
    {
        try
        {
            byte[] bayt = File.ReadAllBytes(yol);
            using var ms = new MemoryStream(bayt);
            using var kaynak = Image.FromStream(ms, false, false);
            if ((long)kaynak.Width * kaynak.Height > EnBuyukPiksel) return (null, null);

            if (kaynak.RawFormat.Equals(ImageFormat.Gif))
            {
                var (anim, ilk) = AnimasyonluResim.Coz(kaynak, maksKenar, onbellekliAnim);
                if (anim != null) return (anim.GecerliKare, anim);
                if (ilk != null) return (ilk, null);
            }

            // EXIF yönü (telefon fotoğrafları yan gelmesin).
            try
            {
                var yon = Array.Find(kaynak.PropertyItems, p => p.Id == 0x0112)?.Value;
                if (yon is { Length: >= 2 })
                {
                    var donus = yon[0] switch { 3 => RotateFlipType.Rotate180FlipNone, 6 => RotateFlipType.Rotate90FlipNone, 8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone };
                    if (donus != RotateFlipType.RotateNoneFlipNone) kaynak.RotateFlip(donus);
                }
            }
            catch { /* Özellik yoksa ya da okunamazsa olduğu gibi. */ }

            double olcek = Math.Min(1.0, (double)maksKenar / Math.Max(kaynak.Width, kaynak.Height));
            int w = Math.Max(1, (int)Math.Round(kaynak.Width * olcek)), h = Math.Max(1, (int)Math.Round(kaynak.Height * olcek));
            var hedef = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(hedef))
            {
                g.InterpolationMode = olcek < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(kaynak, new Rectangle(0, 0, w, h), 0, 0, kaynak.Width, kaynak.Height, GraphicsUnit.Pixel);
            }
            return (hedef, null);
        }
        catch { return (null, null); }
    }

    // ------------------------------------------------------------------ Dosya

    // Dosyayı önbelleğe indirir (zaten varsa hemen) ve yerel yolunu verir. İlerleme 0..1. Başarısızsa null.
    public static Task<string?> Indir(Api api, Ek ek, IProgress<double>? p = null) =>
        Getir(api, ek.Url, TemizAd(ek.Ad), p);

    // Önbelleğe indirir, sonra kullanıcının İndirilenler klasörüne "ad.uzanti" (varsa "ad (1).uzanti") olarak kopyalar.
    // Yeni dosyanın yolunu verir. Başarısızsa null.
    public static async Task<string?> IndirilenlereKaydet(Api api, Ek ek, IProgress<double>? p = null)
    {
        var kaynak = await Indir(api, ek, p).ConfigureAwait(false);
        if (kaynak is null) return null;
        try
        {
            var klasor = IndirilenlerKlasoru();
            Directory.CreateDirectory(klasor);
            var hedef = BenzersizYol(klasor, TemizAd(ek.Ad, Path.GetFileName(kaynak)));
            await Task.Run(() => File.Copy(kaynak, hedef, false)).ConfigureAwait(false);
            return hedef;
        }
        catch { return null; }
    }

    // İndirip varsayılan programla açar (video, ses, belge...). Açılamazsa false.
    public static async Task<bool> AcVarsayilanla(Api api, Ek ek, IProgress<double>? p = null)
    {
        var yol = await Indir(api, ek, p).ConfigureAwait(false);
        if (yol is null) return false;
        return Ac(yol);
    }

    public static bool Ac(string yol)
    {
        try { Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true }); return true; }
        catch { return false; }
    }

    // Gezginde dosyayı seçili olarak gösterir.
    public static void KlasordeGoster(string yol)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{yol}\"") { UseShellExecute = true }); }
        catch { /* Gezgin açılamazsa sessizce geç. */ }
    }

    // Kullanıcının İndirilenler klasörü (taşınmışsa da doğru bulunur).
    public static string IndirilenlerKlasoru()
    {
        try
        {
            var kimlik = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(ref kimlik, 0, IntPtr.Zero, out var ptr) == 0)
            {
                var yol = Marshal.PtrToStringUni(ptr);
                Marshal.FreeCoTaskMem(ptr);
                if (!string.IsNullOrEmpty(yol)) return yol;
            }
        }
        catch { /* Aşağıdaki varsayılana düş. */ }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid kimlik, uint bayrak, IntPtr token, out IntPtr yol);

    // "ad.png" varsa "ad (1).png", o da varsa "ad (2).png"...
    public static string BenzersizYol(string klasor, string dosyaAdi)
    {
        var ad = Path.GetFileNameWithoutExtension(dosyaAdi);
        var uzanti = Path.GetExtension(dosyaAdi);
        var yol = Path.Combine(klasor, ad + uzanti);
        for (int i = 1; File.Exists(yol) || Directory.Exists(yol); i++) yol = Path.Combine(klasor, $"{ad} ({i}){uzanti}");
        return yol;
    }

    // ------------------------------------------------------------------ Disk önbelleği

    private static async Task<string?> Getir(Api api, string url, string ad, IProgress<double>? p)
    {
        var tam = api.TamAdres(url);
        var ozet = Ozet(tam);
        Task<string?> gorev;
        lock (_kilit)
        {
            IndeksiHazirla();
            if (_indeks!.TryGetValue(ozet, out var g) && File.Exists(g.Yol))
            {
                g.SonKullanim = DateTime.UtcNow;
                try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(g.Yol) > TimeSpan.FromHours(12)) File.SetLastWriteTimeUtc(g.Yol, DateTime.UtcNow); } catch { }
                p?.Report(1);
                return g.Yol;
            }
            if (!_indirmeler.TryGetValue(ozet, out gorev!))
                _indirmeler[ozet] = gorev = Task.Run(() => DiskeIndir(api, url, ozet, ad, p));
        }
        try { return await gorev.ConfigureAwait(false); }
        finally { lock (_kilit) _indirmeler.Remove(ozet); }
    }

    private static async Task<string?> DiskeIndir(Api api, string url, string ozet, string ad, IProgress<double>? p)
    {
        string klasor = Path.Combine(Klasor, ozet);
        string yol = Path.Combine(klasor, ad);
        string gecici = yol + ".part";
        try
        {
            Directory.CreateDirectory(klasor);
            bool tamam;
            await using (var fs = new FileStream(gecici, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                tamam = await api.DosyaIndir(url, fs, p).ConfigureAwait(false);
            if (!tamam) { Sil(klasor); return null; }
            File.Move(gecici, yol, true);
            long boyut = new FileInfo(yol).Length;
            List<string> silinecek;
            lock (_kilit)
            {
                if (_indeks!.TryGetValue(ozet, out var eski)) _diskToplam -= eski.Boyut;
                _indeks[ozet] = new DiskGirisi(yol, boyut, DateTime.UtcNow);
                _diskToplam += boyut;
                silinecek = Sigdir(ozet);
            }
            foreach (var k in silinecek) Sil(k);
            p?.Report(1);
            return yol;
        }
        catch { Sil(klasor); return null; }
    }

    // Disk sınırı aşıldıysa en eski kullanılanları indekste siler, silinecek klasörleri verir (silme kilit dışında yapılır).
    private static List<string> Sigdir(string koru)
    {
        var silinecek = new List<string>();
        if (_diskToplam <= DiskEnCok) return silinecek;
        foreach (var kv in _indeks!.Where(k => k.Key != koru).OrderBy(k => k.Value.SonKullanim).ToList())
        {
            if (_diskToplam <= DiskHedef) break;
            _indeks!.Remove(kv.Key);
            _diskToplam -= kv.Value.Boyut;
            silinecek.Add(Path.Combine(Klasor, kv.Key));
        }
        return silinecek;
    }

    private static void IndeksiHazirla()
    {
        if (_indeks != null) return;
        _indeks = [];
        _diskToplam = 0;
        try
        {
            if (Directory.Exists(Klasor))
                foreach (var d in new DirectoryInfo(Klasor).EnumerateDirectories())
                {
                    var dosyalar = d.GetFiles();
                    var iyi = dosyalar.FirstOrDefault(f => !f.Name.EndsWith(".part", StringComparison.Ordinal));
                    if (iyi is null) { Sil(d.FullName); continue; } // yarım kalmış indirme
                    foreach (var f in dosyalar.Where(f => f != iyi)) try { f.Delete(); } catch { }
                    _indeks[d.Name] = new DiskGirisi(iyi.FullName, iyi.Length, iyi.LastWriteTimeUtc);
                    _diskToplam += iyi.Length;
                }
        }
        catch { /* Klasör okunamazsa boş indeksle devam. */ }
        var silinecek = Sigdir("");
        if (silinecek.Count > 0) Task.Run(() => { foreach (var k in silinecek) Sil(k); });
    }

    private static void Sil(string klasor)
    {
        try { if (Directory.Exists(klasor)) Directory.Delete(klasor, true); } catch { /* Kullanımdaysa kalır, sonra silinir. */ }
    }

    // ------------------------------------------------------------------ Yardımcılar

    private static string Ozet(string tamAdres) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tamAdres)), 0, 12).ToLowerInvariant();

    // Adresin son parçasından dosya adı önerir.
    private static string AdOnerisi(string tamAdres)
    {
        try
        {
            var yol = new Uri(tamAdres).AbsolutePath;
            var ad = Uri.UnescapeDataString(yol.TrimEnd('/').Split('/').LastOrDefault() ?? "");
            return TemizAd(ad);
        }
        catch { return "dosya"; }
    }

    // Dosya sistemi için güvenli ad: geçersiz karakterler yok, kısa, boş değil.
    private static string TemizAd(string? ad, string? yedek = null)
    {
        ad = (ad ?? "").Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) ad = ad.Replace(c, '_');
        ad = ad.TrimEnd('.', ' ');
        if (ad.Length > 100)
        {
            var uz = Path.GetExtension(ad);
            ad = ad[..(100 - Math.Min(uz.Length, 20))] + (uz.Length <= 20 ? uz : "");
        }
        if (ad.Length == 0) ad = string.IsNullOrWhiteSpace(yedek) ? "dosya" : yedek;
        if (ad.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) ad += ".bin";
        return ad;
    }
}
