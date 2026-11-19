using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Keşfet: tam sayfa. "Kişiler" ve "Sunucular" sekmeleri, arama kutusu (300 ms gecikmeli), duyarlı kart ızgarası.
// Kartlar tek kontrolde çizilir; sadece görünen satırlar çizilir (yüzlerce kişi olsa da hafif).
public class KesfetSayfasi : Control
{
    public enum Sekme { Kisiler, Sunucular }
    private enum Durum2 { Yukleniyor, Liste, Bos, Hata }

    private readonly Api _api;
    private readonly AvatarOnbellek? _avatarlar;
    private readonly IkonOnbellek? _ikonlar;

    private Sekme _sekme = Sekme.Kisiler;
    private Sekme? _sekmeUzerinde;
    private readonly List<(Sekme s, Rectangle r)> _sekmeAlanlari = [];

    private readonly GirisKutusu _ara = new() { IpucuYazisi = "Kişi ara: ad, @kullanıcıadı ya da #numara" };
    private readonly BosDurum _bos = new() { Visible = false };
    private readonly Dugme _tekrar = new() { Text = "Tekrar dene", Tur = Dugme.Turler.Ikincil, Visible = false };
    private readonly System.Windows.Forms.Timer _gecikme = new() { Interval = 300 };
    private readonly DikeyKaydirma _kaydir;

    private List<KesfetKullanicisi>? _kisiler;
    private List<KesfetSunucusu>? _sunucular;
    private string? _kisiSorgu, _sunucuSorgu;
    private string? _hata;
    private int _istek;
    private readonly HashSet<long> _mesgul = [];
    private (int kart, bool dugme) _uzerinde = (-1, false);

    public event Action<Profil>? ProfilTik;
    public event Action<long>? MesajTik;
    public event Action<long>? SunucuAcTik;        // katıldıktan sonra ya da zaten üyeysen "Aç"a basınca
    public event Action<Sunucu>? SunucuKatildi;    // başarılı katılım (ana form sunucu listesini tazelesin)
    public event Action? ArkadaslikDegisti;        // istek gönderildi / kabul edildi
    public event Action<string, bool>? Bildir;     // (mesaj, hata mı)

    public Sekme Secili
    {
        get => _sekme;
        set { if (_sekme != value) { _sekme = value; SekmeDegisti(); } }
    }

    public KesfetSayfasi(Api api, AvatarOnbellek? avatarlar = null, IkonOnbellek? ikonlar = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Tema.Arkaplan;
        _api = api;
        _avatarlar = avatarlar;
        _ikonlar = ikonlar;
        _kaydir = new DikeyKaydirma(this);
        Controls.AddRange([_ara, _bos, _tekrar]);

        _ara.MaksUzunluk = 60;
        _ara.TextChanged += (_, _) => { _gecikme.Stop(); _gecikme.Start(); };
        _gecikme.Tick += (_, _) => { _gecikme.Stop(); _ = Yukle(sessiz: true); };
        _tekrar.Click += async (_, _) => await Yukle(sessiz: false);
        if (avatarlar != null) avatarlar.Yuklendi += Tazele;
        if (ikonlar != null) ikonlar.Yuklendi += Tazele;
    }

    private void Tazele() { if (!IsDisposed && Visible) Invalidate(); }

    // Sayfa açılınca çağır: listeyi tazeler (arama kutusu korunur).
    public async Task Ac()
    {
        await Yukle(sessiz: true);
    }

    public Task Yenile() => Yukle(sessiz: true);

    private void SekmeDegisti()
    {
        _gecikme.Stop();
        _ara.IpucuYazisi = _sekme == Sekme.Kisiler ? "Kişi ara: ad, @kullanıcıadı ya da #numara" : "Sunucu ara";
        _kaydir.Konum = 0;
        _uzerinde = (-1, false);
        Cursor = Cursors.Default;
        _ = Yukle(sessiz: false);
        Invalidate();
    }

    // ---- Veri ----

