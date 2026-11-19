using Ekip.Ortak;

namespace Ekip.Uygulama;

public static class Zaman
{
    public static DateTime Yerel(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    public static string Uzun(long ms)
    {
        var t = Yerel(ms);
        var gun = t.Date == DateTime.Today ? "Bugün" : t.Date == DateTime.Today.AddDays(-1) ? "Dün" : t.ToString("dd.MM.yyyy");
        return $"{gun} {t:HH:mm}";
    }

    public static string Kisa(long ms) => Yerel(ms).ToString("HH:mm");
    public static string Gun(DateTime g) => g.ToString("d MMMM yyyy", Tema.Turkce);
}

// Sohbet başlığında gösterilecek bilgiler. DM: Karsi (avatar + durum). Kanal: Ad + Alt (konu). Grup: Ad, Renk, Ikon, UyeSayisi.
public sealed class SohbetBasligi
{
    public string Ad { get; init; } = "";
    public string? Alt { get; init; }
    public Profil? Karsi { get; init; }
    public string Renk { get; init; } = "#7C5CFF";
    public Image? Ikon { get; init; }
    public int UyeSayisi { get; init; }

    public static SohbetBasligi Dm(Profil p) => new() { Ad = p.GorunenAd, Karsi = p };
    public static SohbetBasligi Kanal(string ad, string konu) => new() { Ad = ad, Alt = konu };
    public static SohbetBasligi Grup(string ad, string renk, Image? ikon, int uyeSayisi) =>
        new() { Ad = ad, Renk = renk, Ikon = ikon, UyeSayisi = uyeSayisi };
}

// Açık sohbet: üstte başlık, ortada mesajlar, altta yazma kutusu. DM, kanal, grup için aynı panel.
//
//   panel.Depo = depo; panel.Api = api; panel.Ben = ben; panel.Avatarlar = avatarlar;
//   panel.Uyeler = uyeProfilleri;                    // bahsetme önerisi ve gönderen çözümü (kanal/grup)
//   panel.Ac(new KonusmaKimligi(KonusmaTuru.Kanal, id), SohbetBasligi.Kanal("genel", "konu"));
//
// Depo verilmezse (parti sohbeti) Goster(...) ile elle veri verilir, Gonder olayı kullanılır.
public sealed class SohbetPaneli : Control
{
    private readonly MesajListesi _liste = new();
    private readonly YazmaKutusu _yazma = new();
    private SohbetDeposu? _depo;
    private KonusmaKimligi? _kimlik;
    private SohbetBasligi? _baslik;
    private SohbetVerisi? _veri;
    private Profil? _ben;
    private IReadOnlyList<Profil> _uyeler = [];
    private Dictionary<long, Profil> _uyeSozlugu = [];
    private readonly Dictionary<KonusmaKimligi, string> _taslaklar = [];
    private string _yaziyor = "";
    private string _ustHedef = "";
    private bool _basit, _uyeDugmesi, _uyeDugmesiAcik;

    // Depo yokken (parti) yazılan mesaj.
    public event Action<string>? Gonder;
    public event Action? EskiIste;
    public event Action? GrupAyarTik;
    public event Action? UyeDugmesiTik;
    public event Action<string>? BahsetmeTik;
    public event Action<string, bool>? Bildirim;

    public SohbetDeposu? Depo
    {
        get => _depo;
        set
        {
            if (_depo != null) { _depo.Degisti -= DepoDegisti; _depo.YaziyorDegisti -= YaziyorDegisti; }
            _depo = value;
            if (_depo != null) { _depo.Degisti += DepoDegisti; _depo.YaziyorDegisti += YaziyorDegisti; }
        }
    }

    public Api? Api { get => _liste.Api; set { _liste.Api = value; _yazma.Api = value; } }
    public AvatarOnbellek? Avatarlar { get => _liste.Avatarlar; set { _liste.Avatarlar = value; _yazma.Avatarlar = value; } }
    public Profil? Ben
    {
        get => _ben;
        set { _ben = value; if (value != null) _yazma.BenId = value.Id; UyeleriYenile(); Invalidate(true); }
    }
    public Func<long, Profil?>? Cozucu { get; set; }
    public string? Ipucu { get; set; }
    public bool BaslikGizli { get; set; }
    public bool BaskalarininMesajlariniSilebilir { get => _liste.BaskalarininMesajlariniSilebilir; set => _liste.BaskalarininMesajlariniSilebilir = value; }
    public KonusmaKimligi? Kimlik => _kimlik;
    public long? KarsiId => _kimlik is { Tur: KonusmaTuru.Dm } k ? k.Id : null;

