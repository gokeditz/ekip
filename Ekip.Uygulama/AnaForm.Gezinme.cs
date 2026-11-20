using Ekip.Ortak;

namespace Ekip.Uygulama;

// Gezinme: sunucu rayı, sunucu görünümü (kanal listesi + üye listesi), Keşfet, sunucu ayarları ve gruplar.
// Bütün bu mantık burada toplandı; AnaForm.cs'e sadece küçük bağlantı noktaları eklendi (GezinmeKur, GezinmeYukle,
// GezinmeGorunumu, GezinmeYerlestir, GezinmeIcerikAcik, GezinmeTemizle).
public partial class AnaForm
{
    private enum Gorunum { Ana, Sunucu, Kesfet }

    private SunucuRayi _rayi = null!;
    private KanalListesi _kanalListesi = null!;
    private UyeListesi _uyeListesi = null!;
    private KonusmaBasligi _konusmaBasligi = null!;
    private BosDurum _konusmaYeri = null!;
    private SohbetPaneli _konusmaPaneli = null!;
    private KesfetSayfasi _kesfet = null!;
    private SunucuAyarlari _sunucuAyarlari = null!;
    private IkonOnbellek _ikonlar = null!;
    private System.Windows.Forms.Timer _rayYenileme = null!;
    private bool _gezinmeHazir;

    private Gorunum _gorunum = Gorunum.Ana;
    private List<SunucuOzeti> _sunucular = [];
    private List<Grup> _gruplar = [];
    private long? _sunucuId;                       // son açılan sunucu (Ana sayfaya dönünce de kalır)
    private Sunucu? _sunucu;
    private readonly Dictionary<long, Sunucu> _sunucuOnbellek = [];
    private readonly Dictionary<long, long> _sonKanal = [];     // sunucu -> en son seçilen kanal
    private readonly Dictionary<long, long> _kanalSunucu = [];  // kanal -> sunucu (gelen mesajın hangi sunucuya ait olduğunu bilmek için)
    private KonusmaKimligi? _acikKonusma;          // gösterilen kanal ya da grup (DM'ler eski yoldan, _aktif ile)
    private bool _sunucuAyarAcik, _uyelerAcik = true, _kanalGorunuyor;
    private int _sunucuIstekNo, _grupIstekNo;

    // DM'ler başka geliştiricinin alanında (_aktif); birleştirirken sadece bu satır uyarlanır.
    private bool DmAcik => _aktif != null;

    // Sunucu / Keşfet / grup konuşması açıkken arkadaşlar sayfası (üst sekmeler, liste, boş durum) gizli kalır.
    private bool GezinmeIcerikAcik => _gezinmeHazir && (_gorunum != Gorunum.Ana || _acikKonusma is { Tur: KonusmaTuru.Grup });

    // ---- Kurulum ----

