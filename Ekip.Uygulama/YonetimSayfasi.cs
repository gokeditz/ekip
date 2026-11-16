using Ekip.Ortak;

namespace Ekip.Uygulama;

// Admin paneli: ana pencerenin içinde bir sayfa. Sekmeler: Davet kodları, Kullanıcılar.
public class YonetimSayfasi : Control
{
    public enum Sekme { Kodlar, Kullanicilar }

    private readonly Api _api;
    private readonly Form _sahip;
    private long _benId;
    private Sekme _secili = Sekme.Kodlar;
    private Sekme? _uzerinde;
    private readonly List<(Sekme s, Rectangle r)> _alanlar = [];
    private string _ozet = "";

    private readonly Panel _kodPaneli = new() { BackColor = Tema.Arkaplan };
    private readonly Label _yeniBaslik = new() { AutoSize = true, Text = "YENİ DAVET KODU", Font = Tema.Yazi(8.5f, kalin: true), ForeColor = Tema.MetinIkincil };
    private readonly Label _kullanimEtiket = new() { AutoSize = true, Text = "Kaç kişi kullanabilir", Font = Tema.Yazi(9.5f), ForeColor = Tema.MetinIkincil };
    private readonly SecimGrubu _kullanim = new(["1 kişi", "5 kişi", "10 kişi", "Sınırsız"]);
    private readonly Label _sureEtiket = new() { AutoSize = true, Text = "Ne kadar geçerli", Font = Tema.Yazi(9.5f), ForeColor = Tema.MetinIkincil };
    private readonly SecimGrubu _sure = new(["1 gün", "7 gün", "30 gün", "Süresiz"]) { Secili = 1 };
    private readonly Dugme _olustur = new() { Text = "Kod oluştur" };
    private readonly YonetimListesi _kodlar = new();
    private readonly YonetimListesi _kullanicilar = new();
    private readonly BosDurum _kodYok = new() { Glif = "", Baslik = "Henüz davet kodu yok", Aciklama = "Yukarıdan bir kod oluştur ve arkadaşına gönder. Kayıt olurken bu kodu girecek." };

    public event Action<string, bool>? Bildir; // (mesaj, hata mı)

    private static readonly int[] Kullanimlar = [1, 5, 10, 0];
    private static readonly int?[] Gunler = [1, 7, 30, null];

