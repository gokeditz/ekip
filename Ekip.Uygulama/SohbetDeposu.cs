using Ekip.Ortak;

namespace Ekip.Uygulama;

// Bir konuşmanın (DM / kanal / grup) bellekte tutulan durumu. SohbetDeposu doldurur, arayüz sadece okur.
// Parti sohbeti depo kullanmaz; kendi SohbetVerisi'ni elle doldurur (Yuklendi = true).
public sealed class SohbetVerisi
{
    public KonusmaKimligi Kimlik;
    public List<Mesaj> Liste = [];                      // eskiden yeniye; gönderilmekte olanlar (Id < 0) en sonda
    public bool Yuklendi, Tumu, Yukleniyor, YuklemeHatasi;
    public int Okunmamis, Bahsedilme;                    // yerel sayaçlar (sunucudan gelen değerle başlar)
    public HashSet<string> Basarisiz = [];               // gönderilemeyen geçici mesajların IstemciId'leri
    public Dictionary<string, BekleyenGonderim> Bekleyenler = []; // IstemciId -> yüklenmekte olan gönderim
    public Dictionary<long, long> Yazanlar = [];         // kim -> yazıyor göstergesinin biteceği an (TickCount64)
}

// Gönderilecek yerel dosya (henüz yüklenmedi).
public sealed class GonderilecekDosya
{
    public required string Yol { get; init; }
    public required string Ad { get; init; }
    public string Mime { get; init; } = "application/octet-stream";
    public long Boyut { get; init; }
    public int Genislik { get; init; }
    public int Yukseklik { get; init; }
    public bool Gecici { get; init; }   // gönderilince diskten silinir (panodan yapıştırılan resim gibi)
    public bool Resim => Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

// Yükleme + gönderme aşamasındaki mesajın bilgisi (liste ilerlemeyi bundan çizer).
public sealed class BekleyenGonderim
{
    public required KonusmaKimligi Kimlik { get; init; }
    public required string IstemciId { get; init; }
    public required string Metin { get; init; }
    public long? YanitId { get; init; }
    public List<GonderilecekDosya> Dosyalar { get; init; } = [];
    public List<Ek> HazirEkler { get; init; } = [];     // GIF gibi yüklemesi gerekmeyenler
    public double[] Ilerleme { get; set; } = [];        // dosya başına 0..1
    public Ek?[] Yuklenen { get; set; } = [];           // yüklenmiş ekler (tekrar denemede yeniden yüklenmez)
    public bool Yukluyor { get; set; }
    public double Genel => Ilerleme.Length == 0 ? 0 : Ilerleme.Average();
}

// Tüm konuşmaların tek deposu. DM, kanal ve grup AYNI yoldan çalışır:
//
//   var depo = new SohbetDeposu(api, canli, benimId) { Goruyor = k => ... };
//   panel.Depo = depo; panel.Ac(kimlik, baslik);       // yükleme, okundu, gönderme, düzenleme, tepki... hepsi panelle depo arasında
//
// Olaylar arayüz iş parçacığında gelir. Yerel okunmamış sayaçları: başka konuşmaya mesaj gelince artar (o an
// bakılmıyorsa), okununca sıfırlanır; sunucudan gelen ilk değerler OkunmamisAyarla ile verilir.
public sealed class SohbetDeposu : IDisposable
{
    private readonly Api _api;
    private readonly Canli _canli;
    private readonly long _benId;
    private readonly Dictionary<KonusmaKimligi, SohbetVerisi> _veriler = [];
    private readonly System.Windows.Forms.Timer _yaziyorSaati = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _okunduSaati = new() { Interval = 450 };
    private readonly HashSet<KonusmaKimligi> _okunduBekleyen = [];
    private readonly Dictionary<KonusmaKimligi, long> _sonYaziyorBildirimi = [];
    private int _geciciNo;
    private bool _koptu, _atildi;

    private const int YaziyorSuresiMs = 6000;