    private void GezinmeKur()
    {
        _ikonlar = new IkonOnbellek(_api);
        _rayi = new SunucuRayi { Ikonlar = _ikonlar };
        _kanalListesi = new KanalListesi { Avatarlar = _avatarlar, Ben = _ben, Visible = false };
        _uyeListesi = new UyeListesi { Avatarlar = _avatarlar, Visible = false };
        _konusmaBasligi = new KonusmaBasligi { Visible = false };
        _konusmaYeri = new BosDurum { Visible = false, Glif = Glif.Mesaj, Baslik = "Sohbet yükleniyor…", Aciklama = "" };
        _kesfet = new KesfetSayfasi(_api, _avatarlar, _ikonlar) { Visible = false };
        _sunucuAyarlari = new SunucuAyarlari(_api, _avatarlar, _ikonlar) { Visible = false };
        // Kanal ve grup sohbeti: DM'lerle aynı depo; başlığı _konusmaBasligi çizer.
        _konusmaPaneli = new SohbetPaneli { BaslikGizli = true, Api = _api, Avatarlar = _avatarlar, Depo = _depo, Ben = _ben, Visible = false };
        _konusmaPaneli.Bildirim += (m, h) => Bildir(m, h);
        _konusmaPaneli.BahsetmeTik += ad =>
        {
            if (_acikKonusma is { } k && KonusmaUyeleri(k).FirstOrDefault(p => p.KullaniciAdi.Equals(ad, StringComparison.OrdinalIgnoreCase)) is { } p)
                ProfilGoster(p, Rectangle.Empty);
        };
        _rayYenileme = new System.Windows.Forms.Timer { Interval = 1500 };
        Controls.AddRange([_rayi, _kanalListesi, _uyeListesi, _konusmaBasligi, _konusmaYeri, _konusmaPaneli, _kesfet, _sunucuAyarlari]);
        _kenar.Ikonlar = _ikonlar;
        _ekle.Ben = _ben;
        _gezinmeHazir = true;

        // Sunucu rayı
        _rayi.AnaSayfaTik += GoruntuAna;
        _rayi.KesfetTik += KesfetAc;
        _rayi.SunucuSecildi += id => _ = SunucuAcAsync(id);
        _rayi.EkleTik += SunucuEkle;
        _rayi.SunucuMenuTik += RayMenusu;
        _ikonlar.Yuklendi += () => { _kenar.Invalidate(); _konusmaBasligi.Invalidate(); };

        // Kanal listesi (sunucu görünümünde kenar çubuğunun yerinde)
        _kanalListesi.MenuTik += r => { if (_sunucu is { } s) SunucuMenusu(s, r, baslik: true); };
        _kanalListesi.KanalSecildi += KanalSec;
        _kanalListesi.KanalEkleTik += () => { if (_sunucu is { } s) KanalOlustur(s.Id); };
        _kanalListesi.KanalDuzenleTik += k => KanalKarti.Goster(this, _api, k.SunucuId, k, yeni => { _ = SunucuTazele(yeni.SunucuId); });
        _kanalListesi.KanalSilTik += KanalSil;
        _kanalListesi.KullaniciTik += DurumMenusu;
        _kanalListesi.AyarTik += () => AyarlariAc(AyarlarSayfasi.Sekme.Profil);
        _uyeListesi.UyeTik += (p, r) => ProfilGoster(p, r);
        _konusmaBasligi.UyelerTik += () => { _uyelerAcik = !_uyelerAcik; PerformLayout(); };
        _konusmaBasligi.AyarTik += GrupAyarlariAc;

        // Sunucu ayarları sayfası
        _sunucuAyarlari.Kapat += () => { _sunucuAyarAcik = false; GorunumuAyarla(); };
        _sunucuAyarlari.Bildir += (m, h) => Bildir(m, h);
        _sunucuAyarlari.Silindi += () => { if (_sunucu is { } s) SunucuKapandi(s.Id, "Sunucu silindi."); };
        _sunucuAyarlari.Ayrildi += () => { if (_sunucu is { } s) SunucuKapandi(s.Id, "Sunucudan ayrıldın."); };
        _sunucuAyarlari.Guncellendi += s => SunucuUygula(s, ayarlariTazele: false);

        // Keşfet
        _kesfet.ProfilTik += p => ProfilGoster(p, Rectangle.Empty);
        _kesfet.MesajTik += id => { GoruntuAna(); SohbetAc(id); };
        _kesfet.SunucuAcTik += id => _ = SunucuKatildiAc(id);
        _kesfet.SunucuKatildi += s => { SunucuKaydet(s); _ = SunucularYenile(); };
        _kesfet.ArkadaslikDegisti += () => _ = Yenile();
        _kesfet.Bildir += (m, h) => Bildir(m, h);

        // Gruplar (ana sayfadaki DM listesinde)
        _kenar.GrupTik += GrupAc;
        _kenar.GrupEkleTik += GrupOlustur;
        _kenar.ArkadaslarTik += () => { if (_acikKonusma is { Tur: KonusmaTuru.Grup }) { _acikKonusma = null; GorunumuAyarla(); ListeyiGoster(); } };
        _kenar.SohbetlerDegisti += AnaRozetiGuncelle;
        _kenar.BenDegisti += () => { _kanalListesi.Ben = _kenar.Ben; if (_kenar.Ben is { } b) _konusmaPaneli.Ben = b; };

        // Canlı olaylar
        _canli.SunucularDegisti += () => _ = SunucularYenile();
        _canli.SunucuGuncellendi += SunucuGuncellendiGeldi;
        _canli.GruplarDegisti += () => _ = GruplariYenile();
        _canli.MesajGeldi += GezinmeMesajGeldi;
        _canli.KonusmaOkundu += YerelOkundu;
        _canli.DurumDegisti += (id, d) => UyeProfiliniDegistir(id, p => p with { Durum = d });
        _canli.ProfilDegisti += p => UyeProfiliniDegistir(p.Id, eski => p with { Durum = eski.Durum });
        _canli.BaglantiDegisti += b => { if (b && IsHandleCreated) { _ = SunucularYenile(); _ = GruplariYenile(); } };
        _rayYenileme.Tick += (_, _) => { _rayYenileme.Stop(); _ = SunucularYenile(); };
        Activated += (_, _) => GorunenKonusmayiOku();
    }

    private async Task GezinmeYukle()
    {
        await Task.WhenAll(SunucularYenile(), GruplariYenile());
    }

    private void GezinmeTemizle() => _rayYenileme?.Dispose();

    // ---- Görünürlük ve yerleşim (GorunumuAyarla'nın sonunda çağrılır) ----

