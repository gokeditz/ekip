using Ekip.Ortak;

namespace Ekip.Uygulama;

// Parti, davetler, Steam tarzı tam ekran overlay (Ctrl+Tab) ve sistem tepsisi.
public partial class AnaForm
{
    private PartiBilgisi? _parti;
    private readonly SohbetVerisi _partiSohbeti = new() { Yuklendi = true };
    private readonly Dictionary<long, (string ad, string renk)> _partiIsimleri = [];
    private int _partiMesajNo;
    private readonly List<DavetPenceresi> _davetler = [];

    private OverlayPerdesi? _perde;
    private OverlayFormu? _overlay;
    private IntPtr _oyunPenceresi;
    private KlavyeKancasi? _kanca;
    private NotifyIcon? _tepsi;
    private bool _gercektenKapat, _tepsiUyarisiGosterildi;

    private bool OverlayAcik => _overlay?.Acik == true;

    private void PartiOlaylariniBagla()
    {
        _kenar.PartiKurTik += async () => await PartiKur();
        _kenar.PartidenAyrilTik += async () => { PartiAyarla(null); await _canli.PartidenAyril(); };
        _kenar.DavetTik += DavetMenusu;

        _canli.PartiDaveti += DavetGeldi;
        _canli.PartiGuncellendi += b =>
        {
            if (b is null) PartiAyarla(null);
            else if (_parti is null) PartiAyarla(new PartiDurumu(b, []));
            else { _parti = b; PartiArayuzunuGuncelle(); }
        };
        _canli.PartiMesajiGeldi += m =>
        {
            if (_parti is null) return;
            PartiMesajiEkle(m);
            PartiSohbetiniCiz();
            if (m.GonderenId != _ben.Id && !OverlayAcik) Tema.Titret(this);
        };
        // Bağlanınca (ya da kopup gelince) sunucudaki parti durumunu al: kopukken partiden düşmüş olabiliriz.
        _canli.BaglantiDegisti += async bagli => { if (bagli) PartiAyarla(await _canli.PartiDurumum()); };

        KisayollariYukle();
        _kanca = new KlavyeKancasi { TusBasildi = TusBasildi };
        TepsiKur();
    }

    // ---- Kısayollar ----

    private Kisayol _overlayTusu = Kisayol.VarsayilanOverlay, _miniTusu = Kisayol.VarsayilanMini;
    private bool _kisayolYakalaniyor; // Ayarlar'da yeni tuş seçilirken kısayollarımız sussun.

    public void KisayollariYukle()
    {
        _overlayTusu = Kisayol.Coz(_ayarlar.OverlayTusu, Kisayol.VarsayilanOverlay);
        _miniTusu = Kisayol.Coz(_ayarlar.MiniTusu, Kisayol.VarsayilanMini);
        _kenar.OverlayTusu = _overlayTusu.ToString();
        if (_overlay != null) _overlay.TusYazisi = _overlayTusu.ToString();
        if (_mini != null) { _mini.TusYazisi = _miniTusu.ToString(); _mini.Invalidate(); }
    }

    private static bool Eslesir(Kisayol k, Keys t, bool c, bool a, bool s) => k.Tus == t && k.Ctrl == c && k.Alt == a && k.Shift == s;

    // Klavye kancasından her tuşta çağrılır: hızlı olmalı. true dönerse tuş yutulur.
    private bool TusBasildi(Keys tus, bool ctrl, bool alt, bool shift)
    {
        if (_kisayolYakalaniyor) return false;
        if (tus == Keys.Escape)
        {
            // Menü açıksa Esc menüyü kapatsın; Esc'i sadece kendi pencerelerimiz öndeyken alırız.
            if (KoyuMenu.AcikMenu > 0 || !Pencereler.BizimMi(Pencereler.OnPencere())) return false;
            if (OverlayAcik) { BeginInvoke(OverlayKapat); return true; }
            if (_miniYaziyor) { BeginInvoke(() => MiniYazmaKapat(oyunaDon: true)); return true; }
            return false;
        }
        if (Eslesir(_overlayTusu, tus, ctrl, alt, shift)) return OverlayTusuBasildi();
        if (Eslesir(_miniTusu, tus, ctrl, alt, shift)) return MiniTusuBasildi();
        return false;
    }

    // ---- Parti ----

