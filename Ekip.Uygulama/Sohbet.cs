using Ekip.Ortak;

namespace Ekip.Uygulama;

// Bir sohbetin bellekte tutulan mesajları.
public class SohbetVerisi
{
    public List<Mesaj> Liste = [];
    public bool Yuklendi, Tumu, Yukleniyor;
}

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

// Mesajların çizildiği liste. Değişken yükseklikli satırlar, sadece görünenler çizilir.
// Aynı kişinin 5 dakika içindeki ardışık mesajları tek başlık altında toplanır.
public class MesajListesi : Control
{
    private enum Tur { Baslangic, Tarih, Mesaj }
    private record Oge(Tur Tur, Mesaj? M, bool GrupBasi, DateTime Gun, int Y, int H, int MetinH);

    private Profil? _ben, _karsi;
    private List<Mesaj> _mesajlar = [];
    private bool _tumu;
    private HashSet<string> _basarisiz = [];
    private List<Oge> _ogeler = [];
    private int _toplamH, _kaydirma, _olcuGenisligi = -1, _uzerinde = -1;
    private int? _surukleBaslangic;
    private int _surukleKaydirma;

    public AvatarOnbellek? Avatarlar { get; set; }
    public event Action? EskiIste;
    public event Action<Mesaj>? TekrarDene;

    private const TextFormatFlags MetinBicimi = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    private static Font MetinYazisi => Tema.Yazi(10.5f);