    private void GezinmeGorunumu()
    {
        if (!_gezinmeHazir) return;
        if (DmAcik)
        {
            // Bir DM açıldı (kenar çubuğundan, bildirimden, Keşfet'ten...): ana sayfaya geç.
            _acikKonusma = null;
            if (_gorunum != Gorunum.Ana) { _gorunum = Gorunum.Ana; _sunucuAyarAcik = false; RayiSenkronla(); }
        }
        bool ana = _gorunum == Gorunum.Ana, sunucu = _gorunum == Gorunum.Sunucu, kesfet = _gorunum == Gorunum.Kesfet;
        bool ustte = _ayarlarAcik || _yonetimAcik;   // kullanıcı ayarları / yönetim sayfası her şeyin üstünde
        bool grup = ana && !DmAcik && !ustte && _acikKonusma is { Tur: KonusmaTuru.Grup };
        bool kanal = sunucu && !ustte && !_sunucuAyarAcik && _acikKonusma is { Tur: KonusmaTuru.Kanal };
        _kanalGorunuyor = kanal;

        _kenar.Visible = !sunucu;
        _kanalListesi.Visible = sunucu;
        _kanalListesi.AyarlarAcik = _ayarlarAcik;
        _kesfet.Visible = kesfet && !ustte;
        _sunucuAyarlari.Visible = sunucu && !ustte && _sunucuAyarAcik;
        _konusmaBasligi.Visible = grup || kanal;
        _konusmaYeri.Visible = false;
        _konusmaPaneli.Visible = grup || kanal;
        if (!kanal) _uyeListesi.Visible = false;

        if (!ana || grup) { _ust.Visible = false; _arkadaslar.Visible = _bos.Visible = _baslik.Visible = _ekle.Visible = false; }
        _kenar.SecimYok = kesfet || grup;
        _kenar.SeciliGrup = grup ? _acikKonusma!.Value.Id : null;
        _konusmaBasligi.UyelerAcik = _uyelerAcik;
        PerformLayout();
    }

    private void GezinmeYerlestir(int rayW, int kenarW, int x, int y, int sagW, int h)
    {
        if (!_gezinmeHazir) return;
        _rayi.Bounds = new Rectangle(0, 0, rayW, h);
        _kanalListesi.Bounds = new Rectangle(rayW, 0, kenarW, h);
        int ic = Math.Max(0, h - y);
        _kesfet.Bounds = _sunucuAyarlari.Bounds = new Rectangle(x, y, sagW, ic);

        // Üye listesi sığmazsa kendiliğinden gizlenir (düğme de kalkar); sohbet için en az ~420px kalmalı.
        bool sigar = sagW >= UyeListesi.GerekenGenislik + Tema.S(420);
        bool uyeler = _kanalGorunuyor && _uyelerAcik && sigar;
        int uw = uyeler ? UyeListesi.GerekenGenislik : 0, sohbetW = sagW - uw, bh = KonusmaBasligi.GerekenYukseklik;
        _konusmaBasligi.UyeDugmesi = sigar;
        _konusmaBasligi.Bounds = new Rectangle(x, y, sohbetW, bh);
        _konusmaYeri.Bounds = _konusmaPaneli.Bounds = new Rectangle(x, y + bh, sohbetW, Math.Max(0, ic - bh));
        _uyeListesi.Bounds = new Rectangle(x + sohbetW, y, uw, ic);
        if (_uyeListesi.Visible != uyeler) _uyeListesi.Visible = uyeler;
    }

    // Rayda hangi öğenin seçili göründüğünü gerçek duruma getirir.
    private void RayiSenkronla()
    {
        switch (_gorunum)
        {
            case Gorunum.Ana: _rayi.Secili = null; _rayi.KesfetSecili = false; break;
            case Gorunum.Sunucu: _rayi.Secili = _sunucuId; break;
            case Gorunum.Kesfet: _rayi.KesfetSecili = true; break;
        }
    }

    // ---- Ana sayfa / Keşfet ----

    private void GoruntuAna()
    {
        _sunucuIstekNo++;
        _gorunum = Gorunum.Ana;
        _sunucuAyarAcik = false;
        _acikKonusma = null;
        _ayarlarAcik = _yonetimAcik = false;
        RayiSenkronla();
        SohbetKapat();
    }

    private void KesfetAc()
    {
        _sunucuIstekNo++;
        _gorunum = Gorunum.Kesfet;
        _sunucuAyarAcik = false;
        _acikKonusma = null;
        _ayarlarAcik = _yonetimAcik = false;
        RayiSenkronla();
        SohbetKapat();
        _ = _kesfet.Ac();
    }

    private void AnaRozetiGuncelle()
    {
        if (!_gezinmeHazir) return;
        _rayi.AnaSayfaOkunmamis = _kenar.Sohbetler.Sum(x => x.Okunmamis) + _gruplar.Sum(x => x.Okunmamis);
    }

    // ---- Sunucular ----