    public SohbetDeposu(Api api, Canli canli, long benId)
    {
        _api = api;
        _canli = canli;
        _benId = benId;
        canli.MesajGeldi += m => MesajGeldi(m);
        canli.MesajGuncellendi += MesajGuncellendi;
        canli.Yaziyor += kim => YaziyorGeldi(new KonusmaKimligi(KonusmaTuru.Dm, kim), kim);
        canli.KonusmaYaziyor += YaziyorGeldi;
        canli.SohbetOkundu += karsi => DigerCihazdaOkundu(new KonusmaKimligi(KonusmaTuru.Dm, karsi));
        canli.KonusmaOkundu += DigerCihazdaOkundu;
        canli.BaglantiDegisti += b =>
        {
            if (!b) { _koptu = true; return; }
            if (_koptu) { _koptu = false; _ = KopukluktanSonraTazele(); }
        };
        _yaziyorSaati.Tick += (_, _) => YaziyorSuresiniKontrolEt();
        _okunduSaati.Tick += (_, _) => OkunduGonder();
    }

    // ---- Olaylar ----

    // Bir konuşmanın mesaj listesi/durumu değişti (yeni mesaj, düzenleme, tepki, yükleme, ilerleme...).
    public event Action<KonusmaKimligi>? Degisti;
    // Sunucudan yeni bir mesaj geldi (kendi gönderdiğim, başka cihazdan gelenler dahil). Kenar çubuğu sırası, bildirim için.
    public event Action<Mesaj, KonusmaKimligi>? YeniMesajGeldi;
    public event Action<KonusmaKimligi>? OkunmamisDegisti;
    public event Action<KonusmaKimligi>? YaziyorDegisti;
    // Kullanıcıya gösterilecek kısa uyarı/hata (mesaj, hataMi).
    public event Action<string, bool>? Bildirim;

    // Konuşma şu an gözün önünde mi? (pencere açık ve odakta, o sohbet seçili...) Arayüz verir; true ise gelen mesaj okunmuş sayılır.
    public Func<KonusmaKimligi, bool>? Goruyor { get; set; }

    public long BenId => _benId;

    // ---- Veri ----

    public SohbetVerisi Veri(KonusmaKimligi k)
    {
        if (!_veriler.TryGetValue(k, out var v)) _veriler[k] = v = new SohbetVerisi { Kimlik = k };
        return v;
    }

    public bool VeriVar(KonusmaKimligi k) => _veriler.ContainsKey(k);

    // Silinen/ayrılınan grup ya da kanalın verisini bırakır.
    public void Unut(KonusmaKimligi k)
    {
        if (_veriler.Remove(k)) OkunmamisDegisti?.Invoke(k);
    }

    // Konuşmayı açar: ilk sayfayı yükler (yüklüyse dokunmaz); bakılıyorsa okundu işaretler. Panel Ac() bunu çağırır.
    public async Task Ac(KonusmaKimligi k)
    {
        var v = Veri(k);
        if (!v.Yuklendi) await Yukle(k, eski: false);
        GoruntuleniyorsaOkundu(k);
    }

    public Task EskileriYukle(KonusmaKimligi k) => Yukle(k, eski: true);

    private async Task Yukle(KonusmaKimligi k, bool eski)
    {
        var v = Veri(k);
        if (v.Yukleniyor || (eski && (v.Tumu || !v.Yuklendi))) return;
        v.Yukleniyor = true;
        v.YuklemeHatasi = false;
        Degisti?.Invoke(k);
        long? once = eski ? v.Liste.FirstOrDefault(m => m.Id > 0)?.Id : null;
        var s = await _api.KonusmaMesajlari(k, once);
        v.Yukleniyor = false;
        if (_atildi) return;
        if (!s.Basarili)
        {
            if (!v.Yuklendi) v.YuklemeHatasi = true;
            Bildirim?.Invoke(s.Hata!, true);
            Degisti?.Invoke(k);
            return;
        }
        var gelen = s.Veri!;
        if (eski) v.Liste.InsertRange(0, gelen.Where(m => v.Liste.All(x => x.Id != m.Id)));
        else Birlestir(v, gelen);
        v.Tumu = v.Tumu || (eski || !v.Yuklendi) && gelen.Count < Kurallar.MesajSayfasi;
        v.Yuklendi = true;
        Degisti?.Invoke(k);
    }