    public MesajListesi()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Tema.Arkaplan;
    }

    private int MetinX => Tema.S(72);
    private int MetinW => Math.Max(Tema.S(50), Width - MetinX - Tema.S(34));
    private int MaksKaydirma => Math.Max(0, _toplamH - Height);
    private bool KaydirmaVar => _toplamH > Height;
    private bool Altta => _kaydirma >= MaksKaydirma - Tema.S(30);
    // İçerik kısaysa mesajlar alta yaslanır (sohbet uygulamalarındaki gibi).
    private int UstBosluk => Math.Max(0, Height - _toplamH);

    public void Ayarla(Profil ben, Profil karsi, List<Mesaj> mesajlar, bool tumu, HashSet<string> basarisiz)
    {
        bool yeniSohbet = _karsi?.Id != karsi.Id;
        bool alttaydi = Altta;
        int eskiToplam = _toplamH;
        long? eskiIlk = _mesajlar.FirstOrDefault()?.Id;
        bool eskiTumu = _tumu;

        _ben = ben;
        _karsi = karsi;
        _mesajlar = mesajlar;
        _tumu = tumu;
        _basarisiz = basarisiz;
        Yerlesim();

        if (yeniSohbet || eskiIlk is null) _kaydirma = MaksKaydirma;
        else if (eskiIlk != null && mesajlar.Count > 0 && mesajlar[0].Id != eskiIlk && mesajlar.Any(m => m.Id == eskiIlk) || (!eskiTumu && tumu))
            _kaydirma = Math.Clamp(_kaydirma + (_toplamH - eskiToplam), 0, MaksKaydirma); // Eskiler üste eklendi: görünen yer kaymasın.
        else if (alttaydi) _kaydirma = MaksKaydirma;
        else _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
        Invalidate();
        EskileriGerekirseIste();
    }

    public void KarsiGuncelle(Profil p) { _karsi = p; Invalidate(); }
    public void Alta() { _kaydirma = MaksKaydirma; Invalidate(); }

    private void Yerlesim()
    {
        _olcuGenisligi = MetinW;
        var ogeler = new List<Oge>();
        int y = 0;
        if (_tumu && _karsi != null) { ogeler.Add(new Oge(Tur.Baslangic, null, false, default, y, Tema.S(200), 0)); y += Tema.S(200); }

        Mesaj? onceki = null;
        foreach (var m in _mesajlar)
        {
            var gun = Zaman.Yerel(m.Zaman).Date;
            if (onceki is null || Zaman.Yerel(onceki.Zaman).Date != gun)
            {
                ogeler.Add(new Oge(Tur.Tarih, null, false, gun, y, Tema.S(48), 0));
                y += Tema.S(48);
                onceki = null;
            }
            bool grupBasi = onceki is null || onceki.GonderenId != m.GonderenId || m.Zaman - onceki.Zaman > 5 * 60 * 1000;
            int metinH = TextRenderer.MeasureText(m.Metin, MetinYazisi, new Size(_olcuGenisligi, int.MaxValue), MetinBicimi).Height;
            int h = metinH + Tema.S(4) + (grupBasi ? Tema.S(40) : 0) + (Basarisiz(m) ? Tema.S(20) : 0);
            ogeler.Add(new Oge(Tur.Mesaj, m, grupBasi, gun, y, h, metinH));
            y += h;
            onceki = m;
        }
        _ogeler = ogeler;
        _toplamH = y + Tema.S(18);
    }

    private bool Basarisiz(Mesaj m) => m.IstemciId != null && m.Id < 0 && _basarisiz.Contains(m.IstemciId);

    // Grup sohbetinde (parti) gönderen ben ya da karşı taraf değil: kim olduğunu bu söyler.
    public Func<long, Profil?>? Cozucu { get; set; }

    private Profil? Sahibi(Mesaj m) => m.GonderenId == _ben?.Id ? _ben : Cozucu?.Invoke(m.GonderenId) ?? _karsi;

    private int EkranY(Oge o) => o.Y - _kaydirma + UstBosluk;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_karsi is null || _ben is null) return;

        // İlk görünen öğeyi ikili arama ile bul.
        int lo = 0, hi = _ogeler.Count - 1, ilk = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_ogeler[mid].Y + _ogeler[mid].H < _kaydirma - UstBosluk) lo = mid + 1; else { ilk = mid; hi = mid - 1; }
        }

        for (int i = ilk; i < _ogeler.Count; i++)
        {
            var o = _ogeler[i];
            int y = EkranY(o);
            if (y > Height) break;
            var w = Width - (KaydirmaVar ? Tema.S(12) : 0);

            switch (o.Tur)
            {
                case Tur.Baslangic:
                {
                    int x = Tema.S(18);
                    int av = Tema.S(80);
                    Cizim.Avatar(g, new RectangleF(x, y + Tema.S(24), av, av), _karsi, Avatarlar?.Al(_karsi), BackColor, false);
                    TextRenderer.DrawText(g, _karsi.GorunenAd, Tema.Yazi(18, kalin: true), new Point(x - Tema.S(2), y + Tema.S(112)), Tema.Metin, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, $"Bu, {_karsi.GorunenAd} ile olan mesajlaşmanın başlangıcı. Selam ver!",
                        Tema.Yazi(10.5f), new Rectangle(x, y + Tema.S(152), w - x * 2, Tema.S(24)), Tema.MetinIkincil, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    break;
                }
                case Tur.Tarih:
                {
                    int orta = y + o.H / 2 + Tema.S(4);
                    var yazi = Zaman.Gun(o.Gun);
                    var f = Tema.Yazi(8.5f, kalin: true);
                    var olcu = TextRenderer.MeasureText(g, yazi, f, Size.Empty, TextFormatFlags.NoPadding);
                    int tx = (w - olcu.Width) / 2;
                    using (var p = new Pen(Tema.Kenar))
                    {
                        g.DrawLine(p, Tema.S(18), orta, tx - Tema.S(10), orta);
                        g.DrawLine(p, tx + olcu.Width + Tema.S(10), orta, w - Tema.S(18), orta);
                    }
                    TextRenderer.DrawText(g, yazi, f, new Point(tx, orta - olcu.Height / 2), Tema.MetinSoluk, TextFormatFlags.NoPadding);
                    break;
                }
                case Tur.Mesaj:
                {
                    var m = o.M!;
                    if (i == _uzerinde) using (var b = new SolidBrush(ColorTranslator.FromHtml("#1B1C21"))) g.FillRectangle(b, 0, y + (o.GrupBasi ? Tema.S(14) : 0), w, o.H - (o.GrupBasi ? Tema.S(14) : 0));

                    int metinY = y + Tema.S(2);
                    if (o.GrupBasi)
                    {
                        var sahip = Sahibi(m)!;
                        int av = Tema.S(40);
                        Cizim.Avatar(g, new RectangleF(Tema.S(18), y + Tema.S(18), av, av), sahip, Avatarlar?.Al(sahip), BackColor, false);
                        var adF = Tema.Yazi(10.5f, kalin: true);
                        var adW = TextRenderer.MeasureText(g, sahip.GorunenAd, adF, Size.Empty, TextFormatFlags.NoPadding).Width;
                        TextRenderer.DrawText(g, sahip.GorunenAd, adF, new Point(MetinX, y + Tema.S(18)), Tema.Metin, TextFormatFlags.NoPadding);
                        TextRenderer.DrawText(g, Zaman.Uzun(m.Zaman), Tema.Yazi(8.5f), new Point(MetinX + adW + Tema.S(8), y + Tema.S(21)), Tema.MetinSoluk, TextFormatFlags.NoPadding);
                        metinY = y + Tema.S(42);
                    }
                    else if (i == _uzerinde)
                    {
                        TextRenderer.DrawText(g, Zaman.Kisa(m.Zaman), Tema.Yazi(8), new Rectangle(0, metinY + Tema.S(2), MetinX - Tema.S(8), Tema.S(16)),
                            Tema.MetinSoluk, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    }

                    bool basarisiz = Basarisiz(m);
                    var renk = basarisiz ? Tema.Hata : m.Id < 0 ? Tema.MetinSoluk : ColorTranslator.FromHtml("#DCDDE3");
                    TextRenderer.DrawText(g, m.Metin, MetinYazisi, new Rectangle(MetinX, metinY, _olcuGenisligi, o.MetinH + 2), renk, MetinBicimi);
                    if (basarisiz)
                        TextRenderer.DrawText(g, "Gönderilemedi. Tekrar denemek için tıkla.", Tema.Yazi(8.5f, kalin: true),
                            new Point(MetinX, metinY + o.MetinH + Tema.S(3)), Tema.Hata, TextFormatFlags.NoPadding);
                    break;
                }
            }
        }

        if (KaydirmaVar) Cizim.YuvarlakDoldur(g, KaydirmaCubugu(), Tema.S(3), _surukleBaslangic != null ? Tema.KenarHover : Tema.Kenar);
    }

    private Rectangle KaydirmaCubugu()
    {
        int h = Math.Max(Tema.S(40), Height * Height / Math.Max(1, _toplamH));
        int y = MaksKaydirma == 0 ? 0 : (Height - h) * _kaydirma / MaksKaydirma;
        return new Rectangle(Width - Tema.S(9), y, Tema.S(6), h);
    }

    private int Bul(Point p)
    {
        for (int i = 0; i < _ogeler.Count; i++)
        {
            var o = _ogeler[i];
            int y = EkranY(o);
            if (o.Tur == Tur.Mesaj && p.Y >= y + (o.GrupBasi ? Tema.S(14) : 0) && p.Y < y + o.H) return i;
            if (y > p.Y) break;
        }
        return -1;
    }

    private void EskileriGerekirseIste()
    {
        if (!_tumu && _mesajlar.Count > 0 && _kaydirma < Tema.S(300)) EskiIste?.Invoke();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_karsi is null) return;
        bool alttaydi = Altta;
        if (MetinW != _olcuGenisligi) Yerlesim();
        _kaydirma = alttaydi ? MaksKaydirma : Math.Clamp(_kaydirma, 0, MaksKaydirma);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _kaydirma = Math.Clamp(_kaydirma - e.Delta * Tema.S(60) / 120, 0, MaksKaydirma);
        _uzerinde = Bul(e.Location);
        Invalidate();
        EskileriGerekirseIste();
    }

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
        int i = Bul(e.Location);
        if (i == _uzerinde) return;
        _uzerinde = i;
        Cursor = i >= 0 && Basarisiz(_ogeler[i].M!) ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && KaydirmaVar && e.X > Width - Tema.S(14))
        {
            var c = KaydirmaCubugu();
            if (!c.Contains(e.Location)) { _kaydirma = Math.Clamp(_kaydirma + (e.Y < c.Y ? -Height : Height), 0, MaksKaydirma); Invalidate(); return; }
            _surukleBaslangic = e.Y;
            _surukleKaydirma = _kaydirma;
            Capture = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_surukleBaslangic != null) { _surukleBaslangic = null; Capture = false; Invalidate(); return; }
        int i = Bul(e.Location);
        if (i < 0) return;
        var m = _ogeler[i].M!;
        if (e.Button == MouseButtons.Left && Basarisiz(m)) TekrarDene?.Invoke(m);
        else if (e.Button == MouseButtons.Right)
        {
            var menu = new KoyuMenu();
            menu.Ekle("Metni kopyala", () => { try { Clipboard.SetText(m.Metin); } catch { } });
            menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
            menu.Show(this, e.Location);
        }
    }
}

