using System.Diagnostics;
using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Mesajların çizildiği liste. Değişken yükseklikli satırlar, sadece görünenler çizilir.
// Aynı kişinin 5 dakika içindeki ardışık mesajları tek başlık altında toplanır.
// Zengin metin (ZenginMetin), yanıt alıntısı, ekler, tepkiler, üzerine gelince araç çubuğu, sağ tık menüsü.
public sealed class MesajListesi : Control
{
    private enum Tur { Baslangic, Tarih, Mesaj }
    private enum HedefTuru { Yok, Arac, Yanit, Baglanti, Bahsetme, Resim, Indir, Ac, Tepki, TepkiArti, TekrarDene, Vazgec, AltaPil }
    private enum Arac { Tepki, Yanitla, Duzenle, Sil, Diger }

    private readonly record struct Hedef(HedefTuru Tur, int Oge = -1, object? Veri = null, Rectangle Alan = default, int No = 0);

    private sealed class EkKutu
    {
        public Ek Ek = null!;
        public string Anahtar = "";
        public Rectangle Alan;
        public bool Resim;
        public Rectangle Indir, Ac;
    }

    private sealed class TepkiKutu
    {
        public Tepki T = null!;
        public Rectangle Alan;
        public bool Ben;
    }

    private sealed class Oge
    {
        public Tur Tur;
        public Mesaj? M;
        public bool GrupBasi;
        public DateTime Gun;
        public int Y, H;
        public string Anahtar = "";
        public int HoverUst;
        public bool Bahsetti;
        public Rectangle? Yanit;
        public int BaslikY, MetinY, SilindiY = -1;
        public ZenginDuzen? Duzen;
        public Point? DuzenlendiKonum;
        public List<EkKutu> Ekler = [];
        public List<(Rectangle Alan, GonderilecekDosya Dosya, int No)> Yuklemeler = [];
        public BekleyenGonderim? Bekleyen;
        public List<TepkiKutu> Tepkiler = [];
        public Rectangle? TepkiArti;
        public Rectangle? TekrarDeneAlani, VazgecAlani;
        public int BasarisizY;
    }

    private sealed class IndirmeDurumu
    {
        public double Ilerleme;
        public bool Iniyor, Aciliyor, Hata;
        public string? Yol;
    }

    private Profil? _ben;
    private SohbetBasligi? _baslik;
    private KonusmaKimligi? _kimlik;
    private SohbetVerisi? _veri;
    private List<Oge> _ogeler = [];
    private int _toplamH, _kaydirma, _olcuW = -1, _uzerinde = -1;
    private int? _surukleBaslangic;
    private int _surukleKaydirma;
    private Hedef _hedef;
    private string _ipucuYazisi = "";
    private bool _tumu;

    private readonly Dictionary<Mesaj, ZenginDuzen> _duzenler = new(ReferenceEqualityComparer.Instance);
    private int _duzenGenislik = -1;
    private ZenginSecenekler _secenek = new();
    private readonly Dictionary<string, (int W, int H)> _resimBoyutlari = [];
    private readonly HashSet<string> _istenenResimler = [], _basarisizResimler = [];
    private readonly Dictionary<string, IDisposable> _animKayitlari = [];
    private readonly Dictionary<string, (Oge O, EkKutu K)> _ekHaritasi = [];
    private readonly Dictionary<string, IndirmeDurumu> _indirmeler = [];
    private readonly Dictionary<(string, Font), int> _olcumler = [];
    private readonly KoyuIpucu _ipucu = new();
    private readonly System.Windows.Forms.Timer _vurguSaati = new() { Interval = 1800 };
    private long? _vurguId, _bekleyenGit;
    private int _gitDenemesi, _sonYukseklik;

    public Api? Api { get; set; }
    public AvatarOnbellek? Avatarlar { get; set; }
    public Func<long, Profil?>? Cozucu { get; set; }
    public bool BasitMod { get; set; }
    public bool BaskalarininMesajlariniSilebilir { get; set; }
    public long? Secili { get => _secili; set { if (_secili != value) { _secili = value; Invalidate(); } } }
    private long? _secili;

    public event Action? EskiIste;
    public event Action<Mesaj>? TekrarDene, VazgecIstendi, YanitIstendi, DuzenleIstendi, SilIstendi;
    public event Action<Mesaj, string>? TepkiIstendi;
    public event Action<Mesaj, Point>? TepkiSeciciIstendi;   // Point: düğmenin üst-sağ köşesi (ekran koordinatı)
    public event Action<string>? BahsetmeTik;
    public event Action<string, bool>? Bildirim;

    private const TextFormatFlags Bicim = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
    private static Font MetinYazisi => Tema.Yazi(10.5f);
    private static readonly Color MetinRengi = ColorTranslator.FromHtml("#DCDDE3");
    private static readonly Color HoverRengi = ColorTranslator.FromHtml("#1B1C21");
    private static readonly Color Sari = ColorTranslator.FromHtml("#F5B544");

    public MesajListesi()
    {
        // Odak yazma kutusunda kalsın: liste seçilebilir değil.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Tema.Arkaplan;
        _vurguSaati.Tick += (_, _) => { _vurguSaati.Stop(); _vurguId = null; Invalidate(); };
    }

    private int MetinX => Tema.S(72);
    private int MetinW => Math.Max(Tema.S(80), Width - MetinX - Tema.S(34));
    private int IcW => Width - (KaydirmaVar ? Tema.S(12) : 0);
    private int MaksKaydirma => Math.Max(0, _toplamH - Height);
    private bool KaydirmaVar => _toplamH > Height;
    private bool Altta => _kaydirma >= MaksKaydirma - Tema.S(30);
    // İçerik kısaysa mesajlar alta yaslanır (sohbet uygulamalarındaki gibi).
    private int UstBosluk => Math.Max(0, Height - _toplamH);
    private int EkranY(Oge o) => o.Y - _kaydirma + UstBosluk;
    private int MaksResimKenar => Tema.S(400);

    // ---------------------------------------------------------------- Dışarıdan

    public void Ayarla(Profil ben, SohbetBasligi? baslik, KonusmaKimligi? kimlik, SohbetVerisi veri)
    {
        bool yeniSohbet = _kimlik != kimlik || !ReferenceEquals(_veri, veri);
        if (yeniSohbet)
        {
            _ipucuYazisi = "";
            _uzerinde = -1;
            _bekleyenGit = null;
            _vurguId = null;
            _secili = null;
            AnimKayitlariniBirak();
            _istenenResimler.Clear();
        }
        _ben = ben;
        _baslik = baslik;
        _kimlik = kimlik;
        _veri = veri;
        _tumu = veri.Tumu;
        bool alttaydi = Altta;
        Yerlestir(ankrajKoru: !yeniSohbet);
        if (yeniSohbet || alttaydi) _kaydirma = MaksKaydirma;
        else _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
        Invalidate();
        BekleyenGitiDene();
        EskileriGerekirseIste();
    }

    public void BasligiGuncelle(SohbetBasligi? baslik) { _baslik = baslik; Invalidate(); }

    public void UyeleriAyarla(IEnumerable<string> kullaniciAdlari, string? benKullaniciAdi)
    {
        _secenek = new ZenginSecenekler
        {
            Kullanicilar = new HashSet<string>(kullaniciAdlari.Select(k => k.ToLowerInvariant())),
            Ben = benKullaniciAdi?.ToLowerInvariant(),
        };
        _duzenler.Clear();
        if (_veri != null) { bool altta = Altta; Yerlestir(true); if (altta) _kaydirma = MaksKaydirma; Invalidate(); }
    }

    public void Alta() { _kaydirma = MaksKaydirma; Invalidate(); }

    public bool Altindami => Altta;

    // Mesaja kaydırır ve kısa süre vurgular. Mesaj yüklü değilse eski sayfaları ister, bulununca gider. Bulunduysa true.
    public bool MesajaGit(long id)
    {
        var o = _ogeler.FirstOrDefault(x => x.M?.Id == id);
        if (o is null)
        {
            if (_veri is { Tumu: false })
            {
                _bekleyenGit = id;
                _gitDenemesi = 0;
                EskiIste?.Invoke();
            }
            else Bildirim?.Invoke("Bu mesaj artık yok ya da çok eski.", false);
            return false;
        }
        _bekleyenGit = null;
        _kaydirma = Math.Clamp(o.Y - Height / 3, 0, MaksKaydirma);
        _vurguId = id;
        _vurguSaati.Stop();
        _vurguSaati.Start();
        Invalidate();
        return true;
    }