    private async Task SunucularYenile()
    {
        var s = await _api.Sunucular();
        if (IsDisposed || !s.Basarili) return;
        _sunucular = s.Veri!;
        _rayi.Sunucular = _sunucular;
        foreach (var id in _sunucuOnbellek.Keys.Where(id => _sunucular.All(x => x.Id != id)).ToList()) _sunucuOnbellek.Remove(id);

        if (_sunucuId is { } acik && _sunucular.All(x => x.Id != acik))
        {
            // Bu sunucudan atıldım ya da sunucu silindi.
            _sunucuId = null; _sunucu = null;
            if (_gorunum == Gorunum.Sunucu) { GoruntuAna(); Bildir("Artık bu sunucunun üyesi değilsin.", hata: true); }
        }
        RayiSenkronla();
    }

    // Bilinmeyen bir sunucudan mesaj gelince hesaplamayı sunucuya bırakırız; ardışık mesajlarda tek istek.
    private void RayTazeleGec()
    {
        if (!_rayYenileme.Enabled) _rayYenileme.Start();
    }

    private void SunucuKaydet(Sunucu s)
    {
        _sunucuOnbellek[s.Id] = s;
        foreach (var k in s.Kanallar) _kanalSunucu[k.Id] = s.Id;
        int o = s.Kanallar.Sum(k => k.Okunmamis), b = s.Kanallar.Sum(k => k.Bahsedilme);
        if (_sunucular.All(x => x.Id != s.Id)) return;
        var yeni = _sunucular.Select(x => x.Id == s.Id ? x with { Ad = s.Ad, Renk = s.Renk, IkonSurum = s.IkonSurum, Okunmamis = o, Bahsedilme = b } : x).ToList();
        if (yeni.SequenceEqual(_sunucular)) return;
        _sunucular = yeni;
        _rayi.Sunucular = yeni;
        RayiSenkronla();
    }

    private async Task<Sunucu?> SunucuDetayi(long id)
    {
        if (_sunucuOnbellek.TryGetValue(id, out var s)) return s;
        var r = await _api.SunucuGetir(id);
        if (IsDisposed) return null;
        if (!r.Basarili) { Bildir(r.Hata!, hata: true); return null; }
        SunucuKaydet(r.Veri!);
        return r.Veri;
    }

    // Sunucuyu açar (önbellekten anında, sonra arka planda tazeler) ve son kanalını ya da ilk kanalını gösterir.
    private async Task SunucuAcAsync(long id, long? kanal = null)
    {
        int no = ++_sunucuIstekNo;
        bool onbellekten = _sunucuOnbellek.ContainsKey(id);
        var s = await SunucuDetayi(id);
        if (no != _sunucuIstekNo || IsDisposed) return;
        if (s is null) { RayiSenkronla(); _ = SunucularYenile(); return; }

        _gorunum = Gorunum.Sunucu;
        _sunucuId = id;
        _sunucuAyarAcik = false;
        _ayarlarAcik = _yonetimAcik = false;
        _acikKonusma = null;
        RayiSenkronla();
        SohbetKapat();
        SunucuUygula(s, ayarlariTazele: false);

        long? hedef = kanal;
        if (hedef is null && _sonKanal.TryGetValue(id, out var son) && s.Kanallar.Any(k => k.Id == son)) hedef = son;
        hedef ??= s.Kanallar.OrderBy(k => k.Sira).ThenBy(k => k.Id).FirstOrDefault()?.Id;
        if (hedef is { } h) KanalSec(h);
        else { _acikKonusma = null; GorunumuAyarla(); }
        if (onbellekten) _ = SunucuTazele(id);
    }

    private async Task SunucuKatildiAc(long id) { await SunucularYenile(); await SunucuAcAsync(id); }

    // Sunucuyu yeniden çeker ve (açıksa) ekrana uygular. Sunucu artık yoksa ana sayfaya döner.
    private async Task SunucuTazele(long id)
    {
        var r = await _api.SunucuGetir(id);
        if (IsDisposed) return;
        if (r.Basarili) { SunucuUygula(r.Veri!, ayarlariTazele: true); return; }
        if (r.Durum is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            _sunucuOnbellek.Remove(id);
            if (_sunucuId == id) { _sunucuId = null; _sunucu = null; if (_gorunum == Gorunum.Sunucu) GoruntuAna(); }
            _ = SunucularYenile();
        }
        else if (_sunucuId == id && _gorunum == Gorunum.Sunucu) Bildir(r.Hata!, hata: true);
    }

    private void SunucuUygula(Sunucu s, bool ayarlariTazele = true)
    {
        bool acik = _sunucuId == s.Id;
        if (acik && _acikKonusma is { Tur: KonusmaTuru.Kanal } vk && KonusmaGorunuyor(vk))
            s = s with { Kanallar = s.Kanallar.Select(k => k.Id == vk.Id ? k with { Okunmamis = 0, Bahsedilme = 0 } : k).ToList() };
        SunucuKaydet(s);
        if (!acik) return;

        _sunucu = s;
        _kanalListesi.Sunucu = s;
        _uyeListesi.Uyeler = s.Uyeler;
        if (ayarlariTazele && _sunucuAyarAcik && _sunucuAyarlari.Sunucu?.Id == s.Id) _sunucuAyarlari.Yenile(s);

        if (_acikKonusma is { Tur: KonusmaTuru.Kanal } k)
        {
            if (s.Kanallar.All(x => x.Id != k.Id))
            {
                // Açık kanal silinmiş: ilk kanala geç.
                var ilk = s.Kanallar.OrderBy(x => x.Sira).ThenBy(x => x.Id).FirstOrDefault();
                if (ilk != null) KanalSec(ilk.Id);
                else { _acikKonusma = null; GorunumuAyarla(); }
            }
            else BasligiGuncelle();
        }
    }

