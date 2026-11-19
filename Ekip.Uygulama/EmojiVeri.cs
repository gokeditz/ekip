using System.IO.Compression;
using System.Text;

namespace Ekip.Uygulama;

// Emoji kaydı: unicode dizisi + Türkçe/İngilizce ad ve anahtar kelimeler + kısa kodlar (:joy:).
public sealed class EmojiKaydi
{
    public required string Emoji { get; init; }      // Tam hali (FE0F dahil); mesaja bu yazılır
    public required string Anahtar { get; init; }    // Kod noktaları, FE0F'siz, "-" ile ("1f468-200d-1f469")
    public required int Kategori { get; init; }      // EmojiVeri.KategoriAdlari dizini
    public required string TrAd { get; init; }
    public required string EnAd { get; init; }
    public required string[] TrKelimeler { get; init; }
    public required string[] EnKelimeler { get; init; }
    public required string[] KisaKodlar { get; init; }

    // Arama için: harfleri sadeleştirilmiş (küçük harf, aksansız) kelime listeleri.
    internal string[] TrAdKelime = [], EnAdKelime = [], TrAnahtarKelime = [], EnAnahtarKelime = [], KodFold = [];
}

// Emoji verisi (gömülü emoji.zip). İlk kullanımda tembelce yüklenir (~30 ms); açılışta arka planda
// Hazirla() çağrılırsa ilk açılan seçici beklemez. Tüm üyeler iş parçacığı güvenlidir.
public static class EmojiVeri
{
    public static readonly string[] KategoriAdlari =
        ["İfadeler", "İnsanlar", "Hayvanlar ve doğa", "Yiyecek ve içecek", "Seyahat ve yerler", "Etkinlikler", "Nesneler", "Semboller", "Bayraklar"];

    // Sekmelerde simge olarak çizilen emojiler (kategori başına bir tane).
    public static readonly string[] KategoriSimgeleri = ["😀", "👋", "🐱", "🍔", "🚗", "⚽", "💡", "❤️", "🏁"];

    private static readonly object _kilit = new();
    private static ZipArchive? _zip;
    private static Dictionary<string, ZipArchiveEntry>? _png;
    private static List<EmojiKaydi>? _hepsi;
    private static List<EmojiKaydi>[]? _kategoriler;
    private static Dictionary<string, EmojiKaydi>? _anahtarlar;
    private static Dictionary<string, EmojiKaydi>? _kodlar;

    // Arka planda yükletmek için: Task.Run(EmojiVeri.Hazirla).
    public static void Hazirla() => Yukle();

    public static IReadOnlyList<EmojiKaydi> Hepsi { get { Yukle(); return _hepsi!; } }
    public static IReadOnlyList<EmojiKaydi> Kategori(int kategori) { Yukle(); return _kategoriler![kategori]; }
    public static int Sayi => Hepsi.Count;

    // Emoji dizesinden kayıt (FE0F farkı önemsenmez). Yoksa null.
    public static EmojiKaydi? Bul(string emoji) { Yukle(); return _anahtarlar!.GetValueOrDefault(Anahtar(emoji)); }

    // ":joy:" ya da "joy" → kayıt. Büyük-küçük harf duyarsız.
    public static EmojiKaydi? KisaKodBul(string kod)
    {
        Yukle();
        return _kodlar!.GetValueOrDefault(kod.Trim(':').ToLowerInvariant());
    }