    // Parti/overlay: yanıt, düzenleme, tepki, ek ve GIF yok.
    public bool BasitMod { get => _basit; set { _basit = value; _liste.BasitMod = value; _yazma.BasitMod = value; PerformLayout(); } }

    // Başlıkta üye listesi düğmesi (kanal/grup).
    public bool UyeDugmesi { get => _uyeDugmesi; set { _uyeDugmesi = value; Invalidate(); } }
    public bool UyeDugmesiAcik { get => _uyeDugmesiAcik; set { _uyeDugmesiAcik = value; Invalidate(); } }

    // Sohbetin üyeleri (kendim dahil olabilir). Bahsetme önerisi, @bahsetme çizimi ve gönderen adı için.
    public IReadOnlyList<Profil> Uyeler { get => _uyeler; set { _uyeler = value; UyeleriYenile(); } }

    private int BaslikH => BaslikGizli ? 0 : Tema.S(56);
    private int YaziyorH => Tema.S(24);

    public SohbetPaneli()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
        AllowDrop = _liste.AllowDrop = true;
        Controls.AddRange([_liste, _yazma]);
        _liste.Cozucu = Gonderen;

        _yazma.YukseklikDegisti += () => PerformLayout();
        _yazma.TekerlekIstendi += d => _liste.Kaydir(-d * Tema.S(60) / 120);
        _yazma.Bildirim += (m, h) => Bildirim?.Invoke(m, h);
        _yazma.Gonder += YazmaGonder;
        _yazma.DuzenlemeKaydet += async (id, metin) => await DuzenlemeKaydet(id, metin);
        _yazma.ModKapandi += () => _liste.Secili = null;
        _yazma.Yaziyor += () => { if (_depo != null && _kimlik is { } k) _depo.YaziyorBildir(k); };
        _yazma.GifSecildi += g =>
        {
            if (_depo is null || _kimlik is not { } k) return;
            var ek = new Ek("gif", string.IsNullOrWhiteSpace(g.Baslik) ? "gif" : g.Baslik, "image/gif", 0, g.Url, g.OnizlemeUrl, g.Genislik, g.Yukseklik);
            _depo.Gonder(k, "", _yazma.YanitId, null, [ek]);
            _yazma.ModuKapat();
            _liste.Secili = null;
            _liste.Alta();
        };
        _yazma.SonMesajiDuzenle += () =>
        {
            var m = _veri?.Liste.LastOrDefault(x => x.GonderenId == _ben?.Id && x.Id > 0 && !x.Silindi);
            if (m != null) DuzenlemeyeGir(m);
        };

        _liste.EskiIste += () => { if (_depo != null && _kimlik is { } k) _ = _depo.EskileriYukle(k); else EskiIste?.Invoke(); };
        _liste.YenidenYukleIstendi += () => { if (_depo != null && _kimlik is { } k) _ = _depo.Ac(k); };
        _liste.TekrarDene += m => { if (_depo != null && _kimlik is { } k && m.IstemciId != null) _ = _depo.TekrarDene(k, m.IstemciId); };
        _liste.VazgecIstendi += m => { if (_depo != null && _kimlik is { } k && m.IstemciId != null) _depo.Vazgec(k, m.IstemciId); };
        _liste.YanitIstendi += m =>
        {
            _yazma.YanitModu(m, Gonderen(m.GonderenId)?.GorunenAd ?? "?");
            _liste.Secili = m.Id;
        };
        _liste.DuzenleIstendi += DuzenlemeyeGir;
        _liste.SilIstendi += async m => await MesajSil(m);
        _liste.TepkiIstendi += (m, e) => { if (_depo != null && _kimlik is { } k) _ = _depo.TepkiVer(k, m.Id, e); };
        _liste.TepkiSeciciIstendi += (m, nokta) =>
        {
            if (AcilirPanel.YeniKapandi || FindForm() is null) return;
            var s = new EmojiSecici();
            s.Secildi += e => { if (_depo != null && _kimlik is { } k) _ = _depo.TepkiVer(k, m.Id, e); };
            s.Goster(this, nokta);
        };
        _liste.BahsetmeTik += a => BahsetmeTik?.Invoke(a);
        _liste.Bildirim += (m, h) => Bildirim?.Invoke(m, h);

