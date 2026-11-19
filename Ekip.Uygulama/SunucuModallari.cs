using Ekip.Ortak;

namespace Ekip.Uygulama;

// "+" düğmesine basınca açılan modal kart: "Sunucu oluştur" ve "Kodla katıl" sekmeleri.
// Kullanım: SunucuModallari.Goster(form, api, ...) ya da kartı kendin kurup IcModal.Goster ile göster.
public class SunucuModallari : ModalKarti
{
    private readonly Api _api;
    private readonly SekmeSecimi _sekmeler = new("Sunucu oluştur", "Kodla katıl");
    private readonly Panel _olusturPaneli = new() { BackColor = Tema.Yuzey };
    private readonly Panel _katilPaneli = new() { BackColor = Tema.Yuzey };
    private readonly IkonDuzenleyici _ikon = new();
    private readonly GirisKutusu _ad = new() { Etiket = "Sunucu adı", MaksUzunluk = Kurallar.SunucuAdMax, IpucuYazisi = "ör. Akşam Oyun Ekibi" };
    private readonly Label _renkEtiket = EtiketYap("SUNUCU RENGİ");
    private readonly RenkSecici _renk = new();
    private readonly GirisKutusu _kod = new() { Etiket = "Davet kodu", MaksUzunluk = 24, IpucuYazisi = "ABCD-EFGH" };
    private readonly Label _kodIpucu = new() { AutoSize = false, Font = Tema.Yazi(9.5f), ForeColor = Tema.MetinSoluk, BackColor = Tema.Yuzey };
    private bool _duzeltiyor;

    public IkonOnbellek? Ikonlar { get; set; }
    public event Action<Sunucu>? Tamamlandi;
    public event Action<string, bool>? Bildir;

    private int PanelY => Tema.S(116);

