using System.Drawing.Drawing2D;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Sunucu ayarları: ana pencerenin içinde tam sayfa. Solda dikey sekme listesi, sağda seçili sekmenin içeriği.
// Sekmeler: Genel, Kanallar, Üyeler, Davet (yönetici+), Tehlikeli bölge. Yetkiye (Sunucu.Rolum) göre düzenlenebilirlik değişir.
public class SunucuAyarlari : Control
{
    public enum Sekme { Genel, Kanallar, Uyeler, Davet, Tehlike }

    internal readonly Api Api;
    internal readonly AvatarOnbellek? Avatarlar;
    internal readonly IkonOnbellek? Ikonlar;
    private Sunucu? _sunucu;
    private long _benId;
    private Sekme _secili = Sekme.Genel;
    private Sekme? _uzerinde;
    private bool _kapatUzerinde;
    private readonly List<(Sekme s, Rectangle r)> _alanlar = [];

    private readonly GenelPaneli _genel;
    private readonly KanallarPaneli _kanallar;
    private readonly UyelerPaneli _uyeler;
    private readonly DavetPaneli _davet;
    private readonly TehlikePaneli _tehlike;
    private readonly AyarPaneli[] _paneller;

    public event Action? Kapat;
    public event Action<string, bool>? Bildir;   // (mesaj, hata mı)
    public event Action? Silindi;                // sunucu silindi
    public event Action? Ayrildi;                // sunucudan ayrıldın
    public event Action<Sunucu>? Guncellendi;    // bu sayfa sunucuyu değiştirdi (ana form kendi kopyasını tazelesin)

    public Sunucu? Sunucu => _sunucu;
    public Sekme Secili => _secili;

    internal bool Yonetici => _sunucu is { Rolum: >= SunucuRolu.Yonetici };
    internal bool Sahip => _sunucu is { Rolum: SunucuRolu.Sahip };
    internal long BenId => _benId;

    public SunucuAyarlari(Api api, AvatarOnbellek? avatarlar = null, IkonOnbellek? ikonlar = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
        Api = api;
        Avatarlar = avatarlar;
        Ikonlar = ikonlar;
        _genel = new GenelPaneli(this);
        _kanallar = new KanallarPaneli(this);
        _uyeler = new UyelerPaneli(this);
        _davet = new DavetPaneli(this);
        _tehlike = new TehlikePaneli(this);
        _paneller = [_genel, _kanallar, _uyeler, _davet, _tehlike];
        Controls.AddRange(_paneller);
        PanelGoster();
        if (ikonlar != null) ikonlar.Yuklendi += IkonGeldi;
    }

    private void IkonGeldi()
    {
        if (IsDisposed || _sunucu is null) return;
        _genel.IkonTazele();
        Invalidate();
    }

    // Sayfayı bir sunucuyla açar. Kaydedilmemiş değişiklikler atılır.
    public void Ac(Sunucu sunucu, long benId, Sekme sekme = Sekme.Genel)
    {
        _benId = benId;
        Yukle(sunucu, korunsun: false);
        Sec(sekme);
    }

    // Sunucu başka yerden değişti (ör. SunucuGuncellendi olayı): verileri tazele, seçili sekmeyi ve yazılmakta olan Genel alanlarını koru.
    public void Yenile(Sunucu sunucu) => Yukle(sunucu, korunsun: true);

    private void Yukle(Sunucu s, bool korunsun)
    {
        _sunucu = s;
        foreach (var p in _paneller) p.Yukle(s, korunsun);
        if (!SekmeGorunur(_secili)) _secili = Sekme.Genel;
        PanelGoster();
        PerformLayout();
        Invalidate();
    }

    internal async Task SunucuYenile()
    {
        if (_sunucu is null) return;
        var s = await Api.SunucuGetir(_sunucu.Id);
        if (IsDisposed) return;
        if (!s.Basarili) { BildirGonder(s.Hata!, true); return; }
        Yukle(s.Veri!, korunsun: true);
        Guncellendi?.Invoke(s.Veri!);
    }

    internal void BildirGonder(string mesaj, bool hata) => Bildir?.Invoke(mesaj, hata);
    internal void SunucuDegisti(Sunucu s) { Yukle(s, korunsun: false); Guncellendi?.Invoke(s); }
    internal void SilindiBildir() { if (_sunucu != null) Ikonlar?.Unut("sunucu", _sunucu.Id); Silindi?.Invoke(); }
    internal void AyrildiBildir() { if (_sunucu != null) Ikonlar?.Unut("sunucu", _sunucu.Id); Ayrildi?.Invoke(); }

    public void Sec(Sekme s)
    {
        if (!SekmeGorunur(s)) s = Sekme.Genel;
        _secili = s;
        PanelGoster();
        Invalidate();
    }

    private bool SekmeGorunur(Sekme s) => s != Sekme.Davet || Yonetici;

    private void PanelGoster()
    {
        _genel.Visible = _secili == Sekme.Genel;
        _kanallar.Visible = _secili == Sekme.Kanallar;
        _uyeler.Visible = _secili == Sekme.Uyeler;
        _davet.Visible = _secili == Sekme.Davet;
        _tehlike.Visible = _secili == Sekme.Tehlike;
    }

    // ---- Yerleşim ----