        DragEnter += SurukleGirdi; DragDrop += SurukleBirakildi;
        _liste.DragEnter += SurukleGirdi; _liste.DragDrop += SurukleBirakildi;
    }

    private void SurukleGirdi(object? s, DragEventArgs e) =>
        e.Effect = !_basit && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;

    private void SurukleBirakildi(object? s, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] d) _yazma.DosyalariEkle(d);
    }

    // ---- Açma / gösterme ----

    // Depo ile: konuşmayı açar (yükler, okundu yapar). Önce uygulamada "şu an baktığım konuşma" bilgisini güncelle.
    public void Ac(KonusmaKimligi k, SohbetBasligi baslik)
    {
        if (_depo is null || _ben is null) return;
        bool yeni = _kimlik != k;
        if (yeni)
        {
            if (_kimlik is { } eski) _taslaklar[eski] = _yazma.Metin;
            _yazma.ModuKapat();
            _yazma.Temizle();
            _yazma.Metin = _taslaklar.GetValueOrDefault(k, "");
            _yaziyor = "";
        }
        _kimlik = k;
        _baslik = baslik;
        _veri = _depo.Veri(k);
        _yazma.HerkesBahsedilebilir = k.Tur != KonusmaTuru.Dm;
        UyeleriYenile();
        _yazma.IpucuYazisi = Ipucu ?? IpucuUret();
        _liste.Ayarla(_ben, baslik, k, _veri);
        YaziyorYenile();
        Invalidate();
        _ = _depo.Ac(k);
        if (yeni) _yazma.Odaklan();
    }

    // Depo olmadan (parti): veriyi elle ver.
    public void Goster(Profil ben, SohbetBasligi baslik, SohbetVerisi veri)
    {
        _ben = ben;
        _yazma.BenId = ben.Id;
        _baslik = baslik;
        _veri = veri;
        _kimlik = null;
        _yazma.IpucuYazisi = Ipucu ?? IpucuUret();
        _liste.Ayarla(ben, baslik, null, veri);
        Invalidate();
    }

    public void BasligiGuncelle(SohbetBasligi baslik)
    {
        _baslik = baslik;
        _liste.BasligiGuncelle(baslik);
        _yazma.IpucuYazisi = Ipucu ?? IpucuUret();
        Invalidate();
    }

    private string IpucuUret() => _kimlik?.Tur switch
    {
        KonusmaTuru.Kanal => $"#{_baslik?.Ad} kanalına mesaj gönder",
        KonusmaTuru.Grup => $"{_baslik?.Ad} grubuna mesaj gönder",
        _ => $"{_baslik?.Ad} kişisine mesaj gönder",
    };

    public void Odaklan() => _yazma.Odaklan();
    public void TestDosya(params string[] y) => _yazma.DosyalariEkle(y); // TEST-GECICI
    public void TestTikla(int x, int y, bool sag = false) => _liste.TestTikla(x, y, sag); // TEST-GECICI
    public void Alta() => _liste.Alta();
    public void MesajaGit(long id) => _liste.MesajaGit(id);

    private void UyeleriYenile()
    {
        var tum = new List<Profil>(_uyeler);
        if (_baslik?.Karsi is { } k && tum.All(p => p.Id != k.Id)) tum.Add(k);
        if (_ben != null && tum.All(p => p.Id != _ben.Id)) tum.Add(_ben);
        _uyeSozlugu = tum.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        _yazma.Uyeler = tum;
        _liste.UyeleriAyarla(tum.Where(p => p.KullaniciAdi.Length > 0).Select(p => p.KullaniciAdi), _ben?.KullaniciAdi);
    }

    private Profil? Gonderen(long id)
    {
        if (_ben != null && id == _ben.Id) return _ben;
        if (_baslik?.Karsi is { } k && k.Id == id) return k;
        return _uyeSozlugu.GetValueOrDefault(id) ?? Cozucu?.Invoke(id);
    }

    // ---- Depo olayları ----

    private void DepoDegisti(KonusmaKimligi k)
    {
        if (k != _kimlik || _veri is null || _ben is null) return;
        _liste.Ayarla(_ben, _baslik, k, _veri);
    }

    private void YaziyorDegisti(KonusmaKimligi k)
    {
        if (k != _kimlik) return;
        YaziyorYenile();
    }

    private void YaziyorYenile()
    {
        string yeni = "";
        if (_depo != null && _kimlik is { } k)
        {
            var adlar = _depo.Yazanlar(k).Select(id => Gonderen(id)?.GorunenAd ?? "Biri").ToList();
            yeni = adlar.Count switch { 0 => "", 1 => adlar[0], 2 => adlar[0] + " ve " + adlar[1], _ => "Birkaç kişi" };
            if (adlar.Count >= 3) yeni = "!" + yeni; // ön ek: çoğul "yazıyor" metni
        }
        if (yeni == _yaziyor) return;
        _yaziyor = yeni;
        Invalidate(new Rectangle(0, Height - YaziyorH, Width, YaziyorH));
    }

    // ---- Yazma kutusu eylemleri ----

    private void YazmaGonder(string metin, List<GonderilecekDosya> dosyalar)
    {
        if (_depo is null || _kimlik is not { } k) { if (metin.Length > 0) Gonder?.Invoke(metin); return; }
        _depo.Gonder(k, metin, _yazma.YanitId, dosyalar);
        _yazma.ModuKapat();
        _liste.Secili = null;
        _liste.Alta();
    }

    private async Task DuzenlemeKaydet(long id, string metin)
    {
        if (_depo is null || _kimlik is not { } k) return;
        var eski = _veri?.Liste.FirstOrDefault(m => m.Id == id);
        _yazma.ModuKapat(olayVer: true);
        if (eski != null && eski.Metin == metin) return;
        var hata = await _depo.Duzenle(k, id, metin);
        if (hata != null) Bildirim?.Invoke(hata, true);
    }

    private void DuzenlemeyeGir(Mesaj m)
    {
        if (m.GonderenId != _ben?.Id || m.Silindi || _basit) return;
        _yazma.DuzenlemeModu(m);
        _liste.Secili = m.Id;
    }

    private async Task MesajSil(Mesaj m)
    {
        if (_depo is null || _kimlik is not { } k || FindForm() is not { } f) return;
        bool evet = await OnayKarti.Sor(f, "Mesaj silinsin mi?", "Bu mesaj herkes için silinecek. Bu işlem geri alınamaz.", "Sil");
        if (!evet) return;
        var hata = await _depo.Sil(k, m.Id);
        if (hata != null) Bildirim?.Invoke(hata, true);
    }

    // ---- Yerleşim ve çizim ----

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_liste is null) return;
        int yan = Tema.S(16);
        int h = _yazma.GerekenYukseklik;
        int yazmaY = Height - YaziyorH - h;
        _yazma.Bounds = new Rectangle(yan, yazmaY, Math.Max(10, Width - yan * 2), h);
        _liste.Bounds = new Rectangle(0, BaslikH, Width, Math.Max(10, yazmaY - BaslikH - Tema.S(8)));
    }

    // Başlıktaki tıklanabilir düğmeler.
    private Rectangle GrupDugmesi => new(Width - Tema.S(52), (BaslikH - Tema.S(32)) / 2, Tema.S(36), Tema.S(32));
    private Rectangle UyeDugmesiAlani
    {
        get
        {
            int sag = Width - Tema.S(16) - (_kimlik?.Tur == KonusmaTuru.Grup ? Tema.S(44) : 0);
            return new Rectangle(sag - Tema.S(36), (BaslikH - Tema.S(32)) / 2, Tema.S(36), Tema.S(32));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_baslik is null) return;
        if (!BaslikGizli) BaslikCiz(g);
        YaziyorCiz(g);
    }

    private void BaslikCiz(Graphics g)
    {
        var b = _baslik!;
        int x = Tema.S(18);
        int sagSinir = Width - Tema.S(16);
        var tur = _kimlik?.Tur ?? KonusmaTuru.Dm;
        string alt;
        var adF = Tema.Yazi(11.5f, kalin: true);
        if (tur == KonusmaTuru.Kanal)
        {
            TextRenderer.DrawText(g, "#", Tema.Yazi(15, kalin: true), new Rectangle(x, 0, Tema.S(20), BaslikH), Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += Tema.S(22);
            alt = b.Alt ?? "";
        }
        else if (tur == KonusmaTuru.Grup)
        {
            int av = Tema.S(28);
            IkonCizim.Grup(g, new RectangleF(x, (BaslikH - av) / 2f, av, av), b.Renk, b.Ikon);
            x += av + Tema.S(12);
            alt = $"{b.UyeSayisi} üye";
        }
        else
        {
            int av = Tema.S(28);
            if (b.Karsi != null) Cizim.Avatar(g, new RectangleF(x, (BaslikH - av) / 2f, av, av), b.Karsi, Avatarlar?.Al(b.Karsi), BackColor);
            x += av + Tema.S(12);
            var k = b.Karsi;
            alt = k is null ? "" : string.IsNullOrWhiteSpace(k.DurumMesaji) || k.Durum == Durum.Cevrimdisi ? Cizim.DurumAdi(k.Durum) : k.DurumMesaji;
        }
        if (_uyeDugmesi && tur != KonusmaTuru.Dm) sagSinir = UyeDugmesiAlani.Left - Tema.S(8);
        else if (tur == KonusmaTuru.Grup) sagSinir = GrupDugmesi.Left - Tema.S(8);

        int adW = Math.Min(TextRenderer.MeasureText(g, b.Ad, adF, Size.Empty, TextFormatFlags.NoPadding).Width, Math.Max(Tema.S(60), (sagSinir - x) * 2 / 3));
        TextRenderer.DrawText(g, b.Ad, adF, new Rectangle(x, 0, adW + 4, BaslikH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        x += adW + Tema.S(14);
        if (alt.Length > 0 && x + Tema.S(40) < sagSinir)
        {
            using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, BaslikH / 2 - Tema.S(10), x, BaslikH / 2 + Tema.S(10));
            x += Tema.S(14);
            TextRenderer.DrawText(g, alt, Tema.Yazi(9.5f), new Rectangle(x, 0, sagSinir - x, BaslikH), Tema.MetinIkincil,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        if (tur == KonusmaTuru.Grup) BaslikDugmesi(g, GrupDugmesi, Glif.Ayarlar, "grup", false);
        if (_uyeDugmesi && tur != KonusmaTuru.Dm) BaslikDugmesi(g, UyeDugmesiAlani, Glif.Insanlar, "uye", _uyeDugmesiAcik);
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, BaslikH - 1, Width, BaslikH - 1);
    }

    private void BaslikDugmesi(Graphics g, Rectangle r, string glif, string ad, bool secili)
    {
        bool u = _ustHedef == ad;
        if (u || secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), secili ? Tema.YuzeyUst : Tema.Yuzey);
        TextRenderer.DrawText(g, glif, Cizim.Ikon(12), r, u || secili ? Tema.Metin : Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private string BaslikHedef(Point p)
    {
        if (BaslikGizli || p.Y > BaslikH) return "";
        if (_kimlik?.Tur == KonusmaTuru.Grup && GrupDugmesi.Contains(p)) return "grup";
        if (_uyeDugmesi && _kimlik?.Tur != KonusmaTuru.Dm && UyeDugmesiAlani.Contains(p)) return "uye";
        return "";
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = BaslikHedef(e.Location);
        if (h == _ustHedef) return;
        _ustHedef = h;
        Cursor = h.Length > 0 ? Cursors.Hand : Cursors.Default;
        Invalidate(new Rectangle(0, 0, Width, BaslikH));
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_ustHedef.Length > 0) { _ustHedef = ""; Invalidate(new Rectangle(0, 0, Width, BaslikH)); } }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var h = BaslikHedef(e.Location);
        if (h == "grup") GrupAyarTik?.Invoke();
        else if (h == "uye") UyeDugmesiTik?.Invoke();
    }

    private void YaziyorCiz(Graphics g)
    {
        if (_yaziyor.Length == 0) return;
        bool cogul = _yaziyor.StartsWith('!');
        var ad = cogul ? _yaziyor[1..] : _yaziyor;
        int y = Height - YaziyorH;
        var kalin = Tema.Yazi(9, kalin: true);
        int w = TextRenderer.MeasureText(g, ad, kalin, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        w = Math.Min(w, Width - Tema.S(200));
        TextRenderer.DrawText(g, "• • •", kalin, new Rectangle(Tema.S(18), y, Tema.S(30), YaziyorH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, ad, kalin, new Rectangle(Tema.S(50), y, w + 2, YaziyorH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, " yazıyor…", Tema.Yazi(9), new Rectangle(Tema.S(50) + w, y, Tema.S(100), YaziyorH), Tema.MetinIkincil, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Depo = null;
        base.Dispose(disposing);
    }
}
