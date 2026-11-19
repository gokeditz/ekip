using Ekip.Ortak;

namespace Ekip.Uygulama;

// Grup (çok kişilik DM) için uygulama içi modal kartlar. Form içinde göstermek için GrupModallari.Olustur / Ayarlar.
public static class GrupModallari
{
    public static GrupOlusturKarti Olustur(Form sahip, Api api, IEnumerable<Profil> arkadaslar, AvatarOnbellek? avatarlar, Action<Grup>? bitti = null)
        => GrupOlusturKarti.Goster(sahip, api, arkadaslar, avatarlar, bitti);

    public static GrupAyarKarti Ayarlar(Form sahip, Api api, Grup grup, long benId, IEnumerable<Profil> arkadaslar,
        AvatarOnbellek? avatarlar, IkonOnbellek? ikonlar, Action<Grup>? guncellendi = null, Action? ayrildi = null)
        => GrupAyarKarti.Goster(sahip, api, grup, benId, arkadaslar, avatarlar, ikonlar, guncellendi, ayrildi);
}

// "Grup oluştur": arkadaş listesinden çoklu seçim + isteğe bağlı ad.
public class GrupOlusturKarti : ModalKarti
{
    private readonly Api _api;
    private readonly GirisKutusu _ara = new() { IpucuYazisi = "Arkadaşlarında ara" };
    private readonly ArkadasSecimListesi _liste = new() { Maks = Kurallar.GrupUyeMax - 1, BosYazi = "Henüz arkadaşın yok. Önce birkaç arkadaş ekle, sonra grup kurabilirsin." };
    private readonly Label _sayac = new() { AutoSize = false, Font = Tema.Yazi(9.5f, kalin: true), ForeColor = Tema.MetinIkincil, BackColor = Tema.Yuzey, TextAlign = ContentAlignment.MiddleLeft };
    private readonly GirisKutusu _ad = new() { Etiket = "Grup adı (isteğe bağlı)", MaksUzunluk = Kurallar.GrupAdMax, IpucuYazisi = "Boş bırakırsan üyelerin adlarından yapılır" };

    public event Action<Grup>? Tamamlandi;

    public GrupOlusturKarti(Api api, IEnumerable<Profil> arkadaslar, AvatarOnbellek? avatarlar) : base(Tema.S(480))
    {
        _api = api;
        Baslik = "Grup oluştur";
        Birincil.Text = "Grubu oluştur";
        Height = Tema.S(520);
        int w = Width - Ic * 2;

        _ara.Bounds = new Rectangle(Ic, Tema.S(58), w, _ara.Height);
        _ad.Bounds = new Rectangle(Ic, Height - AltH - Tema.S(18) - _ad.Height, w, _ad.Height);
        _sayac.Bounds = new Rectangle(Ic, _ad.Top - Tema.S(8) - Tema.S(22), w, Tema.S(22));
        _listeCerceve = new Rectangle(Ic, _ara.Bottom + Tema.S(10), w, _sayac.Top - Tema.S(6) - _ara.Bottom - Tema.S(10));
        _liste.Bounds = Rectangle.Inflate(_listeCerceve, -Tema.S(3), -Tema.S(3));
        _liste.Avatarlar = avatarlar;
        _liste.Ayarla(arkadaslar);
        Controls.AddRange([_ara, _liste, _sayac, _ad]);

        _ara.TextChanged += (_, _) => _liste.Filtre = _ara.Text;
        _liste.SecimDegisti += Guncelle;
        Birincil.Click += async (_, _) => await Olustur();
        Guncelle();
    }

    private void Guncelle()
    {
        int n = _liste.Secililer.Count;
        _sayac.Text = n == 0 ? $"En az bir arkadaş seç (en fazla {Kurallar.GrupUyeMax - 1})" : $"{n} / {Kurallar.GrupUyeMax - 1} arkadaş seçildi";
        _sayac.ForeColor = _liste.Dolu ? Tema.Vurgu : Tema.MetinIkincil;
        Birincil.Enabled = n >= 1;
        Hata(null);
    }

    public void Odaklan() => _ara.Focus();