    // En yeni sayfayı mevcut listeyle birleştirir: yükleme sırasında gelenler ve gönderilmekte olanlar kaybolmaz.
    private static void Birlestir(SohbetVerisi v, List<Mesaj> gelen)
    {
        if (gelen.Count == 0 && v.Liste.Count == 0) return;
        long enKucuk = gelen.Count == 0 ? long.MaxValue : gelen.Min(m => m.Id);
        long enBuyuk = gelen.Count == 0 ? 0 : gelen.Max(m => m.Id);
        var eskiler = v.Liste.Where(x => x.Id > 0 && x.Id < enKucuk).ToList();   // yeni sayfanın öncesi: olduğu gibi kalsın
        var yeniler = v.Liste.Where(x => x.Id > enBuyuk && x.Id >= enKucuk).ToList(); // istek sürerken canlı gelenler
        var bekleyen = v.Liste.Where(x => x.Id < 0 && gelen.All(m => m.IstemciId == null || m.IstemciId != x.IstemciId)).ToList();
        v.Liste = [.. eskiler, .. gelen, .. yeniler, .. bekleyen];
    }

    // Bağlantı kopup geri gelince açık/yüklü konuşmaların son sayfasını tazeler (kaçırılanlar gelsin).
    private async Task KopukluktanSonraTazele()
    {
        foreach (var (k, v) in _veriler.Where(x => x.Value.Yuklendi).ToList())
        {
            var s = await _api.KonusmaMesajlari(k, null);
            if (_atildi) return;
            if (!s.Basarili) continue;
            Birlestir(v, s.Veri!);
            Degisti?.Invoke(k);
        }
    }

    // ---- Canlı olaylar ----

    private void MesajGeldi(Mesaj m)
    {
        var k = KonusmaKimligi.Mesajin(m, _benId);
        var v = Veri(k);
        bool benden = m.GonderenId == _benId;

        if (v.Yuklendi)
        {
            if (v.Liste.Any(x => x.Id == m.Id)) { Degisti?.Invoke(k); return; } // zaten var (gönderme yanıtı önce geldi)
            Yerlestir(v, m);
        }

        if (!benden)
        {
            if (v.Yazanlar.Remove(m.GonderenId)) YaziyorDegisti?.Invoke(k);
            if (Goruyor?.Invoke(k) == true) Okundu(k, zorla: true);
            else
            {
                v.Okunmamis++;
                if (m.Bahsedilenler?.Contains(_benId) == true) v.Bahsedilme++;
                OkunmamisDegisti?.Invoke(k);
            }
        }
        Degisti?.Invoke(k);
        YeniMesajGeldi?.Invoke(m, k);
    }

    // Sunucudan gelen mesajı listeye koyar: bekleyen kopyası varsa onun yerine geçer; gönderilmekte olanlar hep en altta kalır.
    private static void Yerlestir(SohbetVerisi v, Mesaj m)
    {
        if (v.Liste.Any(x => x.Id == m.Id)) return;
        if (m.IstemciId != null)
        {
            int i = v.Liste.FindIndex(x => x.Id < 0 && x.IstemciId == m.IstemciId);
            if (i >= 0) { v.Liste[i] = m; return; }
        }
        int ilkBekleyen = v.Liste.FindIndex(x => x.Id < 0);
        if (ilkBekleyen < 0) v.Liste.Add(m); else v.Liste.Insert(ilkBekleyen, m);
    }

    private void MesajGuncellendi(Mesaj m)
    {
        var k = KonusmaKimligi.Mesajin(m, _benId);
        if (!_veriler.TryGetValue(k, out var v)) return;
        if (Degistir(v, m)) Degisti?.Invoke(k);
    }

