using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

public class GirisFormu : TemaliForm
{
    private readonly Ayarlar _ayarlar;
    private bool _kayitModu, _mesgul;

    private readonly MarkaPaneli _marka = new();
    private readonly Panel _sag = new() { BackColor = Tema.Arkaplan };
    private readonly Panel _kart = new() { BackColor = Tema.Arkaplan };
    private readonly Label _baslik = YaziEtiketi(Tema.Yazi(20, kalin: true), Tema.Metin);
    private readonly Label _altBaslik = YaziEtiketi(Tema.Yazi(10.5f), Tema.MetinIkincil);
    private readonly GirisKutusu _kullaniciAdi = new() { Etiket = "Kullanıcı adı", MaksUzunluk = Kurallar.KullaniciAdiMax };
    private readonly GirisKutusu _gorunenAd = new() { Etiket = "Görünen ad", MaksUzunluk = Kurallar.GorunenAdMax, IpucuYazisi = "Arkadaşların seni böyle görecek" };
    private readonly GirisKutusu _sifre = new() { Etiket = "Şifre", Sifre = true, MaksUzunluk = Kurallar.SifreMax };
    private readonly GirisKutusu _davetKodu = new() { Etiket = "Davet kodu", MaksUzunluk = 12, IpucuYazisi = "ör. K7M3-Q9XR (admin'den al)" };
    private readonly Label _genelHata = YaziEtiketi(Tema.Yazi(9.5f), Tema.Hata);
    private readonly Dugme _gonder = new();
    private readonly Label _modSoru = YaziEtiketi(Tema.Yazi(9.5f), Tema.MetinIkincil);
    private readonly BaglantiYazi _modDegistir = new();
    private readonly BaglantiYazi _sunucuAyari = new() { ForeColor = Tema.MetinSoluk };

    public event Action<Api, Profil>? GirisYapildi;

    public GirisFormu(Ayarlar ayarlar)
    {
        _ayarlar = ayarlar;
        Text = Tema.UygulamaAdi;
        ClientSize = new Size(Tema.S(980), Tema.S(620));
        MinimumSize = new Size(Tema.S(480), Tema.S(600));

        _kart.Controls.AddRange([_baslik, _altBaslik, _davetKodu, _kullaniciAdi, _gorunenAd, _sifre, _genelHata, _gonder, _modSoru, _modDegistir]);
        _sag.Controls.AddRange([_kart, _sunucuAyari]);
        Controls.AddRange([_sag, _marka]);

        _genelHata.AutoSize = false;
        _genelHata.TextAlign = ContentAlignment.MiddleLeft;
        _kullaniciAdi.Text = _ayarlar.SonKullaniciAdi ?? "";

        _gonder.Click += async (_, _) => await Gonder();
        _modDegistir.Click += (_, _) => ModAyarla(!_kayitModu);
        _sunucuAyari.Click += (_, _) => SunucuDegistir();
        AcceptButton = _gonder;

        ModAyarla(false);
        SunucuYazisiniGuncelle();
    }

    private static Label YaziEtiketi(Font f, Color c) => new() { AutoSize = true, Font = f, ForeColor = c, BackColor = Tema.Arkaplan };

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Kayıtlı oturum varsa şifre sormadan devam et.
        var token = _ayarlar.Token;
        if (token is null) { IlkKutuyaOdaklan(); return; }

        Mesgul(true, "Oturum açılıyor…");
        var api = new Api(_ayarlar.SunucuAdresi) { Token = token };
        var sonuc = await api.Ben();
        // İnternet/sunucu bir anlık cevap vermediyse oturum geçersiz değildir: giriş ekranına düşmeden tekrar dene.
        for (int deneme = 1; !sonuc.Basarili && sonuc.Durum is null && deneme <= 4 && !IsDisposed; deneme++)
        {
            Mesgul(true, $"Sunucuya bağlanılıyor… ({deneme + 1}. deneme)");
            await Task.Delay(TimeSpan.FromSeconds(deneme * 2));
            sonuc = await api.Ben();
        }
        if (IsDisposed) return;
        if (sonuc.Basarili) { GirisYapildi?.Invoke(api, sonuc.Veri!); return; }

