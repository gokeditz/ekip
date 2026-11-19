using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace Ekip.Uygulama;

// Animasyonlu GIF: kareleri (küçültülmüş) bellekte tutar, ilerletmeyi uygulama genelindeki TEK ortak zamanlayıcı yapar.
//
// Kullanım (kontrol tarafı):
//   var resim = await DosyaOnbellegi.ResimAl(api, url, maks);          // ilk kare
//   var anim = AnimasyonluResim.Bul(resim);                            // GIF animasyonluysa dolu, değilse null
//   _kayit = anim is null ? null : AnimasyonluResim.Kaydet(this, anim, () => Invalidate(alan));
//   ...çizerken: g.DrawImage(anim?.Gecerli ?? resim, ...)
//   ...görünmez olunca / kontrol kapanınca: _kayit?.Dispose();
// Aynı GIF'i gösteren birden çok kontrol aynı kareyi paylaşır (ek maliyet yok). Form küçültülünce ya da
// kontrol görünmez olunca zamanlayıcı hiçbir şey yapmaz; kayıtlı kimse kalmayınca zamanlayıcı durur.
public sealed class AnimasyonluResim : IDisposable
{
    // En kısa kare süresi (ms). Çoğu GIF 0-10 ms yazar; bu işlemciyi gereksiz yorar.
    public const int EnKisaSure = 20;

    // Tek bir animasyonun bellekte tutacağı en çok kare baytı; aşarsa kareler seyreltilir (süreleri toplanır).
    private const long KareButcesi = 28L * 1024 * 1024;

    private static readonly ConditionalWeakTable<Image, AnimasyonluResim> _ilkKareler = new();

    private Bitmap[] _kareler;
    private readonly int[] _sureler;
    private int _gecerli;
    private long _sonrakiMs;
    private int _kayitSayisi;
    private bool _atildi;

    private AnimasyonluResim(Bitmap[] kareler, int[] sureler)
    {
        _kareler = kareler;
        _sureler = sureler;
    }

    public int Genislik => _kareler[0].Width;
    public int Yukseklik => _kareler[0].Height;
    public int KareSayisi => _kareler.Length;
    public int Gecerli => _gecerli;

    // Şu an gösterilmesi gereken kare. Çizim sırasında kullan.
    public Bitmap GecerliKare => _kareler[Math.Min(_gecerli, _kareler.Length - 1)];

    // Bellekte kapladığı yaklaşık bayt (önbellek bütçesi için).
    public long Maliyet => _kareler.Sum(k => (long)k.Width * k.Height * 4);

    // DosyaOnbellegi.ResimAl'ın döndürdüğü ilk kareye bağlı animasyonu verir; GIF animasyonlu değilse null.
    public static AnimasyonluResim? Bul(Image? ilkKare) =>
        ilkKare != null && _ilkKareler.TryGetValue(ilkKare, out var a) ? a : null;

    // Kare listesinden animasyon oluşturur (DosyaOnbellegi kullanır). Tek kareyse null döner.
    internal static AnimasyonluResim? Olustur(List<Bitmap> kareler, List<int> sureler, bool ilkKareyeBagla)
    {
        if (kareler.Count < 2) return null;
        var a = new AnimasyonluResim(kareler.ToArray(), sureler.Select(s => Math.Max(EnKisaSure, s)).ToArray());
        if (ilkKareyeBagla) _ilkKareler.Add(a._kareler[0], a);
        return a;
    }

