using Ekip.Ortak;

namespace Ekip.Uygulama;

public enum Sayfa { Cevrimici, Tumu, Bekleyen, Ekle }

// Giriş sonrası ana pencere: solda kenar çubuğu, sağda arkadaşlar sayfası.
public partial class AnaForm : TemaliForm
{
    private readonly Api _api;
    private readonly Canli _canli;
    private readonly AvatarOnbellek _avatarlar;
    private Profil _ben;
    private ArkadasListesi _liste = new([], [], []);
    private Sayfa _sayfa = Sayfa.Cevrimici;
    private int _yenilemeNo;

    private readonly KenarCubugu _kenar = new();
    private readonly UstCubuk _ust = new();
    private readonly Label _bant = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Visible = false, Font = Tema.Yazi(9.5f, kalin: true) };
    private readonly Label _baslik = new() { AutoSize = true, Font = Tema.Yazi(8.5f, kalin: true), ForeColor = Tema.MetinIkincil, BackColor = Tema.Arkaplan };
    private readonly ArkadasListe _arkadaslar = new();
    private readonly BosDurum _bos = new() { Visible = false };
    private readonly EklePaneli _ekle = new() { Visible = false };
    private readonly System.Windows.Forms.Timer _bantZamanlayici = new() { Interval = 4000 };
    private bool _bagli;

    // Sohbetler
    private List<SohbetOzeti> _sohbetler = [];
    private long? _aktif;
    private readonly Dictionary<long, SohbetVerisi> _mesajlar = [];
    private readonly HashSet<string> _basarisiz = [];
    private readonly SohbetPaneli _sohbet = new() { Visible = false };
    private readonly System.Windows.Forms.Timer _yaziyorZamanlayici = new() { Interval = 6000 };
    private DateTime _sonYaziyorBildirimi;
    private int _geciciNo;

    public event Action? CikisYapildi;

    private readonly Ayarlar _ayarlar;
    private readonly AyarlarSayfasi _ayarlarSayfasi;
    private readonly ProfilDuzenleyici _profilDuzenleyici;
    private bool _ayarlarAcik, _yonetimAcik;
    private readonly YonetimSayfasi _yonetim;
    public string? CikisSebebi { get; private set; } // Yasaklanınca giriş ekranında gösterilir.

    public AnaForm(Api api, Profil ben, Ayarlar ayarlar)
    {
        _api = api;
        _ben = ben;
        _ayarlar = ayarlar;
        _avatarlar = new AvatarOnbellek(api);
        _canli = new Canli(api);

        _yonetim = new YonetimSayfasi(api, this) { Visible = false, Avatarlar = _avatarlar };
        _yonetim.Bildir += (m, h) => Bildir(m, h);
        _profilDuzenleyici = new ProfilDuzenleyici(api, ben, _avatarlar);
        _ayarlarSayfasi = new AyarlarSayfasi(_profilDuzenleyici, new KisayolPaneli()) { Visible = false };
        _profilDuzenleyici.Kaydedildi += p =>
        {
            _ben = p with { Durum = _ben.Durum };
            _kenar.Ben = _ben;
            Bildir("Profilin güncellendi.", hata: false);
        };
        var kp = _ayarlarSayfasi.Kisayollar;
        kp.YakalamaDurumu += b => _kisayolYakalaniyor = b;
        kp.Degisti += (o, m) =>
        {
            _ayarlar.OverlayTusu = o.ToString();
            _ayarlar.MiniTusu = m.ToString();
            _ayarlar.Kaydet();
            KisayollariYukle();
        };
        kp.MiniyiSifirla += () =>
        {
            _ayarlar.MiniAlan = null;
            _ayarlar.Kaydet();
            if (_mini != null)
            {
                var oyun = Pencereler.Gecerli(_miniOyun) ? Pencereler.Alan(_miniOyun) : Screen.PrimaryScreen!.Bounds;
                _mini.Bounds = new Rectangle(oyun.Left + Tema.S(24), oyun.Bottom - Tema.S(240) - Tema.S(90), Tema.S(340), Tema.S(240));
            }
        };

        Text = "Ekip";
        ClientSize = new Size(Tema.S(1180), Tema.S(720));
        MinimumSize = new Size(Tema.S(900), Tema.S(560));

        _arkadaslar.Avatarlar = _avatarlar;
        _kenar.Avatarlar = _avatarlar;
        _kenar.Ben = ben;

        _sohbet.Avatarlar = _avatarlar;
        Controls.AddRange([_kenar, _ust, _bant, _baslik, _arkadaslar, _bos, _ekle, _sohbet, _ayarlarSayfasi, _yonetim]);
        _kenar.YonetimTik += async () => await YonetimiAc();

        _ust.Secildi += SayfaAc;
        _kenar.KullaniciTik += DurumMenusu;
        _kenar.AyarTik += () => AyarlariAc(AyarlarSayfasi.Sekme.Profil);
        _kenar.ArkadaslarTik += SohbetKapat;
        _kenar.SohbetTik += id => SohbetAc(id);
        _arkadaslar.Eylem += SatirEylemi;
        _ekle.Gonder += IstekGonder;
        _avatarlar.Yuklendi += () => { _arkadaslar.Invalidate(); _kenar.Invalidate(); _sohbet.Invalidate(true); };
        _bantZamanlayici.Tick += (_, _) => { _bantZamanlayici.Stop(); BantGuncelle(); };

        _sohbet.Gonder += metin => { if (_aktif is { } id) MesajGonder(id, metin); };
        _sohbet.TekrarDene += async m => await Gonderimi(m);
        _sohbet.EskiIste += async () => { if (_aktif is { } id) await MesajlariYukle(id, eski: true); };
        _sohbet.Yaziyor += () =>
        {
            if (_aktif is not { } id || DateTime.Now - _sonYaziyorBildirimi < TimeSpan.FromSeconds(3)) return;
            _sonYaziyorBildirimi = DateTime.Now;
            _ = _canli.YaziyorBildir(id);
        };
        _yaziyorZamanlayici.Tick += (_, _) => { _yaziyorZamanlayici.Stop(); _sohbet.YaziyorGoster(false); };

        _canli.ArkadaslarDegisti += async () => await Yenile();
        _canli.ProfilDegisti += ProfilGeldi;
        _canli.DurumDegisti += DurumGeldi;
        _canli.BaglantiDegisti += b => { _bagli = b; BantGuncelle(); };
        _canli.MesajGeldi += MesajGeldi;
        _canli.SohbetOkundu += id => { OkunmamisAyarla(id, 0); };
        _canli.OturumSonlandi += sebep => { CikisSebebi = sebep; CikisYapildi?.Invoke(); };
        _canli.Yaziyor += id =>
        {
            if (id != _aktif) return;
            _sohbet.YaziyorGoster(true);
            _yaziyorZamanlayici.Stop();
            _yaziyorZamanlayici.Start();
        };

        PartiOlaylariniBagla();
        SayfaAc(Sayfa.Cevrimici);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _bagli = true; // İlk bağlantı denenirken "koptu" bandı yanıp sönmesin.
        _ = AdminMiKontrol();
        _ = _canli.Baslat();
        await Yenile();
    }

    // Yönetim sayfası sadece adminlere görünür. Sunucu admin değilse 403 döner.
    private async Task AdminMiKontrol()
    {
        var s = await _api.YonetimOzeti();
        _kenar.Admin = s.Basarili;
    }

    // Hem pencere kapanınca hem "Çıkış yap"ta çalışır: bağlantıyı, kısayolu ve overlay'i bırakır.
    private bool _temizlendi;
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_temizlendi)
        {
            _temizlendi = true;
            PartiTemizle();
            _bantZamanlayici.Dispose();
            _yaziyorZamanlayici.Dispose();
            _ = _canli.DisposeAsync().AsTask();
        }
        base.Dispose(disposing);
    }

    // Pencereye dönünce açık sohbetteki yeni mesajlar okunmuş sayılır.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_aktif is { } id) _ = OkunduYap(id);
    }

    // ---- Veri ----

    private async Task Yenile()
    {
        int no = ++_yenilemeNo;
        var arkadaslar = _api.Arkadaslar();
        var sohbetler = _api.Sohbetler();
        var a = await arkadaslar;
        var s = await sohbetler;
        if (no != _yenilemeNo || IsDisposed) return; // Daha yeni bir yenileme başladıysa bu sonucu at.
        if (!a.Basarili) { Bildir(a.Hata!, hata: true); return; }
        _liste = a.Veri!;

        if (s.Basarili)
        {
            // Sunucuda henüz mesajı olmayan ama açtığım sohbet listede kalsın.
            var yeni = s.Veri!;
            foreach (var eski in _sohbetler.Where(x => x.SonMesaj is null && yeni.All(y => y.Karsi.Id != x.Karsi.Id)))
                yeni.Insert(0, eski);
            _sohbetler = yeni.Where(x => _liste.Arkadaslar.Any(p => p.Id == x.Karsi.Id))
                .Select(x => x with { Karsi = _liste.Arkadaslar.First(p => p.Id == x.Karsi.Id) }).ToList();
            KenariGuncelle();
        }

        // Açık sohbetteki kişi artık arkadaşım değilse sohbeti kapat.
        if (_aktif is { } id && _liste.Arkadaslar.All(p => p.Id != id)) SohbetKapat();
        ListeyiGoster();
    }

    private void ProfilGeldi(Profil p)
    {
        if (p.Id == _ben.Id) { _ben = p; _kenar.Ben = p; _sohbet.Invalidate(true); return; }
        _liste = new ArkadasListesi(Degistir(_liste.Arkadaslar, p), Degistir(_liste.Gelen, p with { Durum = Durum.Cevrimdisi }), Degistir(_liste.Giden, p with { Durum = Durum.Cevrimdisi }));
        SohbetProfiliGuncelle(p);
        ListeyiGoster();
    }

    private void DurumGeldi(long id, Durum d)
    {
        if (id == _ben.Id) { _ben = _ben with { Durum = d }; _kenar.Ben = _ben; return; }
        var p = _liste.Arkadaslar.FirstOrDefault(x => x.Id == id);
        if (p is null) return;
        p = p with { Durum = d };
        _liste = _liste with { Arkadaslar = Degistir(_liste.Arkadaslar, p) };
        SohbetProfiliGuncelle(p);
        ListeyiGoster();
    }

    private void SohbetProfiliGuncelle(Profil p)
    {
        if (_sohbetler.All(x => x.Karsi.Id != p.Id)) return;
        _sohbetler = _sohbetler.Select(x => x.Karsi.Id == p.Id ? x with { Karsi = p } : x).ToList();
        KenariGuncelle();
        _sohbet.KarsiGuncelle(p);
    }

    // ---- Sohbetler ----

    private SohbetVerisi Veri(long id)
    {
        if (!_mesajlar.TryGetValue(id, out var v)) _mesajlar[id] = v = new SohbetVerisi();
        return v;
    }

    private void KenariGuncelle()
    {
        _kenar.Sohbetler = _sohbetler;
        _kenar.Secili = _aktif;
        int toplam = _sohbetler.Sum(x => x.Okunmamis);
        Text = toplam > 0 ? $"({toplam}) Ekip" : "Ekip";
    }

    private async void SohbetAc(long id)
    {
        var profil = _liste.Arkadaslar.FirstOrDefault(p => p.Id == id) ?? _sohbetler.FirstOrDefault(x => x.Karsi.Id == id)?.Karsi;
        if (profil is null) return;
        if (_sohbetler.All(x => x.Karsi.Id != id)) _sohbetler.Insert(0, new SohbetOzeti(profil, null, 0));

        _aktif = id;
        _ayarlarAcik = _yonetimAcik = false;
        KenariGuncelle();
        _sohbet.YaziyorGoster(false);
        _yaziyorZamanlayici.Stop();
        GorunumuAyarla();

        var v = Veri(id);
        _sohbet.Goster(_ben, profil, v, _basarisiz);
        _sohbet.Odaklan();
        if (!v.Yuklendi) await MesajlariYukle(id, eski: false);
        await OkunduYap(id);
    }

    private void SohbetKapat()
    {
        _aktif = null;
        _ayarlarAcik = _yonetimAcik = false;
        KenariGuncelle();
        GorunumuAyarla();
        ListeyiGoster();
    }

    private async Task YonetimiAc()
    {
        _aktif = null;
        _ayarlarAcik = false;
        _yonetimAcik = true;
        KenariGuncelle();
        GorunumuAyarla();
        await _yonetim.Ac(_ben.Id);
    }

    private void AyarlariAc(AyarlarSayfasi.Sekme sekme)
    {
        _aktif = null;
        _ayarlarAcik = true;
        _yonetimAcik = false;
        _profilDuzenleyici.Yukle(_ben);
        _ayarlarSayfasi.Kisayollar.Yukle(_overlayTusu, _miniTusu);
        _ayarlarSayfasi.Sec(sekme);
        KenariGuncelle();
        GorunumuAyarla();
    }

    // Sağ taraf: arkadaşlar sayfası, açık sohbet ya da ayarlar.
    private void GorunumuAyarla()
    {
        bool sohbet = _aktif != null;
        _sohbet.Visible = sohbet;
        _ayarlarSayfasi.Visible = _ayarlarAcik;
        _yonetim.Visible = _yonetimAcik;
        _ust.Visible = !sohbet && !_ayarlarAcik && !_yonetimAcik;
        _kenar.AyarlarAcik = _ayarlarAcik;
        _kenar.YonetimAcik = _yonetimAcik;
        if (sohbet || _ayarlarAcik || _yonetimAcik) _arkadaslar.Visible = _bos.Visible = _baslik.Visible = _ekle.Visible = false;
        PerformLayout();
    }

    private void SohbetiYenidenCiz(long id)
    {
        var profil = _sohbetler.FirstOrDefault(x => x.Karsi.Id == id)?.Karsi ?? _liste.Arkadaslar.FirstOrDefault(p => p.Id == id);
        if (profil is null) return;
        if (_aktif == id) _sohbet.Goster(_ben, profil, Veri(id), _basarisiz);
        OverlayDmYenile(id, profil); // Overlay'de de aynı sohbet açıksa onu da güncelle.
    }

    // Bu sohbeti şu an gözüyle görüyor mu? (ana pencerede açık ve odakta, ya da overlay'de açık)
    private bool Goruyor(long id) => (_aktif == id && ActiveForm == this) || OverlayDmGoruyor(id) || MiniDmGoruyor(id);

    private async Task MesajlariYukle(long id, bool eski)
    {
        var v = Veri(id);
        if (v.Yukleniyor || (eski && (v.Tumu || !v.Yuklendi))) return;
        v.Yukleniyor = true;
        long? once = eski ? v.Liste.FirstOrDefault(m => m.Id > 0)?.Id : null;
        var s = await _api.Mesajlar(id, once);
        v.Yukleniyor = false;
        if (!s.Basarili) { Bildir(s.Hata!, hata: true); return; }

        var gelen = s.Veri!;
        if (eski) v.Liste.InsertRange(0, gelen.Where(m => v.Liste.All(x => x.Id != m.Id)));
        else
        {
            // Yüklenirken canlı gelmiş ya da gönderilmekte olan mesajlar kaybolmasın.
            var ekstra = v.Liste.Where(x => gelen.All(m => m.Id != x.Id)).ToList();
            v.Liste = [.. gelen, .. ekstra];
        }
        v.Tumu = gelen.Count < Kurallar.MesajSayfasi;
        v.Yuklendi = true;
        SohbetiYenidenCiz(id);
    }

    private async Task OkunduYap(long id)
    {
        var o = _sohbetler.FirstOrDefault(x => x.Karsi.Id == id);
        if (o is null || o.Okunmamis == 0 || !Goruyor(id)) return;
        OkunmamisAyarla(id, 0);
        await _api.Okundu(id);
    }

    private void OkunmamisAyarla(long id, int sayi)
    {
        _sohbetler = _sohbetler.Select(x => x.Karsi.Id == id ? x with { Okunmamis = sayi } : x).ToList();
        KenariGuncelle();
    }

    private void MesajGeldi(Mesaj m)
    {
        bool benden = m.GonderenId == _ben.Id;
        long karsi = benden ? m.AliciId : m.GonderenId;

        var v = Veri(karsi);
        if (v.Yuklendi) MesajiYerlestir(v, m);

        var profil = _liste.Arkadaslar.FirstOrDefault(p => p.Id == karsi) ?? _sohbetler.FirstOrDefault(x => x.Karsi.Id == karsi)?.Karsi;
        if (profil is null) { _ = Yenile(); return; }

        bool goruyorum = Goruyor(karsi);
        var eski = _sohbetler.FirstOrDefault(x => x.Karsi.Id == karsi);
        int okunmamis = (eski?.Okunmamis ?? 0) + (!benden && !goruyorum ? 1 : 0);
        _sohbetler.RemoveAll(x => x.Karsi.Id == karsi);
        _sohbetler.Insert(0, new SohbetOzeti(profil, m, okunmamis));
        KenariGuncelle();

        if (!benden)
        {
            if (_aktif == karsi) { _sohbet.YaziyorGoster(false); _yaziyorZamanlayici.Stop(); }
            if (goruyorum) _ = _api.Okundu(karsi);
            else if (!OverlayAcik) Tema.Titret(this);
        }
        SohbetiYenidenCiz(karsi);
    }

    // Sunucudan gelen mesajı listeye koyar: aynısı varsa atla, bekleyen kopyası varsa onun yerine geç.
    private static void MesajiYerlestir(SohbetVerisi v, Mesaj m)
    {
        if (v.Liste.Any(x => x.Id == m.Id)) return;
        if (m.IstemciId != null)
        {
            int i = v.Liste.FindIndex(x => x.Id < 0 && x.IstemciId == m.IstemciId);
            if (i >= 0) { v.Liste[i] = m; return; }
        }
        // Gönderilmeyi bekleyenler hep en altta kalsın.
        int ilkBekleyen = v.Liste.FindIndex(x => x.Id < 0);
        if (ilkBekleyen < 0) v.Liste.Add(m); else v.Liste.Insert(ilkBekleyen, m);
    }

    private async void MesajGonder(long id, string metin)
    {
        var gecici = new Mesaj(-(++_geciciNo), _ben.Id, id, metin, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("N"));
        var v = Veri(id);
        if (!v.Yuklendi) await MesajlariYukle(id, eski: false);
        v.Liste.Add(gecici);
        SohbetiYenidenCiz(id);
        if (_aktif == id) _sohbet.Alta(); // Kendi mesajını hep görsün, yukarıdayken bile.
        await Gonderimi(gecici);
    }

    private async Task Gonderimi(Mesaj gecici)
    {
        _basarisiz.Remove(gecici.IstemciId!);
        SohbetiYenidenCiz(gecici.AliciId);
        var s = await _api.MesajGonder(gecici.AliciId, gecici.Metin, gecici.IstemciId!);
        var v = Veri(gecici.AliciId);
        if (!s.Basarili)
        {
            _basarisiz.Add(gecici.IstemciId!);
            Bildir(s.Hata!, hata: true);
            SohbetiYenidenCiz(gecici.AliciId);
            return;
        }
        // Canlı bağlantıdan önce gelmiş olabilir; değilse burada yerleştir.
        if (v.Liste.Any(x => x.Id == s.Veri!.Id)) v.Liste.RemoveAll(x => x.Id < 0 && x.IstemciId == gecici.IstemciId);
        else MesajiYerlestir(v, s.Veri! with { IstemciId = gecici.IstemciId });
        if (!_canli.Bagli) MesajGeldi(s.Veri!); // Canlı bağlantı yoksa kenar çubuğunu kendimiz güncelleyelim.
        SohbetiYenidenCiz(gecici.AliciId);
    }

    private static List<Profil> Degistir(List<Profil> liste, Profil yeni) =>
        liste.Select(x => x.Id == yeni.Id ? yeni : x).ToList();

    // ---- Sayfalar ----

    private void SayfaAc(Sayfa s)
    {
        bool degisti = s != _sayfa;
        _sayfa = s;
        _ust.Secili = s;
        if (degisti) _arkadaslar.BasaDon();
        ListeyiGoster();
        if (s == Sayfa.Ekle) _ekle.Odaklan();
    }

    private void ListeyiGoster()
    {
        _ust.Bekleyen = _liste.Gelen.Count;
        _kenar.Bekleyen = _liste.Gelen.Count;

        OverlayArkadaslariYenile();
        if (_aktif != null || _ayarlarAcik || _yonetimAcik) return; // Başka sayfa açıkken arkadaş sayfası gizli.
        bool ekle = _sayfa == Sayfa.Ekle;
        _ekle.Visible = ekle;
        if (ekle) { _arkadaslar.Visible = _bos.Visible = _baslik.Visible = false; return; }

        static int Sira(Profil p) => p.Durum == Durum.Cevrimdisi ? 1 : 0;
        List<Satir> satirlar = _sayfa switch
        {
            Sayfa.Cevrimici => _liste.Arkadaslar.Where(p => p.Durum != Durum.Cevrimdisi).Select(p => new Satir(p, SatirTuru.Arkadas)).ToList(),
            Sayfa.Tumu => _liste.Arkadaslar.OrderBy(Sira).Select(p => new Satir(p, SatirTuru.Arkadas)).ToList(),
            _ => [.. _liste.Gelen.Select(p => new Satir(p, SatirTuru.Gelen)), .. _liste.Giden.Select(p => new Satir(p, SatirTuru.Giden))],
        };

        _baslik.Text = _sayfa switch
        {
            Sayfa.Cevrimici => "ÇEVRİMİÇİ",
            Sayfa.Tumu => "TÜM ARKADAŞLAR",
            _ => "BEKLEYEN İSTEKLER",
        } + " — " + satirlar.Count;

        bool hicArkadasYok = _liste.Arkadaslar.Count == 0;
        (_bos.Glif, _bos.Baslik, _bos.Aciklama) = _sayfa switch
        {
            Sayfa.Bekleyen => (Glif.Saat, "Bekleyen istek yok", "Gelen ve gönderdiğin arkadaşlık istekleri burada görünür."),
            _ when hicArkadasYok => (Glif.ArkadasEkle, "Henüz arkadaşın yok", "\"Arkadaş ekle\" sekmesinden kullanıcı adıyla arkadaşlarını ekleyebilirsin."),
            _ => (Glif.Insanlar, "Şu an kimse çevrimiçi değil", "Arkadaşların bağlandığında burada görünecek."),
        };

        bool bos = satirlar.Count == 0;
        _baslik.Visible = !bos;
        _arkadaslar.Visible = !bos;
        _bos.Visible = bos;
        _bos.Invalidate();
        _arkadaslar.Ayarla(satirlar);
    }

    // ---- Eylemler ----

    private async void SatirEylemi(Satir s, string eylem, Rectangle ekranAlani)
    {
        switch (eylem)
        {
            case "kabul":
                await Sonucla(_api.IstekKabul(s.Profil.Id), $"{s.Profil.GorunenAd} ile artık arkadaşsınız!");
                break;
            case "reddet":
            case "iptal":
                await Sonucla(_api.IliskiSil(s.Profil.Id), null);
                break;
            case "mesaj":
                SohbetAc(s.Profil.Id);
                break;
            case "menu":
                var m = new KoyuMenu();
                m.Ekle("Mesaj gönder", () => SohbetAc(s.Profil.Id));
                if (s.Profil.Durum != Durum.Cevrimdisi && _parti?.Uyeler.Any(u => u.Id == s.Profil.Id) != true)
                    m.Ekle("Partiye davet et", () => PartiyeDavetEt(s.Profil));
                m.Ekle("Profili görüntüle", () => ProfilGoster(s.Profil, ekranAlani));
                m.Ayirici();
                m.Ekle("Arkadaşlıktan çıkar", () => ArkadasCikar(s.Profil), Tema.Hata);
                m.Closed += (_, _) => BeginInvoke(m.Dispose);
                m.Show(ekranAlani.Left, ekranAlani.Bottom + Tema.S(4));
                break;
            case "profil":
                ProfilGoster(s.Profil, ekranAlani);
                break;
        }
    }

    private async void ArkadasCikar(Profil p)
    {
        bool evet = await OnayKarti.Sor(this, $"{p.GorunenAd} arkadaşlıktan çıkarılsın mı?",
            $"{p.GorunenAd} arkadaş listenden çıkacak. İstersen daha sonra tekrar ekleyebilirsin.", "Arkadaşlıktan çıkar");
        if (!evet) return;
        await Sonucla(_api.IliskiSil(p.Id), null);
    }

    private async Task Sonucla(Task<Sonuc<object>> islem, string? basariMesaji)
    {
        var s = await islem;
        if (!s.Basarili) Bildir(s.Hata!, hata: true);
        else if (basariMesaji != null) Bildir(basariMesaji, hata: false);
        await Yenile();
    }

    private async void IstekGonder(string ad)
    {
        _ekle.Mesgul(true);
        var s = await _api.IstekGonder(ad);
        _ekle.Mesgul(false);
        if (!s.Basarili) { _ekle.Sonuc(s.Hata!, hata: true); return; }
        _ekle.Sonuc(s.Veri!.Mesaj, hata: false);
        _ekle.Temizle();
        await Yenile();
    }

    // Arkadaşın profil kartı: ayrı pencere değil, uygulamanın içinde ortada açılır.
    private void ProfilGoster(Profil p, Rectangle yakinindaki)
    {
        int w = Tema.S(340);
        var kart = new ProfilKarti { Profil = p, Resim = _avatarlar.Al(p), Cerceve = true, BackColor = Tema.Arkaplan };
        kart.Size = new Size(w, kart.GerekenYukseklik(w));
        IcModal.Goster(this, kart);
    }

    private void DurumMenusu(Rectangle ekranAlani)
    {
        var m = new KoyuMenu();
        foreach (var d in new[] { Durum.Cevrimici, Durum.Bosta, Durum.RahatsizEtmeyin, Durum.Gorunmez })
        {
            var oge = m.Ekle(Cizim.DurumAdi(d), async () =>
            {
                _ben = _ben with { Durum = d };
                _kenar.Ben = _ben;
                if (!_canli.Bagli) { Bildir("Bağlantı yok, durumun bağlanınca güncellenecek.", hata: true); return; }
                await _canli.DurumAyarla(d);
            }, resim: KoyuMenu.Nokta(Cizim.DurumRengi(d)));
            if (d == _ben.Durum) oge.Font = Tema.Yazi(10, kalin: true);
        }
        m.Ayirici();
        m.Ekle("Profili düzenle", () => AyarlariAc(AyarlarSayfasi.Sekme.Profil));
        m.Ekle("Kısayollar", () => AyarlariAc(AyarlarSayfasi.Sekme.Kisayollar));
        m.Ekle("Çıkış yap", () => CikisYapildi?.Invoke(), Tema.Hata);
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(new Point(ekranAlani.Left, ekranAlani.Top), ToolStripDropDownDirection.AboveRight);
    }

    // ---- Bildirimler ----
    // Kısa bildirimler altta yüzen bir hapta çıkar: içeriği itmez, düğmeler yerinden oynamaz.
    // Üstteki bant sadece "bağlantı koptu" için (o durumda sayfanın üstünde kalıcı uyarı mantıklı).

    private readonly BildirimHapi _hap = new() { Visible = false };

    private void Bildir(string mesaj, bool hata)
    {
        if (!Controls.Contains(_hap)) Controls.Add(_hap);
        _hap.Ayarla(mesaj, hata);
        _hap.Visible = true;
        _hap.BringToFront();
        HapiYerlestir();
        _bantZamanlayici.Stop();
        _bantZamanlayici.Start();
    }

    private void HapiYerlestir()
    {
        if (!_hap.Visible) return;
        int kenarW = Tema.S(248), sagW = ClientSize.Width - kenarW;
        var boyut = _hap.GerekenBoyut(sagW - Tema.S(60));
        _hap.Bounds = new Rectangle(kenarW + (sagW - boyut.Width) / 2, ClientSize.Height - boyut.Height - Tema.S(96), boyut.Width, boyut.Height);
    }

    private void BantGuncelle(bool yeni = false)
    {
        if (!yeni && !_bantZamanlayici.Enabled) _hap.Visible = false;
        bool onceki = _bant.Visible;
        if (!_bagli)
        {
            _bant.Text = "Sunucuyla bağlantı koptu, yeniden bağlanılıyor…";
            _bant.BackColor = ColorTranslator.FromHtml("#5A3B0A");
            _bant.ForeColor = ColorTranslator.FromHtml("#FFD58A");
            _bant.Visible = true;
        }
        else _bant.Visible = false;
        if (onceki != _bant.Visible) PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_kenar is null) return;
        int w = ClientSize.Width, h = ClientSize.Height;
        int kenarW = Tema.S(248);
        _kenar.Bounds = new Rectangle(0, 0, kenarW, h);

        int x = kenarW, sagW = w - kenarW;
        _ust.Bounds = new Rectangle(x, 0, sagW, Tema.S(56));
        int y = _aktif != null || _ayarlarAcik || _yonetimAcik ? 0 : _ust.Bottom;
        if (_bant.Visible) { _bant.Bounds = new Rectangle(x, y, sagW, Tema.S(32)); y = _bant.Bottom; }

        int ic = Tema.S(20);
        var icerik = new Rectangle(x + ic, y + Tema.S(14), sagW - ic * 2 + Tema.S(6), h - y - Tema.S(14));
        _sohbet.Bounds = new Rectangle(x, y, sagW, h - y);
        _ayarlarSayfasi?.Bounds = new Rectangle(x, y, sagW, h - y); // Yapıcı bitmeden de yerleşim çağrılabiliyor.
        _yonetim?.Bounds = new Rectangle(x, y, sagW, h - y);
        if (_hap is not null) HapiYerlestir();
        _baslik.Location = new Point(icerik.X + Tema.S(6), icerik.Y + Tema.S(4));
        int listeY = _baslik.Bottom + Tema.S(10);
        _arkadaslar.Bounds = new Rectangle(icerik.X, listeY, icerik.Width, icerik.Bottom - listeY);
        _bos.Bounds = icerik;
        _ekle.Bounds = new Rectangle(x, y, sagW, h - y);
    }
}