        Mesgul(false);
        if (sonuc.Durum == System.Net.HttpStatusCode.Unauthorized) { _ayarlar.Token = null; _ayarlar.Kaydet(); }
        _genelHata.Text = sonuc.Hata;
        IlkKutuyaOdaklan();
    }

    private void IlkKutuyaOdaklan()
    {
        if (_kayitModu || string.IsNullOrEmpty(_kullaniciAdi.Text)) _kullaniciAdi.Focus(); else _sifre.Focus();
    }

    private void ModAyarla(bool kayit)
    {
        _kayitModu = kayit;
        _baslik.Text = kayit ? "Hesap oluştur" : "Tekrar hoş geldin!";
        _altBaslik.Text = kayit ? "Birkaç saniye sürer, sonra haneye katıl." : "Hane seni bekliyor.";
        _gonder.Text = kayit ? "Kayıt ol" : "Giriş yap";
        _modSoru.Text = kayit ? "Zaten hesabın var mı?" : "Hesabın yok mu?";
        _modDegistir.Text = kayit ? "Giriş yap" : "Kayıt ol";
        _gorunenAd.Visible = kayit;
        _davetKodu.Visible = kayit;
        _davetKodu.Hata = null;
        _sifre.IpucuYazisi = kayit ? $"En az {Kurallar.SifreMin} karakter" : "";
        _kullaniciAdi.Hata = _gorunenAd.Hata = _sifre.Hata = null;
        _genelHata.Text = "";
        PerformLayout();
        if (IsHandleCreated) IlkKutuyaOdaklan();
    }

    private async Task Gonder()
    {
        if (_mesgul) return;
        _genelHata.Text = "";

        var ad = _kullaniciAdi.Text.Trim();
        var sifre = _sifre.Text;
        bool gecerli = true;

        if (_kayitModu)
        {
            gecerli &= Isaretle(_kullaniciAdi, Kurallar.KullaniciAdiKontrol(ad));
            gecerli &= Isaretle(_gorunenAd, Kurallar.GorunenAdKontrol(_gorunenAd.Text));
            gecerli &= Isaretle(_sifre, Kurallar.SifreKontrol(sifre));
        }
        else
        {
            gecerli &= Isaretle(_kullaniciAdi, ad.Length == 0 ? "Boş olamaz" : null);
            gecerli &= Isaretle(_sifre, sifre.Length == 0 ? "Boş olamaz" : null);
        }
        if (!gecerli) return;

        Mesgul(true, _kayitModu ? "Hesap oluşturuluyor…" : "Giriş yapılıyor…");
        var api = new Api(_ayarlar.SunucuAdresi);
        var sonuc = _kayitModu
            ? await api.KayitOl(new KayitIstegi(ad, _gorunenAd.Text.Trim(), sifre, _davetKodu.Text.Trim()))
            : await api.GirisYap(new GirisIstegi(ad, sifre));

        if (!sonuc.Basarili)
        {
            Mesgul(false);
            if (sonuc.Alan == "kod") { _davetKodu.Hata = sonuc.Hata; _davetKodu.Focus(); }
            else if (sonuc.Durum == System.Net.HttpStatusCode.Conflict) { _kullaniciAdi.Hata = sonuc.Hata; _kullaniciAdi.Focus(); }
            else { _genelHata.Text = sonuc.Hata; _sifre.Focus(); }
            return;
        }

        _ayarlar.Token = sonuc.Veri!.Token;
        _ayarlar.SonKullaniciAdi = sonuc.Veri.Kullanici.KullaniciAdi;
        _ayarlar.Kaydet();
        api.Token = sonuc.Veri.Token;

        var profil = await api.Ben();
        if (!profil.Basarili) { Mesgul(false); _genelHata.Text = profil.Hata; return; }
        GirisYapildi?.Invoke(api, profil.Veri!);
    }

    private static bool Isaretle(GirisKutusu k, string? hata) { k.Hata = hata; return hata is null; }

    private void Mesgul(bool mesgul, string? yazi = null)
    {
        _mesgul = mesgul;
        foreach (Control c in new Control[] { _davetKodu, _kullaniciAdi, _gorunenAd, _sifre, _gonder, _modDegistir, _sunucuAyari })
            c.Enabled = !mesgul;
        _gonder.Text = mesgul ? yazi! : _kayitModu ? "Kayıt ol" : "Giriş yap";
        UseWaitCursor = mesgul;
    }

    // Sıfırlanmak üzere: çıkış yapınca form tekrar kullanılır.
    public void Sifirla(string? mesaj = null)
    {
        Mesgul(false);
        _sifre.Text = "";
        _kullaniciAdi.Text = _ayarlar.SonKullaniciAdi ?? "";
        ModAyarla(false);
        _genelHata.Text = mesaj ?? "";
    }

    private void SunucuDegistir()
    {
        using var f = new SunucuFormu(_ayarlar.SunucuAdresi);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        _ayarlar.SunucuAdresi = f.Adres;
        _ayarlar.Token = null;
        _ayarlar.Kaydet();
        SunucuYazisiniGuncelle();
    }

    private void SunucuYazisiniGuncelle()
    {
        var uri = new Uri(_ayarlar.SunucuAdresi);
        // Varsayılan sunucuda teknik adres yerine marka adı (adres Cloudflare'daki eski adla kaldı).
        bool varsayilan = _ayarlar.SunucuAdresi.TrimEnd('/') == Ayarlar.VarsayilanSunucu;
        _sunucuAyari.Text = varsayilan ? $"Sunucu: {Tema.UygulamaAdi} bulutu" : $"Sunucu: {uri.Authority}";
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_kart is null) return;

        int w = ClientSize.Width, h = ClientSize.Height;
        bool markaGorunsun = w >= Tema.S(820);
        _marka.Visible = markaGorunsun;
        int markaGenislik = markaGorunsun ? (int)(w * 0.42) : 0;
        _marka.Bounds = new Rectangle(0, 0, markaGenislik, h);
        _sag.Bounds = new Rectangle(markaGenislik, 0, w - markaGenislik, h);

        int kartGenislik = Math.Min(Tema.S(380), _sag.Width - Tema.S(48));
        // Label'lar metnin soluna kendi boşluğunu ekliyor; kutularla aynı hizaya çekmek için sola kaydırıyoruz.
        int y = 0;
        _baslik.Location = new Point(-Tema.S(7), y); y += _baslik.PreferredHeight + Tema.S(4);
        _altBaslik.Location = new Point(-Tema.S(4), y); y += _altBaslik.PreferredHeight + Tema.S(22);

        foreach (var k in new[] { _davetKodu, _kullaniciAdi, _gorunenAd, _sifre })
        {
            if (!k.Visible) continue;
            k.Bounds = new Rectangle(0, y, kartGenislik, k.Height);
            y += k.Height + Tema.S(14);
        }

        _genelHata.Bounds = new Rectangle(0, y, kartGenislik, Tema.S(28)); y += Tema.S(32);
        _gonder.Bounds = new Rectangle(0, y, kartGenislik, Tema.S(46)); y += _gonder.Height + Tema.S(18);
        _modSoru.Location = new Point(-Tema.S(4), y);
        _modDegistir.Location = new Point(_modSoru.Right - Tema.S(4), y);
        y += _modSoru.PreferredHeight;

        _kart.Size = new Size(kartGenislik, y);
        _kart.Location = new Point((_sag.Width - kartGenislik) / 2, Math.Max(Tema.S(24), (h - y) / 2 - Tema.S(10)));

        _sunucuAyari.Location = new Point(_sag.Width - _sunucuAyari.PreferredWidth - Tema.S(18), h - _sunucuAyari.PreferredHeight - Tema.S(14));
    }
}