    private async Task Yukle(bool sessiz)
    {
        var sorgu = _ara.Text.Trim();
        var sekme = _sekme;
        int no = ++_istek;
        _hata = null;
        // Elimizde bu sekmenin verisi yoksa "yükleniyor" göster; varsa eskisi görünsün (titreme olmasın).
        bool veriVar = sekme == Sekme.Kisiler ? _kisiler != null : _sunucular != null;
        if (!veriVar || !sessiz) { if (!veriVar) Durumu(); }

        if (sekme == Sekme.Kisiler)
        {
            var s = await _api.KesfetKullanicilar(sorgu);
            if (no != _istek || IsDisposed) return;
            if (s.Basarili) { _kisiler = s.Veri ?? []; _kisiSorgu = sorgu; } else { _hata = s.Hata; if (!veriVar) _kisiler = null; }
        }
        else
        {
            var s = await _api.KesfetSunucular(sorgu);
            if (no != _istek || IsDisposed) return;
            if (s.Basarili) { _sunucular = s.Veri ?? []; _sunucuSorgu = sorgu; } else { _hata = s.Hata; if (!veriVar) _sunucular = null; }
        }
        if (_hata != null && veriVar) Bildir?.Invoke(_hata, true);
        _kaydir.Konum = 0;
        Hesapla();
        Durumu();
        Invalidate();
    }

    private int Adet => _sekme == Sekme.Kisiler ? _kisiler?.Count ?? 0 : _sunucular?.Count ?? 0;

    private Durum2 Durumu()
    {
        Durum2 d;
        bool veriVar = _sekme == Sekme.Kisiler ? _kisiler != null : _sunucular != null;
        if (!veriVar) d = _hata != null ? Durum2.Hata : Durum2.Yukleniyor;
        else d = Adet == 0 ? Durum2.Bos : Durum2.Liste;

        _bos.Visible = d != Durum2.Liste;
        _tekrar.Visible = d == Durum2.Hata;
        switch (d)
        {
            case Durum2.Yukleniyor:
                _bos.Glif = Glif.Saat; _bos.Baslik = "Yükleniyor…"; _bos.Aciklama = _sekme == Sekme.Kisiler ? "Kişiler getiriliyor." : "Sunucular getiriliyor.";
                break;
            case Durum2.Hata:
                _bos.Glif = Glif.Uyari; _bos.Baslik = "Yüklenemedi"; _bos.Aciklama = _hata ?? "Bir sorun oldu.";
                break;
            case Durum2.Bos:
                bool arama = _ara.Text.Trim().Length > 0;
                _bos.Glif = Glif.Ara;
                if (_sekme == Sekme.Kisiler) { _bos.Baslik = arama ? "Kimse bulunamadı" : "Henüz kimse yok"; _bos.Aciklama = arama ? "Başka bir ad, @kullanıcıadı ya da #numara dene." : "Ekibe yeni biri katılınca burada görünür."; }
                else { _bos.Baslik = arama ? "Sunucu bulunamadı" : "Henüz herkese açık sunucu yok"; _bos.Aciklama = arama ? "Başka bir arama dene." : "Bir sunucu kurup \"Keşfet'te görünsün\" dersen ilk sen olursun."; }
                break;
        }
        _bos.Invalidate();
        YerlesimBos();
        return d;
    }

    // ---- Yerleşim ----

    private int UstH => Tema.S(56);
    private Rectangle AraAlani => new(Tema.S(32), UstH + Tema.S(16), Math.Max(Tema.S(120), Math.Min(Tema.S(520), Width - Tema.S(64) - Tema.S(110))), Tema.S(44));
    private int IcerikUst => AraAlani.Bottom + Tema.S(18);
    private Rectangle Alan => new(0, IcerikUst, Width, Math.Max(0, Height - IcerikUst));
    private int KartH => Tema.S(176);
    private int Bosluk => Tema.S(16);
    private int Kenar => Tema.S(32);

    private int Sutun
    {
        get
        {
            int kullan = Width - Kenar * 2 - Tema.S(4);
            return Math.Max(1, (kullan + Bosluk) / (Tema.S(300) + Bosluk));
        }
    }

    private int KartW
    {
        get
        {
            int kullan = Width - Kenar * 2 - Tema.S(4);
            return Math.Max(Tema.S(200), (kullan - Bosluk * (Sutun - 1)) / Sutun);
        }
    }

    private void Hesapla()
    {
        int satir = (Adet + Sutun - 1) / Sutun;
        _kaydir.Ayarla(satir == 0 ? 0 : satir * (KartH + Bosluk) - Bosluk + Tema.S(24), Alan.Height);
    }