    // GIF dosyasının karelerini çözer. Her kare en uzun kenarı maksKenar olacak şekilde küçültülür.
    // Hata olursa ya da animasyon yoksa null (o zaman ilk kareyi DosyaOnbellegi tek resim olarak verir).
    internal static (AnimasyonluResim? Anim, Bitmap? Ilk) Coz(Image kaynak, int maksKenar, bool ilkKareyeBagla)
    {
        try
        {
            var boyut = new FrameDimension(kaynak.FrameDimensionsList[0]);
            int n = kaynak.GetFrameCount(boyut);
            if (n < 2) return (null, null);

            double olcek = Math.Min(1.0, (double)maksKenar / Math.Max(kaynak.Width, kaynak.Height));
            int w = Math.Max(1, (int)Math.Round(kaynak.Width * olcek)), h = Math.Max(1, (int)Math.Round(kaynak.Height * olcek));

            // Süreler: 0x5100 özelliği, her kare için 4 bayt, 1/100 sn.
            var sureOzelligi = Array.Find(kaynak.PropertyItems, p => p.Id == 0x5100)?.Value;

            // Bellek bütçesi aşılırsa her 'adim' karede birini al, aradakilerin süresini ona ekle.
            long kareBayti = (long)w * h * 4;
            int adim = (int)Math.Max(1, Math.Ceiling((double)kareBayti * n / KareButcesi));

            var kareler = new List<Bitmap>();
            var sureler = new List<int>();
            int biriken = 0;
            for (int i = 0; i < n; i++)
            {
                int sure = sureOzelligi != null && sureOzelligi.Length >= (i + 1) * 4 ? BitConverter.ToInt32(sureOzelligi, i * 4) * 10 : 100;
                biriken += Math.Max(EnKisaSure, sure);
                if (i % adim != 0 && i != n - 1) continue;

                kaynak.SelectActiveFrame(boyut, i);
                var k = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(k))
                {
                    g.InterpolationMode = olcek < 1 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(kaynak, new Rectangle(0, 0, w, h), 0, 0, kaynak.Width, kaynak.Height, GraphicsUnit.Pixel);
                }
                kareler.Add(k);
                sureler.Add(biriken);
                biriken = 0;
            }
            var anim = Olustur(kareler, sureler, ilkKareyeBagla);
            return anim is null ? (null, kareler.FirstOrDefault()) : (anim, anim._kareler[0]);
        }
        catch { return (null, null); }
    }

    public void Dispose()
    {
        if (_atildi) return;
        _atildi = true;
        // Önce kayıtları sil: zamanlayıcı atılmış kareyi yenilemeye çalışmasın. (Çizen kontroller de kaydını önceden silmeli.)
        foreach (var k in _kayitlar.Where(k => k.Anim == this).ToArray()) k.Dispose();
        foreach (var k in _kareler) k.Dispose();
    }

    // ---- Ortak zamanlayıcı ----

    private sealed class Kayit(AnimasyonluResim anim, Control sahip, Action yenile) : IDisposable
    {
        public readonly AnimasyonluResim Anim = anim;
        public readonly Control Sahip = sahip;
        public readonly Action Yenile = yenile;
        public bool Bitti;

        public void Dispose()
        {
            if (Bitti) return;
            Bitti = true;
            Anim._kayitSayisi--;
            _kayitlar.Remove(this);
            if (_kayitlar.Count == 0) ZamanlayiciyiDurdur();
        }
    }

    private static readonly List<Kayit> _kayitlar = [];
    private static System.Windows.Forms.Timer? _zamanlayici;

    // Bir kontrolün bu animasyonu göstermeye başladığını bildirir. Dönen nesneyi Dispose ederek kaydı sil
    // (kontrol görünmez olunca, resim değişince, kontrol kapanınca). yenile: kareyi yeniden çizdir
    // (ör. () => Invalidate(resimAlani)); UI iş parçacığında çağrılır. Kontrol Dispose edilirse kayıt kendiliğinden düşer.
    public static IDisposable Kaydet(Control sahip, AnimasyonluResim anim, Action yenile)
    {
        var k = new Kayit(anim, sahip, yenile);
        _kayitlar.Add(k);
        if (anim._kayitSayisi++ == 0) anim._sonrakiMs = Environment.TickCount64 + anim._sureler[anim._gecerli];
        if (_zamanlayici is null)
        {
            _zamanlayici = new System.Windows.Forms.Timer { Interval = EnKisaSure };
            _zamanlayici.Tick += Tik;
            _zamanlayici.Start();
        }
        return k;
    }

    private static void ZamanlayiciyiDurdur()
    {
        _zamanlayici?.Stop();
        _zamanlayici?.Dispose();
        _zamanlayici = null;
    }

    private static void Tik(object? s, EventArgs e)
    {
        long simdi = Environment.TickCount64;
        long enYakin = long.MaxValue;
        var formDurumu = new Dictionary<Form, bool>();
        var ilerleyenler = new HashSet<AnimasyonluResim>();
        bool gorunenVar = false;

        // Düşen/kapanan kontrolleri temizle, görünür olanlara bak.
        for (int i = _kayitlar.Count - 1; i >= 0; i--)
        {
            var k = _kayitlar[i];
            if (k.Sahip.IsDisposed || k.Anim._atildi) { k.Dispose(); continue; }
        }
        if (_kayitlar.Count == 0) return;

        foreach (var k in _kayitlar.ToArray())
        {
            if (!Gorunur(k.Sahip, formDurumu)) continue;
            gorunenVar = true;
            var a = k.Anim;
            if (ilerleyenler.Add(a))
            {
                // Süresi dolan kareleri ilerlet (geride kaldıysak çok kare atlama: en fazla birkaç kare).
                int atlama = 0;
                while (simdi >= a._sonrakiMs && atlama++ < 4)
                {
                    a._gecerli = (a._gecerli + 1) % a._kareler.Length;
                    a._sonrakiMs += a._sureler[a._gecerli];
                    a._degisti = true;
                }
                if (simdi >= a._sonrakiMs) a._sonrakiMs = simdi + a._sureler[a._gecerli];
                enYakin = Math.Min(enYakin, a._sonrakiMs);
            }
            if (a._degisti)
            {
                try { k.Yenile(); } catch { /* Kontrol o anda kapanıyor olabilir. */ }
            }
        }
        foreach (var a in ilerleyenler) a._degisti = false;

        // Hiçbir şey görünmüyorsa (küçültülmüş/gizli) seyrek bak; görünüyorsa bir sonraki kareye göre uyu.
        int sonraki = !gorunenVar || enYakin == long.MaxValue ? 400 : (int)Math.Clamp(enYakin - Environment.TickCount64, 10, 200);
        if (_zamanlayici != null && _zamanlayici.Interval != sonraki) _zamanlayici.Interval = sonraki;
    }

    private bool _degisti;

    // Kontrol gerçekten ekranda mı: kendisi ve üstleri görünür, form küçültülmemiş.
    private static bool Gorunur(Control c, Dictionary<Form, bool> formDurumu)
    {
        if (!c.IsHandleCreated || !c.Visible) return false;
        var f = c.FindForm();
        if (f is null) return false;
        if (!formDurumu.TryGetValue(f, out var ok))
            formDurumu[f] = ok = f.Visible && f.WindowState != FormWindowState.Minimized;
        return ok;
    }
}