// Açık sohbet: üstte kişi bilgisi, ortada mesajlar, altta yazma kutusu.
public class SohbetPaneli : Control
{
    private Profil? _karsi;
    private string? _yaziyor;
    private readonly MesajListesi _liste = new();
    private readonly GirisKutusu _girdi = new() { CokSatir = true, MaksUzunluk = Kurallar.MesajMax };

    public event Action<string>? Gonder;
    public event Action? Yaziyor;
    public event Action? EskiIste { add => _liste.EskiIste += value; remove => _liste.EskiIste -= value; }
    public event Action<Mesaj>? TekrarDene { add => _liste.TekrarDene += value; remove => _liste.TekrarDene -= value; }

    public AvatarOnbellek? Avatarlar { get => _liste.Avatarlar; set => _liste.Avatarlar = value; }
    public long? KarsiId => _karsi?.Id;
    public Func<long, Profil?>? Cozucu { get => _liste.Cozucu; set => _liste.Cozucu = value; }
    public string? Ipucu { get; set; } // Yazma kutusundaki ipucunu değiştirmek için (parti sohbeti).

    public bool BaslikGizli { get; set; } // Overlay'deki parti sohbetinde başlık sekmede zaten var.
    private int BaslikH => BaslikGizli ? 0 : Tema.S(56);
    private int YaziyorH => Tema.S(24);