    private async Task PartiKur()
    {
        var d = await _canli.PartiKur();
        if (d is null) { Bildir("Sunucuya bağlı değilsin, parti kurulamadı.", hata: true); return; }
        PartiAyarla(d);
        if (!OverlayAcik) Bildir($"Parti kuruldu. Arkadaşlarını davet et; oyundayken {_overlayTusu} ile overlay'i aç.", hata: false);
    }

    private void PartiAyarla(PartiDurumu? d)
    {
        bool yeniParti = d?.Bilgi.Id != _parti?.Id;
        _parti = d?.Bilgi;
        if (yeniParti)
        {
            _partiSohbeti.Liste = [];
            _partiIsimleri.Clear();
            foreach (var m in d?.Mesajlar ?? []) PartiMesajiEkle(m);
        }
        PartiArayuzunuGuncelle();
    }

    private void PartiMesajiEkle(PartiMesaji m)
    {
        _partiIsimleri[m.GonderenId] = (m.GonderenAd, m.GonderenRenk);
        _partiSohbeti.Liste.Add(new Mesaj(++_partiMesajNo, m.GonderenId, 0, m.Metin, m.Zaman));
        if (_partiSohbeti.Liste.Count > 100) _partiSohbeti.Liste.RemoveAt(0);
    }

    private void PartiArayuzunuGuncelle()
    {
        _kenar.Parti = _parti;
        _overlay?.PartiAyarla(_parti);
        PartiSohbetiniCiz();
    }

    // Parti mesajları normal sohbet görünümüyle çizilir; "karşı taraf" yerine partinin kendisi var.
    private void PartiSohbetiniCiz()
    {
        if (_mini != null && _miniParti) MiniYenile(); // Partiden çıkınca sabitli parti sohbeti de kapanır.
        if (_overlay is null || _parti is null) return;
        var baslik = new Profil(-1, "parti", "Parti sohbeti", "", $"{_parti.Uyeler.Count} kişi", "#7C5CFF", 0, Durum.Cevrimici);
        _overlay.PartiSohbetiGoster(_ben, baslik, _partiSohbeti, id =>
            _parti.Uyeler.FirstOrDefault(u => u.Id == id)
            ?? (_partiIsimleri.TryGetValue(id, out var x) ? new Profil(id, "", x.ad, "", "", x.renk, 0, Durum.Cevrimdisi) : null));
    }

    // ---- Mini sohbet (oyuna sabitlenmiş sohbet) ----

    private MiniSohbet? _mini;
    private bool _miniParti;
    private long? _miniDm;
    private IntPtr _miniOyun;
    private OnPencereIzleyici? _miniIzleyici;

    // Sabitli DM mini sohbette ekranda görünüyorsa gelen mesaj okunmuş sayılır.
    private bool MiniDmGoruyor(long id) => _mini is { Visible: true } && !_miniParti && _miniDm == id;

    private void SabitlemeyiDegistir()
    {
        if (_overlay is null) return;
        var sekme = _overlay.Secili;
        long? dm = sekme == OverlayFormu.Sekme.Dm ? _overlay.DmKisi?.Id : null;
        bool zatenSabitli = _overlay.SabitliMi?.Invoke(sekme, dm) == true;
        MiniKapat();
        if (!zatenSabitli)
        {
            _miniParti = sekme == OverlayFormu.Sekme.Parti;
            _miniDm = dm;
            _miniOyun = _oyunPenceresi;
            MiniOlustur();
            MiniYenile();
            _mini!.Show();
            _mini.EtkilesimAyarla(true); // Overlay açık: taşınabilir, boyutlanabilir.
            _mini.EnUsteGetir();
            _miniIzleyici = new OnPencereIzleyici();
            _miniIzleyici.Degisti += h => { if (!OverlayAcik) MiniGorunurluk(h); };
            if (dm is { } d) _ = OkunduYap(d); // Sabitlenen DM artık gözünün önünde.
        }
        _overlay.SabitDurumunuYenile();
    }

    private bool _miniYaziyor;
    private IntPtr _miniDonulecek;