    // Listedeki aynı Id'li mesajın yerine yenisini koyar (IstemciId korunur).
    private static bool Degistir(SohbetVerisi v, Mesaj yeni)
    {
        int i = v.Liste.FindIndex(x => x.Id == yeni.Id);
        if (i < 0) return false;
        v.Liste[i] = yeni with { IstemciId = v.Liste[i].IstemciId ?? yeni.IstemciId };
        return true;
    }

    private void YaziyorGeldi(KonusmaKimligi k, long kim)
    {
        if (kim == _benId) return;
        var v = Veri(k);
        bool yeni = !v.Yazanlar.ContainsKey(kim) || v.Yazanlar[kim] - Environment.TickCount64 < YaziyorSuresiMs - 1500;
        v.Yazanlar[kim] = Environment.TickCount64 + YaziyorSuresiMs;
        if (!_yaziyorSaati.Enabled) _yaziyorSaati.Start();
        if (yeni) YaziyorDegisti?.Invoke(k);
    }

    private void YaziyorSuresiniKontrolEt()
    {
        long simdi = Environment.TickCount64;
        bool kaldi = false;
        foreach (var (k, v) in _veriler.ToList())
        {
            if (v.Yazanlar.Count == 0) continue;
            var bitenler = v.Yazanlar.Where(x => x.Value <= simdi).Select(x => x.Key).ToList();
            foreach (var b in bitenler) v.Yazanlar.Remove(b);
            if (bitenler.Count > 0) YaziyorDegisti?.Invoke(k);
            if (v.Yazanlar.Count > 0) kaldi = true;
        }
        if (!kaldi) _yaziyorSaati.Stop();
    }

    // Şu an yazanların kimlikleri (süresi dolmamış olanlar).
    public IReadOnlyList<long> Yazanlar(KonusmaKimligi k)
    {
        if (!_veriler.TryGetValue(k, out var v) || v.Yazanlar.Count == 0) return [];
        long simdi = Environment.TickCount64;
        return v.Yazanlar.Where(x => x.Value > simdi).Select(x => x.Key).ToList();
    }

    // Ben yazıyorum: karşıya haber ver (aynı konuşma için en sık 3 saniyede bir).
    public void YaziyorBildir(KonusmaKimligi k)
    {
        long simdi = Environment.TickCount64;
        if (_sonYaziyorBildirimi.TryGetValue(k, out var son) && simdi - son < 3000) return;
        _sonYaziyorBildirimi[k] = simdi;
        _ = k.Tur == KonusmaTuru.Dm ? _canli.YaziyorBildir(k.Id) : _canli.KonusmaYaziyorBildir(k);
    }

    // ---- Okunmamış ----

    public int Okunmamis(KonusmaKimligi k) => _veriler.TryGetValue(k, out var v) ? v.Okunmamis : 0;
    public int Bahsedilme(KonusmaKimligi k) => _veriler.TryGetValue(k, out var v) ? v.Bahsedilme : 0;
    public int ToplamDmOkunmamis => _veriler.Where(x => x.Key.Tur == KonusmaTuru.Dm).Sum(x => x.Value.Okunmamis);

    // Sunucudaki listelerden (sohbetler, sunucu kanalları, gruplar) gelen değerleri yerel sayaca yazar.
    public void OkunmamisAyarla(KonusmaKimligi k, int okunmamis, int bahsedilme = 0)
    {
        var v = Veri(k);
        if (v.Okunmamis == okunmamis && v.Bahsedilme == bahsedilme) return;
        v.Okunmamis = okunmamis;
        v.Bahsedilme = bahsedilme;
        OkunmamisDegisti?.Invoke(k);
    }