    private Rectangle KartAlani(int i)
    {
        int s = Sutun, w = KartW;
        int satir = i / s, sutun = i % s;
        return new Rectangle(Kenar + sutun * (w + Bosluk), Alan.Y + satir * (KartH + Bosluk) - _kaydir.Konum, w, KartH);
    }

    private Rectangle DugmeAlani(Rectangle kart) => new(kart.X + Tema.S(16), kart.Bottom - Tema.S(16) - Tema.S(34), kart.Width - Tema.S(32), Tema.S(34));

    private void YerlesimBos()
    {
        var a = Alan;
        _bos.Bounds = new Rectangle(0, a.Y, Width, Math.Max(0, a.Height - (_tekrar.Visible ? Tema.S(56) : 0)));
        _tekrar.Bounds = new Rectangle((Width - Tema.S(150)) / 2, a.Bottom - Tema.S(56) - Tema.S(8), Tema.S(150), Tema.S(40));
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_ara is null) return;
        _ara.Bounds = AraAlani;
        Hesapla();
        YerlesimBos();
    }

    // ---- Çizim ----

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // Önce kartlar (TextRenderer Graphics kırpmasına uymaz, taşan yazılar üstteki başlıkla örtülür), sonra başlık alanı.
        var alan = Alan;
        if (!_bos.Visible && Adet > 0)
        {
            g.SetClip(alan);
            int s = Sutun;
            int ilkSatir = Math.Max(0, (_kaydir.Konum) / (KartH + Bosluk));
            int sonSatir = (_kaydir.Konum + alan.Height) / (KartH + Bosluk);
            for (int satir = ilkSatir; satir <= sonSatir; satir++)
                for (int c = 0; c < s; c++)
                {
                    int i = satir * s + c;
                    if (i >= Adet) break;
                    if (_sekme == Sekme.Kisiler) KisiKarti(g, i); else SunucuKarti(g, i);
                }
            g.ResetClip();
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, 0, 0, Width, alan.Y);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            _kaydir.Ciz(g, alan);
        }

        UstCubuk(g);
        var ara = AraAlani;
        string toplam = _sekme == Sekme.Kisiler ? (_kisiler is null ? "" : $"{_kisiler.Count} kişi") : (_sunucular is null ? "" : $"{_sunucular.Count} sunucu");
        TextRenderer.DrawText(g, toplam, Tema.Yazi(10), new Rectangle(ara.Right + Tema.S(16), ara.Y, Math.Max(0, Width - ara.Right - Tema.S(48)), ara.Height), Tema.MetinSoluk,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
    }

    private void UstCubuk(Graphics g)
    {
        int orta = UstH / 2, x = Tema.S(22);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        SunucuRayi.Pusula(g, new Rectangle(x - Tema.S(2), 0, Tema.S(28), UstH), Tema.MetinSoluk, 0.9f);
        x += Tema.S(34);
        var bf = Tema.Yazi(11.5f, kalin: true);
        int bw = TextRenderer.MeasureText(g, "Keşfet", bf, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping).Width;
        TextRenderer.DrawText(g, "Keşfet", bf, new Rectangle(x, 0, bw + 4, UstH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
        x += bw + Tema.S(18);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, orta - Tema.S(12), x, orta + Tema.S(12));
        x += Tema.S(16);

        _sekmeAlanlari.Clear();
        var f = Tema.Yazi(10, kalin: true);
        foreach (var (s, ad) in new[] { (Sekme.Kisiler, "Kişiler"), (Sekme.Sunucular, "Sunucular") })
        {
            int w = TextRenderer.MeasureText(g, ad, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping).Width + Tema.S(24);
            var r = new Rectangle(x, orta - Tema.S(16), w, Tema.S(32));
            _sekmeAlanlari.Add((s, r));
            if (s == _sekme) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
            else if (s == _sekmeUzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.Yuzey);
            TextRenderer.DrawText(g, ad, f, r, s == _sekme || s == _sekmeUzerinde ? Tema.Metin : Tema.MetinIkincil,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
            x = r.Right + Tema.S(8);
        }
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, UstH - 1, Width, UstH - 1);
    }

    private void KartZemini(Graphics g, Rectangle r, bool uz)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var yol = Tema.YuvarlakDikdortgen(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Tema.S(12));
        using var b = new SolidBrush(uz ? Tema.YuzeyUst : Tema.Yuzey);
        using var p = new Pen(uz ? Tema.KenarHover : Tema.Kenar);
        g.FillPath(b, yol);
        g.DrawPath(p, yol);
    }

    // Kart düğmesi: dolgu rengi ve yazı rengi duruma göre.
    private void KartDugmesi(Graphics g, Rectangle r, string yazi, Color dolgu, Color renk)
    {
        Cizim.YuvarlakDoldur(g, r, Tema.S(8), dolgu);
        TextRenderer.DrawText(g, yazi, Tema.Yazi(10, kalin: true), r, renk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
    }

    private static Color Karisim(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    private void KisiKarti(Graphics g, int i)
    {
        var k = _kisiler![i];
        var p = k.Profil;
        var r = KartAlani(i);
        if (r.Bottom < Alan.Top || r.Top > Alan.Bottom) return;
        bool uz = _uzerinde.kart == i, dUz = uz && _uzerinde.dugme;
        KartZemini(g, r, uz);
        var zemin = uz ? Tema.YuzeyUst : Tema.Yuzey;

        bool arkadas = k.Iliski == 1;
        int av = Tema.S(52);
        Cizim.Avatar(g, new RectangleF(r.X + Tema.S(16), r.Y + Tema.S(16), av, av), p, _avatarlar?.Al(p), zemin, arkadas);

        int tx = r.X + Tema.S(16) + av + Tema.S(14), tw = r.Right - tx - Tema.S(16);
        TextRenderer.DrawText(g, p.GorunenAd, Tema.Yazi(10.5f, kalin: true), new Rectangle(tx, r.Y + Tema.S(17), tw, Tema.S(20)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, $"@{p.KullaniciAdi}  ·  #{p.Id}", Tema.Yazi(9), new Rectangle(tx, r.Y + Tema.S(38), tw, Tema.S(18)), Tema.MetinSoluk, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
        if (arkadas)
        {
            var durum = p.Durum == Ekip.Ortak.Durum.Gorunmez ? Ekip.Ortak.Durum.Cevrimdisi : p.Durum;
            var yazi = durum != Ekip.Ortak.Durum.Cevrimdisi && !string.IsNullOrWhiteSpace(p.DurumMesaji) ? p.DurumMesaji : Cizim.DurumAdi(durum);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Cizim.DurumRengi(durum))) g.FillEllipse(b, tx, r.Y + Tema.S(60) + Tema.S(5), Tema.S(8), Tema.S(8));
            TextRenderer.DrawText(g, yazi, Tema.Yazi(9), new Rectangle(tx + Tema.S(14), r.Y + Tema.S(58), tw - Tema.S(14), Tema.S(18)), Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
        }

        bool bos = string.IsNullOrWhiteSpace(p.Hakkimda);
        TextRenderer.DrawText(g, MetinYardimci.Satirla(bos ? "Henüz bir şey yazmamış." : p.Hakkimda, Tema.Yazi(9.5f), r.Width - Tema.S(32), 2), Tema.Yazi(9.5f),
            new Rectangle(r.X + Tema.S(16), r.Y + Tema.S(82), r.Width - Tema.S(32), Tema.S(36)), bos ? Tema.MetinSoluk : Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);

        var d = DugmeAlani(r);
        bool mesgul = _mesgul.Contains(p.Id);
        if (mesgul) KartDugmesi(g, d, "…", Tema.Giris, Tema.MetinSoluk);
        else switch (k.Iliski)
        {
            case 1: KartDugmesi(g, d, "Mesaj gönder", dUz ? Tema.Kenar : Karisim(Tema.YuzeyUst, Tema.Kenar, uz ? 0.5f : 0f), Tema.Metin); break;
            case 2: KartDugmesi(g, d, "İstek gönderildi", Tema.Giris, Tema.MetinSoluk); break;
            case 3: KartDugmesi(g, d, "Kabul et", Color.FromArgb(dUz ? 70 : 40, Tema.Basari).Birlestir(zemin), Tema.Basari); break;
            default: KartDugmesi(g, d, "Arkadaş ekle", dUz ? Tema.VurguHover : Tema.Vurgu, Color.White); break;
        }
    }

    private void SunucuKarti(Graphics g, int i)
    {
        var s = _sunucular![i];
        var r = KartAlani(i);
        if (r.Bottom < Alan.Top || r.Top > Alan.Bottom) return;
        bool uz = _uzerinde.kart == i, dUz = uz && _uzerinde.dugme;
        KartZemini(g, r, uz);

        int ic = Tema.S(56);
        IkonCizim.Sunucu(g, new RectangleF(r.X + Tema.S(16), r.Y + Tema.S(16), ic, ic), s.Ad, s.Renk, _ikonlar?.Sunucu(s.Id, s.IkonSurum), Tema.S(16));
        int tx = r.X + Tema.S(16) + ic + Tema.S(14), tw = r.Right - tx - Tema.S(16);
        TextRenderer.DrawText(g, s.Ad, Tema.Yazi(10.5f, kalin: true), new Rectangle(tx, r.Y + Tema.S(20), tw, Tema.S(20)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Tema.Basari)) g.FillEllipse(b, tx, r.Y + Tema.S(46) + Tema.S(5), Tema.S(8), Tema.S(8));
        TextRenderer.DrawText(g, $"{s.Cevrimici} çevrimiçi  ·  {s.UyeSayisi} üye", Tema.Yazi(9), new Rectangle(tx + Tema.S(14), r.Y + Tema.S(46), tw - Tema.S(14), Tema.S(18)), Tema.MetinIkincil,
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.EndEllipsis);

        bool bos = string.IsNullOrWhiteSpace(s.Aciklama);
        TextRenderer.DrawText(g, MetinYardimci.Satirla(bos ? "Açıklama yok." : s.Aciklama, Tema.Yazi(9.5f), r.Width - Tema.S(32), 2), Tema.Yazi(9.5f),
            new Rectangle(r.X + Tema.S(16), r.Y + Tema.S(82), r.Width - Tema.S(32), Tema.S(36)), bos ? Tema.MetinSoluk : Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);

        var d = DugmeAlani(r);
        if (_mesgul.Contains(s.Id)) KartDugmesi(g, d, "Katılıyor…", Tema.Giris, Tema.MetinSoluk);
        else if (s.Uyesin) KartDugmesi(g, d, "Aç", dUz ? Tema.Kenar : Karisim(Tema.YuzeyUst, Tema.Kenar, uz ? 0.5f : 0f), Tema.Metin);
        else KartDugmesi(g, d, "Katıl", dUz ? Tema.VurguHover : Tema.Vurgu, Color.White);
    }

    // ---- Fare ----

    private int KartBul(Point p)
    {
        var a = Alan;
        if (!a.Contains(p) || _bos.Visible || _kaydir.Seritte(p, a)) return -1;
        int w = KartW, s = Sutun;
        int sutun = (p.X - Kenar) / (w + Bosluk);
        if (p.X < Kenar || sutun >= s || (p.X - Kenar) % (w + Bosluk) >= w) return -1;
        int y = p.Y - a.Y + _kaydir.Konum;
        int satir = y / (KartH + Bosluk);
        if (y % (KartH + Bosluk) >= KartH) return -1;
        int i = satir * s + sutun;
        return i < Adet ? i : -1;
    }

    private bool DugmeEtkin(int i)
    {
        if (_sekme == Sekme.Kisiler) return _kisiler![i].Iliski != 2 && !_mesgul.Contains(_kisiler[i].Profil.Id);
        return !_mesgul.Contains(_sunucular![i].Id);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_kaydir.FareHareket(e.Location, Alan)) return;
        var sb = _sekmeAlanlari.Where(a => a.r.Contains(e.Location)).Select(a => (Sekme?)a.s).FirstOrDefault();
        if (sb != _sekmeUzerinde) { _sekmeUzerinde = sb; Invalidate(new Rectangle(0, 0, Width, UstH)); }

        int i = KartBul(e.Location);
        bool dugme = i >= 0 && DugmeAlani(KartAlani(i)).Contains(e.Location) && DugmeEtkin(i);
        Cursor = sb != null || i >= 0 && (dugme || _sekme == Sekme.Kisiler) ? Cursors.Hand : Cursors.Default;
        if ((i, dugme) == _uzerinde) return;
        var eski = _uzerinde.kart;
        _uzerinde = (i, dugme);
        if (eski >= 0 && eski < Adet) Invalidate(KartAlani(eski));
        if (i >= 0) Invalidate(KartAlani(i));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _sekmeUzerinde = null;
        _uzerinde = (-1, false);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left) _kaydir.FareBasla(e.Location, Alan);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_kaydir.Tekerlek(e.Delta, KartH / 2 + Bosluk)) return;
        OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, e.X, e.Y, 0));
        Invalidate();
    }

    protected override async void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_kaydir.FareBirak() || e.Button != MouseButtons.Left) return;
        var sb = _sekmeAlanlari.Where(a => a.r.Contains(e.Location)).Select(a => (Sekme?)a.s).FirstOrDefault();
        if (sb is { } yeni) { Secili = yeni; return; }

        int i = KartBul(e.Location);
        if (i < 0) return;
        bool dugme = DugmeAlani(KartAlani(i)).Contains(e.Location);
        if (_sekme == Sekme.Kisiler)
        {
            var k = _kisiler![i];
            if (dugme) await KisiEylemi(k);
            else ProfilTik?.Invoke(k.Profil);
        }
        else if (dugme) await SunucuEylemi(_sunucular![i]);
    }

    // ---- Eylemler ----

    private async Task KisiEylemi(KesfetKullanicisi k)
    {
        long id = k.Profil.Id;
        if (_mesgul.Contains(id)) return;
        switch (k.Iliski)
        {
            case 1: MesajTik?.Invoke(id); return;
            case 2: return;
            case 0:
                _mesgul.Add(id); Invalidate(Alan);
                var s = await _api.IstekGonderId(id);
                _mesgul.Remove(id);
                if (IsDisposed) return;
                if (!s.Basarili) { Bildir?.Invoke(s.Hata!, true); Invalidate(Alan); return; }
                IliskiyiGuncelle(id, s.Veri!.ArkadasOldunuz ? 1 : 2);
                Bildir?.Invoke(s.Veri.Mesaj, false);
                ArkadaslikDegisti?.Invoke();
                return;
            case 3:
                _mesgul.Add(id); Invalidate(Alan);
                var c = await _api.IstekKabul(id);
                _mesgul.Remove(id);
                if (IsDisposed) return;
                if (!c.Basarili) { Bildir?.Invoke(c.Hata!, true); Invalidate(Alan); return; }
                IliskiyiGuncelle(id, 1);
                Bildir?.Invoke($"{k.Profil.GorunenAd} ile artık arkadaşsınız.", false);
                ArkadaslikDegisti?.Invoke();
                return;
        }
    }

    private void IliskiyiGuncelle(long id, int iliski)
    {
        if (_kisiler is null) return;
        int i = _kisiler.FindIndex(x => x.Profil.Id == id);
        if (i >= 0) _kisiler[i] = _kisiler[i] with { Iliski = iliski };
        Invalidate(Alan);
    }

    private async Task SunucuEylemi(KesfetSunucusu s)
    {
        if (_mesgul.Contains(s.Id)) return;
        if (s.Uyesin) { SunucuAcTik?.Invoke(s.Id); return; }
        _mesgul.Add(s.Id); Invalidate(Alan);
        var r = await _api.SunucuyaKatil(s.Id);
        _mesgul.Remove(s.Id);
        if (IsDisposed) return;
        if (!r.Basarili) { Bildir?.Invoke(r.Hata!, true); Invalidate(Alan); return; }
        int i = _sunucular?.FindIndex(x => x.Id == s.Id) ?? -1;
        if (i >= 0) _sunucular![i] = _sunucular[i] with { Uyesin = true, UyeSayisi = _sunucular[i].UyeSayisi + 1 };
        Invalidate(Alan);
        Bildir?.Invoke($"{s.Ad} sunucusuna katıldın.", false);
        SunucuKatildi?.Invoke(r.Veri!);
        SunucuAcTik?.Invoke(s.Id);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gecikme.Stop();
            _gecikme.Dispose();
            if (_avatarlar != null) _avatarlar.Yuklendi -= Tazele;
            if (_ikonlar != null) _ikonlar.Yuklendi -= Tazele;
        }
        base.Dispose(disposing);
    }
}

internal static class RenkYardimcisi
{
    // Yarı saydam rengi zemin üzerine karıştırıp düz renk yapar (düğme zeminleri için).
    public static Color Birlestir(this Color ust, Color alt)
    {
        float a = ust.A / 255f;
        return Color.FromArgb((int)(ust.R * a + alt.R * (1 - a)), (int)(ust.G * a + alt.G * (1 - a)), (int)(ust.B * a + alt.B * (1 - a)));
    }
}