    public SunucuModallari(Api api, bool katilSekmesi = false, string? varsayilanAd = null) : base(Tema.S(480))
    {
        _api = api;
        Baslik = "Sunucu ekle";
        int w = Width - Ic * 2;
        int px = Ic - 1; // paneller çerçeve çizgisini örtmesin diye 1px içeride; çocuklar buna göre kayar

        _sekmeler.Bounds = new Rectangle(Ic, Tema.S(62), w, Tema.S(40));

        _ikon.Bounds = new Rectangle(px, 0, w, Tema.S(76));
        _ad.Bounds = new Rectangle(px, Tema.S(92), w, _ad.Height);
        _renkEtiket.Location = new Point(px - Tema.S(3), Tema.S(92) + _ad.Height + Tema.S(14));
        _renk.Bounds = new Rectangle(px, _renkEtiket.Bottom + Tema.S(6), w, Tema.S(36));
        _olusturPaneli.Controls.AddRange([_ikon, _ad, _renkEtiket, _renk]);
        _olusturPaneli.Size = new Size(Width - 2, _renk.Bottom + Tema.S(20));

        _kod.IcKutu.Font = new Font("Consolas", 13f, FontStyle.Bold);
        _kod.IcKutu.CharacterCasing = CharacterCasing.Upper;
        _kod.Bounds = new Rectangle(px, 0, w, _kod.Height);
        _kodIpucu.Text = "Arkadaşın sana ABCD-EFGH gibi bir kod verdiyse buraya yaz. Küçük harf ve boşluk sorun olmaz, kendimiz düzeltiriz.";
        _kodIpucu.Bounds = new Rectangle(px, _kod.Bottom + Tema.S(12), w, TextRenderer.MeasureText(_kodIpucu.Text, _kodIpucu.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + Tema.S(4));
        _katilPaneli.Controls.AddRange([_kod, _kodIpucu]);
        _katilPaneli.Size = new Size(Width - 2, _kodIpucu.Bottom + Tema.S(20));

        _olusturPaneli.Location = _katilPaneli.Location = new Point(1, PanelY);
        Controls.AddRange([_sekmeler, _olusturPaneli, _katilPaneli]);

        _ad.Text = varsayilanAd ?? "";
        _ikon.Ad = _ad.Text;
        _ikon.Renk = _renk.Secili;
        _ad.TextChanged += (_, _) => _ikon.Ad = _ad.Text;
        _renk.Degisti += r => _ikon.Renk = r;
        _ikon.Hata += m => Hata(m);
        _ikon.Degisti += () => Hata(null);
        _sekmeler.Degisti += _ => SekmeGoster();
        _kod.TextChanged += (_, _) => KodDuzelt();
        Birincil.Click += async (_, _) => await Calistir();
        _ad.KeyDown += (_, e) => EnterBas(e);
        _kod.KeyDown += (_, e) => EnterBas(e);

        _sekmeler.Secili = katilSekmesi ? 1 : 0;
        SekmeGoster();
    }

    private void EnterBas(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        Birincil.PerformClick();
    }

    private bool KatilSekmesi => _sekmeler.Secili == 1;

    private void SekmeGoster()
    {
        Hata(null);
        _olusturPaneli.Visible = !KatilSekmesi;
        _katilPaneli.Visible = KatilSekmesi;
        Birincil.Text = KatilSekmesi ? "Katıl" : "Oluştur";
        Height = PanelY + (KatilSekmesi ? _katilPaneli.Height : _olusturPaneli.Height) + AltH;
        PerformLayout();
        (Parent as Control)?.PerformLayout(); // IcModal kartı yeniden ortalasın
        Invalidate();
    }

    // Yazarken "abcd efgh" -> "ABCD-EFGH".
    private void KodDuzelt()
    {
        if (_duzeltiyor) return;
        var ham = new string(_kod.Text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (ham.Length > 8) ham = ham[..8];
        var bicim = ham.Length > 4 ? ham[..4] + "-" + ham[4..] : ham;
        if (bicim == _kod.Text) return;
        _duzeltiyor = true;
        _kod.Text = bicim;
        _kod.IcKutu.SelectionStart = bicim.Length;
        _duzeltiyor = false;
    }

    public void Odaklan() { if (KatilSekmesi) _kod.Focus(); else _ad.Focus(); }

    private async Task Calistir()
    {
        Hata(null);
        if (KatilSekmesi) await Katil(); else await Olustur();
    }

    private async Task Olustur()
    {
        var ad = _ad.Text.Trim();
        if (ad.Length == 0) { _ad.Hata = "Sunucu adı boş olamaz."; _ad.Focus(); return; }
        Mesgul(true, "Oluştur", "Oluşturuluyor…", _sekmeler, _ikon, _ad, _renk);
        var s = await _api.SunucuOlustur(new SunucuAyari(ad, "", _renk.Secili, false));
        if (!s.Basarili) { Mesgul(false, "Oluştur", ""); Hata(s.Hata); return; }
        var sunucu = s.Veri!;
        if (_ikon.YeniResim is { } png)
        {
            var i = await _api.SunucuIkonYukle(sunucu.Id, png);
            if (i.Basarili)
            {
                sunucu = i.Veri!;
                Ikonlar?.Koy("sunucu", sunucu.Id, sunucu.IkonSurum, Cizim.ResimCoz(png)); // Tekrar indirmeye gerek yok.
            }
            else Bildir?.Invoke("Sunucu oluşturuldu ama ikonu yüklenemedi: " + i.Hata, true);
        }
        Tamamlandi?.Invoke(sunucu);
    }

    private async Task Katil()
    {
        var kod = _kod.Text.Trim();
        if (kod.Replace("-", "").Length < 8) { _kod.Hata = "Kod 8 karakter olmalı."; _kod.Focus(); return; }
        Mesgul(true, "Katıl", "Katılıyor…", _sekmeler, _kod);
        var s = await _api.KodlaKatil(kod);
        if (!s.Basarili) { Mesgul(false, "Katıl", ""); Hata(s.Hata); return; }
        Tamamlandi?.Invoke(s.Veri!);
    }

    // Kartı form içinde gösterir; tamamlanınca ya da kapatılınca modal kapanır. Tamamlandi'ya sonra abone olabilirsin.
    public static SunucuModallari Goster(Form sahip, Api api, IkonOnbellek? ikonlar = null, bool katilSekmesi = false, string? varsayilanAd = null)
    {
        var kart = new SunucuModallari(api, katilSekmesi, varsayilanAd) { Ikonlar = ikonlar };
        var m = ModalAc(sahip, kart);
        kart.Tamamlandi += _ => m.Kapat();
        kart.Odaklan();
        return kart;
    }
}

// Kanal adı yazarken: küçük harf, boşluk -> tire, geçersiz karakterler atılır.
public static class KanalAdi
{
    public static string Duzelt(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var kucuk = Tema.Turkce.TextInfo.ToLower(s.TrimStart());
        var sb = new System.Text.StringBuilder();
        foreach (var c in kucuk)
        {
            if (c == ' ') { if (sb.Length > 0 && sb[^1] != '-') sb.Append('-'); }
            else if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
        }
        return sb.Length > Kurallar.KanalAdMax ? sb.ToString(0, Kurallar.KanalAdMax) : sb.ToString();
    }
}

// Kanal oluştur / düzenle kartı (sunucu ayarlarındaki "Kanallar" sekmesi ve kanal listesindeki "+" için).
public class KanalKarti : ModalKarti
{
    private readonly Api _api;
    private readonly long _sunucuId;
    private readonly Kanal? _kanal;
    private readonly GirisKutusu _ad = new() { Etiket = "Kanal adı", MaksUzunluk = Kurallar.KanalAdMax, IpucuYazisi = "ör. oyun-sohbeti" };
    private readonly GirisKutusu _konu = new() { Etiket = "Konu (isteğe bağlı)", MaksUzunluk = Kurallar.KanalKonuMax, IpucuYazisi = "Bu kanal ne için?" };
    private bool _duzeltiyor;

    public event Action<Kanal>? Tamamlandi;

    public KanalKarti(Api api, long sunucuId, Kanal? duzenlenen = null) : base(Tema.S(440))
    {
        _api = api;
        _sunucuId = sunucuId;
        _kanal = duzenlenen;
        Baslik = duzenlenen is null ? "Kanal oluştur" : "Kanalı düzenle";
        Birincil.Text = duzenlenen is null ? "Oluştur" : "Kaydet";
        int w = Width - Ic * 2, y = UstH;
        _ad.Bounds = new Rectangle(Ic, y, w, _ad.Height); y = _ad.Bottom + Tema.S(12);
        _konu.Bounds = new Rectangle(Ic, y, w, _konu.Height); y = _konu.Bottom + Tema.S(24);
        Height = y + AltH;
        Controls.AddRange([_ad, _konu]);

        if (duzenlenen != null) { _ad.Text = duzenlenen.Ad; _konu.Text = duzenlenen.Konu; }
        _ad.TextChanged += (_, _) =>
        {
            if (_duzeltiyor) return;
            var d = KanalAdi.Duzelt(_ad.Text);
            if (d == _ad.Text) return;
            _duzeltiyor = true;
            _ad.Text = d;
            _ad.IcKutu.SelectionStart = d.Length;
            _duzeltiyor = false;
        };
        _ad.KeyDown += (_, e) => EnterBas(e);
        _konu.KeyDown += (_, e) => EnterBas(e);
        Birincil.Click += async (_, _) => await Kaydet();
    }

    private void EnterBas(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        Birincil.PerformClick();
    }

    public void Odaklan() => _ad.Focus();

    private async Task Kaydet()
    {
        Hata(null);
        var ad = KanalAdi.Duzelt(_ad.Text).Trim('-');
        if (Kurallar.KanalAdKontrol(ad) is { } h) { _ad.Hata = h; _ad.Focus(); return; }
        var ayar = new KanalAyari(ad, _konu.Text.Trim());
        string normal = _kanal is null ? "Oluştur" : "Kaydet";
        Mesgul(true, normal, "Kaydediliyor…", _ad, _konu);
        var s = _kanal is null ? await _api.KanalOlustur(_sunucuId, ayar) : await _api.KanalGuncelle(_kanal.Id, ayar);
        if (!s.Basarili) { Mesgul(false, normal, ""); Hata(s.Hata); return; }
        Tamamlandi?.Invoke(s.Veri!);
    }

    public static KanalKarti Goster(Form sahip, Api api, long sunucuId, Kanal? duzenlenen, Action<Kanal>? bitti = null)
    {
        var kart = new KanalKarti(api, sunucuId, duzenlenen);
        var m = ModalAc(sahip, kart);
        kart.Tamamlandi += k => { m.Kapat(); bitti?.Invoke(k); };
        kart.Odaklan();
        return kart;
    }
}

// Sunucuyu silme onayı: adı yazmadan silinmez.
public class SunucuSilKarti : ModalKarti
{
    private readonly Api _api;
    private readonly Sunucu _sunucu;
    private readonly GirisKutusu _ad = new() { Etiket = "Onaylamak için sunucu adını yaz" };

    public event Action? Silindi;

    public SunucuSilKarti(Api api, Sunucu sunucu) : base(Tema.S(460))
    {
        _api = api;
        _sunucu = sunucu;
        Baslik = $"{Kisalt(sunucu.Ad)} silinsin mi?";
        Birincil.Text = "Sunucuyu sil";
        Birincil.Tur = Dugme.Turler.Tehlike;
        Birincil.Enabled = false;
        int w = Width - Ic * 2;
        var metin = new Label
        {
            AutoSize = false, Font = Tema.Yazi(10), ForeColor = Tema.MetinIkincil, BackColor = Tema.Yuzey,
            Text = "Bu sunucudaki tüm kanallar, mesajlar ve üyelik bilgileri kalıcı olarak silinir. Bu işlem geri alınamaz.",
        };
        metin.Bounds = new Rectangle(Ic, UstH - Tema.S(8), w, TextRenderer.MeasureText(metin.Text, metin.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + Tema.S(4));
        _ad.Bounds = new Rectangle(Ic, metin.Bottom + Tema.S(16), w, _ad.Height);
        _ad.IpucuYazisi = sunucu.Ad;
        Controls.AddRange([metin, _ad]);
        Height = _ad.Bottom + Tema.S(24) + AltH;

        _ad.TextChanged += (_, _) => Birincil.Enabled = string.Equals(_ad.Text.Trim(), _sunucu.Ad.Trim(), StringComparison.CurrentCultureIgnoreCase);
        _ad.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (Birincil.Enabled) Birincil.PerformClick(); } };
        Birincil.Click += async (_, _) => await Sil();
    }

    private static string Kisalt(string s) => s.Length > 24 ? s[..24] + "…" : s;

    public void Odaklan() => _ad.Focus();

    private async Task Sil()
    {
        Hata(null);
        Mesgul(true, "Sunucuyu sil", "Siliniyor…", _ad);
        var s = await _api.SunucuSil(_sunucu.Id);
        if (!s.Basarili) { Mesgul(false, "Sunucuyu sil", ""); Birincil.Enabled = true; Hata(s.Hata); return; }
        Silindi?.Invoke();
    }

    public static SunucuSilKarti Goster(Form sahip, Api api, Sunucu sunucu, Action? silindi = null)
    {
        var kart = new SunucuSilKarti(api, sunucu);
        var m = ModalAc(sahip, kart);
        kart.Silindi += () => { m.Kapat(); silindi?.Invoke(); };
        kart.Odaklan();
        return kart;
    }
}