    private void MiniOlustur()
    {
        var m = new MiniSohbet { TusYazisi = _miniTusu.ToString() };
        _mini = m;
        m.KapatIstendi += () => { MiniYazmaKapat(oyunaDon: true); MiniKapat(); _overlay?.SabitDurumunuYenile(); };
        m.Gonder += metin =>
        {
            if (_miniParti) _ = _canli.PartiMesajiGonder(metin);
            else if (_miniDm is { } d) MesajGonder(d, metin);
        };
        m.YazmaBitti += oyunaDon => MiniYazmaKapat(oyunaDon);
        m.AlanDegisti += r => { _ayarlar.MiniAlan = [r.X, r.Y, r.Width, r.Height]; _ayarlar.Kaydet(); };
        // Yazarken oyuna (ya da başka yere) tıklarsa yazma modundan çık.
        m.Deactivate += (_, _) => BeginInvoke(() => { if (_miniYaziyor && !OverlayAcik && ActiveForm != _mini) MiniYazmaKapat(oyunaDon: false); });

        // Kayıtlı yer/boyut hâlâ bir ekranın içindeyse onu kullan; yoksa oyunun sol alt köşesi.
        if (_ayarlar.MiniAlan is [var x, var y, var w, var h] && Screen.AllScreens.Any(s => s.Bounds.IntersectsWith(new Rectangle(x, y, w, h))))
            m.Bounds = new Rectangle(x, y, w, h);
        else
        {
            var oyun = Pencereler.Gecerli(_miniOyun) ? Pencereler.Alan(_miniOyun) : Screen.PrimaryScreen!.Bounds;
            m.Location = new Point(oyun.Left + Tema.S(24), oyun.Bottom - m.Height - Tema.S(90));
        }
    }

    private bool MiniTusuBasildi()
    {
        if (_mini is not { Visible: true } || _miniYaziyor || OverlayAcik) return false;
        var on = Pencereler.OnPencere();
        // Sadece mini sohbetin sabitlendiği oyun öndeyken: başka yerde T normal harf olarak çalışsın.
        if (Pencereler.BizimMi(on) || (on != _miniOyun && Pencereler.SurecAdi(on) != Pencereler.SurecAdi(_miniOyun))) return false;
        BeginInvoke(() => MiniYazmaAc(on));
        return true;
    }

    // T: oyun girdiyi bırakır, klavye ve fare mini sohbete geçer.
    private void MiniYazmaAc(IntPtr oyun)
    {
        if (_mini is null) return;
        _miniYaziyor = true;
        _miniDonulecek = oyun;
        _mini.EtkilesimAyarla(true);
        _mini.EnUsteGetir();
        Pencereler.OneGetir(_mini.Handle);
        _mini.Activate();
        _mini.YaziKutusunaOdaklan();
        if (!_miniParti && _miniDm is { } d) _ = OkunduYap(d);
    }

    private void MiniYazmaKapat(bool oyunaDon)
    {
        if (!_miniYaziyor || _mini is null) return;
        _miniYaziyor = false;
        if (!OverlayAcik) _mini.EtkilesimAyarla(false);
        if (oyunaDon) Pencereler.OneGetir(_miniDonulecek);
    }

    private void MiniKapat()
    {
        _miniYaziyor = false;
        _miniIzleyici?.Dispose();
        _miniIzleyici = null;
        _mini?.Dispose();
        _mini = null;
    }

    private void MiniYenile()
    {
        if (_mini is null) return;
        if (_miniParti)
        {
            if (_parti is null) { MiniKapat(); _overlay?.SabitDurumunuYenile(); return; }
            var satirlar = _partiSohbeti.Liste.TakeLast(100).Select(m =>
            {
                var (ad, renk) = m.GonderenId == _ben.Id ? (_ben.GorunenAd, _ben.ProfilRengi)
                    : _partiIsimleri.TryGetValue(m.GonderenId, out var x) ? x : ("?", "#7C5CFF");
                return new MiniSohbet.Satir(ad, ColorTranslator.FromHtml(renk), m.Metin, m.Zaman);
            }).ToList();
            _mini.Ayarla($"Parti · {_parti.Uyeler.Count} kişi", satirlar);
        }
        else if (_miniDm is { } id)
        {
            var kisi = _liste.Arkadaslar.FirstOrDefault(p => p.Id == id);
            if (kisi is null) { MiniKapat(); _overlay?.SabitDurumunuYenile(); return; }
            var satirlar = Veri(id).Liste.TakeLast(100).Select(m =>
            {
                var p = m.GonderenId == _ben.Id ? _ben : kisi;
                return new MiniSohbet.Satir(p.GorunenAd, ColorTranslator.FromHtml(p.ProfilRengi), m.Metin, m.Zaman);
            }).ToList();
            _mini.Ayarla(kisi.GorunenAd, satirlar);
        }
    }