    public YonetimSayfasi(Api api, Form sahip)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
        _api = api;
        _sahip = sahip;
        _kodPaneli.Controls.AddRange([_yeniBaslik, _kullanimEtiket, _kullanim, _sureEtiket, _sure, _olustur, _kodlar, _kodYok]);
        Controls.AddRange([_kodPaneli, _kullanicilar]);
        _olustur.Click += async (_, _) => await KodOlustur();
        _kodlar.Eylem += async (etiket, ad) => await KodEylemi((DavetKodu)etiket, ad);
        _kullanicilar.Eylem += async (etiket, ad) => await KullaniciEylemi((YonetimKullanicisi)etiket, ad);
        Sec(Sekme.Kodlar);
    }

    public AvatarOnbellek? Avatarlar { set => _kullanicilar.Avatarlar = value; }

    public async Task Ac(long benId)
    {
        _benId = benId;
        await Yenile();
    }

    private void Sec(Sekme s)
    {
        _secili = s;
        _kodPaneli.Visible = s == Sekme.Kodlar;
        _kullanicilar.Visible = s == Sekme.Kullanicilar;
        Invalidate();
    }

    private async Task Yenile()
    {
        var ozet = _api.YonetimOzeti();
        var kodlar = _api.Kodlar();
        var kullanicilar = _api.YonetimKullanicilari();
        var o = await ozet; var k = await kodlar; var u = await kullanicilar;
        if (!k.Basarili || !u.Basarili) { Bildir?.Invoke(k.Hata ?? u.Hata!, true); return; }
        if (o.Basarili) _ozet = $"{o.Veri!.Kullanici} kullanıcı  ·  {o.Veri.Cevrimici} çevrimiçi  ·  {o.Veri.AktifKod} aktif kod";
        KodlariGoster(k.Veri!);
        KullanicilariGoster(u.Veri!);
        Invalidate();
    }

    // ---- Kodlar ----

    private static (string yazi, Color renk, bool aktif) KodDurumu(DavetKodu k)
    {
        if (k.Iptal) return ("İPTAL", Tema.Hata, false);
        if (k.SonKullanma is { } s && s < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return ("SÜRESİ DOLDU", Tema.MetinSoluk, false);
        if (k.KalanKullanim == 0) return ("KULLANILDI", Tema.MetinSoluk, false);
        return ("AKTİF", Tema.Basari, true);
    }

    private void KodlariGoster(List<DavetKodu> kodlar)
    {
        _kodlar.Ayarla(kodlar.Select(k =>
        {
            var (durum, renk, aktif) = KodDurumu(k);
            var kalan = k.KalanKullanim < 0 ? "sınırsız" : $"{k.KalanKullanim} kaldı";
            var son = k.SonKullanma is { } s ? "son gün " + DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime.ToString("dd.MM.yyyy HH:mm") : "süresiz";
            var alt = $"{kalan}  ·  {k.Kullanan} kişi kullandı  ·  {son}  ·  {k.Olusturan} oluşturdu";
            var dugmeler = new List<YonetimListesi.Dugme> { new("kopyala", "Kopyala", false) };
            if (aktif) dugmeler.Add(new("iptal", "İptal et", true));
            return new YonetimListesi.Oge(k, k.Kod, alt, null, [(durum, renk)], dugmeler, Kod: true, Glif: "");
        }).ToList());
        _kodYok.Visible = kodlar.Count == 0;
        _kodlar.Visible = kodlar.Count > 0;
    }

    private async Task KodOlustur()
    {
        _olustur.Enabled = false;
        var s = await _api.KodOlustur(Kullanimlar[_kullanim.Secili], Gunler[_sure.Secili]);
        _olustur.Enabled = true;
        if (!s.Basarili) { Bildir?.Invoke(s.Hata!, true); return; }
        try { Clipboard.SetText(s.Veri!.Kod); } catch { }
        Bildir?.Invoke($"{s.Veri!.Kod} oluşturuldu ve panoya kopyalandı. Arkadaşına gönderebilirsin.", false);
        await Yenile();
    }

    private async Task KodEylemi(DavetKodu k, string ad)
    {
        if (ad == "kopyala")
        {
            try { Clipboard.SetText(k.Kod); Bildir?.Invoke($"{k.Kod} panoya kopyalandı.", false); } catch { }
            return;
        }
        if (ad == "iptal")
        {
            if (!await OnayKarti.Sor(_sahip, $"{k.Kod} iptal edilsin mi?", "Bu kodla artık kimse kayıt olamaz. Kodla daha önce kaydolanlar etkilenmez.", "İptal et")) return;
            var s = await _api.KodIptal(k.Kod);
            if (!s.Basarili) Bildir?.Invoke(s.Hata!, true);
            await Yenile();
        }
    }

    // ---- Kullanıcılar ----

    private void KullanicilariGoster(List<YonetimKullanicisi> liste)
    {
        _kullanicilar.Ayarla(liste.OrderByDescending(x => x.Cevrimici).ThenBy(x => x.GorunenAd, StringComparer.Create(Tema.Turkce, true)).Select(u =>
        {
            var p = new Profil(u.Id, u.KullaniciAdi, u.GorunenAd, "", "", u.ProfilRengi, u.AvatarSurum, u.Cevrimici ? Durum.Cevrimici : Durum.Cevrimdisi);
            var kayit = DateTimeOffset.FromUnixTimeSeconds(u.Kayit).LocalDateTime.ToString("dd.MM.yyyy");
            var alt = $"@{u.KullaniciAdi}  ·  {kayit} katıldı  ·  {(u.DavetKodu is null ? "kurucu" : "kod: " + u.DavetKodu)}";
            var rozetler = new List<(string, Color)>();
            if (u.Id == _benId) rozetler.Add(("SEN", Tema.MetinIkincil));
            if (u.Admin) rozetler.Add(("ADMİN", Tema.Vurgu));
            if (u.Yasakli) rozetler.Add(("YASAKLI", Tema.Hata));
            var dugmeler = new List<YonetimListesi.Dugme>();
            if (u.Id != _benId)
            {
                dugmeler.Add(new("admin", u.Admin ? "Adminliği al" : "Admin yap", false));
                dugmeler.Add(new("yasak", u.Yasakli ? "Yasağı kaldır" : "Yasakla", !u.Yasakli));
            }
            return new YonetimListesi.Oge(u, u.GorunenAd, alt, p, rozetler, dugmeler);
        }).ToList());
    }

    private async Task KullaniciEylemi(YonetimKullanicisi u, string ad)
    {
        Sonuc<object> s;
        if (ad == "admin")
        {
            if (!u.Admin && !await OnayKarti.Sor(_sahip, $"{u.GorunenAd} admin yapılsın mı?",
                    "Admin olunca davet kodu oluşturabilir, kullanıcıları yasaklayabilir ve başkalarını admin yapabilir.", "Admin yap", tehlike: false)) return;
            s = await _api.AdminAyarla(u.Id, !u.Admin);
        }
        else
        {
            if (!u.Yasakli && !await OnayKarti.Sor(_sahip, $"{u.GorunenAd} yasaklansın mı?",
                    "Hemen oturumdan atılır ve bir daha giriş yapamaz. İstersen sonra yasağı kaldırabilirsin.", "Yasakla")) return;
            s = await _api.YasakAyarla(u.Id, !u.Yasakli);
        }
        if (!s.Basarili) Bildir?.Invoke(s.Hata!, true);
        await Yenile();
    }

    // ---- Yerleşim ve başlık ----

    private int UstH => Tema.S(56);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_kodPaneli is null) return;
        var icerik = new Rectangle(0, UstH, Width, Height - UstH);
        _kodPaneli.Bounds = icerik;
        _kullanicilar.Bounds = new Rectangle(Tema.S(20), UstH + Tema.S(14), Width - Tema.S(34), Height - UstH - Tema.S(14));

        // Genişliğe göre: iki seçim grubu + düğme aynı satırda, pencere daralırsa gruplar da daralır.
        int x = Tema.S(32), y = Tema.S(24), ara = Tema.S(24), dugmeW = Tema.S(150);
        int grupW = Math.Clamp((_kodPaneli.Width - x * 2 - dugmeW - ara * 2) / 2, Tema.S(220), Tema.S(360));
        _yeniBaslik.Location = new Point(x - Tema.S(3), y); y = _yeniBaslik.Bottom + Tema.S(14);
        _kullanimEtiket.Location = new Point(x - Tema.S(3), y);
        _sureEtiket.Location = new Point(x + grupW + ara - Tema.S(3), y);
        y = _kullanimEtiket.Bottom + Tema.S(6);
        _kullanim.Bounds = new Rectangle(x, y, grupW, Tema.S(38));
        _sure.Bounds = new Rectangle(x + grupW + ara, y, grupW, Tema.S(38));
        _olustur.Bounds = new Rectangle(_sure.Right + ara, y - Tema.S(2), dugmeW, Tema.S(42));
        y = _kullanim.Bottom + Tema.S(26);
        _kodlar.Bounds = new Rectangle(Tema.S(20), y, _kodPaneli.Width - Tema.S(34), _kodPaneli.Height - y);
        _kodYok.Bounds = new Rectangle(0, y, _kodPaneli.Width, _kodPaneli.Height - y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int orta = UstH / 2, x = Tema.S(22);
        TextRenderer.DrawText(g, "", Cizim.Ikon(13), new Rectangle(x, 0, Tema.S(24), UstH), Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += Tema.S(32);
        var bf = Tema.Yazi(11.5f, kalin: true);
        int bw = TextRenderer.MeasureText(g, "Yönetim", bf, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, "Yönetim", bf, new Rectangle(x, 0, bw + 4, UstH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += bw + Tema.S(18);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, orta - Tema.S(12), x, orta + Tema.S(12));
        x += Tema.S(16);

        _alanlar.Clear();
        var f = Tema.Yazi(10, kalin: true);
        foreach (var (s, ad) in new[] { (Sekme.Kodlar, "Davet kodları"), (Sekme.Kullanicilar, "Kullanıcılar") })
        {
            int w = TextRenderer.MeasureText(g, ad, f, Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(24);
            var r = new Rectangle(x, orta - Tema.S(16), w, Tema.S(32));
            _alanlar.Add((s, r));
            if (s == _secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
            else if (s == _uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.Yuzey);
            TextRenderer.DrawText(g, ad, f, r, s == _secili || s == _uzerinde ? Tema.Metin : Tema.MetinIkincil,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x = r.Right + Tema.S(8);
        }
        TextRenderer.DrawText(g, _ozet, Tema.Yazi(9.5f), new Rectangle(x + Tema.S(10), 0, Width - x - Tema.S(32), UstH), Tema.MetinIkincil,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, UstH - 1, Width, UstH - 1);
    }

    private Sekme? Bul(Point p) => _alanlar.Where(a => a.r.Contains(p)).Select(a => (Sekme?)a.s).FirstOrDefault();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var b = Bul(e.Location);
        if (b == _uzerinde) return;
        _uzerinde = b;
        Cursor = b != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = null; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (Bul(e.Location) is { } s) Sec(s); }
}

// Yan yana seçenek düğmeleri (biri seçili).
public class SecimGrubu : Control
{
    private readonly string[] _secenekler;
    private int _secili, _uzerinde = -1;
    public int Secili { get => _secili; set { _secili = value; Invalidate(); } }

    public SecimGrubu(string[] secenekler)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _secenekler = secenekler;
    }

    private Rectangle Alan(int i)
    {
        int w = (Width - Tema.S(4)) / _secenekler.Length;
        return new Rectangle(Tema.S(2) + i * w, Tema.S(2), w, Height - Tema.S(4));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        Cizim.YuvarlakDoldur(g, ClientRectangle, Tema.S(8), Tema.Giris);
        for (int i = 0; i < _secenekler.Length; i++)
        {
            var r = Alan(i);
            if (i == _secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.Vurgu);
            else if (i == _uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
            TextRenderer.DrawText(g, _secenekler[i], Tema.Yazi(9.5f, kalin: i == _secili), r, i == _secili ? Color.White : Tema.MetinIkincil,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private int Bul(Point p) { for (int i = 0; i < _secenekler.Length; i++) if (Alan(i).Contains(p)) return i; return -1; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = Bul(e.Location);
        if (i == _uzerinde) return;
        _uzerinde = i;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = -1; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); int i = Bul(e.Location); if (i >= 0) Secili = i; }
}

// Yönetim listeleri: satır başına simge/avatar, başlık, açıklama, rozetler ve sağda yazılı düğmeler.
public class YonetimListesi : Control
{
    public record Dugme(string Ad, string Yazi, bool Tehlike);
    public record Oge(object Etiket, string Baslik, string Alt, Profil? Avatar, List<(string yazi, Color renk)> Rozetler, List<Dugme> Dugmeler, bool Kod = false, string? Glif = null);

    private List<Oge> _ogeler = [];
    private int _kaydirma, _uzerindeSatir = -1, _uzerindeDugme = -1;
    public AvatarOnbellek? Avatarlar { get; set; }
    public event Action<object, string>? Eylem;

    public YonetimListesi()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Tema.Arkaplan;
    }

    private int SatirH => Tema.S(68);
    private int MaksKaydirma => Math.Max(0, _ogeler.Count * SatirH - Height);

    public void Ayarla(List<Oge> ogeler) { _ogeler = ogeler; _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma); Invalidate(); }

    private Rectangle SatirAlani(int i) => new(0, i * SatirH - _kaydirma, Width - Tema.S(10), SatirH);

    private List<Rectangle> DugmeAlanlari(int i, Graphics? g = null)
    {
        var r = SatirAlani(i);
        var list = new List<Rectangle>();
        int x = r.Right - Tema.S(12);
        var f = Tema.Yazi(9.5f, kalin: true);
        for (int j = _ogeler[i].Dugmeler.Count - 1; j >= 0; j--)
        {
            int w = TextRenderer.MeasureText(_ogeler[i].Dugmeler[j].Yazi, f).Width + Tema.S(16);
            x -= w;
            list.Insert(0, new Rectangle(x, r.Y + (r.Height - Tema.S(34)) / 2, w, Tema.S(34)));
            x -= Tema.S(8);
        }
        return list;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        for (int i = 0; i < _ogeler.Count; i++)
        {
            var r = SatirAlani(i);
            if (r.Bottom < 0 || r.Top > Height) continue;
            var o = _ogeler[i];
            bool uzerinde = i == _uzerindeSatir;
            if (uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(8), Tema.Yuzey);
            else if (i > 0 && i - 1 != _uzerindeSatir) using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, r.X + Tema.S(10), r.Y, r.Right - Tema.S(10), r.Y);

            int av = Tema.S(40);
            var avR = new RectangleF(r.X + Tema.S(12), r.Y + (r.Height - av) / 2f, av, av);
            if (o.Avatar is { } p2) Cizim.Avatar(g, avR, p2, Avatarlar?.Al(p2), uzerinde ? Tema.Yuzey : BackColor);
            else
            {
                Cizim.YuvarlakDoldur(g, avR, Tema.S(10), Tema.YuzeyUst);
                TextRenderer.DrawText(g, o.Glif ?? "", Cizim.Ikon(14), Rectangle.Round(avR), Tema.Vurgu, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            var dugmeler = DugmeAlanlari(i);
            int tx = (int)avR.Right + Tema.S(14), sag = (dugmeler.Count > 0 ? dugmeler[0].X : r.Right) - Tema.S(16);
            var bf = o.Kod ? new Font("Consolas", 13, FontStyle.Bold) : Tema.Yazi(10.5f, kalin: true);
            int bw = TextRenderer.MeasureText(g, o.Baslik, bf, Size.Empty, TextFormatFlags.NoPadding).Width;
            int ust = r.Y + Tema.S(13);
            TextRenderer.DrawText(g, o.Baslik, bf, new Point(tx, ust - (o.Kod ? Tema.S(2) : 0)), Tema.Metin, TextFormatFlags.NoPadding);
            if (o.Kod) bf.Dispose();
            int rx = tx + bw + Tema.S(10);
            foreach (var (yazi, renk) in o.Rozetler)
            {
                var rf = Tema.Yazi(7.5f, kalin: true);
                int rw = TextRenderer.MeasureText(yazi, rf).Width + Tema.S(4);
                var rr = new Rectangle(rx, ust + Tema.S(1), rw, Tema.S(18));
                Cizim.YuvarlakDoldur(g, rr, Tema.S(4), Color.FromArgb(45, renk));
                TextRenderer.DrawText(g, yazi, rf, rr, renk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                rx = rr.Right + Tema.S(6);
            }
            TextRenderer.DrawText(g, o.Alt, Tema.Yazi(9), new Rectangle(tx, r.Y + Tema.S(38), Math.Max(10, sag - tx), Tema.S(18)), Tema.MetinIkincil,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            for (int j = 0; j < dugmeler.Count; j++)
            {
                var d = o.Dugmeler[j];
                bool du = uzerinde && j == _uzerindeDugme;
                var zemin = d.Tehlike ? (du ? Color.FromArgb(70, Tema.Hata) : Color.FromArgb(35, Tema.Hata)) : (du ? Tema.Kenar : Tema.YuzeyUst);
                Cizim.YuvarlakDoldur(g, dugmeler[j], Tema.S(6), zemin);
                TextRenderer.DrawText(g, d.Yazi, Tema.Yazi(9.5f, kalin: true), dugmeler[j], d.Tehlike ? Tema.Hata : Tema.Metin,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    private (int satir, int dugme) Bul(Point p)
    {
        int i = (p.Y + _kaydirma) / SatirH;
        if (p.Y < 0 || i < 0 || i >= _ogeler.Count) return (-1, -1);
        var d = DugmeAlanlari(i);
        for (int j = 0; j < d.Count; j++) if (d[j].Contains(p)) return (i, j);
        return (i, -1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (s, d) = Bul(e.Location);
        if (s == _uzerindeSatir && d == _uzerindeDugme) return;
        _uzerindeSatir = s;
        _uzerindeDugme = d;
        Cursor = d >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerindeSatir = _uzerindeDugme = -1; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var (s, d) = Bul(e.Location);
        if (s >= 0 && d >= 0) Eylem?.Invoke(_ogeler[s].Etiket, _ogeler[s].Dugmeler[d].Ad);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _kaydirma = Math.Clamp(_kaydirma - e.Delta * SatirH / 120, 0, MaksKaydirma);
        Invalidate();
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma); }
}