    private Rectangle _listeCerceve;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        CerceveCiz(e.Graphics, _listeCerceve);
    }

    private async Task Olustur()
    {
        if (_liste.Secililer.Count < 1) return;
        Hata(null);
        Mesgul(true, "Grubu oluştur", "Oluşturuluyor…", _ara, _liste, _ad);
        var s = await _api.GrupOlustur(new GrupOlusturma(_ad.Text.Trim(), _liste.Secililer.ToList()));
        if (!s.Basarili) { Mesgul(false, "Grubu oluştur", ""); Birincil.Enabled = true; Hata(s.Hata); return; }
        Tamamlandi?.Invoke(s.Veri!);
    }

    public static GrupOlusturKarti Goster(Form sahip, Api api, IEnumerable<Profil> arkadaslar, AvatarOnbellek? avatarlar, Action<Grup>? bitti = null)
    {
        var kart = new GrupOlusturKarti(api, arkadaslar, avatarlar);
        var m = ModalAc(sahip, kart);
        kart.Tamamlandi += g => { m.Kapat(); bitti?.Invoke(g); };
        kart.Odaklan();
        return kart;
    }
}

// "Grup ayarları": ikon, ad, açıklama, renk, üyeler (ekle / çıkar), gruptan ayrıl.
public class GrupAyarKarti : ModalKarti
{
    private readonly Api _api;
    private readonly long _benId;
    private readonly List<Profil> _arkadaslar;
    private readonly AvatarOnbellek? _avatarlar;
    private readonly IkonOnbellek? _ikonlar;
    private Grup _grup;
    private bool _ekleModu;

    private readonly IkonDuzenleyici _ikon = new() { Yuvarlak = true };
    private readonly GirisKutusu _ad = new() { Etiket = "Grup adı", MaksUzunluk = Kurallar.GrupAdMax };
    private readonly GirisKutusu _aciklama = new() { Etiket = "Açıklama", MaksUzunluk = Kurallar.SunucuAciklamaMax, CokSatir = true, IpucuYazisi = "Bu grup ne için?" };
    private readonly Label _renkEtiket = EtiketYap("GRUP RENGİ");
    private readonly RenkSecici _renk = new();
    private readonly Label _uyeEtiket = EtiketYap("ÜYELER");
    private readonly SatirListe _uyeler = new() { SatirYuksekligi = Tema.S(56) };
    private readonly GirisKutusu _ekleAra = new() { IpucuYazisi = "Eklenecek arkadaşı ara" };
    private readonly ArkadasSecimListesi _ekleListe = new() { Coklu = false, BosYazi = "Eklenebilecek başka arkadaşın yok." };
    private readonly Dugme _ekle = new() { Text = "Üye ekle", Tur = Dugme.Turler.Ikincil };
    private Rectangle _listeCerceve;

    public event Action<Grup>? Tamamlandi;   // Kaydet'e basıldı (kart kapanır)
    public event Action<Grup>? Guncellendi;  // üye eklendi/çıkarıldı (kart açık kalır)
    public event Action? Ayrildi;            // gruptan ayrıldın (kart kapanır)

    private bool Kurucu => _grup.KurucuId == _benId;

    private int _sagX, _sagW;

    public GrupAyarKarti(Api api, Grup grup, long benId, IEnumerable<Profil> arkadaslar, AvatarOnbellek? avatarlar, IkonOnbellek? ikonlar) : base(Tema.S(740))
    {
        _api = api;
        _grup = grup;
        _benId = benId;
        _arkadaslar = arkadaslar.ToList();
        _avatarlar = avatarlar;
        _ikonlar = ikonlar;
        Baslik = "Grup ayarları";
        Birincil.Text = "Kaydet";
        Vazgec.Text = "Kapat";
        SolDugme.Text = "Gruptan ayrıl";
        SolDugme.Visible = true;
        Height = Tema.S(470);

        int solW = Tema.S(370), x = Ic, y = UstH;
        _ikon.Bounds = new Rectangle(x, y, solW, Tema.S(76)); y = _ikon.Bottom + Tema.S(12);
        _ad.Bounds = new Rectangle(x, y, solW, _ad.Height); y = _ad.Bottom + Tema.S(10);
        _aciklama.Height = Tema.S(24) + Tema.S(60);
        _aciklama.Bounds = new Rectangle(x, y, solW, _aciklama.Height); y = _aciklama.Bottom + Tema.S(12);
        _renkEtiket.Location = new Point(x - Tema.S(3), y); y = _renkEtiket.Bottom + Tema.S(6);
        _renk.Bounds = new Rectangle(x, y, solW, Tema.S(36));

        _sagX = x + solW + Tema.S(28);
        _sagW = Width - _sagX - Ic;
        _uyeEtiket.Location = new Point(_sagX - Tema.S(3), UstH);
        _ekle.Bounds = new Rectangle(_sagX, Height - AltH - Tema.S(20) - Tema.S(38), _sagW, Tema.S(38));
        _ekleAra.Visible = _ekleListe.Visible = false;
        _ekleListe.Avatarlar = avatarlar;
        _uyeler.BackColor = Tema.Giris;
        _uyeler.AvatarKaynagi = avatarlar;
        ListeYerlestir();

        Controls.AddRange([_ikon, _ad, _aciklama, _renkEtiket, _renk, _uyeEtiket, _uyeler, _ekleAra, _ekleListe, _ekle]);

        _ad.TextChanged += (_, _) => _ikon.Ad = _ad.Text;
        _renk.Degisti += r => _ikon.Renk = r;
        _ikon.Hata += m => Hata(m);
        _ikon.Degisti += () => Hata(null);
        _ekle.Click += (_, _) => { _ekleModu = !_ekleModu; ModGuncelle(); };
        _ekleAra.TextChanged += (_, _) => _ekleListe.Filtre = _ekleAra.Text;
        _ekleListe.Tikla += async p => await UyeEkle(p);
        _uyeler.Eylem += async (e, ad, _) => { if (ad == "cikar" && e is Profil p) await UyeCikar(p); };
        Birincil.Click += async (_, _) => await Kaydet();
        SolDugme.Click += async (_, _) => await Ayril();

        Yukle(grup);
    }