// ---------------------------------------------------------------------------------------------

// Sağ üstteki başlık ve sekmeler.
public class UstCubuk : Control
{
    private Sayfa _secili;
    private int _bekleyen;
    private Sayfa? _uzerinde;
    private readonly List<(Sayfa sayfa, Rectangle alan)> _sekmeler = [];

    public event Action<Sayfa>? Secildi;
    public Sayfa Secili { get => _secili; set { _secili = value; Invalidate(); } }
    public int Bekleyen { get => _bekleyen; set { if (_bekleyen != value) { _bekleyen = value; Invalidate(); } } }

    public UstCubuk()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
    }

    private static string Ad(Sayfa s) => s switch
    {
        Sayfa.Cevrimici => "Çevrimiçi",
        Sayfa.Tumu => "Tümü",
        Sayfa.Bekleyen => "Bekleyen",
        _ => "Arkadaş ekle",
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int orta = Height / 2;

        int x = Tema.S(22);
        TextRenderer.DrawText(g, Glif.Insanlar, Cizim.Ikon(13), new Rectangle(x, 0, Tema.S(24), Height), Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += Tema.S(32);
        var baslikF = Tema.Yazi(11.5f, kalin: true);
        var bw = TextRenderer.MeasureText(g, "Arkadaşlar", baslikF, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, "Arkadaşlar", baslikF, new Rectangle(x, 0, bw + 4, Height), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += bw + Tema.S(18);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, orta - Tema.S(12), x, orta + Tema.S(12));
        x += Tema.S(16);

        _sekmeler.Clear();
        var f = Tema.Yazi(10, kalin: true);
        foreach (var s in Enum.GetValues<Sayfa>())
        {
            var ad = Ad(s);
            int yw = TextRenderer.MeasureText(g, ad, f, Size.Empty, TextFormatFlags.NoPadding).Width;
            int rozetW = s == Sayfa.Bekleyen && _bekleyen > 0 ? Tema.S(26) : 0;
            var r = new Rectangle(x, orta - Tema.S(16), yw + Tema.S(24) + rozetW, Tema.S(32));
            _sekmeler.Add((s, r));

            bool secili = s == _secili, uzerinde = s == _uzerinde;
            Color yazi;
            if (s == Sayfa.Ekle)
            {
                if (secili) yazi = Tema.Vurgu;
                else { Cizim.YuvarlakDoldur(g, r, Tema.S(6), uzerinde ? Tema.VurguHover : Tema.Vurgu); yazi = Color.White; }
            }
            else
            {
                if (secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
                else if (uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.Yuzey);
                yazi = secili || uzerinde ? Tema.Metin : Tema.MetinIkincil;
            }
            TextRenderer.DrawText(g, ad, f, new Rectangle(r.X + Tema.S(12), r.Y, yw + 2, r.Height), yazi, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (rozetW > 0) Cizim.Rozet(g, new Point(r.Right - Tema.S(10), orta), _bekleyen);
            x = r.Right + Tema.S(8);
        }

        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
    }

    private Sayfa? Bul(Point p) => _sekmeler.Where(s => s.alan.Contains(p)).Select(s => (Sayfa?)s.sayfa).FirstOrDefault();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var s = Bul(e.Location);
        if (s == _uzerinde) return;
        _uzerinde = s;
        Cursor = s != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = null; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && Bul(e.Location) is { } s) Secildi?.Invoke(s);
    }
}

