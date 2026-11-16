using Ekip.Ortak;

namespace Ekip.Sunucu;

// Partiler sadece bellekte durur: sunucu yeniden başlarsa dağılır, bu bilerek böyle (geçici oyun grupları).
public class PartiYoneticisi(Veritabani db, Varlik varlik)
{
    private class Parti
    {
        public required string Id;
        public long Lider;
        public readonly HashSet<long> Uyeler = [];
        public readonly HashSet<long> Davetliler = [];
        public string? Oyun;
        public readonly List<PartiMesaji> Mesajlar = [];
    }

    private readonly Lock _kilit = new();
    private readonly Dictionary<string, Parti> _partiler = [];
    private readonly Dictionary<long, Parti> _uyeninPartisi = [];

    public PartiDurumu? Durum(long ben)
    {
        lock (_kilit) return _uyeninPartisi.TryGetValue(ben, out var p) ? DurumYap(p) : null;
    }

    public PartiDurumu Kur(long ben)
    {
        lock (_kilit)
        {
            if (_uyeninPartisi.TryGetValue(ben, out var mevcut)) return DurumYap(mevcut);
            var p = new Parti { Id = Guid.NewGuid().ToString("N")[..12], Lider = ben };
            p.Uyeler.Add(ben);
            _partiler[p.Id] = p;
            _uyeninPartisi[ben] = p;
            return DurumYap(p);
        }
    }

    // Hata varsa mesajını döner.
    public string? DavetEt(long ben, long kimi, out string partiId)
    {
        partiId = "";
        lock (_kilit)
        {
            if (!_uyeninPartisi.TryGetValue(ben, out var p)) return "Önce bir parti kur.";
            if (db.IliskiGetir(ben, kimi) != Veritabani.Iliski.Arkadas) return "Sadece arkadaşlarını davet edebilirsin.";
            if (!varlik.Cevrimici(kimi)) return "Bu arkadaşın şu an çevrimdışı.";
            if (p.Uyeler.Contains(kimi)) return "Zaten partide.";
            if (p.Uyeler.Count >= Kurallar.PartiUyeMax) return "Parti dolu.";
            p.Davetliler.Add(kimi);
            partiId = p.Id;
            return null;
        }
    }

    // Katılınca eski partisinden çıkar. (yeni durum, eski partinin kalan üyeleri)
    public (PartiDurumu? durum, List<long> eskiUyeler) Katil(long ben, string partiId)
    {
        lock (_kilit)
        {
            if (!_partiler.TryGetValue(partiId, out var p) || !p.Davetliler.Remove(ben)) return (null, []);
            if (p.Uyeler.Count >= Kurallar.PartiUyeMax) return (null, []);
            var eski = AyrilKilitli(ben);
            p.Uyeler.Add(ben);
            _uyeninPartisi[ben] = p;
            return (DurumYap(p), eski);
        }
    }

    public void Reddet(long ben, string partiId)
    {
        lock (_kilit) if (_partiler.TryGetValue(partiId, out var p)) p.Davetliler.Remove(ben);
    }

    // Ayrılınca kalan üyeler döner (onlara güncelleme gidecek).
    public List<long> Ayril(long ben)
    {
        lock (_kilit) return AyrilKilitli(ben);
    }

    private List<long> AyrilKilitli(long ben)
    {
        if (!_uyeninPartisi.Remove(ben, out var p)) return [];
        p.Uyeler.Remove(ben);
        if (p.Uyeler.Count == 0) { _partiler.Remove(p.Id); return []; }
        if (p.Lider == ben) p.Lider = p.Uyeler.First();
        return [.. p.Uyeler];
    }

    public PartiBilgisi? OyunSec(long ben, string? oyun)
    {
        lock (_kilit)
        {
            if (!_uyeninPartisi.TryGetValue(ben, out var p)) return null;
            p.Oyun = string.IsNullOrWhiteSpace(oyun) ? null : oyun.Trim()[..Math.Min(oyun.Trim().Length, 200)];
            return Bilgi(p);
        }
    }

    public (PartiMesaji? mesaj, List<long> alicilar) MesajEkle(long ben, string metin)
    {
        metin = (metin ?? "").Trim();
        if (metin.Length == 0 || metin.Length > Kurallar.PartiMesajMax) return (null, []);
        var profil = db.ProfilGetir(ben);
        if (profil is null) return (null, []);
        lock (_kilit)
        {
            if (!_uyeninPartisi.TryGetValue(ben, out var p)) return (null, []);
            var m = new PartiMesaji(ben, profil.GorunenAd, profil.ProfilRengi, metin, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            p.Mesajlar.Add(m);
            if (p.Mesajlar.Count > 50) p.Mesajlar.RemoveAt(0);
            return (m, [.. p.Uyeler]);
        }
    }

    public PartiBilgisi? Bilgisi(long uye)
    {
        lock (_kilit) return _uyeninPartisi.TryGetValue(uye, out var p) ? Bilgi(p) : null;
    }

    private PartiDurumu DurumYap(Parti p) => new(Bilgi(p), [.. p.Mesajlar]);

    private PartiBilgisi Bilgi(Parti p) => new(p.Id, p.Lider,
        p.Uyeler.Select(db.ProfilGetir).OfType<Profil>().Select(varlik.Baskasina).ToList(), p.Oyun);
}