// Sol taraftaki tanıtım alanı. Sadece bir kere çizilir, hareket yok.
public class MarkaPaneli : Control
{
    public MarkaPaneli()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var arka = new LinearGradientBrush(r, ColorTranslator.FromHtml("#2A1E6E"), ColorTranslator.FromHtml("#120F24"), 60f))
            g.FillRectangle(arka, r);

        // Dekoratif daireler
        void Daire(float x, float y, float cap, int alfa)
        {
            using var b = new SolidBrush(Color.FromArgb(alfa, Tema.Vurgu));
            g.FillEllipse(b, x - cap / 2, y - cap / 2, cap, cap);
        }
        Daire(r.Width * 0.95f, r.Height * 0.12f, Tema.S(260), 38);
        Daire(r.Width * 0.08f, r.Height * 0.92f, Tema.S(320), 30);
        Daire(r.Width * 0.75f, r.Height * 0.78f, Tema.S(110), 45);

        int sol = Tema.S(48);

        // Logo
        var logo = new RectangleF(sol, Tema.S(48), Tema.S(44), Tema.S(44));
        Tema.Logo(g, logo);
        TextRenderer.DrawText(g, Tema.UygulamaAdi, Tema.Yazi(16, kalin: true), new Point((int)logo.Right + Tema.S(12), (int)logo.Y + Tema.S(6)), Color.White);

        // Slogan
        int y = (int)(r.Height * 0.42);
        var genislik = r.Width - sol * 2;
        var slogan = "Arkadaşlarınla,\noyunun tam içinde.";
        var buyuk = Tema.Yazi(24, kalin: true);
        var boyut = TextRenderer.MeasureText(g, slogan, buyuk, new Size(genislik, 0), TextFormatFlags.WordBreak);
        TextRenderer.DrawText(g, slogan, buyuk, new Rectangle(sol, y, genislik, boyut.Height), Color.White, TextFormatFlags.WordBreak);
        y += boyut.Height + Tema.S(12);

        TextRenderer.DrawText(g, "Mesajlaş, davet et, oyunun üstünde sohbet et.\nHafif, hızlı, sadece sizin için.",
            Tema.Yazi(10.5f), new Rectangle(sol, y, genislik, Tema.S(60)), ColorTranslator.FromHtml("#C9C2F2"), TextFormatFlags.WordBreak);
    }
}