    // Kod noktalarından anahtar: FE0F atılır, küçük hex, "-" ile. Eşleme anahtarı budur.
    public static string Anahtar(string emoji)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < emoji.Length; i++)
        {
            int cp = char.ConvertToUtf32(emoji, i);
            if (char.IsHighSurrogate(emoji[i])) i++;
            if (cp == 0xFE0F) continue;
            if (sb.Length > 0) sb.Append('-');
            sb.Append(cp.ToString("x"));
        }
        return sb.ToString();
    }

    // Türkçe ya da İngilizce ad/anahtar kelime/kısa kod ile arar. Her sorgu kelimesi bir kelimenin başıyla eşleşmeli
    // ("gül yüz" → gülen yüz). Türkçe harfler sadeleştirilir ("gulen" → "gülen"). En alakalı önce.
    public static List<EmojiKaydi> Ara(string sorgu, int enCok = 200)
    {
        Yukle();
        var belirteler = Sadelestir(sorgu).Split([' ', ':', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (belirteler.Length == 0) return [];

        var sonuc = new List<(EmojiKaydi E, int Puan, int Sira)>();
        int sira = 0;
        foreach (var e in _hepsi!)
        {
            int toplam = 0;
            foreach (var b in belirteler)
            {
                int p = Math.Max(Math.Max(Puan(e.TrAdKelime, b, 100, 70), Puan(e.EnAdKelime, b, 90, 60)),
                         Math.Max(Math.Max(Puan(e.TrAnahtarKelime, b, 60, 40), Puan(e.EnAnahtarKelime, b, 50, 35)), Puan(e.KodFold, b, 55, 45)));
                if (p == 0) { toplam = 0; break; }
                toplam += p;
            }
            if (toplam > 0) sonuc.Add((e, toplam, sira));
            sira++;
        }
        return sonuc.OrderByDescending(s => s.Puan).ThenBy(s => s.Sira).Take(enCok).Select(s => s.E).ToList();
    }

    // ":ma" yazarken öneri için: kısa kodu bu önekle başlayanlar (en kısa kod önce).
    public static List<(EmojiKaydi E, string Kod)> KisaKodOner(string onek, int enCok = 8)
    {
        Yukle();
        onek = onek.Trim(':').ToLowerInvariant();
        if (onek.Length == 0) return [];
        return _hepsi!
            .SelectMany(e => e.KisaKodlar.Select(k => (E: e, Kod: k)))
            .Where(x => x.Kod.StartsWith(onek, StringComparison.Ordinal))
            .OrderBy(x => x.Kod.Length).ThenBy(x => x.Kod, StringComparer.Ordinal)
            .GroupBy(x => x.E).Select(g => g.First())
            .Take(enCok).ToList();
    }

    private static int Puan(string[] kelimeler, string belirte, int tam, int onek)
    {
        int en = 0;
        foreach (var k in kelimeler)
        {
            if (k == belirte) return tam;
            if (k.StartsWith(belirte, StringComparison.Ordinal)) en = onek;
        }
        return en;
    }

    // Küçük harf + Türkçe/aksanlı harfleri ASCII'ye indirger ("İ", "I", "ı" hepsi "i").
    internal static string Sadelestir(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                'İ' or 'I' or 'ı' => 'i',
                'Ş' or 'ş' => 's',
                'Ç' or 'ç' => 'c',
                'Ğ' or 'ğ' => 'g',
                'Ö' or 'ö' => 'o',
                'Ü' or 'ü' => 'u',
                'â' or 'Â' => 'a',
                'î' or 'Î' => 'i',
                'û' or 'Û' => 'u',
                _ => char.ToLowerInvariant(c),
            });
        }
        return sb.ToString();
    }

    private static string[] Kelimele(IEnumerable<string> parcalar) =>
        parcalar.SelectMany(p => Sadelestir(p).Split([' ', ':', '-', '_', ',', '.', '’', '\''], StringSplitOptions.RemoveEmptyEntries)).Distinct().ToArray();

    // Gömülü PNG'yi (72x72) verir; yoksa null. Çağıran Dispose eder.
    internal static Bitmap? PngAc(string anahtar)
    {
        Yukle();
        if (!_png!.TryGetValue(anahtar, out var giris)) return null;
        byte[] bayt;
        lock (_kilit)
        {
            using var s = giris.Open();
            using var ms = new MemoryStream((int)giris.Length);
            s.CopyTo(ms);
            bayt = ms.ToArray();
        }
        using var ak = new MemoryStream(bayt);
        using var kaynak = Image.FromStream(ak);
        return new Bitmap(kaynak);
    }

    internal static bool PngVar(string anahtar) { Yukle(); return _png!.ContainsKey(anahtar); }

    private static void Yukle()
    {
        if (_hepsi != null) return;
        lock (_kilit)
        {
            if (_hepsi != null) return;
            var kaynak = typeof(EmojiVeri).Assembly.GetManifestResourceStream("emoji.zip")
                ?? throw new InvalidOperationException("emoji.zip gömülü kaynağı bulunamadı.");
            var zip = new ZipArchive(kaynak, ZipArchiveMode.Read);
            var png = new Dictionary<string, ZipArchiveEntry>();
            ZipArchiveEntry? veri = null;
            foreach (var g in zip.Entries)
            {
                if (g.Name == "veri.tsv") veri = g;
                else if (g.Name.EndsWith(".png", StringComparison.Ordinal)) png[g.Name[..^4]] = g;
            }

            var hepsi = new List<EmojiKaydi>(1800);
            var kategoriler = Enumerable.Range(0, KategoriAdlari.Length).Select(_ => new List<EmojiKaydi>()).ToArray();
            var anahtarlar = new Dictionary<string, EmojiKaydi>();
            var kodlar = new Dictionary<string, EmojiKaydi>();
            if (veri != null)
            {
                using var okuyucu = new StreamReader(veri.Open(), Encoding.UTF8);
                string? satir;
                while ((satir = okuyucu.ReadLine()) != null)
                {
                    var a = satir.Split('\t');
                    if (a.Length < 7) continue;
                    string[] Liste(string s) => s.Length == 0 ? [] : s.Split('|');
                    var e = new EmojiKaydi
                    {
                        Emoji = a[0], Anahtar = Anahtar(a[0]), Kategori = int.Parse(a[1]),
                        TrAd = a[2], TrKelimeler = Liste(a[3]), EnAd = a[4], EnKelimeler = Liste(a[5]), KisaKodlar = Liste(a[6]),
                    };
                    e.TrAdKelime = Kelimele([e.TrAd]);
                    e.EnAdKelime = Kelimele([e.EnAd]);
                    e.TrAnahtarKelime = Kelimele(e.TrKelimeler);
                    e.EnAnahtarKelime = Kelimele(e.EnKelimeler);
                    e.KodFold = Kelimele(e.KisaKodlar);
                    hepsi.Add(e);
                    kategoriler[e.Kategori].Add(e);
                    anahtarlar[e.Anahtar] = e;
                    foreach (var k in e.KisaKodlar) kodlar.TryAdd(k.ToLowerInvariant(), e);
                }
            }
            _zip = zip;
            _png = png;
            _anahtarlar = anahtarlar;
            _kodlar = kodlar;
            _kategoriler = kategoriler;
            _hepsi = hepsi; // En son atanır: diğer iş parçacıkları tam yüklenmeden görmesin.
        }
    }
}