    public SohbetPaneli()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
        Controls.AddRange([_liste, _girdi]);

        var tb = _girdi.IcKutu;
        tb.AcceptsReturn = true;
        tb.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                var metin = tb.Text.Trim();
                if (metin.Length == 0) return;
                tb.Clear();
                Gonder?.Invoke(metin);
            }
        };
        tb.TextChanged += (_, _) =>
        {
            GirdiYuksekliginiAyarla();
            if (tb.TextLength > 0) Yaziyor?.Invoke();
        };
    }

    public void Goster(Profil ben, Profil karsi, SohbetVerisi v, HashSet<string> basarisiz)
    {
        bool yeni = _karsi?.Id != karsi.Id;
        _karsi = karsi;
        if (yeni)
        {
            _yaziyor = null;
            _girdi.Text = "";
        }
        _girdi.IpucuYazisi = Ipucu ?? $"{karsi.GorunenAd} kişisine mesaj gönder";
        _liste.Ayarla(ben, karsi, v.Liste, v.Tumu, basarisiz);
        Invalidate();
    }

    public void KarsiGuncelle(Profil p)
    {
        if (p.Id != _karsi?.Id) return;
        _karsi = p;
        _liste.KarsiGuncelle(p);
        _girdi.IpucuYazisi = Ipucu ?? $"{p.GorunenAd} kişisine mesaj gönder";
        Invalidate();
    }

    public void YaziyorGoster(bool goster)
    {
        _yaziyor = goster && _karsi != null ? _karsi.GorunenAd : null;
        Invalidate(new Rectangle(0, Height - YaziyorH, Width, YaziyorH));
    }

    public void Odaklan() => _girdi.IcKutu.Focus();
    public void Alta() => _liste.Alta();

    private void GirdiYuksekliginiAyarla()
    {
        var tb = _girdi.IcKutu;
        var olcu = TextRenderer.MeasureText(tb.Text.Length == 0 ? "A" : tb.Text + "A", tb.Font,
            new Size(Math.Max(10, tb.Width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        int h = Math.Clamp(olcu.Height + Tema.S(26), Tema.S(48), Tema.S(170));
        if (h != _girdi.Height) { _girdi.Height = h; PerformLayout(); }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_liste is null) return;
        int yan = Tema.S(16);
        if (_girdi.Height < Tema.S(48)) _girdi.Height = Tema.S(48);
        int girdiY = Height - YaziyorH - _girdi.Height;
        _girdi.Bounds = new Rectangle(yan, girdiY, Width - yan * 2, _girdi.Height);
        _liste.Bounds = new Rectangle(0, BaslikH, Width, girdiY - BaslikH - Tema.S(8));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (_karsi is null) return;
        if (!BaslikGizli) BaslikCiz(g);
        YaziyorCiz(g);
    }

    private void BaslikCiz(Graphics g)
    {
        if (_karsi is null) return;
        int av = Tema.S(28);
        int x = Tema.S(18);
        Cizim.Avatar(g, new RectangleF(x, (BaslikH - av) / 2f, av, av), _karsi, Avatarlar?.Al(_karsi), BackColor);
        x += av + Tema.S(12);
        var adF = Tema.Yazi(11.5f, kalin: true);
        var adW = TextRenderer.MeasureText(g, _karsi.GorunenAd, adF, Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, _karsi.GorunenAd, adF, new Rectangle(x, 0, adW + 4, BaslikH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += adW + Tema.S(14);

        var durum = string.IsNullOrWhiteSpace(_karsi.DurumMesaji) || _karsi.Durum == Durum.Cevrimdisi ? Cizim.DurumAdi(_karsi.Durum) : _karsi.DurumMesaji;
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, BaslikH / 2 - Tema.S(10), x, BaslikH / 2 + Tema.S(10));
        x += Tema.S(14);
        TextRenderer.DrawText(g, durum, Tema.Yazi(9.5f), new Rectangle(x, 0, Width - x - Tema.S(16), BaslikH), Tema.MetinIkincil,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        using (var p = new Pen(ColorTranslator.FromHtml("#0E0F12"))) g.DrawLine(p, 0, BaslikH - 1, Width, BaslikH - 1);
    }

    private void YaziyorCiz(Graphics g)
    {
        if (_yaziyor != null)
        {
            int y = Height - YaziyorH;
            var kalin = Tema.Yazi(9, kalin: true);
            var w = TextRenderer.MeasureText(g, _yaziyor, kalin, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, "• • •", Tema.Yazi(9, kalin: true), new Rectangle(Tema.S(18), y, Tema.S(30), YaziyorH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _yaziyor, kalin, new Rectangle(Tema.S(50), y, w + 2, YaziyorH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, " yazıyor…", Tema.Yazi(9), new Rectangle(Tema.S(50) + w, y, Tema.S(100), YaziyorH), Tema.MetinIkincil, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