    private void SunucuGuncellendiGeldi(long id)
    {
        if (id == _sunucuId) { _ = SunucuTazele(id); return; }
        _sunucuOnbellek.Remove(id);
        RayTazeleGec();
    }

    // Sunucu silindi / ayrıldım: önbellekleri temizle, açıksa ana sayfaya dön.
    private void SunucuKapandi(long id, string mesaj)
    {
        _sunucuOnbellek.Remove(id);
        _sonKanal.Remove(id);
        _ikonlar.Unut("sunucu", id);
        bool acikti = _sunucuId == id && _gorunum == Gorunum.Sunucu;
        if (_sunucuId == id) { _sunucuId = null; _sunucu = null; }
        if (acikti) GoruntuAna();
        _ = SunucularYenile();
        Bildir(mesaj, hata: false);
    }

    private void SunucuEkle()
    {
        var kart = SunucuModallari.Goster(this, _api, _ikonlar);
        kart.Bildir += (m, h) => Bildir(m, h);
        kart.Tamamlandi += s => _ = YeniSunucuyaGec(s);
    }

    private async Task YeniSunucuyaGec(Sunucu s)
    {
        SunucuKaydet(s);
        await SunucularYenile();
        await SunucuAcAsync(s.Id);
    }

    // ---- Kanallar ----

    private void KanalSec(long id)
    {
        if (_sunucu is not { } sv || sv.Kanallar.All(x => x.Id != id)) return;
        _ayarlarAcik = _yonetimAcik = _sunucuAyarAcik = false;
        _sonKanal[sv.Id] = id;
        _kanalListesi.SeciliKanal = id;
        KonusmaGoster(new KonusmaKimligi(KonusmaTuru.Kanal, id));
    }

    // Bir kanal ya da grup konuşmasını içerik alanında gösterir (DM'ler SohbetAc ile açılır).
    private void KonusmaGoster(KonusmaKimligi k)
    {
        if (k.Tur == KonusmaTuru.Dm) { SohbetAc(k.Id); return; }
        _acikKonusma = k;
        BasligiGuncelle();
        GorunumuAyarla();
        if (PanelBasligi(k) is { } baslik) _konusmaPaneli.Ac(k, baslik);
        KonusmaOkunduYap(k);
    }

    // Sohbet panelinin başlık bilgisi (ipucu yazısı ve "konuşmanın başlangıcı" satırı bundan üretilir).
    private SohbetBasligi? PanelBasligi(KonusmaKimligi k)
    {
        if (k.Tur == KonusmaTuru.Kanal && _sunucuOnbellek.Values.SelectMany(s => s.Kanallar).FirstOrDefault(x => x.Id == k.Id) is { } kanal)
            return SohbetBasligi.Kanal(kanal.Ad, kanal.Konu);
        if (k.Tur == KonusmaTuru.Grup && _gruplar.FirstOrDefault(x => x.Id == k.Id) is { } g)
            return SohbetBasligi.Grup(GrupAdi(g), g.Renk, _ikonlar.Grup(g.Id, g.IkonSurum), g.Uyeler.Count);
        return null;
    }

    // Açık kanal/grup sohbetinin üyelerini, yetkisini ve başlığını güncel tutar.
    private void KonusmaPaneliTazele()
    {
        if (_acikKonusma is not { } k || k.Tur == KonusmaTuru.Dm) return;
        _konusmaPaneli.Uyeler = KonusmaUyeleri(k);
        _konusmaPaneli.BaskalarininMesajlariniSilebilir = KonusmadaSilebilir(k);
        if (PanelBasligi(k) is { } b) _konusmaPaneli.BasligiGuncelle(b);
    }

    // Sohbet panelinin üye önerisi / bahsetme için kullanacağı üyeler.
    private List<Profil> KonusmaUyeleri(KonusmaKimligi k) => k.Tur switch
    {
        KonusmaTuru.Kanal => _sunucuOnbellek.Values.FirstOrDefault(s => s.Kanallar.Any(x => x.Id == k.Id))?.Uyeler.Select(u => u.Profil).ToList() ?? [],
        KonusmaTuru.Grup => _gruplar.FirstOrDefault(g => g.Id == k.Id)?.Uyeler.ToList() ?? [],
        _ => [_ben, .. _liste.Arkadaslar.Where(p => p.Id == k.Id)],
    };

