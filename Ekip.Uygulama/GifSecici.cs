using System.Drawing.Drawing2D;
using System.Net;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// GIF seçici paneli (~420x460): arama (400 ms bekleyerek), boşken popüler GIF'ler, 2 sütunlu duvar ızgarası,
// kaydırdıkça sonraki sayfa. Önizlemeler DosyaOnbellegi ile iner; sadece GÖRÜNEN önizlemeler oynar
// (AnimasyonluResim ortak zamanlayıcısı), uzaktakilerin kareleri bellekten bırakılır.
//
//   var s = new GifSecici(api);
//   s.Secildi += gif => gonder(gif);                 // gif.Url mesaj ekine (Ek, Tur "gif") çevrilir
//   s.Goster(form, gifDugmesi.PointToScreen(new Point(gifDugmesi.Width, 0)));
//
// Sunucuda GIF anahtarı ayarlı değilse (HTTP 503) "GIF servisi henüz ayarlanmadı" boş durumu gösterilir.
// Her örnek tek kullanımlıktır (kapanınca Dispose edilir).
public sealed class GifSecici : AcilirPanel
{
    private sealed class Oge
    {
        public required GifSonucu Veri;
        public Rectangle Alan;               // içerik koordinatında
        public Image? Resim;
        public AnimasyonluResim? Anim;
        public IDisposable? Kayit;
        public bool Istendi, Basarisiz;
    }

    private enum Durumlar { Bos, Yukleniyor, Servis, Hata, Sonuc }

    private readonly Api _api;
    private readonly AramaKutusu _ara;
    private readonly System.Windows.Forms.Timer _bekleme;
    private readonly List<Oge> _ogeler = [];
    private readonly int[] _sutunY = new int[2];
    private string _sorgu = "";
    private string? _sonraki;
    private int _surum, _kaydirma, _icerikH, _uzerinde = -1;
    private bool _sonrakiYukleniyor, _sayfaHatasi, _kapandi;
    private Durumlar _durum = Durumlar.Yukleniyor;
    private string? _hataMesaji;
    private int? _surukleY;
    private int _surukleKaydirma;
    private Rectangle _tekrarAlani = Rectangle.Empty;

    // Seçilen GIF. Url = orta boy GIF (gönderilecek), OnizlemeUrl = küçük hali.
    public event Action<GifSonucu>? Secildi;

    private int Pad => Tema.S(10);
    private int Bosluk => Tema.S(6);
    private int KaydirmaBosluk => Tema.S(10);
    private int BaslikH => Tema.S(26);
    private Rectangle AramaAlani => new(Pad, Pad, Width - Pad * 2, Tema.S(36));
    private Rectangle IzgaraAlani => new(0, AramaAlani.Bottom + Tema.S(8), Width, Height - AramaAlani.Bottom - Tema.S(8) - Tema.S(4));
    private int SutunGenisligi => (Width - Pad * 2 - KaydirmaBosluk - Bosluk) / 2;
    private int MaksKaydirma => Math.Max(0, _icerikH - IzgaraAlani.Height);