    private void BekleyenGitiDene()
    {
        if (_bekleyenGit is not { } id) return;
        if (_ogeler.Any(x => x.M?.Id == id)) { MesajaGit(id); return; }
        if (_veri is { Tumu: false, Yukleniyor: false } && ++_gitDenemesi <= 8) EskiIste?.Invoke();
        else if (_veri is { Yukleniyor: false }) { _bekleyenGit = null; Bildirim?.Invoke("Bu mesaj artık yok ya da çok eski.", false); }
    }

    // ---------------------------------------------------------------- Yerleşim

    private int Olc(string yazi, Font f)
    {
        if (_olcumler.TryGetValue((yazi, f), out var w)) return w;
        w = TextRenderer.MeasureText(yazi, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        if (_olcumler.Count > 2000) _olcumler.Clear();
        return _olcumler[(yazi, f)] = w;
    }

    private ZenginDuzen Duzen(Mesaj m, string metin, int w)
    {
        if (_duzenGenislik != w) { _duzenler.Clear(); _duzenGenislik = w; }
        if (!_duzenler.TryGetValue(m, out var d))
        {
            if (_duzenler.Count > 1500) _duzenler.Clear();
            _duzenler[m] = d = ZenginMetin.Yerlestir(metin, MetinYazisi, w, _secenek);
        }
        return d;
    }

    private void Yerlestir(bool ankrajKoru)
    {
        // Görünen ilk öğeyi hatırla: yerleşim değişince (eski mesajlar üste eklenince, resim yüklenince) ekran kaymasın.
        string? ankraj = null;
        int ankrajFark = 0;
        if (ankrajKoru && _ogeler.Count > 0 && MaksKaydirma > 0 && !Altta)
        {
            var a = _ogeler.FirstOrDefault(x => x.Y + x.H > _kaydirma);
            if (a != null) { ankraj = a.Anahtar; ankrajFark = _kaydirma - a.Y; }
        }

        _olcuW = MetinW;
        var yeni = new List<Oge>();
        _ekHaritasi.Clear();
        int y = 0;
        if (_tumu && _baslik != null && _kimlik != null)
        {
            int h = Tema.S(_kimlik.Value.Tur == KonusmaTuru.Dm ? 200 : 170);
            yeni.Add(new Oge { Tur = Tur.Baslangic, Y = y, H = h, Anahtar = "b" });
            y += h;
        }

        Mesaj? onceki = null;
        foreach (var m in _veri?.Liste ?? [])
        {
            var gun = Zaman.Yerel(m.Zaman).Date;
            if (onceki is null || Zaman.Yerel(onceki.Zaman).Date != gun)
            {
                yeni.Add(new Oge { Tur = Tur.Tarih, Gun = gun, Y = y, H = Tema.S(48), Anahtar = "t" + gun.Ticks });
                y += Tema.S(48);
                onceki = null;
            }
            bool grupBasi = onceki is null || onceki.GonderenId != m.GonderenId || m.Zaman - onceki.Zaman > 5 * 60 * 1000
                || (m.YanitId != null && !BasitMod && !m.Silindi);
            var o = MesajOgesi(m, grupBasi, gun, y);
            yeni.Add(o);
            y += o.H;
            onceki = m;
        }
        _ogeler = yeni;
        _toplamH = y + Tema.S(14);

        if (ankraj != null)
        {
            var a = _ogeler.FirstOrDefault(x => x.Anahtar == ankraj);
            if (a != null) _kaydirma = a.Y + ankrajFark;
        }
        _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
        if (_uzerinde >= _ogeler.Count) _uzerinde = -1;
    }

    private bool Basarisiz(Mesaj m) => m.IstemciId != null && m.Id < 0 && _veri?.Basarisiz.Contains(m.IstemciId) == true;

    private (int W, int H) ResimBoyutu(Ek ek)
    {
        int dw = ek.Genislik, dh = ek.Yukseklik;
        if ((dw <= 0 || dh <= 0) && _resimBoyutlari.TryGetValue(ek.OnizlemeUrl ?? ek.Url, out var b)) (dw, dh) = b;
        if (dw <= 0 || dh <= 0) (dw, dh) = (Tema.S(300), Tema.S(200));
        int maxW = Math.Min(MaksResimKenar, MetinW), maxH = Tema.S(300);
        double oran = Math.Min(1.0, Math.Min((double)maxW / dw, (double)maxH / dh));
        return (Math.Max(Tema.S(48), (int)Math.Round(dw * oran)), Math.Max(Tema.S(48), (int)Math.Round(dh * oran)));
    }

    private static bool ResimEki(Ek e) => e.Tur is "resim" or "gif" || e.Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && e.Tur != "dosya" && e.Tur != "video" && e.Tur != "ses";

    // Resim/GIF için gösterilecek adres: GIF'te orta boy Url, resimde küçük önizleme (varsa).
    private static string ResimAdresi(Ek e) => e.Tur == "gif" ? e.Url : e.OnizlemeUrl ?? e.Url;

    private Oge MesajOgesi(Mesaj m, bool grupBasi, DateTime gun, int y)
    {
        var o = new Oge { Tur = Tur.Mesaj, M = m, GrupBasi = grupBasi, Gun = gun, Y = y, Anahtar = "m" + (m.IstemciId ?? m.Id.ToString()) };
        int w = _olcuW, x = MetinX, cy = 0;
        o.Bahsetti = !m.Silindi && m.GonderenId != _ben?.Id && _ben != null && m.Bahsedilenler?.Contains(_ben.Id) == true;
        if (grupBasi) cy += Tema.S(14);
        o.HoverUst = grupBasi ? Tema.S(12) : 0;

        if (!BasitMod && m.YanitId != null && !m.Silindi)
        {
            o.Yanit = new Rectangle(x, cy, Math.Min(w, Tema.S(560)), Tema.S(20));
            cy += Tema.S(22);
        }
        if (grupBasi) { o.BaslikY = cy; cy += Tema.S(22); }
        else cy += Tema.S(1);
        o.MetinY = cy;

        bool oncekiBlok = false;
        if (m.Silindi)
        {
            o.SilindiY = cy;
            cy += Tema.S(22);
        }
        else
        {
            string metin = BasitMod ? MesajYardimci.KisaMetin(m) : m.Metin;
            if (metin.Length > 0)
            {
                var d = Duzen(m, metin, w);
                o.Duzen = d;
                cy += d.Yukseklik;
                oncekiBlok = true;
                if (m.Duzenlenme != null && !BasitMod)
                {
                    int etiketW = Olc("(düzenlendi)", Tema.Yazi(8f)) + Tema.S(4);
                    var son = d.Parcalar.Count > 0 ? d.Parcalar[^1] : null;
                    if (son != null && !d.BuyukEmoji && son.X + son.W + etiketW <= w)
                        o.DuzenlendiKonum = new Point(x + son.X + son.W + Tema.S(4), o.MetinY + son.Satir * d.SatirH + (d.SatirH - Tema.S(14)) / 2);
                    else { o.DuzenlendiKonum = new Point(x, cy + Tema.S(1)); cy += Tema.S(16); }
                }
            }

            if (!BasitMod)
            {
                // Ekler
                int i = 0;
                foreach (var ek in m.Ekler ?? [])
                {
                    if (oncekiBlok) cy += Tema.S(4);
                    oncekiBlok = true;
                    var k = new EkKutu { Ek = ek, Anahtar = o.Anahtar + ":" + i++ };
                    if (ResimEki(ek))
                    {
                        var (bw, bh) = ResimBoyutu(ek);
                        k.Resim = true;
                        k.Alan = new Rectangle(x, cy, bw, bh);
                        cy += bh;
                    }
                    else
                    {
                        int cw = Math.Min(Tema.S(420), w);
                        k.Alan = new Rectangle(x, cy, cw, Tema.S(64));
                        int dh = Tema.S(30), dy = cy + (Tema.S(64) - dh) / 2;
                        k.Ac = new Rectangle(k.Alan.Right - Tema.S(14) - Tema.S(52), dy, Tema.S(52), dh);
                        k.Indir = new Rectangle(k.Ac.Left - Tema.S(8) - Tema.S(70), dy, Tema.S(70), dh);
                        cy += Tema.S(64);
                    }
                    o.Ekler.Add(k);
                    _ekHaritasi[k.Anahtar] = (o, k);
                }

                // Yüklenmekte olan dosyalar
                if (m.IstemciId != null && _veri?.Bekleyenler.TryGetValue(m.IstemciId, out var b) == true && b.Dosyalar.Count > 0)
                {
                    o.Bekleyen = b;
                    for (int j = 0; j < b.Dosyalar.Count; j++)
                    {
                        if (oncekiBlok) cy += Tema.S(4);
                        oncekiBlok = true;
                        o.Yuklemeler.Add((new Rectangle(x, cy, Math.Min(Tema.S(420), w), Tema.S(52)), b.Dosyalar[j], j));
                        cy += Tema.S(52);
                    }
                }

                // Tepkiler
                if (m.Tepkiler is { Count: > 0 })
                {
                    if (oncekiBlok) cy += Tema.S(4);
                    oncekiBlok = true;
                    int px = x, py = cy, ph = Tema.S(24), gap = Tema.S(4);
                    var sayiF = Tema.Yazi(9f, kalin: true);
                    foreach (var t in m.Tepkiler)
                    {
                        int pw = Tema.S(10) + Tema.S(16) + Tema.S(5) + Olc(t.Kullanicilar.Count.ToString(), sayiF) + Tema.S(10);
                        if (px > x && px + pw > x + w) { px = x; py += ph + gap; }
                        o.Tepkiler.Add(new TepkiKutu { T = t, Alan = new Rectangle(px, py, pw, ph), Ben = _ben != null && t.Kullanicilar.Contains(_ben.Id) });
                        px += pw + gap;
                    }
                    int artiW = Tema.S(34);
                    if (px > x && px + artiW > x + w) { px = x; py += ph + gap; }
                    o.TepkiArti = new Rectangle(px, py, artiW, ph);
                    cy = py + ph;
                }
            }
        }

        if (Basarisiz(m))
        {
            cy += Tema.S(3);
            o.BasarisizY = cy;
            var f = Tema.Yazi(8.5f, kalin: true);
            int a = Olc("Gönderilemedi. ", f), t = Olc("Tekrar dene", f), s = Olc(" · ", f);
            o.TekrarDeneAlani = new Rectangle(x + a, cy, t, Tema.S(16));
            o.VazgecAlani = new Rectangle(x + a + t + s, cy, Olc("Vazgeç", f), Tema.S(16));
            cy += Tema.S(18);
        }

        cy += Tema.S(grupBasi ? 3 : 2);
        o.H = Math.Max(cy, grupBasi ? o.BaslikY + Tema.S(44) : Tema.S(24));
        return o;
    }

    // ---------------------------------------------------------------- Çizim

    // Ekranda görünen ilk öğenin indeksi (ikili arama).
    private int IlkGorunen()
    {
        int lo = 0, hi = _ogeler.Count - 1, ilk = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_ogeler[mid].Y + _ogeler[mid].H < _kaydirma - UstBosluk) lo = mid + 1; else { ilk = mid; hi = mid - 1; }
        }
        return ilk;
    }

    private IEnumerable<Oge> Gorunenler()
    {
        for (int i = IlkGorunen(); i < _ogeler.Count; i++)
        {
            if (EkranY(_ogeler[i]) > Height) yield break;
            yield return _ogeler[i];
        }
    }

    private Profil Sahibi(long id)
    {
        if (id == _ben?.Id) return _ben;
        return Cozucu?.Invoke(id) ?? new Profil(id, "", "Bilinmeyen", "", "", "#64748B", 0, Durum.Cevrimdisi);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_ben is null || _veri is null) return;
        var klip = e.ClipRectangle;

        if (_ogeler.Count == 0)
        {
            string yazi = _veri.YuklemeHatasi ? "Mesajlar yüklenemedi. Tekrar denemek için tıkla." : !_veri.Yuklendi ? "Mesajlar yükleniyor…" : "";
            if (yazi.Length > 0)
                TextRenderer.DrawText(g, yazi, Tema.Yazi(10), ClientRectangle, Tema.MetinSoluk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }

        int w = IcW;
        for (int i = IlkGorunen(); i < _ogeler.Count; i++)
        {
            var o = _ogeler[i];
            int y = EkranY(o);
            if (y > Height || y > klip.Bottom) break;
            if (y + o.H < klip.Top) continue;
            switch (o.Tur)
            {
                case Tur.Baslangic: BaslangicCiz(g, o, y, w); break;
                case Tur.Tarih: TarihCiz(g, o, y, w); break;
                default: MesajCiz(g, o, i, y, w); break;
            }
        }

        if (KaydirmaVar) Cizim.YuvarlakDoldur(g, KaydirmaCubugu(), Tema.S(3), _surukleBaslangic != null ? Tema.KenarHover : Tema.Kenar);

        if (AltaPilGorunur)
        {
            var r = AltaPilAlani;
            bool u = _hedef.Tur == HedefTuru.AltaPil;
            Cizim.YuvarlakDoldur(g, r, r.Height / 2f, u ? Tema.VurguHover : Tema.Vurgu);
            TextRenderer.DrawText(g, "Güncele dön  ↓", Tema.Yazi(9, kalin: true), r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        AnimKayitlariniGuncelle();
    }

    private bool AltaPilGorunur => MaksKaydirma - _kaydirma > Height;
    private Rectangle AltaPilAlani
    {
        get
        {
            int pw = Olc("Güncele dön  ↓", Tema.Yazi(9, kalin: true)) + Tema.S(32), ph = Tema.S(30);
            return new Rectangle((IcW - pw) / 2, Height - ph - Tema.S(12), pw, ph);
        }
    }

    private void BaslangicCiz(Graphics g, Oge o, int y, int w)
    {
        var b = _baslik!;
        int x = Tema.S(18), av = Tema.S(80);
        string baslik, aciklama;
        switch (_kimlik!.Value.Tur)
        {
            case KonusmaTuru.Dm:
                if (b.Karsi != null) Cizim.Avatar(g, new RectangleF(x, y + Tema.S(24), av, av), b.Karsi, Avatarlar?.Al(b.Karsi), BackColor, false);
                baslik = b.Ad;
                aciklama = $"Bu, {b.Ad} ile olan mesajlaşmanın başlangıcı. Selam ver!";
                break;
            case KonusmaTuru.Kanal:
            {
                var r = new Rectangle(x, y + Tema.S(24), av, av);
                Cizim.YuvarlakDoldur(g, r, av / 2f, Tema.YuzeyUst);
                TextRenderer.DrawText(g, "#", Tema.Yazi(30, kalin: true), r, Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                baslik = $"#{b.Ad} kanalına hoş geldin!";
                aciklama = $"Burası #{b.Ad} kanalının başlangıcı." + (string.IsNullOrWhiteSpace(b.Alt) ? "" : " " + b.Alt);
                break;
            }
            default:
                IkonCizim.Grup(g, new RectangleF(x, y + Tema.S(24), av, av), b.Renk, b.Ikon);
                baslik = b.Ad;
                aciklama = $"Bu, {b.Ad} grubunun başlangıcı. Selam ver!";
                break;
        }
        TextRenderer.DrawText(g, baslik, Tema.Yazi(18, kalin: true), new Rectangle(x - Tema.S(2), y + Tema.S(112), w - x * 2, Tema.S(34)), Tema.Metin,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, aciklama, Tema.Yazi(10.5f), new Rectangle(x, y + Tema.S(150), w - x * 2, Tema.S(24)), Tema.MetinIkincil,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private void TarihCiz(Graphics g, Oge o, int y, int w)
    {
        int orta = y + o.H / 2 + Tema.S(4);
        var yazi = Zaman.Gun(o.Gun);
        var f = Tema.Yazi(8.5f, kalin: true);
        int tw = Olc(yazi, f);
        int tx = (w - tw) / 2;
        using (var p = new Pen(Tema.Kenar))
        {
            g.DrawLine(p, Tema.S(18), orta, tx - Tema.S(10), orta);
            g.DrawLine(p, tx + tw + Tema.S(10), orta, w - Tema.S(18), orta);
        }
        TextRenderer.DrawText(g, yazi, f, new Point(tx, orta - Tema.S(8)), Tema.MetinSoluk, TextFormatFlags.NoPadding);
    }

    private void MesajCiz(Graphics g, Oge o, int idx, int y, int w)
    {
        var m = o.M!;
        bool uzerinde = idx == _uzerinde;
        bool vurgulu = _vurguId == m.Id;
        bool secili = _secili == m.Id;

        // Zemin
        Color? zemin = null;
        if (vurgulu) zemin = Color.FromArgb(46, Tema.Vurgu);
        else if (secili) zemin = Color.FromArgb(26, Tema.Vurgu);
        else if (o.Bahsetti) zemin = Color.FromArgb(uzerinde ? 44 : 30, Sari);
        else if (uzerinde) zemin = HoverRengi;
        int zy = y + o.HoverUst;
        if (zemin is { } zc) using (var b = new SolidBrush(zc)) g.FillRectangle(b, 0, zy, w, o.H - o.HoverUst);
        if (o.Bahsetti || secili || vurgulu)
            using (var b = new SolidBrush(o.Bahsetti ? Sari : Tema.Vurgu)) g.FillRectangle(b, 0, zy, Tema.S(3), o.H - o.HoverUst);

        // Yanıt alıntısı
        if (o.Yanit is { } yr) YanitCiz(g, o, new Rectangle(yr.X, y + yr.Y, yr.Width, yr.Height));

        // Başlık: avatar, ad, zaman
        if (o.GrupBasi)
        {
            var sahip = Sahibi(m.GonderenId);
            int av = Tema.S(40);
            int ay = y + o.BaslikY - Tema.S(2);
            Cizim.Avatar(g, new RectangleF(Tema.S(18), ay, av, av), sahip, Avatarlar?.Al(sahip), uzerinde ? HoverRengi : BackColor, false);
            var adF = Tema.Yazi(10.5f, kalin: true);
            int adW = Math.Min(Olc(sahip.GorunenAd, adF), Math.Max(Tema.S(40), w - MetinX - Tema.S(200)));
            TextRenderer.DrawText(g, sahip.GorunenAd, adF, new Rectangle(MetinX, y + o.BaslikY, adW + 2, Tema.S(20)), Tema.Metin, Bicim);
            TextRenderer.DrawText(g, Zaman.Uzun(m.Zaman), Tema.Yazi(8.5f), new Rectangle(MetinX + adW + Tema.S(8), y + o.BaslikY, Tema.S(150), Tema.S(20)), Tema.MetinSoluk, Bicim & ~TextFormatFlags.EndEllipsis);
        }
        else if (uzerinde)
            TextRenderer.DrawText(g, Zaman.Kisa(m.Zaman), Tema.Yazi(8), new Rectangle(0, y + o.MetinY, MetinX - Tema.S(10), Tema.S(22)), Tema.MetinSoluk,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Metin
        bool basarisiz = Basarisiz(m);
        if (m.Silindi)
        {
            TextRenderer.DrawText(g, "Bu mesaj silindi.", Tema.Yazi(10.5f, stil: FontStyle.Italic), new Rectangle(MetinX, y + o.SilindiY, w - MetinX, Tema.S(22)), Tema.MetinSoluk, Bicim);
        }
        else
        {
            var renk = basarisiz ? Tema.Hata : m.Id < 0 ? Tema.MetinSoluk : MetinRengi;
            o.Duzen?.Ciz(g, MetinX, y + o.MetinY, renk);
            if (o.DuzenlendiKonum is { } dk)
                TextRenderer.DrawText(g, "(düzenlendi)", Tema.Yazi(8f), new Point(dk.X, y + dk.Y), Tema.MetinSoluk, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            foreach (var k in o.Ekler)
            {
                var r = k.Alan; r.Offset(0, y);
                if (k.Resim) ResimCiz(g, k, r); else KartCiz(g, k, r);
            }
            foreach (var (alan, dosya, no) in o.Yuklemeler)
            {
                var r = alan; r.Offset(0, y);
                YuklemeKartiCiz(g, o.Bekleyen!, dosya, no, r, basarisiz);
            }
            foreach (var t in o.Tepkiler)
            {
                var r = t.Alan; r.Offset(0, y);
                TepkiCiz(g, t, r);
            }
            if (o.TepkiArti is { } ta)
            {
                var r = ta; r.Offset(0, y);
                bool u = _hedef.Tur == HedefTuru.TepkiArti && _hedef.Oge == idx;
                Cizim.YuvarlakDoldur(g, r, r.Height / 2f, u ? Tema.Kenar : Tema.Yuzey);
                TextRenderer.DrawText(g, Glif.Arti, Cizim.Ikon(8f), r, u ? Tema.Metin : Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        if (basarisiz && o.TekrarDeneAlani is { } td && o.VazgecAlani is { } vg)
        {
            var f = Tema.Yazi(8.5f, kalin: true);
            int by = y + o.BasarisizY;
            TextRenderer.DrawText(g, "Gönderilemedi. ", f, new Point(MetinX, by), Tema.Hata, TextFormatFlags.NoPadding);
            bool tu = _hedef.Tur == HedefTuru.TekrarDene && _hedef.Oge == idx, vu = _hedef.Tur == HedefTuru.Vazgec && _hedef.Oge == idx;
            TextRenderer.DrawText(g, "Tekrar dene", Tema.Yazi(8.5f, true, tu ? FontStyle.Underline : FontStyle.Regular), new Point(td.X, by), Tema.Hata, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, " · ", f, new Point(td.Right, by), Tema.MetinSoluk, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Vazgeç", Tema.Yazi(8.5f, true, vu ? FontStyle.Underline : FontStyle.Regular), new Point(vg.X, by), Tema.MetinIkincil, TextFormatFlags.NoPadding);
        }

        if (uzerinde && !BasitMod) AracCubuguCiz(g, o, y, w);
    }

    // ---- Yanıt alıntısı

    private void YanitCiz(Graphics g, Oge o, Rectangle r)
    {
        var m = o.M!;
        var gercek = _veri?.Liste.FirstOrDefault(x => x.Id == m.YanitId);
        var oz = gercek != null ? MesajYardimci.Ozet(gercek) : m.Yanit;
        int orta = r.Y + r.Height / 2;

        // Avatara bağlayan kancalı çizgi
        int xc = Tema.S(38), rad = Tema.S(7);
        using (var kalem = new Pen(Color.FromArgb(120, Tema.MetinSoluk), Math.Max(1.5f, Tema.S(2) * 0.8f)))
        {
            var eski = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var yol = new GraphicsPath();
            yol.AddLine(xc, o.Y == 0 ? r.Bottom + Tema.S(2) : r.Bottom + Tema.S(2), xc, orta + rad);
            yol.AddArc(xc, orta, rad * 2, rad * 2, 180, 90);
            yol.AddLine(xc + rad, orta, MetinX - Tema.S(8), orta);
            g.DrawPath(kalem, yol);
            g.SmoothingMode = eski;
        }

        int x = r.X;
        if (oz is null)
        {
            TextRenderer.DrawText(g, "Orijinal mesaj yüklenmedi", Tema.Yazi(9f, stil: FontStyle.Italic), new Rectangle(x, r.Y, r.Width, r.Height), Tema.MetinSoluk, Bicim);
            return;
        }
        bool u = _hedef.Tur == HedefTuru.Yanit && _hedef.Oge >= 0 && _ogeler[_hedef.Oge] == o;
        var yazan = Sahibi(oz.GonderenId);
        int av = Tema.S(16);
        Cizim.Avatar(g, new RectangleF(x, r.Y + (r.Height - av) / 2f, av, av), yazan, Avatarlar?.Al(yazan), BackColor, false);
        x += av + Tema.S(6);
        var adF = Tema.Yazi(9f, kalin: true);
        int adW = Math.Min(Olc(yazan.GorunenAd, adF), Tema.S(160));
        TextRenderer.DrawText(g, yazan.GorunenAd, adF, new Rectangle(x, r.Y, adW + 2, r.Height), u ? Tema.Metin : Tema.MetinIkincil, Bicim);
        x += adW + Tema.S(6);
        string metin;
        FontStyle stil = FontStyle.Regular;
        if (oz.Silindi) { metin = "Orijinal mesaj silindi."; stil = FontStyle.Italic; }
        else
        {
            metin = BicimiTemizle(oz.Metin.Replace('\n', ' '));
            if (oz.EkVar) metin = metin.Length == 0 ? "📎 Ek" : metin + " 📎";
            if (gercek?.Ekler is { Count: > 0 } ek && gercek.Metin.Length == 0) metin = MesajYardimci.EkOzeti(ek[0]);
        }
        if (x < r.Right)
            YanitMetniCiz(g, metin, stil, new Rectangle(x, r.Y, r.Right - x, r.Height), u);
    }

    // Alıntıda **kalın**, *italik*, `kod`, ~~çizili~~ işaretleri görünmesin.
    private static string BicimiTemizle(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\*\*(.+?)\*\*|~~(.+?)~~|`(.+?)`|(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", m =>
            m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);

    // Tek satır yazı + renkli emoji (kısaltarak).
    private static void YanitMetniCiz(Graphics g, string metin, FontStyle stil, Rectangle alan, bool u)
    {
        var f = Tema.Yazi(9f, stil: stil);
        var renk = u ? Tema.MetinIkincil : Tema.MetinSoluk;
        int x = alan.X, i = 0;
        int emoji = Tema.S(14);
        while (i < metin.Length && x < alan.Right)
        {
            int eu = EmojiCizici.Uzunluk(metin, i);
            if (eu > 0)
            {
                if (x + emoji > alan.Right) break;
                EmojiCizici.Ciz(g, metin.Substring(i, eu), x, alan.Y + (alan.Height - emoji) / 2, emoji);
                x += emoji + Tema.S(2);
                i += eu;
                continue;
            }
            int j = i;
            while (j < metin.Length && EmojiCizici.Uzunluk(metin, j) == 0) j++;
            var parca = metin[i..j];
            int pw = TextRenderer.MeasureText(parca, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            bool son = j >= metin.Length;
            TextRenderer.DrawText(g, parca, f, new Rectangle(x, alan.Y, Math.Min(pw + 2, alan.Right - x), alan.Height), renk,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            x += pw;
            if (!son && x >= alan.Right) break;
            i = j;
        }
    }

    // ---- Ekler

    private void ResimCiz(Graphics g, EkKutu k, Rectangle r)
    {
        var adres = ResimAdresi(k.Ek);
        Image? resim = Api is null ? null : DosyaOnbellegi.BellektenAl(Api, adres, MaksResimKenar);
        if (resim is null && Api != null) ResimIste(adres);

        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var yol = Tema.YuvarlakDikdortgen(r, Tema.S(8));
        if (resim is null)
        {
            using (var b = new SolidBrush(Tema.Yuzey)) g.FillPath(b, yol);
            using (var p = new Pen(Tema.Kenar)) g.DrawPath(p, yol);
            bool hata = _basarisizResimler.Contains(adres);
            TextRenderer.DrawText(g, hata ? "Resim yüklenemedi" : "Yükleniyor…", Tema.Yazi(9), r, Tema.MetinSoluk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            g.SmoothingMode = eski;
            return;
        }
        var anim = AnimasyonluResim.Bul(resim);
        Image kare = anim?.GecerliKare ?? resim;
        using (var f = new TextureBrush(kare, WrapMode.Clamp))
        {
            f.TranslateTransform(r.X, r.Y);
            f.ScaleTransform(r.Width / (float)kare.Width, r.Height / (float)kare.Height);
            g.FillPath(f, yol);
        }
        g.SmoothingMode = eski;
        bool u = _hedef.Tur == HedefTuru.Resim && _hedef.Veri == k;
        if (u) using (var p = new Pen(Color.FromArgb(90, Color.White))) { g.SmoothingMode = SmoothingMode.AntiAlias; g.DrawPath(p, yol); g.SmoothingMode = eski; }
        if (k.Ek.Tur == "gif")
        {
            var etiket = new Rectangle(r.X + Tema.S(8), r.Y + Tema.S(8), Tema.S(34), Tema.S(18));
            Cizim.YuvarlakDoldur(g, etiket, Tema.S(4), Color.FromArgb(170, 0, 0, 0));
            TextRenderer.DrawText(g, "GIF", Tema.Yazi(7.5f, kalin: true), etiket, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private async void ResimIste(string adres)
    {
        if (Api is null || _basarisizResimler.Contains(adres) || !_istenenResimler.Add(adres)) return;
        Image? r = null;
        try { r = await DosyaOnbellegi.ResimAl(Api, adres, MaksResimKenar); } catch { }
        _istenenResimler.Remove(adres);
        if (IsDisposed) return;
        if (r is null) { _basarisizResimler.Add(adres); Invalidate(); return; }

        // Boyutu bilinmeyen resimde gerçek orana göre yerleşimi güncelle.
        bool bilinmiyor = _ogeler.Any(o => o.Ekler.Any(k => k.Resim && ResimAdresi(k.Ek) == adres && (k.Ek.Genislik <= 0 || k.Ek.Yukseklik <= 0)));
        if (bilinmiyor && !_resimBoyutlari.ContainsKey(adres))
        {
            _resimBoyutlari[adres] = (r.Width, r.Height);
            bool altta = Altta;
            Yerlestir(true);
            if (altta) _kaydirma = MaksKaydirma;
        }
        Invalidate();
    }

    private void KartCiz(Graphics g, EkKutu k, Rectangle r)
    {
        var ek = k.Ek;
        var durum = _indirmeler.GetValueOrDefault(ek.Url);
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), Tema.S(8)))
        {
            using var b = new SolidBrush(Tema.Yuzey);
            using var p = new Pen(Tema.Kenar);
            g.FillPath(b, yol);
            g.DrawPath(p, yol);
        }
        g.SmoothingMode = eski;

        int ib = Tema.S(40);
        var ikon = new Rectangle(r.X + Tema.S(12), r.Y + (r.Height - ib) / 2, ib, ib);
        Cizim.YuvarlakDoldur(g, ikon, Tema.S(8), Tema.VurguKoyu);
        string glif = ek.Tur switch { "video" => "", "ses" => "", _ => "" };
        TextRenderer.DrawText(g, glif, Cizim.Ikon(14), ikon, Tema.VurguHover, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int tx = ikon.Right + Tema.S(12), tw = Math.Max(Tema.S(30), k.Indir.Left - Tema.S(10) - tx);
        TextRenderer.DrawText(g, ek.Ad, Tema.Yazi(10, kalin: true), new Rectangle(tx, r.Y + Tema.S(12), tw, Tema.S(20)), Tema.Metin, Bicim);
        string alt = durum is { Hata: true } ? "İndirilemedi. Tekrar dene." : durum is { Yol: not null } ? "İndirilenler klasörüne kaydedildi" : ResimGoruntuleyici.BoyutYazisi(ek.Boyut);
        TextRenderer.DrawText(g, alt, Tema.Yazi(8.5f), new Rectangle(tx, r.Y + Tema.S(33), tw, Tema.S(18)),
            durum is { Hata: true } ? Tema.Hata : durum is { Yol: not null } ? Tema.Basari : Tema.MetinSoluk, Bicim);

        bool mesgul = durum is { Iniyor: true } or { Aciliyor: true };
        var indir = k.Indir; indir.Offset(0, r.Y - k.Alan.Y);
        var ac = k.Ac; ac.Offset(0, r.Y - k.Alan.Y);
        bool iu = _hedef.Tur == HedefTuru.Indir && _hedef.Veri == k, au = _hedef.Tur == HedefTuru.Ac && _hedef.Veri == k;
        string indirYazi = durum is { Iniyor: true } ? $"%{(int)(durum.Ilerleme * 100)}" : durum is { Yol: not null } ? "Klasör" : "İndir";
        KartDugmesi(g, indir, indirYazi, iu && !mesgul, false, mesgul && durum!.Iniyor);
        KartDugmesi(g, ac, durum is { Aciliyor: true } ? $"%{(int)(durum.Ilerleme * 100)}" : "Aç", au && !mesgul, true, mesgul && durum!.Aciliyor);

        if (mesgul)
        {
            var bar = new Rectangle(r.X + Tema.S(12), r.Bottom - Tema.S(6), r.Width - Tema.S(24), Tema.S(3));
            Cizim.YuvarlakDoldur(g, bar, bar.Height / 2f, Tema.Kenar);
            if (durum!.Ilerleme > 0.01) Cizim.YuvarlakDoldur(g, new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Min(1, durum.Ilerleme)), bar.Height), bar.Height / 2f, Tema.Vurgu);
        }
    }

    private static void KartDugmesi(Graphics g, Rectangle r, string yazi, bool hover, bool birincil, bool meşgul)
    {
        Color zemin = birincil ? (hover ? Tema.VurguHover : Tema.Vurgu) : (hover ? Tema.Kenar : Tema.YuzeyUst);
        if (meşgul) zemin = Color.FromArgb(120, zemin);
        Cizim.YuvarlakDoldur(g, r, Tema.S(6), zemin);
        TextRenderer.DrawText(g, yazi, Tema.Yazi(9, kalin: true), r, birincil ? Color.White : Tema.Metin, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void YuklemeKartiCiz(Graphics g, BekleyenGonderim b, GonderilecekDosya d, int no, Rectangle r, bool basarisiz)
    {
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), Tema.S(8)))
        {
            using var br = new SolidBrush(Tema.Yuzey);
            using var p = new Pen(basarisiz ? Tema.Hata : Tema.Kenar);
            g.FillPath(br, yol);
            g.DrawPath(p, yol);
        }
        g.SmoothingMode = eski;
        int ib = Tema.S(32);
        var ikon = new Rectangle(r.X + Tema.S(10), r.Y + (r.Height - ib) / 2, ib, ib);
        Cizim.YuvarlakDoldur(g, ikon, Tema.S(8), Tema.VurguKoyu);
        TextRenderer.DrawText(g, d.Resim ? "" : "", Cizim.Ikon(12), ikon, Tema.VurguHover, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        int tx = ikon.Right + Tema.S(10), tw = r.Right - tx - Tema.S(12);
        TextRenderer.DrawText(g, d.Ad, Tema.Yazi(9.5f, kalin: true), new Rectangle(tx, r.Y + Tema.S(7), tw, Tema.S(18)), Tema.Metin, Bicim);
        double p0 = no < b.Ilerleme.Length ? b.Ilerleme[no] : 0;
        string durum = basarisiz ? "Gönderilemedi" : p0 >= 1 ? "Gönderiliyor…" : b.Yukluyor ? $"Yükleniyor… %{(int)(p0 * 100)}" : "Sırada";
        TextRenderer.DrawText(g, durum, Tema.Yazi(8.5f), new Rectangle(tx, r.Y + Tema.S(25), tw, Tema.S(16)), basarisiz ? Tema.Hata : Tema.MetinSoluk, Bicim);
        var bar = new Rectangle(tx, r.Bottom - Tema.S(8), tw, Tema.S(3));
        Cizim.YuvarlakDoldur(g, bar, bar.Height / 2f, Tema.Kenar);
        if (p0 > 0.01) Cizim.YuvarlakDoldur(g, new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Min(1, p0)), bar.Height), bar.Height / 2f, basarisiz ? Tema.Hata : Tema.Vurgu);
    }

    // ---- Tepkiler

    private void TepkiCiz(Graphics g, TepkiKutu t, Rectangle r)
    {
        bool u = _hedef.Tur == HedefTuru.Tepki && _hedef.Veri == t;
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), r.Height / 2f))
        {
            using var b = new SolidBrush(t.Ben ? Color.FromArgb(u ? 90 : 64, Tema.Vurgu) : u ? Tema.YuzeyUst : Tema.Yuzey);
            using var p = new Pen(t.Ben ? Tema.Vurgu : u ? Tema.KenarHover : Tema.Kenar);
            g.FillPath(b, yol);
            g.DrawPath(p, yol);
        }
        g.SmoothingMode = eski;
        int e = Tema.S(16);
        EmojiCizici.Ciz(g, t.T.Emoji, r.X + Tema.S(10), r.Y + (r.Height - e) / 2, e);
        TextRenderer.DrawText(g, t.T.Kullanicilar.Count.ToString(), Tema.Yazi(9f, kalin: true),
            new Rectangle(r.X + Tema.S(10) + e + Tema.S(5), r.Y, r.Width, r.Height), t.Ben ? Color.White : Tema.MetinIkincil,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    // ---- Araç çubuğu

    private List<Arac> Araclar(Mesaj m)
    {
        var l = new List<Arac> { Arac.Tepki, Arac.Yanitla };
        if (m.GonderenId == _ben?.Id) l.Add(Arac.Duzenle);
        if (m.GonderenId == _ben?.Id || BaskalarininMesajlariniSilebilir) l.Add(Arac.Sil);
        l.Add(Arac.Diger);
        return l;
    }

    private bool AracVar(Oge o) => !BasitMod && o.Tur == Tur.Mesaj && o.M is { Id: > 0, Silindi: false };

    private Rectangle AracAlani(Oge o, int ekranY, int w)
    {
        int n = Araclar(o.M!).Count, bw = Tema.S(32);
        int tw = n * bw + Tema.S(6);
        return new Rectangle(w - Tema.S(22) - tw, Math.Max(Tema.S(2), ekranY + o.HoverUst - Tema.S(14)), tw, Tema.S(30));
    }

    private Rectangle AracDugmesi(Oge o, int ekranY, int w, int sira)
    {
        var a = AracAlani(o, ekranY, w);
        int bw = Tema.S(32);
        return new Rectangle(a.X + Tema.S(3) + sira * bw, a.Y + Tema.S(3), bw, a.Height - Tema.S(6));
    }

    private void AracCubuguCiz(Graphics g, Oge o, int y, int w)
    {
        if (!AracVar(o)) return;
        var a = AracAlani(o, y, w);
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(a.X + .5f, a.Y + .5f, a.Width - 1, a.Height - 1), Tema.S(6)))
        {
            using var b = new SolidBrush(Tema.YuzeyUst);
            using var p = new Pen(Tema.Kenar);
            g.FillPath(b, yol);
            g.DrawPath(p, yol);
        }
        var liste = Araclar(o.M!);
        for (int i = 0; i < liste.Count; i++)
        {
            var r = AracDugmesi(o, y, w, i);
            bool u = _hedef.Tur == HedefTuru.Arac && _hedef.No == i && _hedef.Oge >= 0 && _ogeler[_hedef.Oge] == o;
            bool tehlike = liste[i] == Arac.Sil;
            if (u) Cizim.YuvarlakDoldur(g, r, Tema.S(4), tehlike ? Color.FromArgb(60, Tema.Hata) : Tema.Kenar);
            var renk = u ? (tehlike ? Tema.Hata : Tema.Metin) : Tema.MetinIkincil;
            IkonCiz(g, liste[i], r, renk);
        }
        g.SmoothingMode = eski;
    }

    private static void IkonCiz(Graphics g, Arac a, Rectangle r, Color renk)
    {
        float s = Tema.S(16);
        float x0 = r.X + (r.Width - s) / 2f, y0 = r.Y + (r.Height - s) / 2f;
        float k = s / 16f;
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var kalem = new Pen(renk, Math.Max(1.3f, 1.4f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (a)
        {
            case Arac.Tepki:
                g.DrawEllipse(kalem, x0 + 1.5f * k, y0 + 1.5f * k, 13 * k, 13 * k);
                using (var b = new SolidBrush(renk))
                {
                    g.FillEllipse(b, x0 + 5.2f * k, y0 + 6f * k, 1.9f * k, 1.9f * k);
                    g.FillEllipse(b, x0 + 9f * k, y0 + 6f * k, 1.9f * k, 1.9f * k);
                }
                g.DrawArc(kalem, x0 + 4.6f * k, y0 + 4.6f * k, 6.8f * k, 6.8f * k, 25, 130);
                break;
            case Arac.Yanitla:
                g.DrawLines(kalem, [new PointF(x0 + 6f * k, y0 + 3f * k), new PointF(x0 + 1.5f * k, y0 + 7.5f * k), new PointF(x0 + 6f * k, y0 + 12f * k)]);
                g.DrawLine(kalem, x0 + 1.5f * k, y0 + 7.5f * k, x0 + 9f * k, y0 + 7.5f * k);
                g.DrawBezier(kalem, new PointF(x0 + 9f * k, y0 + 7.5f * k), new PointF(x0 + 14f * k, y0 + 7.5f * k), new PointF(x0 + 14.5f * k, y0 + 10f * k), new PointF(x0 + 14.5f * k, y0 + 13f * k));
                break;
            case Arac.Duzenle:
                TextRenderer.DrawText(g, Glif.Duzenle, Cizim.Ikon(9.5f), r, renk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;
            case Arac.Sil:
                TextRenderer.DrawText(g, "", Cizim.Ikon(9.5f), r, renk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;
            default:
                using (var b = new SolidBrush(renk))
                    for (int i = 0; i < 3; i++) g.FillEllipse(b, x0 + (2f + i * 4.5f) * k, y0 + 6.3f * k, 2.6f * k, 2.6f * k);
                break;
        }
        g.SmoothingMode = eski;
    }

    private Rectangle KaydirmaCubugu()
    {
        int h = Math.Max(Tema.S(40), Height * Height / Math.Max(1, _toplamH));
        int y = MaksKaydirma == 0 ? 0 : (Height - h) * _kaydirma / MaksKaydirma;
        return new Rectangle(Width - Tema.S(9), y, Tema.S(6), h);
    }

    // ---------------------------------------------------------------- Animasyonlu GIF kayıtları

    // Sadece ekranda görünen GIF'ler ortak zamanlayıcıya kayıtlı olur; kaydırılıp çıkanların kaydı silinir.
    private void AnimKayitlariniGuncelle()
    {
        if (Api is null) return;
        HashSet<string>? gorunen = null;
        foreach (var o in Gorunenler())
        {
            if (o.Tur != Tur.Mesaj) continue;
            foreach (var k in o.Ekler)
            {
                if (!k.Resim) continue;
                var resim = DosyaOnbellegi.BellektenAl(Api, ResimAdresi(k.Ek), MaksResimKenar);
                var anim = AnimasyonluResim.Bul(resim);
                if (anim is null) continue;
                (gorunen ??= []).Add(k.Anahtar);
                if (!_animKayitlari.ContainsKey(k.Anahtar))
                {
                    var anahtar = k.Anahtar;
                    _animKayitlari[anahtar] = AnimasyonluResim.Kaydet(this, anim, () => AnimYenile(anahtar));
                }
            }
        }
        foreach (var anahtar in _animKayitlari.Keys.Where(a => gorunen?.Contains(a) != true).ToList())
        {
            _animKayitlari[anahtar].Dispose();
            _animKayitlari.Remove(anahtar);
        }
    }

    private void AnimYenile(string anahtar)
    {
        if (!_ekHaritasi.TryGetValue(anahtar, out var h)) return;
        var r = h.K.Alan;
        r.Offset(0, EkranY(h.O));
        if (r.Bottom < 0 || r.Top > Height) return;
        Invalidate(r);
    }

    private void AnimKayitlariniBirak()
    {
        foreach (var k in _animKayitlari.Values) k.Dispose();
        _animKayitlari.Clear();
    }

    // ---------------------------------------------------------------- Fare

    private int Bul(Point p)
    {
        int i = IlkGorunen();
        for (; i < _ogeler.Count; i++)
        {
            var o = _ogeler[i];
            int y = EkranY(o);
            if (y > p.Y) break;
            if (o.Tur == Tur.Mesaj && p.Y >= y + o.HoverUst && p.Y < y + o.H) return i;
        }
        return -1;
    }

    private Hedef HedefBul(Point p, int oge)
    {
        if (AltaPilGorunur && AltaPilAlani.Contains(p)) return new Hedef(HedefTuru.AltaPil);
        int w = IcW;
        // Araç çubuğu üstteki satırın üzerine taşabilir: önce o.
        if (oge >= 0 && AracVar(_ogeler[oge]))
        {
            var o = _ogeler[oge];
            int y = EkranY(o);
            if (AracAlani(o, y, w).Contains(p))
            {
                var liste = Araclar(o.M!);
                for (int i = 0; i < liste.Count; i++)
                    if (AracDugmesi(o, y, w, i).Contains(p)) return new Hedef(HedefTuru.Arac, oge, liste[i], No: i);
                return new Hedef(HedefTuru.Yok, oge);
            }
        }
        if (oge < 0) return default;
        var og = _ogeler[oge];
        int ey = EkranY(og);
        var rel = new Point(p.X, p.Y - ey);
        if (og.Yanit is { } yr && yr.Contains(rel)) return new Hedef(HedefTuru.Yanit, oge);
        if (og.TekrarDeneAlani is { } td && td.Contains(rel)) return new Hedef(HedefTuru.TekrarDene, oge);
        if (og.VazgecAlani is { } vg && vg.Contains(rel)) return new Hedef(HedefTuru.Vazgec, oge);
        foreach (var k in og.Ekler)
        {
            if (!k.Alan.Contains(rel)) continue;
            if (k.Resim) return new Hedef(HedefTuru.Resim, oge, k);
            if (k.Indir.Contains(rel)) return new Hedef(HedefTuru.Indir, oge, k);
            if (k.Ac.Contains(rel)) return new Hedef(HedefTuru.Ac, oge, k);
            return new Hedef(HedefTuru.Yok, oge);
        }
        foreach (var t in og.Tepkiler)
            if (t.Alan.Contains(rel)) return new Hedef(HedefTuru.Tepki, oge, t);
        if (og.TepkiArti is { } ta && ta.Contains(rel)) return new Hedef(HedefTuru.TepkiArti, oge);
        if (og.Duzen != null && rel.Y >= og.MetinY && rel.Y < og.MetinY + og.Duzen.Yukseklik)
        {
            var v = og.Duzen.Bul(new Point(rel.X - MetinX, rel.Y - og.MetinY));
            if (v is { } vurgu)
                return vurgu.Tur == ZenginVurguTuru.Baglanti ? new Hedef(HedefTuru.Baglanti, oge, vurgu.Deger) : new Hedef(HedefTuru.Bahsetme, oge, vurgu.Deger);
        }
        return new Hedef(HedefTuru.Yok, oge);
    }

    private static bool Etkilesimli(HedefTuru t) => t is not (HedefTuru.Yok);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_surukleBaslangic is { } bas)
        {
            int yol = Math.Max(1, Height - KaydirmaCubugu().Height);
            _kaydirma = Math.Clamp(_surukleKaydirma + (e.Y - bas) * MaksKaydirma / yol, 0, MaksKaydirma);
            Invalidate();
            EskileriGerekirseIste();
            return;
        }
        FareyiIsle(e.Location);
    }

    private void FareyiIsle(Point p)
    {
        // Araç çubuğu üstteki satıra taşabilir: önceki satırdayken çubuğun üstündeysek satırı koru.
        int i = Bul(p);
        if (_uzerinde >= 0 && _uzerinde < _ogeler.Count && i != _uzerinde && AracVar(_ogeler[_uzerinde])
            && AracAlani(_ogeler[_uzerinde], EkranY(_ogeler[_uzerinde]), IcW).Contains(p)) i = _uzerinde;
        var h = HedefBul(p, i);
        bool degisti = i != _uzerinde || h.Tur != _hedef.Tur || h.Oge != _hedef.Oge || h.No != _hedef.No || !Equals(h.Veri, _hedef.Veri);
        _uzerinde = i;
        _hedef = h;
        if (!degisti) return;
        Cursor = Etkilesimli(h.Tur) ? Cursors.Hand : Cursors.Default;
        string ipucu = h.Tur switch
        {
            HedefTuru.Arac => (Arac)h.Veri! switch { Arac.Tepki => "Tepki ekle", Arac.Yanitla => "Yanıtla", Arac.Duzenle => "Düzenle", Arac.Sil => "Sil", _ => "Diğer" },
            HedefTuru.TepkiArti => "Tepki ekle",
            HedefTuru.Tepki => TepkiIpucu(((TepkiKutu)h.Veri!).T),
            HedefTuru.Baglanti => (string)h.Veri!,
            _ => "",
        };
        if (ipucu != _ipucuYazisi) { _ipucuYazisi = ipucu; _ipucu.SetToolTip(this, ipucu); }
        Invalidate();
    }

    private string TepkiIpucu(Tepki t)
    {
        var adlar = t.Kullanicilar.Take(5).Select(id => Sahibi(id).GorunenAd).ToList();
        string s = string.Join(", ", adlar);
        if (t.Kullanicilar.Count > 5) s += $" ve {t.Kullanicilar.Count - 5} kişi daha";
        return s;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _uzerinde = -1;
        _hedef = default;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Kaydir(-e.Delta * Tema.S(60) / 120);
        FareyiIsle(PointToClient(MousePosition));
    }

    // Dışarıdan (yazma kutusundan) iletilen tekerlek hareketi için.
    public void Kaydir(int piksel)
    {
        _kaydirma = Math.Clamp(_kaydirma + piksel, 0, MaksKaydirma);
        Invalidate();
        EskileriGerekirseIste();
    }

    private void EskileriGerekirseIste()
    {
        if (_veri is { Tumu: false, Yukleniyor: false, Yuklendi: true } && _ogeler.Count > 0 && _kaydirma < Tema.S(300)) EskiIste?.Invoke();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int eskiH = _sonYukseklik;
        _sonYukseklik = Height;
        if (_veri is null) return;
        // Yükseklik değişince "en alttaydı mı" eski yüksekliğe göre bakılır (yazma kutusu büyüyünce son mesaj örtülmesin).
        bool altta = _kaydirma >= Math.Max(0, _toplamH - eskiH) - Tema.S(30);
        if (MetinW != _olcuW) Yerlestir(true);
        _kaydirma = altta ? MaksKaydirma : Math.Clamp(_kaydirma, 0, MaksKaydirma);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && KaydirmaVar && e.X > Width - Tema.S(14))
        {
            var c = KaydirmaCubugu();
            if (!c.Contains(e.Location)) { Kaydir(e.Y < c.Y ? -Height : Height); return; }
            _surukleBaslangic = e.Y;
            _surukleKaydirma = _kaydirma;
            Capture = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_surukleBaslangic != null) { _surukleBaslangic = null; Capture = false; Invalidate(); return; }
        if (_veri is { YuklemeHatasi: true } && _ogeler.Count == 0) { YenidenYukleIstendi?.Invoke(); return; }

        int i = Bul(e.Location);
        var h = HedefBul(e.Location, i);
        if (h.Tur == HedefTuru.AltaPil && e.Button == MouseButtons.Left) { Alta(); return; }
        if (i < 0 && h.Tur != HedefTuru.Arac) return;
        var o = h.Oge >= 0 ? _ogeler[h.Oge] : i >= 0 ? _ogeler[i] : null;
        var m = o?.M;
        if (o is null || m is null) return;

        if (e.Button == MouseButtons.Left)
        {
            switch (h.Tur)
            {
                case HedefTuru.Arac: AracTiklandi(o, (Arac)h.Veri!, h.No); break;
                case HedefTuru.Yanit: if (m.YanitId is { } yid) MesajaGit(yid); break;
                case HedefTuru.TekrarDene: TekrarDene?.Invoke(m); break;
                case HedefTuru.Vazgec: VazgecIstendi?.Invoke(m); break;
                case HedefTuru.Baglanti: Ac((string)h.Veri!); break;
                case HedefTuru.Bahsetme: BahsetmeTik?.Invoke((string)h.Veri!); break;
                case HedefTuru.Resim: ResmiAc((EkKutu)h.Veri!); break;
                case HedefTuru.Indir: _ = Indir((EkKutu)h.Veri!); break;
                case HedefTuru.Ac: _ = DosyayiAc((EkKutu)h.Veri!); break;
                case HedefTuru.Tepki: TepkiIstendi?.Invoke(m, ((TepkiKutu)h.Veri!).T.Emoji); break;
                case HedefTuru.TepkiArti:
                {
                    var r = o.TepkiArti!.Value; r.Offset(0, EkranY(o));
                    TepkiSeciciIstendi?.Invoke(m, PointToScreen(new Point(r.Right, r.Top)));
                    break;
                }
            }
        }
        else if (e.Button == MouseButtons.Right) MenuGoster(o, e.Location);
    }

    public event Action? YenidenYukleIstendi;


    private static void Ac(string adres)
    {
        if (!Uri.TryCreate(adres, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
    }

    private void ResmiAc(EkKutu k)
    {
        if (Api is null || FindForm() is not { } f) return;
        ResimGoruntuleyici.Goster(f, Api, k.Ek);
    }

    private void AracTiklandi(Oge o, Arac a, int sira)
    {
        var m = o.M!;
        int ey = EkranY(o);
        var dugme = AracDugmesi(o, ey, IcW, sira);
        switch (a)
        {
            case Arac.Tepki:
                if (AcilirPanel.YeniKapandi) return;
                TepkiSeciciIstendi?.Invoke(m, PointToScreen(new Point(dugme.Right, dugme.Top)));
                break;
            case Arac.Yanitla: YanitIstendi?.Invoke(m); break;
            case Arac.Duzenle: DuzenleIstendi?.Invoke(m); break;
            case Arac.Sil: SilIstendi?.Invoke(m); break;
            default: MenuGoster(o, new Point(dugme.Right, dugme.Bottom + Tema.S(2)), dugmedenMi: true); break;
        }
    }

    // ---- Menü

    private void MenuGoster(Oge o, Point konum, bool dugmedenMi = false)
    {
        var m = o.M!;
        if (m.Silindi) return;
        var menu = new KoyuMenu();
        bool tam = !BasitMod && m.Id > 0;
        if (tam)
        {
            menu.Ekle("Tepki ekle", () =>
            {
                var r = new Point(Math.Min(IcW - Tema.S(30), konum.X + Tema.S(200)), Math.Max(Tema.S(40), konum.Y));
                TepkiSeciciIstendi?.Invoke(m, PointToScreen(r));
            });
            menu.Ekle("Yanıtla", () => YanitIstendi?.Invoke(m));
            if (m.GonderenId == _ben?.Id) menu.Ekle("Düzenle", () => DuzenleIstendi?.Invoke(m));
            menu.Ayirici();
        }
        var metin = o.Duzen?.DuzMetin ?? m.Metin;
        if (m.Metin.Length > 0) menu.Ekle("Metni kopyala", () => { try { Clipboard.SetText(m.Metin); } catch { } });
        var baglanti = o.Duzen?.Vurgular.Select(v => v.Vurgu).FirstOrDefault(v => v.Tur == ZenginVurguTuru.Baglanti).Deger
            ?? (m.Ekler is { Count: > 0 } ek && Api != null ? Api.TamAdres(ek[0].Url) : null);
        if (!string.IsNullOrEmpty(baglanti)) menu.Ekle("Bağlantıyı kopyala", () => { try { Clipboard.SetText(baglanti); } catch { } });
        if (tam) menu.Ekle("Mesaj kimliğini kopyala", () => { try { Clipboard.SetText(m.Id.ToString()); } catch { } });
        if (tam && (m.GonderenId == _ben?.Id || BaskalarininMesajlariniSilebilir))
        {
            menu.Ayirici();
            menu.Ekle("Mesajı sil", () => SilIstendi?.Invoke(m), Tema.Hata);
        }
        if (menu.Items.Count == 0) { menu.Dispose(); return; }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(this, konum);
    }

    // ---- İndirme / açma

    private async Task Indir(EkKutu k)
    {
        if (Api is null) return;
        var ek = k.Ek;
        if (!_indirmeler.TryGetValue(ek.Url, out var d)) _indirmeler[ek.Url] = d = new IndirmeDurumu();
        if (d.Iniyor || d.Aciliyor) return;
        if (d.Yol != null && File.Exists(d.Yol)) { DosyaOnbellegi.KlasordeGoster(d.Yol); return; }
        d.Yol = null; d.Hata = false; d.Iniyor = true; d.Ilerleme = 0;
        Invalidate();
        double son = 0;
        var ilerleme = new Progress<double>(p => { d.Ilerleme = p; if (p - son >= 0.02 || p >= 1) { son = p; if (!IsDisposed) Invalidate(); } });
        string? yol = null;
        try { yol = await DosyaOnbellegi.IndirilenlereKaydet(Api, ek, ilerleme); } catch { }
        d.Iniyor = false;
        d.Yol = yol;
        d.Hata = yol is null;
        if (yol is null) Bildirim?.Invoke("Dosya indirilemedi.", true);
        else Bildirim?.Invoke($"{ek.Ad} İndirilenler klasörüne kaydedildi.", false);
        if (!IsDisposed) Invalidate();
    }

    private async Task DosyayiAc(EkKutu k)
    {
        if (Api is null) return;
        var ek = k.Ek;
        if (!_indirmeler.TryGetValue(ek.Url, out var d)) _indirmeler[ek.Url] = d = new IndirmeDurumu();
        if (d.Iniyor || d.Aciliyor) return;
        d.Aciliyor = true; d.Ilerleme = 0; d.Hata = false;
        Invalidate();
        double son = 0;
        var ilerleme = new Progress<double>(p => { d.Ilerleme = p; if (p - son >= 0.02 || p >= 1) { son = p; if (!IsDisposed) Invalidate(); } });
        bool ok = false;
        try { ok = await DosyaOnbellegi.AcVarsayilanla(Api, ek, ilerleme); } catch { }
        d.Aciliyor = false;
        if (!ok) { d.Hata = true; Bildirim?.Invoke("Dosya açılamadı.", true); }
        if (!IsDisposed) Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AnimKayitlariniBirak();
            _vurguSaati.Dispose();
            _ipucu.Dispose();
        }
        base.Dispose(disposing);
    }
}