    // Kanalda sunucu yöneticisi/sahibi başkalarının mesajlarını da silebilir.
    private bool KonusmadaSilebilir(KonusmaKimligi k) =>
        k.Tur == KonusmaTuru.Kanal
        && _sunucuOnbellek.Values.FirstOrDefault(s => s.Kanallar.Any(x => x.Id == k.Id)) is { Rolum: >= SunucuRolu.Yonetici };

    private void BasligiGuncelle()
    {
        if (_acikKonusma is { Tur: KonusmaTuru.Kanal } k && _sunucu?.Kanallar.FirstOrDefault(x => x.Id == k.Id) is { } kanal)
            _konusmaBasligi.KanalGoster(kanal.Ad, kanal.Konu);
        else if (_acikKonusma is { Tur: KonusmaTuru.Grup } gk && _gruplar.FirstOrDefault(x => x.Id == gk.Id) is { } grup)
            _konusmaBasligi.GrupGoster(GrupAdi(grup), grup.Uyeler.Count, grup.Renk, _ikonlar.Grup(grup.Id, grup.IkonSurum));
        KonusmaPaneliTazele();
    }

    private static string GrupAdi(Grup g) => string.IsNullOrWhiteSpace(g.Ad) ? string.Join(", ", g.Uyeler.Select(u => u.GorunenAd)) : g.Ad;

    private void KanalOlustur(long sunucuId) =>
        KanalKarti.Goster(this, _api, sunucuId, null, async k => { await SunucuTazele(sunucuId); if (_sunucuId == sunucuId && _gorunum == Gorunum.Sunucu) KanalSec(k.Id); });

    private async void KanalSil(Kanal k)
    {
        bool evet = await OnayKarti.Sor(this, $"#{k.Ad} silinsin mi?", "Bu kanaldaki tüm mesajlar kalıcı olarak silinir. Bu işlem geri alınamaz.", "Kanalı sil");
        if (!evet) return;
        var s = await _api.KanalSil(k.Id);
        if (!s.Basarili) { Bildir(s.Hata!, hata: true); return; }
        await SunucuTazele(k.SunucuId);
    }

    // ---- Sunucu menüleri ----

    private async void RayMenusu(long id, Rectangle yer)
    {
        var s = await SunucuDetayi(id);
        if (s != null) SunucuMenusu(s, yer, baslik: false);
    }

    // baslik: kanal listesinin üstündeki sunucu adından açıldıysa true (menü başlığın altına), rayda sağ tık ise false (simgenin sağına).
    private void SunucuMenusu(Sunucu s, Rectangle yer, bool baslik)
    {
        var m = new KoyuMenu();
        m.Ekle("Sunucu ayarları", () => _ = SunucuAyarlariAc(s.Id));
        if (s.Rolum >= SunucuRolu.Yonetici)
        {
            if (!string.IsNullOrEmpty(s.DavetKodu))
                m.Ekle("Davet kodunu kopyala", () =>
                {
                    try { Clipboard.SetText(s.DavetKodu); Bildir($"Davet kodu kopyalandı: {s.DavetKodu}", hata: false); }
                    catch { Bildir("Panoya kopyalanamadı.", hata: true); }
                });
            m.Ekle("Kanal oluştur", () => KanalOlustur(s.Id));
        }
        m.Ayirici();
        if (s.Rolum == SunucuRolu.Sahip) m.Ekle("Sunucuyu sil", () => SunucuSilKarti.Goster(this, _api, s, () => SunucuKapandi(s.Id, "Sunucu silindi.")), Tema.Hata);
        else m.Ekle("Sunucudan ayrıl", () => SunucudanAyril(s), Tema.Hata);

        if (baslik) { m.MinimumSize = new Size(Math.Max(0, yer.Width - Tema.S(20)), 0); _kanalListesi.MenuAcik = true; }
        m.Closed += (_, _) => { if (baslik) _kanalListesi.MenuAcik = false; BeginInvoke(m.Dispose); };
        m.Show(baslik ? new Point(yer.Left + Tema.S(10), yer.Bottom - Tema.S(2)) : new Point(yer.Right + Tema.S(8), yer.Top));
    }

    private async Task SunucuAyarlariAc(long id, SunucuAyarlari.Sekme sekme = SunucuAyarlari.Sekme.Genel)
    {
        if (!(_gorunum == Gorunum.Sunucu && _sunucuId == id)) await SunucuAcAsync(id);
        if (_sunucu is null || _sunucu.Id != id) return;
        _ayarlarAcik = _yonetimAcik = false;
        _sunucuAyarAcik = true;
        _sunucuAyarlari.Ac(_sunucu, _ben.Id, sekme);
        GorunumuAyarla();
    }

    private async void SunucudanAyril(Sunucu s)
    {
        bool evet = await OnayKarti.Sor(this, $"{s.Ad} sunucusundan ayrılınsın mı?", "Ayrılırsan bu sunucuya yeniden katılmak için davet kodu gerekebilir.", "Sunucudan ayrıl");
        if (!evet) return;
        var r = await _api.SunucudanAyril(s.Id);
        if (!r.Basarili) { Bildir(r.Hata!, hata: true); return; }
        SunucuKapandi(s.Id, "Sunucudan ayrıldın.");
    }