    private int UstH => Tema.S(56);
    private int SekmeGenisligi => Tema.S(210);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_paneller is null) return;
        var r = new Rectangle(SekmeGenisligi, UstH, Math.Max(0, Width - SekmeGenisligi), Math.Max(0, Height - UstH));
        foreach (var p in _paneller) p.Bounds = r;
    }

    // ---- Çizim ----

    private static (Sekme s, string ad, string glif, bool hash)[] Sekmeler =>
    [
        (Sekme.Genel, "Genel", Glif.Duzenle, false),
        (Sekme.Kanallar, "Kanallar", "#", true),
        (Sekme.Uyeler, "Üyeler", Glif.Insanlar, false),
        (Sekme.Davet, "Davet", Glif.ArkadasEkle, false),
        (Sekme.Tehlike, "Tehlikeli bölge", Glif.Uyari, false),
    ];

    private Rectangle KapatAlani => new(Width - Tema.S(22) - Tema.S(96), (UstH - Tema.S(34)) / 2, Tema.S(96), Tema.S(34));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // Üst çubuk
        int orta = UstH / 2, x = Tema.S(22);
        TextRenderer.DrawText(g, Glif.Ayarlar, Cizim.Ikon(13), new Rectangle(x, 0, Tema.S(24), UstH), Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += Tema.S(32);
        var bf = Tema.Yazi(11.5f, kalin: true);
        const string baslik = "Sunucu ayarları";
        int bw = TextRenderer.MeasureText(g, baslik, bf, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, baslik, bf, new Rectangle(x, 0, bw + 4, UstH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += bw + Tema.S(18);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, orta - Tema.S(12), x, orta + Tema.S(12));
        x += Tema.S(16);
        var k = KapatAlani;
        TextRenderer.DrawText(g, _sunucu?.Ad ?? "", Tema.Yazi(10.5f), new Rectangle(x, 0, Math.Max(0, k.X - x - Tema.S(16)), UstH), Tema.MetinIkincil,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        if (_kapatUzerinde) Cizim.YuvarlakDoldur(g, k, Tema.S(6), Tema.YuzeyUst);
        TextRenderer.DrawText(g, Glif.Kapat, Cizim.Ikon(9), new Rectangle(k.X + Tema.S(10), k.Y, Tema.S(20), k.Height), _kapatUzerinde ? Tema.Metin : Tema.MetinIkincil,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "Kapat", Tema.Yazi(10, kalin: true), new Rectangle(k.X + Tema.S(34), k.Y, k.Width - Tema.S(40), k.Height), _kapatUzerinde ? Tema.Metin : Tema.MetinIkincil,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, UstH - 1, Width, UstH - 1);

        // Sol sekme sütunu
        var sutun = new Rectangle(0, UstH, SekmeGenisligi, Height - UstH);
        using (var b = new SolidBrush(Tema.Yuzey)) g.FillRectangle(b, sutun);
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, sutun.Right - 1, sutun.Top, sutun.Right - 1, sutun.Bottom);

        _alanlar.Clear();
        int y = UstH + Tema.S(18);
        TextRenderer.DrawText(g, "AYARLAR", Tema.Yazi(8.5f, kalin: true), new Point(Tema.S(24), y - Tema.S(2)), Tema.MetinSoluk, TextFormatFlags.NoPadding);
        y += Tema.S(22);
        foreach (var (s, ad, glif, hash) in Sekmeler)
        {
            if (!SekmeGorunur(s)) continue;
            var r = new Rectangle(Tema.S(10), y, SekmeGenisligi - Tema.S(21), Tema.S(38));
            _alanlar.Add((s, r));
            bool tehlike = s == Sekme.Tehlike, secili = s == _secili, uz = s == _uzerinde;
            if (secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), tehlike ? Color.FromArgb(40, Tema.Hata) : Tema.YuzeyUst);
            else if (uz) Cizim.YuvarlakDoldur(g, r, Tema.S(6), ColorTranslator.FromHtml("#232429"));
            var renk = tehlike ? Tema.Hata : secili || uz ? Tema.Metin : Tema.MetinIkincil;
            var ikonAlan = new Rectangle(r.X + Tema.S(12), r.Y, Tema.S(24), r.Height);
            if (hash) TextRenderer.DrawText(g, glif, Tema.Yazi(13, kalin: true), ikonAlan, renk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            else TextRenderer.DrawText(g, glif, Cizim.Ikon(11.5f), ikonAlan, renk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, ad, Tema.Yazi(10, kalin: true), new Rectangle(r.X + Tema.S(42), r.Y, r.Width - Tema.S(46), r.Height), renk,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            y = r.Bottom + Tema.S(2);
            if (s == Sekme.Davet || (s == Sekme.Uyeler && !Yonetici))
            {
                // Tehlikeli bölgeden önce ince ayırıcı
                y += Tema.S(6);
                using var p = new Pen(Tema.Kenar);
                g.DrawLine(p, Tema.S(20), y, SekmeGenisligi - Tema.S(20), y);
                y += Tema.S(8);
            }
        }
    }

    private Sekme? Bul(Point p) => _alanlar.Where(a => a.r.Contains(p)).Select(a => (Sekme?)a.s).FirstOrDefault();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var b = Bul(e.Location);
        bool k = KapatAlani.Contains(e.Location);
        if (b == _uzerinde && k == _kapatUzerinde) return;
        _uzerinde = b;
        _kapatUzerinde = k;
        Cursor = b != null || k ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = null; _kapatUzerinde = false; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (KapatAlani.Contains(e.Location)) { Kapat?.Invoke(); return; }
        if (Bul(e.Location) is { } s) Sec(s);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && Visible && KoyuMenu.AcikMenu == 0) { Kapat?.Invoke(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Ikonlar != null) Ikonlar.Yuklendi -= IkonGeldi;
        base.Dispose(disposing);
    }

    // ================= Paneller =================

    internal abstract class AyarPaneli : Panel
    {
        protected readonly SunucuAyarlari S;

        protected AyarPaneli(SunucuAyarlari s)
        {
            S = s;
            BackColor = Tema.Arkaplan;
            ForeColor = Tema.Metin;
            DoubleBuffered = true;
            Visible = false;
        }

        public abstract void Yukle(Sunucu sunucu, bool korunsun);

        protected static Label BaslikYap(string s) => new() { AutoSize = true, Text = s, Font = Tema.Yazi(16, kalin: true), ForeColor = Tema.Metin };
        protected static Label AltBaslikYap(string s) => new() { AutoSize = true, Text = s, Font = Tema.Yazi(10), ForeColor = Tema.MetinIkincil };
        protected static Label EtiketYap(string s) => new() { AutoSize = true, Text = s, Font = Tema.Yazi(8.5f, kalin: true), ForeColor = Tema.MetinIkincil };

        protected Form? Form => FindForm();

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);
    }

    // ---- Genel ----

    private sealed class GenelPaneli : AyarPaneli
    {
        private readonly KaydirmaAlani _kaydir = new();
        private readonly Panel _alt = new() { BackColor = Tema.Yuzey };
        private readonly Label _hata = new() { AutoSize = false, Font = Tema.Yazi(9.5f, kalin: true), ForeColor = Tema.Hata, BackColor = Tema.Yuzey, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Dugme _vazgec = new() { Text = "Geri al", Tur = Dugme.Turler.Ikincil };
        private readonly Dugme _kaydet = new() { Text = "Kaydet" };

        private readonly Label _baslik = BaslikYap("Genel");
        private readonly Label _altBaslik = AltBaslikYap("Sunucunun adı, görünümü ve görünürlüğü.");
        private readonly Label _ikonEtiket = EtiketYap("SUNUCU İKONU");
        private readonly IkonDuzenleyici _ikon = new();
        private readonly GirisKutusu _ad = new() { Etiket = "Sunucu adı", MaksUzunluk = Kurallar.SunucuAdMax };
        private readonly GirisKutusu _aciklama = new() { Etiket = "Açıklama", MaksUzunluk = Kurallar.SunucuAciklamaMax, CokSatir = true, IpucuYazisi = "Sunucun ne hakkında? Keşfet'te görünür." };
        private readonly Label _renkEtiket = EtiketYap("SUNUCU RENGİ");
        private readonly RenkSecici _renk = new();
        private readonly Anahtar _herkese = new() { Baslik = "Keşfet'te görünsün", Aciklama = "Açıkken sunucun Keşfet sayfasında listelenir ve herkes kodsuz katılabilir." };
        private readonly Label _onizEtiket = EtiketYap("ÖNİZLEME");
        private readonly SunucuOnizleme _onizleme = new();
        private Sunucu? _sunucu;

        public GenelPaneli(SunucuAyarlari s) : base(s)
        {
            _aciklama.Height = Tema.S(24) + Tema.S(84);
            _alt.Controls.AddRange([_hata, _vazgec, _kaydet]);
            _kaydir.Icerik.Controls.AddRange([_baslik, _altBaslik, _ikonEtiket, _ikon, _ad, _aciklama, _renkEtiket, _renk, _herkese, _onizEtiket, _onizleme]);
            _kaydir.Yerlestir = Yerlestir;
            Controls.AddRange([_kaydir, _alt]);

            _ad.TextChanged += (_, _) => Onizle();
            _aciklama.TextChanged += (_, _) => Onizle();
            _renk.Degisti += _ => Onizle();
            _herkese.Degisti += Onizle;
            _ikon.Degisti += () => { _hata.Text = ""; Onizle(); };
            _ikon.Hata += m => _hata.Text = m;
            _vazgec.Click += (_, _) => { if (_sunucu != null) Doldur(_sunucu); };
            _kaydet.Click += async (_, _) => await Kaydet();
        }

        public void IkonTazele()
        {
            if (_sunucu is null) return;
            _ikon.MevcutGuncelle(S.Ikonlar?.Sunucu(_sunucu.Id, _sunucu.IkonSurum));
        }

        private bool Degismis =>
            _sunucu is { } s && (_ad.Text.Trim() != s.Ad || _aciklama.Text.Trim() != s.Aciklama || _renk.Secili != s.Renk || _herkese.Acik != s.Herkese
                                 || _ikon.YeniResim != null || _ikon.Kaldirildi);

        public override void Yukle(Sunucu sunucu, bool korunsun)
        {
            bool koru = korunsun && _sunucu is { } eski && eski.Id == sunucu.Id && Degismis;
            _sunucu = sunucu;
            bool duzenle = S.Yonetici;
            foreach (var c in new Control[] { _ikon, _ad, _aciklama, _renk, _herkese }) c.Enabled = duzenle;
            _alt.Visible = duzenle;
            _altBaslik.Text = duzenle ? "Sunucunun adı, görünümü ve görünürlüğü." : "Bu ayarları sadece yöneticiler ve sahip değiştirebilir.";
            if (koru) { _ikon.MevcutGuncelle(S.Ikonlar?.Sunucu(sunucu.Id, sunucu.IkonSurum)); Onizle(); }
            else Doldur(sunucu);
            PerformLayout();
            _kaydir.YenidenYerlestir();
        }

        private void Doldur(Sunucu s)
        {
            _ad.Text = s.Ad;
            _aciklama.Text = s.Aciklama;
            _renk.Secili = s.Renk;
            _herkese.Acik = s.Herkese;
            _ad.Hata = null;
            _ikon.Sifirla(S.Ikonlar?.Sunucu(s.Id, s.IkonSurum));
            _hata.Text = "";
            Onizle();
        }

        private void Onizle()
        {
            _ikon.Ad = _ad.Text;
            _ikon.Renk = _renk.Secili;
            _onizleme.Ad = string.IsNullOrWhiteSpace(_ad.Text) ? "Sunucu adı" : _ad.Text.Trim();
            _onizleme.Aciklama = _aciklama.Text.Trim();
            _onizleme.Renk = _renk.Secili;
            _onizleme.Herkese = _herkese.Acik;
            _onizleme.Resim = _ikon.Gosterilen;
            if (_sunucu != null)
            {
                _onizleme.UyeSayisi = _sunucu.Uyeler.Count;
                _onizleme.Cevrimici = _sunucu.Uyeler.Count(u => u.Profil.Durum is not (Durum.Cevrimdisi or Durum.Gorunmez));
            }
            _hata.Text = "";
            _kaydir.YenidenYerlestir();
            _onizleme.Invalidate();
        }

        private int Yerlestir(int w)
        {
            int x = Tema.S(32), y = Tema.S(22);
            bool genis = w >= Tema.S(760);
            int solW = genis ? Tema.S(380) : Math.Max(Tema.S(200), Math.Min(Tema.S(460), w - x * 2));
            _baslik.Location = new Point(x - Tema.S(5), y); y = _baslik.Bottom;
            _altBaslik.Location = new Point(x - Tema.S(3), y); y = _altBaslik.Bottom + Tema.S(20);
            _ikonEtiket.Location = new Point(x - Tema.S(3), y);
            int etiketY = y;
            y = _ikonEtiket.Bottom + Tema.S(8);
            _ikon.Bounds = new Rectangle(x, y, solW, Tema.S(76)); y = _ikon.Bottom + Tema.S(16);
            _ad.Bounds = new Rectangle(x, y, solW, _ad.Height); y = _ad.Bottom + Tema.S(12);
            _aciklama.Bounds = new Rectangle(x, y, solW, _aciklama.Height); y = _aciklama.Bottom + Tema.S(14);
            _renkEtiket.Location = new Point(x - Tema.S(3), y); y = _renkEtiket.Bottom + Tema.S(6);
            _renk.Bounds = new Rectangle(x, y, solW, Tema.S(36)); y = _renk.Bottom + Tema.S(16);
            _herkese.Bounds = new Rectangle(x, y, solW, Tema.S(72)); y = _herkese.Bottom + Tema.S(24);

            if (genis)
            {
                int sagX = x + solW + Tema.S(40), sagW = Math.Min(Tema.S(320), w - sagX - Tema.S(32));
                _onizEtiket.Location = new Point(sagX - Tema.S(3), etiketY);
                _onizleme.Bounds = new Rectangle(sagX, _onizEtiket.Bottom + Tema.S(8), sagW, _onizleme.GerekenYukseklik(sagW));
            }
            else
            {
                _onizEtiket.Location = new Point(x - Tema.S(3), y);
                _onizleme.Bounds = new Rectangle(x, _onizEtiket.Bottom + Tema.S(8), solW, _onizleme.GerekenYukseklik(solW));
                y = _onizleme.Bottom + Tema.S(24);
            }
            return Math.Max(y, _onizleme.Bottom + Tema.S(24));
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_alt is null) return;
            int altH = _alt.Visible ? Tema.S(68) : 0;
            _kaydir.Bounds = new Rectangle(0, 0, Width, Math.Max(0, Height - altH));
            _alt.Bounds = new Rectangle(0, Height - Tema.S(68), Width, Tema.S(68));
            _kaydet.Bounds = new Rectangle(Width - Tema.S(28) - Tema.S(120), Tema.S(14), Tema.S(120), Tema.S(40));
            _vazgec.Bounds = new Rectangle(_kaydet.Left - Tema.S(10) - Tema.S(100), Tema.S(14), Tema.S(100), Tema.S(40));
            _hata.Bounds = new Rectangle(Tema.S(28), 0, Math.Max(0, _vazgec.Left - Tema.S(40)), Tema.S(68));
        }

        private void Mesgul(bool m)
        {
            foreach (var c in new Control[] { _ikon, _ad, _aciklama, _renk, _herkese, _kaydet, _vazgec }) c.Enabled = !m && (S.Yonetici || c == _kaydet);
            _kaydet.Text = m ? "Kaydediliyor…" : "Kaydet";
            UseWaitCursor = m;
        }

        private async Task Kaydet()
        {
            if (_sunucu is null) return;
            var ad = _ad.Text.Trim();
            if (ad.Length == 0) { _ad.Hata = "Sunucu adı boş olamaz."; _ad.Focus(); return; }
            var aciklama = _aciklama.Text.Trim();
            Mesgul(true);
            Sunucu son = _sunucu;
            if (_ikon.Kaldirildi && _sunucu.IkonSurum != 0)
            {
                var s = await S.Api.SunucuIkonSil(_sunucu.Id);
                if (!Bitti(s)) return;
                S.Ikonlar?.Koy("sunucu", _sunucu.Id, 0, null);
                son = s.Veri!;
            }
            if (_ikon.YeniResim is { } png)
            {
                var s = await S.Api.SunucuIkonYukle(_sunucu.Id, png);
                if (!Bitti(s)) return;
                S.Ikonlar?.Koy("sunucu", _sunucu.Id, s.Veri!.IkonSurum, Cizim.ResimCoz(png));
                son = s.Veri!;
            }
            var g = await S.Api.SunucuGuncelle(_sunucu.Id, new SunucuAyari(ad, aciklama, _renk.Secili, _herkese.Acik));
            if (!Bitti(g)) return;
            Mesgul(false);
            S.BildirGonder("Sunucu ayarların kaydedildi.", false);
            S.SunucuDegisti(g.Veri ?? son);
        }

        private bool Bitti<T>(Sonuc<T> s)
        {
            if (s.Basarili) return true;
            Mesgul(false);
            _hata.Text = s.Hata ?? "Kaydedilemedi.";
            return false;
        }
    }

    // ---- Kanallar ----

    private sealed class KanallarPaneli : AyarPaneli
    {
        private readonly Label _baslik = BaslikYap("Kanallar");
        private readonly Label _altBaslik = AltBaslikYap("Sunucudaki metin kanalları. Adlar küçük harf olur, boşluklar tireye çevrilir.");
        private readonly GirisKutusu _yeni = new() { Etiket = "Yeni kanal", MaksUzunluk = Kurallar.KanalAdMax, IpucuYazisi = "ör. oyun-sohbeti" };
        private readonly Dugme _ekle = new() { Text = "Kanal ekle" };
        private readonly SatirListe _liste = new() { SatirYuksekligi = Tema.S(64) };
        private Sunucu? _sunucu;
        private bool _duzeltiyor;

        public KanallarPaneli(SunucuAyarlari s) : base(s)
        {
            Controls.AddRange([_baslik, _altBaslik, _yeni, _ekle, _liste]);
            _yeni.TextChanged += (_, _) =>
            {
                if (_duzeltiyor) return;
                var d = KanalAdi.Duzelt(_yeni.Text);
                if (d == _yeni.Text) return;
                _duzeltiyor = true;
                _yeni.Text = d;
                _yeni.IcKutu.SelectionStart = d.Length;
                _duzeltiyor = false;
            };
            _yeni.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Ekle(); } };
            _ekle.Click += async (_, _) => await Ekle();
            _liste.Eylem += async (etiket, ad, _) => { if (etiket is Kanal k) await Eylem(k, ad); };
        }

        public override void Yukle(Sunucu sunucu, bool korunsun)
        {
            _sunucu = sunucu;
            bool yonet = S.Yonetici;
            _yeni.Visible = _ekle.Visible = yonet;
            _liste.BosYazi = "Bu sunucuda henüz kanal yok.";
            var kanallar = sunucu.Kanallar.OrderBy(k => k.Sira).ThenBy(k => k.Id).ToList();
            _altBaslik.Text = yonet
                ? $"{kanallar.Count} / {Kurallar.SunucuKanalMax} kanal. Adlar küçük harf olur, boşluklar tireye çevrilir."
                : $"{kanallar.Count} kanal. Kanalları sadece yöneticiler ve sahip değiştirebilir.";
            _liste.Ayarla(kanallar.Select(k =>
            {
                var d = new List<SatirListe.Dugme>();
                if (yonet)
                {
                    d.Add(new("duzenle", "Düzenle"));
                    d.Add(new("sil", "Sil", true, kanallar.Count > 1));
                }
                return new SatirListe.Oge
                {
                    Etiket = k, Baslik = k.Ad, Alt = string.IsNullOrWhiteSpace(k.Konu) ? "Konu yok" : k.Konu, Dugmeler = d,
                    Sol = (g, r, z) =>
                    {
                        Cizim.YuvarlakDoldur(g, r, Tema.S(10), Tema.YuzeyUst);
                        TextRenderer.DrawText(g, "#", Tema.Yazi(15, kalin: true), Rectangle.Round(r), Tema.Vurgu,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    },
                };
            }).ToList());
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_liste is null) return;
            int x = Tema.S(32), y = Tema.S(22);
            _baslik.Location = new Point(x - Tema.S(5), y); y = _baslik.Bottom;
            _altBaslik.Location = new Point(x - Tema.S(3), y); y = _altBaslik.Bottom + Tema.S(18);
            if (_yeni.Visible)
            {
                int toplam = Math.Min(Tema.S(620), Width - x * 2);
                int dugmeW = Tema.S(130);
                _yeni.Bounds = new Rectangle(x, y, toplam - dugmeW - Tema.S(12), _yeni.Height);
                _ekle.Bounds = new Rectangle(_yeni.Right + Tema.S(12), y + Tema.S(24), dugmeW, Tema.S(44));
                y = _yeni.Bottom + Tema.S(14);
            }
            _liste.Bounds = new Rectangle(Tema.S(20), y, Math.Max(0, Width - Tema.S(34)), Math.Max(0, Height - y));
        }

        private async Task Ekle()
        {
            if (_sunucu is null || !S.Yonetici) return;
            var ad = KanalAdi.Duzelt(_yeni.Text).Trim('-');
            if (Kurallar.KanalAdKontrol(ad) is { } h) { _yeni.Hata = h; _yeni.Focus(); return; }
            _ekle.Enabled = _yeni.Enabled = false;
            var s = await S.Api.KanalOlustur(_sunucu.Id, new KanalAyari(ad, ""));
            _ekle.Enabled = _yeni.Enabled = true;
            if (s.Basarili) { _yeni.Text = ""; S.BildirGonder($"#{ad} kanalı oluşturuldu.", false); await S.SunucuYenile(); }
            else S.BildirGonder(s.Hata!, true);
        }

        private async Task Eylem(Kanal k, string ad)
        {
            if (_sunucu is null || Form is not { } f) return;
            if (ad == "duzenle")
            {
                KanalKarti.Goster(f, S.Api, _sunucu.Id, k, async _ => { S.BildirGonder("Kanal güncellendi.", false); await S.SunucuYenile(); });
                return;
            }
            if (ad == "sil")
            {
                if (!await OnayKarti.Sor(f, $"#{k.Ad} silinsin mi?", "Bu kanaldaki tüm mesajlar kalıcı olarak silinir. Bu işlem geri alınamaz.", "Kanalı sil")) return;
                var s = await S.Api.KanalSil(k.Id);
                if (!s.Basarili) { S.BildirGonder(s.Hata!, true); return; }
                S.BildirGonder($"#{k.Ad} silindi.", false);
                await S.SunucuYenile();
            }
        }
    }

    // ---- Üyeler ----

    private sealed class UyelerPaneli : AyarPaneli
    {
        private readonly Label _baslik = BaslikYap("Üyeler");
        private readonly Label _altBaslik = AltBaslikYap("");
        private readonly SatirListe _liste = new();
        private Sunucu? _sunucu;

        public UyelerPaneli(SunucuAyarlari s) : base(s)
        {
            Controls.AddRange([_baslik, _altBaslik, _liste]);
            _liste.AvatarKaynagi = s.Avatarlar;
            _liste.Eylem += async (etiket, ad, alan) => { if (etiket is SunucuUyesi u) await Eylem(u, ad, alan); };
        }

        public override void Yukle(Sunucu sunucu, bool korunsun)
        {
            _sunucu = sunucu;
            var tr = StringComparer.Create(Tema.Turkce, true);
            var uyeler = sunucu.Uyeler.OrderByDescending(u => u.Rol).ThenBy(u => u.Profil.GorunenAd, tr).ToList();
            _altBaslik.Text = S.Sahip ? $"{uyeler.Count} üye. Rolleri değiştirebilir, üyeleri çıkarabilir, sahipliği devredebilirsin."
                : S.Yonetici ? $"{uyeler.Count} üye. Üyeleri sunucudan çıkarabilirsin."
                : $"{uyeler.Count} üye.";
            _liste.Ayarla(uyeler.Select(u =>
            {
                var p = u.Profil;
                bool ben = p.Id == S.BenId;
                var rozet = new List<(string, Color)>();
                if (u.Rol == SunucuRolu.Sahip) rozet.Add(("SAHİP", ColorTranslator.FromHtml("#F5B544")));
                else if (u.Rol == SunucuRolu.Yonetici) rozet.Add(("YÖNETİCİ", Tema.Vurgu));
                if (ben) rozet.Add(("SEN", Tema.MetinIkincil));
                var d = new List<SatirListe.Dugme>();
                if (!ben && S.Sahip) d.Add(new("rol", "Rol  ▾"));
                if (!ben && (S.Sahip || (S.Yonetici && u.Rol == SunucuRolu.Uye))) d.Add(new("at", "At", true));
                return new SatirListe.Oge
                {
                    Etiket = u, Baslik = p.GorunenAd, Alt = $"@{p.KullaniciAdi}  ·  {Katilma(u.Katilma)} katıldı", Rozetler = rozet, Dugmeler = d,
                    Sol = (g, r, z) => Cizim.Avatar(g, r, p, S.Avatarlar?.Al(p), z, p.Durum is not (Durum.Cevrimdisi or Durum.Gorunmez)),
                };
            }).ToList());
            PerformLayout();
        }

        private static string Katilma(long ms)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("dd.MM.yyyy"); }
            catch { return "—"; }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_liste is null) return;
            int x = Tema.S(32), y = Tema.S(22);
            _baslik.Location = new Point(x - Tema.S(5), y); y = _baslik.Bottom;
            _altBaslik.Location = new Point(x - Tema.S(3), y); y = _altBaslik.Bottom + Tema.S(14);
            _liste.Bounds = new Rectangle(Tema.S(20), y, Math.Max(0, Width - Tema.S(34)), Math.Max(0, Height - y));
        }

        private async Task Eylem(SunucuUyesi u, string ad, Rectangle alan)
        {
            if (_sunucu is null || Form is not { } f) return;
            var p = u.Profil;
            if (ad == "rol")
            {
                var m = new KoyuMenu();
                if (u.Rol == SunucuRolu.Uye) m.Ekle("Yönetici yap", async () => await RolVer(u, SunucuRolu.Yonetici));
                else m.Ekle("Üyeliğe düşür", async () => await RolVer(u, SunucuRolu.Uye));
                m.Ayirici();
                m.Ekle("Sahipliği devret", async () => await Devret(u));
                m.Closed += (_, _) => BeginInvoke(() => m.Dispose());
                m.Show(new Point(alan.Left, alan.Bottom + Tema.S(4)));
            }
            else if (ad == "at")
            {
                if (!await OnayKarti.Sor(f, $"{p.GorunenAd} sunucudan çıkarılsın mı?", "Üye sunucudan atılır. Davet koduyla ya da Keşfet'ten tekrar katılabilir.", "Çıkar")) return;
                var s = await S.Api.UyeAt(_sunucu.Id, p.Id);
                if (!s.Basarili) { S.BildirGonder(s.Hata!, true); return; }
                S.BildirGonder($"{p.GorunenAd} sunucudan çıkarıldı.", false);
                await S.SunucuYenile();
            }
        }

        private async Task RolVer(SunucuUyesi u, SunucuRolu rol)
        {
            if (_sunucu is null) return;
            var s = await S.Api.RolAyarla(_sunucu.Id, u.Profil.Id, rol);
            if (!s.Basarili) { S.BildirGonder(s.Hata!, true); return; }
            S.BildirGonder(rol == SunucuRolu.Yonetici ? $"{u.Profil.GorunenAd} artık yönetici." : $"{u.Profil.GorunenAd} artık sıradan üye.", false);
            await S.SunucuYenile();
        }

        private async Task Devret(SunucuUyesi u)
        {
            if (_sunucu is null || Form is not { } f) return;
            if (!await OnayKarti.Sor(f, $"Sahiplik {u.Profil.GorunenAd} kişisine devredilsin mi?",
                    "Sunucunun yeni sahibi o olur, sen yönetici olarak kalırsın. Sahipliği sadece yeni sahip geri verebilir.", "Sahipliği devret")) return;
            var s = await S.Api.RolAyarla(_sunucu.Id, u.Profil.Id, SunucuRolu.Sahip);
            if (!s.Basarili) { S.BildirGonder(s.Hata!, true); return; }
            S.BildirGonder($"Sunucunun yeni sahibi {u.Profil.GorunenAd}.", false);
            await S.SunucuYenile();
        }
    }

    // ---- Davet ----

    private sealed class DavetPaneli : AyarPaneli
    {
        private readonly Label _baslik = BaslikYap("Davet");
        private readonly Label _altBaslik = AltBaslikYap("Arkadaşların bu kodla sunucuna katılabilir.");
        private readonly Dugme _kopyala = new() { Text = "Kopyala" };
        private readonly Dugme _yenile = new() { Text = "Yeni kod oluştur", Tur = Dugme.Turler.Ikincil };
        private readonly Label _not = new() { AutoSize = false, Font = Tema.Yazi(9.5f), ForeColor = Tema.MetinSoluk };
        private Sunucu? _sunucu;
        private string _kod = "";
        private Rectangle _kart;
        private readonly ZeminliDugme _kopyalaSar, _yenileSar;

        public DavetPaneli(SunucuAyarlari s) : base(s)
        {
            _kopyalaSar = new ZeminliDugme(_kopyala, Tema.Yuzey);
            _yenileSar = new ZeminliDugme(_yenile, Tema.Yuzey);
            Controls.AddRange([_baslik, _altBaslik, _kopyalaSar, _yenileSar, _not]);
            _kopyala.Click += (_, _) =>
            {
                if (_kod.Length == 0) return;
                try { Clipboard.SetText(_kod); S.BildirGonder($"{_kod} panoya kopyalandı.", false); } catch { S.BildirGonder("Panoya kopyalanamadı.", true); }
            };
            _yenile.Click += async (_, _) => await Yenile();
        }

        public override void Yukle(Sunucu sunucu, bool korunsun)
        {
            _sunucu = sunucu;
            _kod = sunucu.DavetKodu ?? "";
            _not.Text = "Katılmak isteyen, soldaki sunucu listesinin altındaki \"+\" düğmesinden \"Kodla katıl\" sekmesine bu kodu yazar. "
                + "Kodu yenilersen eski kod çalışmaz olur." + (sunucu.Herkese ? " Sunucun Keşfet'te de göründüğü için isteyenler kodsuz da katılabilir." : "");
            _kopyala.Enabled = _kod.Length > 0;
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_not is null) return;
            int x = Tema.S(32), y = Tema.S(22);
            _baslik.Location = new Point(x - Tema.S(5), y); y = _baslik.Bottom;
            _altBaslik.Location = new Point(x - Tema.S(3), y); y = _altBaslik.Bottom + Tema.S(22);
            int w = Math.Min(Tema.S(620), Width - x * 2);
            _kart = new Rectangle(x, y, w, Tema.S(150));
            int by = _kart.Bottom - Tema.S(18) - Tema.S(40);
            _kopyalaSar.Bounds = new Rectangle(_kart.Right - Tema.S(18) - Tema.S(120), by, Tema.S(120), Tema.S(40));
            _yenileSar.Bounds = new Rectangle(_kopyalaSar.Left - Tema.S(10) - Tema.S(160), by, Tema.S(160), Tema.S(40));
            int nh = TextRenderer.MeasureText(_not.Text, _not.Font, new Size(w, 0), TextFormatFlags.WordBreak).Height + Tema.S(4);
            _not.Bounds = new Rectangle(x, _kart.Bottom + Tema.S(16), w, nh);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(_kart.X + 0.5f, _kart.Y + 0.5f, _kart.Width - 1, _kart.Height - 1), Tema.S(12)))
            using (var b = new SolidBrush(Tema.Yuzey))
            using (var p = new Pen(Tema.Kenar))
            {
                g.FillPath(b, yol);
                g.DrawPath(p, yol);
            }
            TextRenderer.DrawText(g, "DAVET KODU", Tema.Yazi(8.5f, kalin: true), new Point(_kart.X + Tema.S(20), _kart.Y + Tema.S(18)), Tema.MetinIkincil, TextFormatFlags.NoPadding);
            using var f = new Font("Consolas", 28f, FontStyle.Bold);
            TextRenderer.DrawText(g, _kod.Length == 0 ? "—" : _kod, f, new Rectangle(_kart.X + Tema.S(20), _kart.Y + Tema.S(34), _kart.Width - Tema.S(40), Tema.S(54)),
                _kod.Length == 0 ? Tema.MetinSoluk : Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        private async Task Yenile()
        {
            if (_sunucu is null || Form is not { } f) return;
            if (!await OnayKarti.Sor(f, "Yeni davet kodu oluşturulsun mu?", "Eski kod hemen çalışmaz olur. Üyeler sunucuda kalır, sadece henüz katılmamış olanlar yeni kodu kullanmalı.", "Yeni kod oluştur", tehlike: false)) return;
            _yenile.Enabled = false;
            var s = await S.Api.DavetKoduYenile(_sunucu.Id);
            _yenile.Enabled = true;
            if (!s.Basarili) { S.BildirGonder(s.Hata!, true); return; }
            S.BildirGonder("Yeni davet kodu oluşturuldu.", false);
            await S.SunucuYenile();
        }
    }

    // ---- Tehlikeli bölge ----

    private sealed class TehlikePaneli : AyarPaneli
    {
        private readonly Label _baslik = BaslikYap("Tehlikeli bölge");
        private readonly Label _altBaslik = AltBaslikYap("Bu işlemler geri alınamaz, dikkatli ol.");
        private readonly Dugme _sil = new() { Text = "Sunucuyu sil", Tur = Dugme.Turler.Tehlike };
        private readonly Dugme _ayril = new() { Text = "Sunucudan ayrıl", Tur = Dugme.Turler.Tehlike };
        private Sunucu? _sunucu;
        private Rectangle _silKart, _ayrilKart;
        private readonly ZeminliDugme _silSar, _ayrilSar;

        public TehlikePaneli(SunucuAyarlari s) : base(s)
        {
            _silSar = new ZeminliDugme(_sil, Tema.Yuzey);
            _ayrilSar = new ZeminliDugme(_ayril, Tema.Yuzey);
            Controls.AddRange([_baslik, _altBaslik, _silSar, _ayrilSar]);
            _sil.Click += (_, _) =>
            {
                if (_sunucu is null || Form is not { } f) return;
                SunucuSilKarti.Goster(f, S.Api, _sunucu, () => { S.BildirGonder($"{_sunucu.Ad} silindi.", false); S.SilindiBildir(); });
            };
            _ayril.Click += async (_, _) => await Ayril();
        }

        public override void Yukle(Sunucu sunucu, bool korunsun)
        {
            _sunucu = sunucu;
            bool sahip = S.Sahip;
            _silSar.Visible = sahip;
            _ayrilSar.Visible = !sahip;
            PerformLayout();
            Invalidate();
        }

        private const string SilAciklama = "Sunucudaki tüm kanallar, mesajlar ve üyelik bilgileri kalıcı olarak silinir. Onaylamak için sunucunun adını yazman istenecek.";

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_ayrilSar is null) return;
            int x = Tema.S(32), y = Tema.S(22);
            _baslik.Location = new Point(x - Tema.S(5), y); y = _baslik.Bottom;
            _altBaslik.Location = new Point(x - Tema.S(3), y); y = _altBaslik.Bottom + Tema.S(22);
            int w = Math.Min(Tema.S(620), Width - x * 2);
            int metinW = w - Tema.S(40) - Tema.S(170);
            bool sahip = _sunucu?.Rolum == SunucuRolu.Sahip;
            string ayrilAciklama = sahip
                ? "Sahip olarak ayrılamazsın. Önce Üyeler sekmesinden sahipliği başka birine devret ya da sunucuyu sil."
                : "Sunucudaki kanalları göremezsin. Davet koduyla ya da Keşfet'ten tekrar katılabilirsin.";
            if (sahip)
            {
                int h = Math.Max(Tema.S(96), Tema.S(52) + TextRenderer.MeasureText(SilAciklama, Tema.Yazi(9.5f), new Size(metinW, 0), TextFormatFlags.WordBreak).Height + Tema.S(16));
                _silKart = new Rectangle(x, y, w, h);
                _silSar.Bounds = new Rectangle(_silKart.Right - Tema.S(18) - Tema.S(150), _silKart.Y + (h - Tema.S(40)) / 2, Tema.S(150), Tema.S(40));
                y = _silKart.Bottom + Tema.S(16);
            }
            else _silKart = Rectangle.Empty;
            int ah = Math.Max(Tema.S(96), Tema.S(52) + TextRenderer.MeasureText(ayrilAciklama, Tema.Yazi(9.5f), new Size(metinW, 0), TextFormatFlags.WordBreak).Height + Tema.S(16));
            _ayrilKart = new Rectangle(x, y, w, ah);
            _ayrilSar.Bounds = new Rectangle(_ayrilKart.Right - Tema.S(18) - Tema.S(170), _ayrilKart.Y + (ah - Tema.S(40)) / 2, Tema.S(170), Tema.S(40));
            _ayrilAciklama = ayrilAciklama;
            _metinW = metinW;
            Invalidate();
        }

        private string _ayrilAciklama = "";
        private int _metinW;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_sunucu is null) return;
            var g = e.Graphics;
            if (!_silKart.IsEmpty) Kart(g, _silKart, "Sunucuyu sil", SilAciklama);
            Kart(g, _ayrilKart, "Sunucudan ayrıl", _ayrilAciklama);
        }

        private void Kart(Graphics g, Rectangle r, string baslik, string aciklama)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), Tema.S(12)))
            using (var b = new SolidBrush(Tema.Yuzey))
            using (var p = new Pen(Color.FromArgb(120, Tema.Hata)))
            {
                g.FillPath(b, yol);
                g.DrawPath(p, yol);
            }
            TextRenderer.DrawText(g, baslik, Tema.Yazi(11, kalin: true), new Rectangle(r.X + Tema.S(20), r.Y + Tema.S(16), _metinW, Tema.S(24)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, aciklama, Tema.Yazi(9.5f), new Rectangle(r.X + Tema.S(20), r.Y + Tema.S(44), _metinW, r.Height - Tema.S(52)), Tema.MetinIkincil, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        private async Task Ayril()
        {
            if (_sunucu is null || Form is not { } f || S.Sahip) return;
            if (!await OnayKarti.Sor(f, $"{_sunucu.Ad} sunucusundan ayrılsın mı?", "Sunucudaki kanalları göremezsin. Davet koduyla ya da Keşfet'ten tekrar katılabilirsin.", "Ayrıl")) return;
            var s = await S.Api.SunucudanAyril(_sunucu.Id);
            if (!s.Basarili) { S.BildirGonder(s.Hata!, true); return; }
            S.BildirGonder($"{_sunucu.Ad} sunucusundan ayrıldın.", false);
            S.AyrildiBildir();
        }
    }
}