    // Mini sohbet sadece sabitlendiği oyun öndeyken görünür (Alt+Tab yapınca gizlenir).
    private void MiniGorunurluk(IntPtr on)
    {
        if (_mini is null || on == _mini.Handle) return;
        // Aynı pencere ya da aynı oyunun yeni penceresi (oyun yeniden açıldıysa).
        bool oyunOnde = on == _miniOyun
            || (on != IntPtr.Zero && !Pencereler.BizimMi(on) && Pencereler.SurecAdi(on) is { } s && s == Pencereler.SurecAdi(_miniOyun));
        if (oyunOnde) { if (!_mini.Visible) _mini.Show(); _mini.EnUsteGetir(); }
        else if (_mini.Visible) _mini.Hide();
    }

    private void DavetMenusu(Rectangle yakin)
    {
        var m = new KoyuMenu();
        var adaylar = _liste.Arkadaslar.Where(p => p.Durum != Durum.Cevrimdisi && _parti?.Uyeler.All(u => u.Id != p.Id) != false).ToList();
        if (adaylar.Count == 0) m.Ekle("Çevrimiçi arkadaşın yok", () => { }).Enabled = false;
        foreach (var p in adaylar) m.Ekle(p.GorunenAd, () => PartiyeDavetEt(p), resim: KoyuMenu.Nokta(Cizim.DurumRengi(p.Durum)));
        m.Closed += (_, _) => BeginInvoke(m.Dispose);
        m.Show(new Point(yakin.Left, yakin.Top), ToolStripDropDownDirection.AboveRight);
    }

    private async void PartiyeDavetEt(Profil p)
    {
        if (_parti is null) await PartiKur();
        if (_parti is null) return;
        var hata = await _canli.PartiyeDavetEt(p.Id);
        Bildir(hata ?? $"{p.GorunenAd} partiye davet edildi.", hata: hata != null);
    }

    private void DavetGeldi(string partiId, Profil davetEden)
    {
        var f = new DavetPenceresi(davetEden, _avatarlar.Al(davetEden), _davetler.Count);
        f.Katil += async () =>
        {
            var d = await _canli.DaveteKatil(partiId);
            if (d is null) { Bildir("Bu davet artık geçerli değil.", hata: true); return; }
            PartiAyarla(d);
            Bildir($"{davetEden.GorunenAd} adlı kişinin partisine katıldın.", hata: false);
        };
        f.Reddet += () => _ = _canli.DavetiReddet(partiId);
        f.FormClosed += (_, _) => _davetler.Remove(f);
        _davetler.Add(f);
        f.Show();
        Tema.Titret(this);
    }

    // ---- Overlay ----

    private bool OverlayTusuBasildi()
    {
        if (OverlayAcik) { BeginInvoke(OverlayKapat); return true; }
        var on = Pencereler.OnPencere();
        bool ekipOnde = on == Handle;
        if (!ekipOnde && (Pencereler.BizimMi(on) || !Pencereler.TamEkran(on))) return false; // Tarayıcıda vs. Ctrl+Tab normal çalışsın.
        BeginInvoke(() => OverlayAc(on));
        return true;
    }

    private void OverlayAc(IntPtr on)
    {
        if (_overlay is null)
        {
            _perde = new OverlayPerdesi();
            _perde.Tiklandi += OverlayKapat;
            _overlay = new OverlayFormu { Avatarlar = _avatarlar, Owner = _perde, TusYazisi = _overlayTusu.ToString() };
            _overlay.KapatIstendi += OverlayKapat;
            _overlay.Deactivate += (_, _) => BeginInvoke(() =>
            {
                // Başka bir programa geçildiyse (Alt+Tab) overlay'i kapat. Kendi menülerimiz sayılmaz.
                if (OverlayAcik && ActiveForm is null && !Pencereler.BizimMi(Pencereler.OnPencere())) OverlayKapat(oyunaDon: false);
            });
            _overlay.DmSecildi += OverlayDmAc;
            _overlay.DmGonder += MesajGonder;
            _overlay.DmEskiIste += async id => await MesajlariYukle(id, eski: true);
            _overlay.PartiGonder += metin => _ = _canli.PartiMesajiGonder(metin);
            _overlay.Davet += PartiyeDavetEt;
            _overlay.PartiKur += async () => await PartiKur();
            _overlay.PartidenAyril += async () => { PartiAyarla(null); await _canli.PartidenAyril(); };
            _overlay.SabitleTik += SabitlemeyiDegistir;
            _overlay.SabitliMi = (s, dm) => _mini != null && (s == OverlayFormu.Sekme.Parti ? _miniParti : !_miniParti && _miniDm == dm);
        }

        _oyunPenceresi = on;
        var ekran = Screen.FromHandle(on).Bounds;
        _overlay.ArkadaslariAyarla(_liste.Arkadaslar);
        _overlay.PartiAyarla(_parti);
        PartiSohbetiniCiz();
        if (_overlay.DmKisi is { } kisi) SohbetiYenidenCiz(kisi.Id);

        _perde!.Bounds = ekran;
        _perde.Show();
        _overlay.Ac(ekran, on == Handle ? "Ekip" : Pencereler.Baslik(on));
        _overlay.SabitDurumunuYenile();
        if (_mini != null)
        {
            // Overlay açıkken mini sohbet düzenlenebilir: taşınır, boyutlanır, kapatılır. Perdenin üstünde durmalı.
            _miniYaziyor = false;
            if (!_mini.Visible) _mini.Show();
            _mini.EtkilesimAyarla(true);
            _mini.EnUsteGetir();
        }
        if (_overlay.DmKisi is { } k2) _ = OkunduYap(k2.Id);
    }