    // Konuşma okundu: yerel sayaç sıfırlanır, sunucuya kısa bir gecikmeyle (birleştirilerek) bildirilir.
    // zorla: sayaç zaten 0 olsa da sunucuya bildir (açık sohbete mesaj gelince).
    public void Okundu(KonusmaKimligi k, bool zorla = false)
    {
        var v = Veri(k);
        bool degisti = v.Okunmamis != 0 || v.Bahsedilme != 0;
        v.Okunmamis = v.Bahsedilme = 0;
        if (degisti) OkunmamisDegisti?.Invoke(k);
        if (degisti || zorla)
        {
            _okunduBekleyen.Add(k);
            _okunduSaati.Stop();
            _okunduSaati.Start();
        }
    }

    // Konuşma gözün önündeyse ve okunmamışı varsa okundu yapar (pencere odağa gelince, sohbet açılınca).
    public void GoruntuleniyorsaOkundu(KonusmaKimligi k)
    {
        if (Okunmamis(k) == 0 && Bahsedilme(k) == 0) return;
        if (Goruyor?.Invoke(k) == true) Okundu(k);
    }

    private void OkunduGonder()
    {
        _okunduSaati.Stop();
        var liste = _okunduBekleyen.ToList();
        _okunduBekleyen.Clear();
        foreach (var k in liste) _ = _api.KonusmaOkundu(k);
    }

    private void DigerCihazdaOkundu(KonusmaKimligi k)
    {
        if (!_veriler.TryGetValue(k, out var v) || (v.Okunmamis == 0 && v.Bahsedilme == 0)) return;
        v.Okunmamis = v.Bahsedilme = 0;
        OkunmamisDegisti?.Invoke(k);
    }

    // ---- Gönderme ----

    // Mesajı hemen listeye koyar (geçici negatif Id), arka planda dosyaları yükleyip gönderir.
    // dosyalar: yerel dosyalar (yüklenir); ekler: hazır ekler (GIF). Dönen: geçici mesajın IstemciId'si.
    public string Gonder(KonusmaKimligi k, string metin, long? yanitId = null,
        IReadOnlyList<GonderilecekDosya>? dosyalar = null, IReadOnlyList<Ek>? ekler = null)
    {
        var b = new BekleyenGonderim
        {
            Kimlik = k, IstemciId = Guid.NewGuid().ToString("N"), Metin = metin.Trim(), YanitId = yanitId,
            Dosyalar = dosyalar?.ToList() ?? [], HazirEkler = ekler?.ToList() ?? [],
        };
        b.Ilerleme = new double[b.Dosyalar.Count];
        b.Yuklenen = new Ek?[b.Dosyalar.Count];
        _ = GonderAsync(b);
        return b.IstemciId;
    }

    private async Task GonderAsync(BekleyenGonderim b)
    {
        var k = b.Kimlik;
        var v = Veri(k);
        if (!v.Yuklendi) await Yukle(k, eski: false);
        if (_atildi) return;

        var yanit = b.YanitId is { } yid ? v.Liste.FirstOrDefault(x => x.Id == yid) : null;
        var gecici = new Mesaj(-(++_geciciNo), _benId, k.Tur == KonusmaTuru.Dm ? k.Id : 0, b.Metin,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), b.IstemciId,
            KanalId: k.Tur == KonusmaTuru.Kanal ? k.Id : null, GrupId: k.Tur == KonusmaTuru.Grup ? k.Id : null,
            YanitId: b.YanitId, Yanit: yanit is null ? null : MesajYardimci.Ozet(yanit),
            Ekler: b.HazirEkler.Count > 0 ? b.HazirEkler : null);
        v.Liste.Add(gecici);
        if (b.Dosyalar.Count > 0) v.Bekleyenler[b.IstemciId] = b;
        Degisti?.Invoke(k);
        await GonderimiYurut(b, gecici);
    }

    // Başarısız bir mesajı yeniden dener (yüklenmiş dosyalar tekrar yüklenmez).
    public async Task TekrarDene(KonusmaKimligi k, string istemciId)
    {
        var v = Veri(k);
        var gecici = v.Liste.FirstOrDefault(x => x.Id < 0 && x.IstemciId == istemciId);
        if (gecici is null) return;
        if (!v.Bekleyenler.TryGetValue(istemciId, out var b))
            b = new BekleyenGonderim
            {
                Kimlik = k, IstemciId = istemciId, Metin = gecici.Metin, YanitId = gecici.YanitId,
                HazirEkler = gecici.Ekler?.ToList() ?? [],
            };
        await GonderimiYurut(b, gecici);
    }