    private void Yukle(Grup g)
    {
        _grup = g;
        _ad.Text = g.Ad;
        _aciklama.Text = g.Aciklama;
        _renk.Secili = g.Renk;
        _ikon.Ad = g.Ad;
        _ikon.Renk = g.Renk;
        _ikon.Sifirla(_ikonlar?.Grup(g.Id, g.IkonSurum));
        Listele();
    }

    private void Listele()
    {
        var tr = StringComparer.Create(Tema.Turkce, true);
        _uyeEtiket.Text = $"ÜYELER — {_grup.Uyeler.Count} / {Kurallar.GrupUyeMax}";
        _uyeler.Ayarla(_grup.Uyeler.OrderBy(p => p.Id == _grup.KurucuId ? 0 : 1).ThenBy(p => p.GorunenAd, tr).Select(p =>
        {
            var rozet = new List<(string, Color)>();
            if (p.Id == _grup.KurucuId) rozet.Add(("KURUCU", ColorTranslator.FromHtml("#F5B544")));
            if (p.Id == _benId) rozet.Add(("SEN", Tema.MetinIkincil));
            var d = new List<SatirListe.Dugme>();
            if (Kurucu && p.Id != _benId) d.Add(new("cikar", "Çıkar", true));
            return new SatirListe.Oge
            {
                Etiket = p, Baslik = p.GorunenAd, Alt = "@" + p.KullaniciAdi, Rozetler = rozet, Dugmeler = d,
                Sol = (g, r, z) => Cizim.Avatar(g, r, p, _avatarlar?.Al(p), z, false),
            };
        }).ToList());
        var ids = _grup.Uyeler.Select(u => u.Id).ToHashSet();
        _ekleListe.Ayarla(_arkadaslar.Where(a => !ids.Contains(a.Id)));
        _ekle.Enabled = _ekleModu || _grup.Uyeler.Count < Kurallar.GrupUyeMax;
        ModGuncelle();
    }

    // Üye modunda liste çerçevesi tüm sütunu kaplar; ekleme modunda üstte arama kutusu, altında çerçeveli liste olur.
    private void ListeYerlestir()
    {
        int ly = _uyeEtiket.Bottom + Tema.S(8);
        int ust = ly;
        if (_ekleModu)
        {
            _ekleAra.Bounds = new Rectangle(_sagX, ly, _sagW, _ekleAra.Height);
            ust = _ekleAra.Bottom + Tema.S(10);
        }
        _listeCerceve = new Rectangle(_sagX, ust, _sagW, _ekle.Top - Tema.S(10) - ust);
        var ic = Rectangle.Inflate(_listeCerceve, -Tema.S(3), -Tema.S(3));
        _uyeler.Bounds = ic;
        _ekleListe.Bounds = ic;
    }

    private void ModGuncelle()
    {
        ListeYerlestir();
        _uyeler.Visible = !_ekleModu;
        _ekleAra.Visible = _ekleListe.Visible = _ekleModu;
        _ekle.Text = _ekleModu ? "Üye listesine dön" : _grup.Uyeler.Count >= Kurallar.GrupUyeMax ? "Grup dolu" : "Üye ekle";
        _ekle.Enabled = _ekleModu || _grup.Uyeler.Count < Kurallar.GrupUyeMax;
        if (_ekleModu) _ekleAra.Focus();
        Invalidate();
    }