    public GifSecici(Api api) : base(new Size(Tema.S(420), Tema.S(460)))
    {
        _api = api;
        _ara = new AramaKutusu("GIF ara…");
        _ara.MetinDegisti += () => { _bekleme!.Stop(); _bekleme.Start(); };
        _ara.IcKutu.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _bekleme!.Stop(); Ara(_ara.Metin.Trim()); }
        };
        Controls.Add(_ara);
        _ara.Bounds = AramaAlani;
        _bekleme = new System.Windows.Forms.Timer { Interval = 400 }; // yazmayı bırakınca ara
        _bekleme.Tick += (_, _) => { _bekleme.Stop(); Ara(_ara.Metin.Trim()); };
    }

    protected override void Acildi()
    {
        _ara.Odakla();
        Ara("");
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_ara == null) return;
        _ara.Bounds = AramaAlani;
        YenidenYerlestir();
    }

    // ---------------------------------------------------------------- Veri

    private void Temizle()
    {
        foreach (var o in _ogeler) o.Kayit?.Dispose();
        _ogeler.Clear();
        _sutunY[0] = _sutunY[1] = BaslikH;
        _icerikH = 0;
        _kaydirma = 0;
        _uzerinde = -1;
        _sonraki = null;
        _sonrakiYukleniyor = _sayfaHatasi = false;
    }

    private async void Ara(string sorgu)
    {
        int surum = ++_surum;
        _sorgu = sorgu;
        Temizle();
        _durum = Durumlar.Yukleniyor;
        _hataMesaji = null;
        Invalidate();
        var s = await _api.GifAra(sorgu.Length == 0 ? null : sorgu);
        if (_kapandi || IsDisposed || surum != _surum) return;
        if (!s.Basarili || s.Veri is null)
        {
            _durum = s.Durum == HttpStatusCode.ServiceUnavailable ? Durumlar.Servis : Durumlar.Hata;
            _hataMesaji = s.Hata;
            Invalidate();
            return;
        }
        Ekle(s.Veri);
        _durum = _ogeler.Count == 0 ? Durumlar.Bos : Durumlar.Sonuc;
        YenidenYerlestir();
    }

    private async void SonrakiSayfa()
    {
        if (_sonrakiYukleniyor || _sonraki is null || _durum != Durumlar.Sonuc) return;
        _sonrakiYukleniyor = true;
        _sayfaHatasi = false;
        int surum = _surum;
        Invalidate();
        var s = await _api.GifAra(_sorgu.Length == 0 ? null : _sorgu, _sonraki);
        if (_kapandi || IsDisposed || surum != _surum) return;
        _sonrakiYukleniyor = false;
        if (!s.Basarili || s.Veri is null) { _sayfaHatasi = true; Invalidate(); return; }
        Ekle(s.Veri);
        YenidenYerlestir();
    }

    private void Ekle(GifSayfasi sayfa)
    {
        _sonraki = string.IsNullOrEmpty(sayfa.Sonraki) ? null : sayfa.Sonraki;
        foreach (var g in sayfa.Sonuclar)
        {
            if (_ogeler.Any(o => o.Veri.Id == g.Id)) continue;
            _ogeler.Add(new Oge { Veri = g });
        }
    }

    // Sütun genişliği değişince ya da öğe eklenince tüm duvarı yeniden diz.
    private void YenidenYerlestir()
    {
        int w = SutunGenisligi;
        _sutunY[0] = _sutunY[1] = BaslikH;
        foreach (var o in _ogeler)
        {
            int h = o.Veri.Genislik > 0 && o.Veri.Yukseklik > 0 ? (int)Math.Round(w * (double)o.Veri.Yukseklik / o.Veri.Genislik) : w * 3 / 4;
            h = Math.Clamp(h, Tema.S(70), Tema.S(250));
            int sutun = _sutunY[0] <= _sutunY[1] ? 0 : 1;
            o.Alan = new Rectangle(Pad + sutun * (w + Bosluk), _sutunY[sutun], w, h);
            _sutunY[sutun] += h + Bosluk;
        }
        _icerikH = Math.Max(_sutunY[0], _sutunY[1]) + Tema.S(30);
        _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
        GorunurleriGuncelle();
        Invalidate();
    }

    // Görünen öğelerin resmini iste ve animasyonu kaydet; yakındakileri önceden yükle; uzaktakilerin belleğini bırak.
    private void GorunurleriGuncelle()
    {
        var iz = IzgaraAlani;
        int ust = _kaydirma, alt = _kaydirma + iz.Height;
        for (int i = 0; i < _ogeler.Count; i++)
        {
            var o = _ogeler[i];
            bool gorunur = o.Alan.Bottom >= ust && o.Alan.Top <= alt;
            bool yakin = o.Alan.Bottom >= ust - iz.Height && o.Alan.Top <= alt + iz.Height;
            if (yakin && !o.Istendi && !o.Basarisiz) Yukle(o);
            if (!yakin && (o.Resim != null || o.Istendi))
            {
                o.Kayit?.Dispose(); o.Kayit = null;
                o.Resim = null; o.Anim = null; o.Istendi = false; // önbellek belleği gerekirse alır
            }
            if (gorunur && o.Anim != null && o.Kayit == null)
            {
                o.Kayit = AnimasyonluResim.Kaydet(this, o.Anim, () => InvalidateOge(o));
            }
            else if (!gorunur && o.Kayit != null) { o.Kayit.Dispose(); o.Kayit = null; }
        }
        if (_sonraki != null && !_sonrakiYukleniyor && !_sayfaHatasi && _kaydirma + iz.Height * 2 >= _icerikH) SonrakiSayfa();
    }

    private void InvalidateOge(Oge o)
    {
        var iz = IzgaraAlani;
        var r = new Rectangle(o.Alan.X, iz.Top + o.Alan.Y - _kaydirma, o.Alan.Width, o.Alan.Height);
        if (r.Bottom > iz.Top && r.Top < iz.Bottom) Invalidate(r);
    }

    private async void Yukle(Oge o)
    {
        o.Istendi = true;
        int surum = _surum;
        var resim = await DosyaOnbellegi.ResimAl(_api, string.IsNullOrEmpty(o.Veri.OnizlemeUrl) ? o.Veri.Url : o.Veri.OnizlemeUrl, Math.Max(o.Alan.Width, o.Alan.Height));
        if (_kapandi || IsDisposed || surum != _surum || !o.Istendi) return;
        if (resim is null) { o.Basarisiz = true; o.Istendi = false; InvalidateOge(o); return; }
        o.Resim = resim;
        o.Anim = AnimasyonluResim.Bul(resim);
        InvalidateOge(o);
        GorunurleriGuncelle();
    }

    // ---------------------------------------------------------------- Çizim

    protected override void IcCiz(Graphics g)
    {
        var iz = IzgaraAlani;
        var eski = g.Clip;
        g.SetClip(iz);
        IzgaraCiz(g, iz);
        g.Clip = eski;
    }

    private void IzgaraCiz(Graphics g, Rectangle iz)
    {
        _tekrarAlani = Rectangle.Empty;
        if (_durum != Durumlar.Sonuc)
        {
            DurumCiz(g, iz);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g, _sorgu.Length == 0 ? "Popüler" : $"“{_sorgu}” için sonuçlar", Tema.Yazi(8.5f, true),
            new Rectangle(Pad + Tema.S(2), iz.Top - _kaydirma, Width - Pad * 2 - KaydirmaBosluk, BaslikH - Tema.S(4)), Tema.MetinIkincil,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        for (int i = 0; i < _ogeler.Count; i++)
        {
            var o = _ogeler[i];
            var r = new Rectangle(o.Alan.X, iz.Top + o.Alan.Y - _kaydirma, o.Alan.Width, o.Alan.Height);
            if (r.Bottom < iz.Top) continue;
            if (r.Top > iz.Bottom) continue; // duvar sütunlu: sonrakiler daha yukarıda olabilir, o yüzden break yok
            var img = o.Anim?.GecerliKare ?? o.Resim;
            using var yol = Tema.YuvarlakDikdortgen(new RectangleF(r.X, r.Y, r.Width, r.Height), Tema.S(6));
            if (img != null)
            {
                // "Kapla" kırpması: oran farklıysa ortadan kırp.
                float s = Math.Max(r.Width / (float)img.Width, r.Height / (float)img.Height);
                using var firca = new TextureBrush(img, WrapMode.Clamp);
                firca.TranslateTransform(r.X + (r.Width - img.Width * s) / 2f, r.Y + (r.Height - img.Height * s) / 2f);
                firca.ScaleTransform(s, s);
                g.FillPath(firca, yol);
            }
            else
            {
                using var zemin = new SolidBrush(Tema.YuzeyUst);
                g.FillPath(zemin, yol);
                if (o.Basarisiz)
                    TextRenderer.DrawText(g, Glif.Uyari, Cizim.Ikon(14), r, Tema.MetinSoluk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (i == _uzerinde)
            {
                using var kalem = new Pen(Tema.Vurgu, Tema.S(2));
                using var ic = Tema.YuvarlakDikdortgen(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Tema.S(6));
                g.DrawPath(kalem, ic);
            }
        }

        if (_sonrakiYukleniyor || _sayfaHatasi)
        {
            int fy = iz.Top + Math.Max(_sutunY[0], _sutunY[1]) - _kaydirma + Tema.S(4);
            var fr = new Rectangle(0, fy, Width, Tema.S(22));
            if (_sayfaHatasi)
            {
                TextRenderer.DrawText(g, "Devamı yüklenemedi · Tekrar dene", Tema.Yazi(9.5f, true), fr, Tema.Vurgu, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                _tekrarAlani = Rectangle.Intersect(fr, iz);
            }
            else
                TextRenderer.DrawText(g, "Yükleniyor…", Tema.Yazi(9.5f), fr, Tema.MetinSoluk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        if (MaksKaydirma > 0)
            Cizim.YuvarlakDoldur(g, KaydirmaCubugu(), Tema.S(3), _surukleY != null ? Tema.KenarHover : Tema.Kenar);
    }

    private void DurumCiz(Graphics g, Rectangle iz)
    {
        string glif, baslik, alt;
        switch (_durum)
        {
            case Durumlar.Yukleniyor:
                glif = Glif.Saat; baslik = "GIF’ler yükleniyor…"; alt = "";
                break;
            case Durumlar.Servis:
                glif = Glif.Uyari; baslik = "GIF servisi henüz ayarlanmadı"; alt = "Sunucu sahibi GIF anahtarını ekleyince burada görünecek.";
                break;
            case Durumlar.Hata:
                glif = Glif.Uyari; baslik = "GIF’ler yüklenemedi"; alt = _hataMesaji ?? "Biraz sonra tekrar dene.";
                break;
            default:
                glif = Glif.Ara; baslik = "Sonuç bulunamadı"; alt = "Başka bir şey aramayı dene.";
                break;
        }
        int y = iz.Top + (iz.Height - Tema.S(150)) / 2;
        TextRenderer.DrawText(g, glif, Cizim.Ikon(24), new Rectangle(0, y, Width, Tema.S(44)), Tema.MetinSoluk, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, baslik, Tema.Yazi(11, true), new Rectangle(Pad, y + Tema.S(54), Width - Pad * 2, Tema.S(24)), Tema.Metin,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        if (alt.Length > 0)
            TextRenderer.DrawText(g, alt, Tema.Yazi(9.5f), new Rectangle(Tema.S(30), y + Tema.S(82), Width - Tema.S(60), Tema.S(40)), Tema.MetinSoluk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        if (_durum is Durumlar.Hata or Durumlar.Servis)
        {
            var d = new Rectangle((Width - Tema.S(120)) / 2, y + Tema.S(124), Tema.S(120), Tema.S(32));
            Cizim.YuvarlakDoldur(g, d, Tema.S(8), _uzerinde == -2 ? Tema.Kenar : Tema.YuzeyUst);
            TextRenderer.DrawText(g, "Tekrar dene", Tema.Yazi(9.5f, true), d, Tema.Metin, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            _tekrarAlani = d;
        }
    }

    private Rectangle KaydirmaCubugu()
    {
        var iz = IzgaraAlani;
        int h = Math.Max(Tema.S(36), iz.Height * iz.Height / Math.Max(1, _icerikH));
        int y = MaksKaydirma == 0 ? 0 : (iz.Height - h) * _kaydirma / MaksKaydirma;
        return new Rectangle(Width - Tema.S(12), iz.Top + y, Tema.S(6), h);
    }

    // ---------------------------------------------------------------- Etkileşim

    private int OgeBul(Point p)
    {
        var iz = IzgaraAlani;
        if (!iz.Contains(p) || _durum != Durumlar.Sonuc) return -1;
        var ic = new Point(p.X, p.Y - iz.Top + _kaydirma);
        for (int i = 0; i < _ogeler.Count; i++) if (_ogeler[i].Alan.Contains(ic)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_surukleY is { } bas)
        {
            int aralik = Math.Max(1, IzgaraAlani.Height - KaydirmaCubugu().Height);
            Kaydir(_surukleKaydirma + (e.Y - bas) * MaksKaydirma / aralik);
            return;
        }
        int i = OgeBul(e.Location);
        bool tekrar = !_tekrarAlani.IsEmpty && _tekrarAlani.Contains(e.Location);
        int yeni = tekrar && _durum != Durumlar.Sonuc ? -2 : i;
        if (yeni != _uzerinde)
        {
            // Eski ve yeni öğeyi yeniden çiz.
            int eski = _uzerinde;
            _uzerinde = yeni;
            Cursor = yeni != -1 || tekrar ? Cursors.Hand : Cursors.Default;
            if (eski >= 0 && eski < _ogeler.Count) InvalidateOge(_ogeler[eski]);
            if (yeni >= 0) InvalidateOge(_ogeler[yeni]); else Invalidate();
        }
        else if (tekrar) Cursor = Cursors.Hand;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_uzerinde != -1) { int eski = _uzerinde; _uzerinde = -1; Cursor = Cursors.Default; if (eski >= 0 && eski < _ogeler.Count) InvalidateOge(_ogeler[eski]); else Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (MaksKaydirma > 0 && _durum == Durumlar.Sonuc && Rectangle.Inflate(KaydirmaCubugu(), Tema.S(4), 0).Contains(e.Location))
        {
            _surukleY = e.Y;
            _surukleKaydirma = _kaydirma;
            return;
        }
        if (!_tekrarAlani.IsEmpty && _tekrarAlani.Contains(e.Location))
        {
            if (_durum == Durumlar.Sonuc) { _sayfaHatasi = false; SonrakiSayfa(); }
            else Ara(_sorgu);
            return;
        }
        int i = OgeBul(e.Location);
        if (i >= 0)
        {
            var gif = _ogeler[i].Veri;
            Secildi?.Invoke(gif);
            Kapat();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_surukleY != null) { _surukleY = null; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Tekerlek(e.Delta); }
    protected override void Tekerlek(int delta) => Kaydir(_kaydirma - delta * Tema.S(80) / 120);

    private void Kaydir(int yeni)
    {
        yeni = Math.Clamp(yeni, 0, MaksKaydirma);
        if (yeni == _kaydirma) return;
        _kaydirma = yeni;
        _uzerinde = -1;
        GorunurleriGuncelle();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _kapandi = true;
            _surum++;
            _bekleme.Dispose();
            foreach (var o in _ogeler) { o.Kayit?.Dispose(); o.Kayit = null; o.Resim = null; o.Anim = null; }
        }
        base.Dispose(disposing);
    }
}