    // Başarısız mesajdan vazgeç (listeden kaldır).
    public void Vazgec(KonusmaKimligi k, string istemciId)
    {
        var v = Veri(k);
        v.Liste.RemoveAll(x => x.Id < 0 && x.IstemciId == istemciId);
        v.Basarisiz.Remove(istemciId);
        if (v.Bekleyenler.Remove(istemciId, out var b)) GeciciDosyalariSil(b);
        Degisti?.Invoke(k);
    }

    private async Task GonderimiYurut(BekleyenGonderim b, Mesaj gecici)
    {
        var k = b.Kimlik;
        var v = Veri(k);
        v.Basarisiz.Remove(b.IstemciId);
        b.Yukluyor = b.Dosyalar.Count > 0;
        Degisti?.Invoke(k);

        string? hata = null;
        var ekler = new List<Ek>();
        long sonIlerleme = 0;
        for (int i = 0; i < b.Dosyalar.Count && hata is null; i++)
        {
            if (b.Yuklenen[i] is { } hazir) { ekler.Add(hazir); continue; }
            var d = b.Dosyalar[i];
            int idx = i;
            var ilerleme = new Progress<double>(p =>
            {
                b.Ilerleme[idx] = p;
                long simdi = Environment.TickCount64;
                if (simdi - sonIlerleme < 90 && p < 1) return;
                sonIlerleme = simdi;
                Degisti?.Invoke(k);
            });
            var s = await _api.DosyaYukle(d.Yol, d.Mime, ilerleme);
            if (_atildi) return;
            if (!s.Basarili) { hata = s.Hata; break; }
            var ek = s.Veri!;
            if (ek.Genislik <= 0 && d.Genislik > 0) ek = ek with { Genislik = d.Genislik, Yukseklik = d.Yukseklik };
            b.Yuklenen[i] = ek;
            b.Ilerleme[i] = 1;
            ekler.Add(ek);
        }
        b.Yukluyor = false;

        Sonuc<Mesaj>? sonuc = null;
        if (hata is null)
        {
            ekler.AddRange(b.HazirEkler);
            sonuc = await _api.KonusmayaGonder(k, new MesajGonderme(b.Metin, b.IstemciId, b.YanitId, ekler.Count > 0 ? ekler : null));
            if (_atildi) return;
            if (!sonuc.Basarili) hata = sonuc.Hata;
        }

        if (hata != null)
        {
            if (v.Liste.Any(x => x.Id < 0 && x.IstemciId == b.IstemciId)) v.Basarisiz.Add(b.IstemciId); // vazgeçilmediyse
            Bildirim?.Invoke(hata, true);
            Degisti?.Invoke(k);
            return;
        }

        v.Bekleyenler.Remove(b.IstemciId);
        GeciciDosyalariSil(b);
        var yeni = sonuc!.Veri! with { IstemciId = b.IstemciId };
        // Canlı bağlantıdan önce gelmiş olabilir; değilse burada yerleştir.
        if (v.Liste.Any(x => x.Id == yeni.Id)) v.Liste.RemoveAll(x => x.Id < 0 && x.IstemciId == b.IstemciId);
        if (!_canli.Bagli) MesajGeldi(yeni);   // canlı bağlantı yoksa olay gelmez: kendimiz işleyelim
        else
        {
            if (v.Yuklendi && v.Liste.All(x => x.Id != yeni.Id)) Yerlestir(v, yeni);
            Degisti?.Invoke(k);
            YeniMesajGeldi?.Invoke(yeni, k);
        }
    }

    private static void GeciciDosyalariSil(BekleyenGonderim b)
    {
        foreach (var d in b.Dosyalar.Where(d => d.Gecici))
            try { File.Delete(d.Yol); } catch { }
    }