// "Arkadaş ekle" sayfası.
public class EklePaneli : Panel
{
    private readonly Label _baslik = new() { AutoSize = true, Text = "Arkadaş ekle", Font = Tema.Yazi(14, kalin: true), ForeColor = Tema.Metin };
    private readonly Label _aciklama = new() { AutoSize = true, Text = "Arkadaşını kullanıcı adıyla ekleyebilirsin. Büyük-küçük harf fark etmez.", Font = Tema.Yazi(10), ForeColor = Tema.MetinIkincil };
    private readonly GirisKutusu _ad = new() { Etiket = "Kullanıcı adı", IpucuYazisi = "ör. ahmet_42", MaksUzunluk = Kurallar.KullaniciAdiMax + 1 };
    private readonly Dugme _gonder = new() { Text = "İstek gönder" };
    private readonly Label _sonuc = new() { AutoSize = false, Font = Tema.Yazi(10, kalin: true) };

    public event Action<string>? Gonder;

    public EklePaneli()
    {
        BackColor = Tema.Arkaplan;
        Controls.AddRange([_baslik, _aciklama, _ad, _gonder, _sonuc]);
        _gonder.Click += (_, _) => GonderTik();
        _ad.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GonderTik(); } };
        _ad.TextChanged += (_, _) => { _sonuc.Text = ""; _gonder.Enabled = _ad.Text.Trim().Length > 0; };
        _gonder.Enabled = false;
    }

    private void GonderTik()
    {
        var ad = _ad.Text.Trim();
        if (ad.Length > 0 && _gonder.Enabled) Gonder?.Invoke(ad);
    }

    public void Odaklan() => _ad.Focus();
    public void Temizle() { var s = _sonuc.Text; var c = _sonuc.ForeColor; _ad.Text = ""; _sonuc.Text = s; _sonuc.ForeColor = c; }

    public void Mesgul(bool m)
    {
        _ad.Enabled = !m;
        _gonder.Enabled = !m && _ad.Text.Trim().Length > 0;
        _gonder.Text = m ? "Gönderiliyor…" : "İstek gönder";
    }

    public void Sonuc(string mesaj, bool hata)
    {
        _sonuc.Text = mesaj;
        _sonuc.ForeColor = hata ? Tema.Hata : Tema.Basari;
        if (hata) _ad.Focus();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = Tema.S(32), y = Tema.S(28);
        int w = Math.Min(Tema.S(620), Width - x * 2);
        _baslik.Location = new Point(x - Tema.S(4), y); y = _baslik.Bottom + Tema.S(4);
        _aciklama.Location = new Point(x - Tema.S(3), y); y = _aciklama.Bottom + Tema.S(20);
        int dw = Tema.S(140);
        _ad.Bounds = new Rectangle(x, y, w - dw - Tema.S(10), _ad.Height);
        _gonder.Bounds = new Rectangle(_ad.Right + Tema.S(10), _ad.Bottom - Tema.S(44), dw, Tema.S(44));
        _sonuc.Bounds = new Rectangle(x, _ad.Bottom + Tema.S(10), w, Tema.S(24));

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(ColorTranslator.FromHtml("#0E0F12"));
        int y = _sonuc.Bottom + Tema.S(20);
        e.Graphics.DrawLine(p, Tema.S(32), y, Width - Tema.S(32), y);
    }
}