public class SunucuFormu : TemaliForm
{
    private readonly GirisKutusu _adres = new() { Etiket = "Sunucu adresi", IpucuYazisi = "http://sunucu-adresi:5080" };
    public string Adres { get; private set; } = "";

    public SunucuFormu(string mevcut)
    {
        Text = "Sunucu ayarı";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(Tema.S(420), Tema.S(190));

        var aciklama = new Label
        {
            Text = "Arkadaşınla aynı sunucuya bağlanmanız gerekiyor.",
            AutoSize = true, ForeColor = Tema.MetinIkincil, BackColor = BackColor,
            Location = new Point(Tema.S(20), Tema.S(16)),
        };
        _adres.Text = mevcut;
        _adres.Bounds = new Rectangle(Tema.S(20), Tema.S(40), Tema.S(380), _adres.Height);

        var iptal = new Dugme { Text = "Vazgeç", Tur = Dugme.Turler.Ikincil, DialogResult = DialogResult.Cancel };
        var kaydet = new Dugme { Text = "Kaydet" };
        iptal.Bounds = new Rectangle(Tema.S(200), Tema.S(130), Tema.S(95), Tema.S(40));
        kaydet.Bounds = new Rectangle(Tema.S(305), Tema.S(130), Tema.S(95), Tema.S(40));
        iptal.Click += (_, _) => Close();
        kaydet.Click += (_, _) =>
        {
            var a = _adres.Text.Trim();
            if (!a.Contains("://")) a = "http://" + a;
            if (!Uri.TryCreate(a, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            {
                _adres.Hata = "Geçerli bir adres değil";
                return;
            }
            Adres = u.GetLeftPart(UriPartial.Authority);
            DialogResult = DialogResult.OK;
        };

        Controls.AddRange([aciklama, _adres, iptal, kaydet]);
        AcceptButton = kaydet;
        CancelButton = iptal;
    }
}