    // ---- Gruplar ----

    private async Task GruplariYenile()
    {
        int no = ++_grupIstekNo;
        var s = await _api.Gruplar();
        if (no != _grupIstekNo || IsDisposed || !s.Basarili) return;
        _gruplar = s.Veri!;
        if (_acikKonusma is { Tur: KonusmaTuru.Grup } k && KonusmaGorunuyor(k))
            _gruplar = _gruplar.Select(g => g.Id == k.Id ? g with { Okunmamis = 0, Bahsedilme = 0 } : g).ToList();
        _kenar.Gruplar = _gruplar;
        AnaRozetiGuncelle();

        if (_acikKonusma is { Tur: KonusmaTuru.Grup } acik)
        {
            if (_gruplar.All(g => g.Id != acik.Id)) { if (_gorunum == Gorunum.Ana) GoruntuAna(); else _acikKonusma = null; }
            else BasligiGuncelle();
        }
    }

    private void GrupAc(long id)
    {
        if (_gruplar.All(g => g.Id != id)) return;
        _sunucuIstekNo++;
        _gorunum = Gorunum.Ana;
        _sunucuAyarAcik = false;
        _ayarlarAcik = _yonetimAcik = false;
        _acikKonusma = null;
        RayiSenkronla();
        SohbetKapat();
        KonusmaGoster(new KonusmaKimligi(KonusmaTuru.Grup, id));
    }

    private void GrupOlustur() =>
        GrupModallari.Olustur(this, _api, _liste.Arkadaslar, _avatarlar, g => _ = GrupOlusturuldu(g));

    private async Task GrupOlusturuldu(Grup g)
    {
        await GruplariYenile();
        if (_gruplar.All(x => x.Id != g.Id)) { _gruplar = [g, .. _gruplar]; _kenar.Gruplar = _gruplar; }
        GrupAc(g.Id);
    }

    private void GrupAyarlariAc()
    {
        if (_acikKonusma is not { Tur: KonusmaTuru.Grup } k || _gruplar.FirstOrDefault(g => g.Id == k.Id) is not { } grup) return;
        GrupModallari.Ayarlar(this, _api, grup, _ben.Id, _liste.Arkadaslar, _avatarlar, _ikonlar,
            guncellendi: ng =>
            {
                _gruplar = _gruplar.Select(x => x.Id == ng.Id ? ng with { Okunmamis = x.Okunmamis, Bahsedilme = x.Bahsedilme } : x).ToList();
                _kenar.Gruplar = _gruplar;
                BasligiGuncelle();
            },
            ayrildi: () =>
            {
                _ikonlar.Unut("grup", grup.Id);
                _gruplar = _gruplar.Where(x => x.Id != grup.Id).ToList();
                _kenar.Gruplar = _gruplar;
                AnaRozetiGuncelle();
                GoruntuAna();
                _ = GruplariYenile();
                Bildir("Gruptan ayrıldın.", hata: false);
            });
    }

    // ---- Okunmamışlar ve gelen mesajlar ----

    // Konuşma şu an gözümün önünde mi? (pencere öndeyken, ilgili sayfa açıkken)
    private bool KonusmaGorunuyor(KonusmaKimligi k)
    {
        if (ActiveForm != this || WindowState == FormWindowState.Minimized || _ayarlarAcik || _yonetimAcik) return false;
        return k.Tur switch
        {
            KonusmaTuru.Kanal => _gorunum == Gorunum.Sunucu && !_sunucuAyarAcik,
            KonusmaTuru.Grup => _gorunum == Gorunum.Ana && !DmAcik,
            _ => false,
        } && _acikKonusma == k;
    }

    private void GorunenKonusmayiOku()
    {
        if (_acikKonusma is { } k && KonusmaGorunuyor(k)) KonusmaOkunduYap(k);
    }

    // Konuşmayı okundu say: yerel sayaçlar sıfırlanır, okunmamış varsa sunucuya bildirilir.
    private void KonusmaOkunduYap(KonusmaKimligi k)
    {
        if (YerelSifirla(k)) _ = _api.KonusmaOkundu(k);
    }

    // Başka cihazımda okundu.
    private void YerelOkundu(KonusmaKimligi k) => YerelSifirla(k);