    // ---- Düzenleme, silme, tepki ----

    // Hata mesajını döner; başarılıysa null.
    public async Task<string?> Duzenle(KonusmaKimligi k, long mesajId, string metin)
    {
        var s = await _api.MesajDuzenle(mesajId, metin.Trim());
        if (!s.Basarili) return s.Hata;
        if (_veriler.TryGetValue(k, out var v) && Degistir(v, s.Veri!)) Degisti?.Invoke(k);
        return null;
    }

    public async Task<string?> Sil(KonusmaKimligi k, long mesajId)
    {
        var s = await _api.MesajSil(mesajId);
        if (!s.Basarili) return s.Hata;
        if (_veriler.TryGetValue(k, out var v))
        {
            int i = v.Liste.FindIndex(x => x.Id == mesajId);
            if (i >= 0)
            {
                v.Liste[i] = v.Liste[i] with { Silindi = true, Metin = "", Ekler = [], Tepkiler = [] };
                Degisti?.Invoke(k);
            }
        }
        return null;
    }

    // Tepkiyi hemen uygular (aç/kapa), sunucu yanıtıyla düzeltir; olmazsa geri alır.
    public async Task TepkiVer(KonusmaKimligi k, long mesajId, string emoji)
    {
        if (!_veriler.TryGetValue(k, out var v)) return;
        int i = v.Liste.FindIndex(x => x.Id == mesajId);
        if (i < 0) return;
        var eski = v.Liste[i];
        var tepkiler = (eski.Tepkiler ?? []).Select(t => new Tepki(t.Emoji, [.. t.Kullanicilar])).ToList();
        var t = tepkiler.FirstOrDefault(x => x.Emoji == emoji);
        if (t is null) tepkiler.Add(new Tepki(emoji, [_benId]));
        else if (!t.Kullanicilar.Remove(_benId)) t.Kullanicilar.Add(_benId);
        tepkiler.RemoveAll(x => x.Kullanicilar.Count == 0);
        var gecici = eski with { Tepkiler = tepkiler };
        v.Liste[i] = gecici;
        Degisti?.Invoke(k);

        var s = await _api.TepkiVer(mesajId, emoji);
        if (_atildi) return;
        if (s.Basarili) { if (Degistir(v, s.Veri!)) Degisti?.Invoke(k); return; }
        Bildirim?.Invoke(s.Hata!, true);
        int j = v.Liste.IndexOf(gecici);
        if (j >= 0) { v.Liste[j] = eski; Degisti?.Invoke(k); }
    }

    public void Dispose()
    {
        _atildi = true;
        _yaziyorSaati.Dispose();
        _okunduSaati.Dispose();
    }
}

// Mesajla ilgili küçük yardımcılar: kısa metin, ek özeti, yanıt özeti.
public static class MesajYardimci
{
    public static string EkOzeti(Ek e) => e.Tur switch
    {
        "resim" => "📷 Fotoğraf",
        "gif" => "🎞️ GIF",
        "video" => "🎬 Video",
        "ses" => "🎵 Ses kaydı",
        _ => "📎 " + (string.IsNullOrWhiteSpace(e.Ad) ? "Dosya" : e.Ad),
    };

    // Mini sohbet, overlay, bildirim gibi sade yerler için: metin, yoksa ek özeti.
    public static string KisaMetin(Mesaj m)
    {
        if (m.Silindi) return "Bu mesaj silindi.";
        var ekler = m.Ekler is { Count: > 0 } ? string.Join("  ", m.Ekler.Select(EkOzeti)) : "";
        return ekler.Length == 0 ? m.Metin : m.Metin.Length == 0 ? ekler : m.Metin + "\n" + ekler;
    }

    public static MesajOzeti Ozet(Mesaj m)
    {
        var metin = m.Metin.Replace('\n', ' ');
        if (metin.Length > 120) metin = metin[..120];
        return new MesajOzeti(m.Id, m.GonderenId, metin, m.Ekler is { Count: > 0 }, m.Silindi);
    }
}