    private async Task UyeEkle(Profil p)
    {
        Hata(null);
        if (_grup.Uyeler.Count >= Kurallar.GrupUyeMax) { Hata($"Bir grupta en fazla {Kurallar.GrupUyeMax} kişi olabilir."); return; }
        _ekleListe.Enabled = false;
        var s = await _api.GrubaEkle(_grup.Id, p.Id);
        _ekleListe.Enabled = true;
        if (!s.Basarili) { Hata(s.Hata); return; }
        _grup = s.Veri!;
        _ekleModu = false;
        Listele();
        Guncellendi?.Invoke(_grup);
    }

    private async Task UyeCikar(Profil p)
    {
        Hata(null);
        if (FindForm() is not { } f) return;
        if (!await OnayKarti.Sor(f, $"{p.GorunenAd} gruptan çıkarılsın mı?", "Gruptaki eski mesajları göremez, sonradan tekrar eklenebilir.", "Çıkar")) return;
        var s = await _api.GruptanCikar(_grup.Id, p.Id);
        if (!s.Basarili) { Hata(s.Hata); return; }
        _grup = _grup with { Uyeler = _grup.Uyeler.Where(u => u.Id != p.Id).ToList() };
        Listele();
        Guncellendi?.Invoke(_grup);
    }

    private async Task Ayril()
    {
        Hata(null);
        if (FindForm() is not { } f) return;
        if (!await OnayKarti.Sor(f, "Gruptan ayrılsın mı?", "Ayrılınca bu gruptaki sohbeti göremezsin. Biri seni tekrar ekleyebilir.", "Ayrıl")) return;
        var s = await _api.GruptanCikar(_grup.Id, _benId);
        if (!s.Basarili) { Hata(s.Hata); return; }
        _ikonlar?.Unut("grup", _grup.Id);
        Ayrildi?.Invoke();
    }

    private async Task Kaydet()
    {
        Hata(null);
        var ad = _ad.Text.Trim();
        if (ad.Length == 0) { _ad.Hata = "Grup adı boş olamaz."; _ad.Focus(); return; }
        var kilitler = new Control[] { _ikon, _ad, _aciklama, _renk, _ekle, _uyeler };
        Mesgul(true, "Kaydet", "Kaydediliyor…", kilitler);
        Grup? son = null;
        if (_ikon.Kaldirildi && _grup.IkonSurum != 0)
        {
            var s = await _api.GrupIkonSil(_grup.Id);
            if (!s.Basarili) { Mesgul(false, "Kaydet", ""); Hata(s.Hata); return; }
            _ikonlar?.Koy("grup", _grup.Id, 0, null);
            son = s.Veri;
        }
        if (_ikon.YeniResim is { } png)
        {
            var s = await _api.GrupIkonYukle(_grup.Id, png);
            if (!s.Basarili) { Mesgul(false, "Kaydet", ""); Hata(s.Hata); return; }
            _ikonlar?.Koy("grup", _grup.Id, s.Veri!.IkonSurum, Cizim.ResimCoz(png));
            son = s.Veri;
        }
        var g = await _api.GrupGuncelle(_grup.Id, new GrupAyari(ad, _aciklama.Text.Trim(), _renk.Secili));
        if (!g.Basarili) { Mesgul(false, "Kaydet", ""); Hata(g.Hata); return; }
        Tamamlandi?.Invoke(g.Veri ?? son ?? _grup);
    }

    public void Odaklan() => _ad.Focus();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        CerceveCiz(e.Graphics, _listeCerceve);
    }

    public static GrupAyarKarti Goster(Form sahip, Api api, Grup grup, long benId, IEnumerable<Profil> arkadaslar,
        AvatarOnbellek? avatarlar, IkonOnbellek? ikonlar, Action<Grup>? guncellendi = null, Action? ayrildi = null)
    {
        var kart = new GrupAyarKarti(api, grup, benId, arkadaslar, avatarlar, ikonlar);
        var m = ModalAc(sahip, kart);
        kart.Tamamlandi += g => { m.Kapat(); guncellendi?.Invoke(g); };
        kart.Guncellendi += g => guncellendi?.Invoke(g);
        kart.Ayrildi += () => { m.Kapat(); ayrildi?.Invoke(); };
        kart.Odaklan();
        return kart;
    }
}