    // Sayaçları sıfırlar; sıfırlanacak bir şey olduysa true.
    private bool YerelSifirla(KonusmaKimligi k)
    {
        if (k.Tur == KonusmaTuru.Grup)
        {
            var g = _gruplar.FirstOrDefault(x => x.Id == k.Id);
            if (g is null || (g.Okunmamis == 0 && g.Bahsedilme == 0)) return false;
            _gruplar = _gruplar.Select(x => x.Id == k.Id ? x with { Okunmamis = 0, Bahsedilme = 0 } : x).ToList();
            _kenar.Gruplar = _gruplar;
            AnaRozetiGuncelle();
            return true;
        }
        if (k.Tur != KonusmaTuru.Kanal) return false;
        if (!_kanalSunucu.TryGetValue(k.Id, out var sid) || !_sunucuOnbellek.TryGetValue(sid, out var s)) return false;
        var kanal = s.Kanallar.FirstOrDefault(x => x.Id == k.Id);
        if (kanal is null || (kanal.Okunmamis == 0 && kanal.Bahsedilme == 0)) return false;
        KanalSayaciDegistir(k.Id, x => x with { Okunmamis = 0, Bahsedilme = 0 });
        return true;
    }

    // Bir kanalın sayaçlarını değiştirir; kanal listesini ve raydaki sunucu toplamını buna göre günceller.
    private void KanalSayaciDegistir(long kanalId, Func<Kanal, Kanal> degistir)
    {
        if (!_kanalSunucu.TryGetValue(kanalId, out var sid) || !_sunucuOnbellek.TryGetValue(sid, out var s)) { RayTazeleGec(); return; }
        var eski = s.Kanallar.FirstOrDefault(k => k.Id == kanalId);
        if (eski is null) return;
        var yeni = degistir(eski);
        if (yeni == eski) return;
        s = s with { Kanallar = s.Kanallar.Select(k => k.Id == kanalId ? yeni : k).ToList() };
        _sunucuOnbellek[sid] = s;
        if (_sunucu?.Id == sid) { _sunucu = s; _kanalListesi.Sunucu = s; }
        int dO = yeni.Okunmamis - eski.Okunmamis, dB = yeni.Bahsedilme - eski.Bahsedilme;
        _sunucular = _sunucular.Select(x => x.Id == sid ? x with { Okunmamis = Math.Max(0, x.Okunmamis + dO), Bahsedilme = Math.Max(0, x.Bahsedilme + dB) } : x).ToList();
        _rayi.Sunucular = _sunucular;
        RayiSenkronla();
    }

    // Sadece kanal ve grup mesajları burada işlenir; DM'ler sohbet tarafında.
    private void GezinmeMesajGeldi(Mesaj m)
    {
        bool benden = m.GonderenId == _ben.Id;
        bool bahis = m.Bahsedilenler?.Contains(_ben.Id) == true;
        if (m.KanalId is { } kid)
        {
            var k = new KonusmaKimligi(KonusmaTuru.Kanal, kid);
            if (benden) return;
            if (KonusmaGorunuyor(k)) { _ = _api.KonusmaOkundu(k); return; }
            KanalSayaciDegistir(kid, x => x with { Okunmamis = x.Okunmamis + 1, Bahsedilme = x.Bahsedilme + (bahis ? 1 : 0) });
            if (bahis) Tema.Titret(this);
        }
        else if (m.GrupId is { } gid)
        {
            var k = new KonusmaKimligi(KonusmaTuru.Grup, gid);
            if (_gruplar.All(g => g.Id != gid)) { _ = GruplariYenile(); return; }
            bool goruyor = KonusmaGorunuyor(k);
            _gruplar = _gruplar.Select(g => g.Id != gid ? g : g with
            {
                SonMesaj = m,
                Okunmamis = benden || goruyor ? (goruyor ? 0 : g.Okunmamis) : g.Okunmamis + 1,
                Bahsedilme = benden || goruyor ? (goruyor ? 0 : g.Bahsedilme) : g.Bahsedilme + (bahis ? 1 : 0),
            }).ToList();
            _kenar.Gruplar = _gruplar;
            AnaRozetiGuncelle();
            if (goruyor && !benden) _ = _api.KonusmaOkundu(k);
            else if (!benden) Tema.Titret(this);
        }
    }

    // Çevrimiçi durumu / profil değişen kişi açık sunucunun üyesiyse üye listesi güncellenir.
    private void UyeProfiliniDegistir(long id, Func<Profil, Profil> degistir)
    {
        if (!_gezinmeHazir || _sunucu is null || _sunucu.Uyeler.All(u => u.Profil.Id != id)) return;
        var s = _sunucu with { Uyeler = _sunucu.Uyeler.Select(u => u.Profil.Id == id ? u with { Profil = degistir(u.Profil) } : u).ToList() };
        _sunucu = s;
        _sunucuOnbellek[s.Id] = s;
        _uyeListesi.Uyeler = s.Uyeler;
        KonusmaPaneliTazele();
    }

    // ---- Kimlik ----

    private void KimligiKopyala()
    {
        try { Clipboard.SetText("#" + _ben.Id); Bildir($"Kimliğin kopyalandı: #{_ben.Id}. Arkadaşların bunu \"Arkadaş ekle\"ye yazabilir.", hata: false); }
        catch { Bildir("Panoya kopyalanamadı.", hata: true); }
    }
}