    private void OverlayKapat() => OverlayKapat(oyunaDon: true);

    private void OverlayKapat(bool oyunaDon)
    {
        if (_overlay is null || !_overlay.Acik) return;
        _overlay.Kapat();
        _perde?.Hide();
        if (oyunaDon && _oyunPenceresi != Handle) Pencereler.OneGetir(_oyunPenceresi);
        if (_mini != null)
        {
            _mini.EtkilesimAyarla(false);
            MiniGorunurluk(Pencereler.OnPencere());
        }
    }

    private async void OverlayDmAc(long id)
    {
        var kisi = _liste.Arkadaslar.FirstOrDefault(p => p.Id == id);
        if (kisi is null || _overlay is null) return;
        if (_sohbetler.All(x => x.Karsi.Id != id)) { _sohbetler.Insert(0, new SohbetOzeti(kisi, null, 0)); KenariGuncelle(); }
        _overlay.DmAc(kisi);
        var v = Veri(id);
        _overlay.DmGoster(_ben, kisi, v, _basarisiz);
        if (!v.Yuklendi) await MesajlariYukle(id, eski: false);
        await OkunduYap(id);
    }

    private bool OverlayDmGoruyor(long id) =>
        OverlayAcik && _overlay!.Secili == OverlayFormu.Sekme.Dm && _overlay.DmKisi?.Id == id;

    private void OverlayDmYenile(long id, Profil profil)
    {
        if (_overlay?.DmKisi?.Id == id) _overlay.DmGoster(_ben, profil, Veri(id), _basarisiz);
        if (_mini != null && !_miniParti && _miniDm == id) MiniYenile();
    }

    private void OverlayArkadaslariYenile()
    {
        if (OverlayAcik) _overlay!.ArkadaslariAyarla(_liste.Arkadaslar);
    }

    // ---- Sistem tepsisi: pencereyi kapatınca uygulama arka planda çalışmaya devam eder ----

    private void TepsiKur()
    {
        var menu = new KoyuMenu();
        menu.Ekle("Ekip'i aç", PencereyiAc);
        menu.Ayirici();
        menu.Ekle("Çıkış", () => { _gercektenKapat = true; Close(); }, Tema.Hata);
        _tepsi = new NotifyIcon { Icon = Tema.Ikon, Text = "Ekip", Visible = true, ContextMenuStrip = menu };
        _tepsi.DoubleClick += (_, _) => PencereyiAc();
        _tepsi.BalloonTipClicked += (_, _) => PencereyiAc();
    }

    private void PencereyiAc()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Pencereler.OneGetir(Handle);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_gercektenKapat || e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true; // X'e basınca kapanma, tepsiye in.
        Hide();
        if (!_tepsiUyarisiGosterildi && _tepsi != null)
        {
            _tepsiUyarisiGosterildi = true;
            _tepsi.ShowBalloonTip(4000, "Ekip arka planda çalışıyor",
                $"Oyundayken {_overlayTusu} ile overlay'i açabilirsin. Kapatmak için tepsideki simgeye sağ tıkla.", ToolTipIcon.None);
        }
    }

    private void PartiTemizle()
    {
        _kanca?.Dispose();
        _kanca = null;
        MiniKapat();
        _overlay?.Dispose();
        _perde?.Dispose();
        _overlay = null;
        _perde = null;
        if (_tepsi != null) { _tepsi.Visible = false; _tepsi.ContextMenuStrip?.Dispose(); _tepsi.Dispose(); _tepsi = null; }
    }
}