// Sunucu önizleme kartı (profil kartı tarzı): renkli bant, ikon, ad, çevrimiçi/üye sayısı, açıklama.
public class SunucuOnizleme : Control
{
    private string _ad = "", _aciklama = "", _renk = Kurallar.ProfilRenkleri[0];
    private bool _herkese;
    private Image? _resim;
    private int _uyeSayisi, _cevrimici;

    public string Ad { get => _ad; set { _ad = value; Invalidate(); } }
    public string Aciklama { get => _aciklama; set { _aciklama = value; Invalidate(); } }
    public string Renk { get => _renk; set { _renk = value; Invalidate(); } }
    public bool Herkese { get => _herkese; set { _herkese = value; Invalidate(); } }
    public Image? Resim { get => _resim; set { _resim = value; Invalidate(); } }
    public int UyeSayisi { get => _uyeSayisi; set { _uyeSayisi = value; Invalidate(); } }
    public int Cevrimici { get => _cevrimici; set { _cevrimici = value; Invalidate(); } }

    public SunucuOnizleme()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
    }

    private int BantH => Tema.S(84);
    private int IkonCap => Tema.S(72);
    private int Ic => Tema.S(18);

    private string AciklamaYazisi => string.IsNullOrWhiteSpace(_aciklama) ? "Henüz bir açıklama yazılmamış." : _aciklama;

    public int GerekenYukseklik(int genislik)
    {
        int y = BantH + IkonCap / 2 + Tema.S(14);
        y += Tema.S(12) + Tema.S(28) + Tema.S(22) + Tema.S(8) + Tema.S(10) + Tema.S(22);
        var satirli = MetinYardimci.Satirla(AciklamaYazisi, Tema.Yazi(10), genislik - Ic * 4 - 1, 4);
        int h = TextRenderer.MeasureText(satirli, Tema.Yazi(10), Size.Empty, TextFormatFlags.NoPadding).Height;
        y += h + Tema.S(16);
        return y + Ic;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        var kart = new Rectangle(0, 0, Width - 1, Height - 1);
        Cizim.YuvarlakDoldur(g, kart, Tema.S(12), Tema.Yuzey);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(0, 0, kart.Width, BantH + Tema.S(14)), Tema.S(12)))
        using (var b = new SolidBrush(IkonCizim.RenkCoz(_renk)))
        {
            g.SetClip(new Rectangle(0, 0, Width, BantH));
            g.FillPath(b, yol);
            g.ResetClip();
        }

        int halka = Tema.S(6);
        float ix = Ic - halka, iy = BantH - IkonCap / 2f - halka;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(ix, iy, IkonCap + halka * 2, IkonCap + halka * 2), Tema.S(22) + halka / 2f))
        using (var b = new SolidBrush(Tema.Yuzey))
            g.FillPath(b, yol);
        IkonCizim.Sunucu(g, new RectangleF(Ic, BantH - IkonCap / 2f, IkonCap, IkonCap), _ad, _renk, _resim, Tema.S(20));

        int y0 = BantH + IkonCap / 2 + Tema.S(14);
        var bilgi = new Rectangle(Ic, y0, kart.Width - Ic * 2, kart.Height - y0 - Ic);
        Cizim.YuvarlakDoldur(g, bilgi, Tema.S(8), Tema.Arkaplan);

        int x = bilgi.X + Ic, w = bilgi.Width - Ic * 2, y = bilgi.Y + Tema.S(12);
        TextRenderer.DrawText(g, _ad, Tema.Yazi(14, kalin: true), new Rectangle(x, y, w, Tema.S(28)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        y += Tema.S(28);

        // "● 3 çevrimiçi · 12 üye"
        var nokta = new Rectangle(x, y + Tema.S(6), Tema.S(8), Tema.S(8));
        using (var b = new SolidBrush(Tema.Basari)) g.FillEllipse(b, nokta);
        TextRenderer.DrawText(g, $"{_cevrimici} çevrimiçi  ·  {_uyeSayisi} üye", Tema.Yazi(10), new Rectangle(x + Tema.S(14), y, w - Tema.S(14), Tema.S(20)), Tema.MetinIkincil,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        y += Tema.S(22) + Tema.S(8);
        using (var pen = new Pen(Tema.Kenar)) g.DrawLine(pen, x, y, x + w, y);
        y += Tema.S(10);

        TextRenderer.DrawText(g, _herkese ? "KEŞFET'TE GÖRÜNÜR" : "SADECE DAVETLE", Tema.Yazi(8.5f, kalin: true), new Point(x, y), _herkese ? Tema.Basari : Tema.MetinIkincil, TextFormatFlags.NoPadding);
        y += Tema.S(22);
        bool bos = string.IsNullOrWhiteSpace(_aciklama);
        TextRenderer.DrawText(g, MetinYardimci.Satirla(AciklamaYazisi, Tema.Yazi(10), w, 4), Tema.Yazi(10), new Rectangle(x, y, w, bilgi.Bottom - y - Tema.S(8)), bos ? Tema.MetinSoluk : Tema.Metin,
            TextFormatFlags.NoPadding);

        using var cerceve = Tema.YuvarlakDikdortgen(new RectangleF(0.5f, 0.5f, kart.Width, kart.Height), Tema.S(12));
        using var pen2 = new Pen(Tema.Kenar);
        g.DrawPath(pen2, cerceve);
    }
}
