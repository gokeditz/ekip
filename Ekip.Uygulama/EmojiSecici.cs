using System.Drawing.Drawing2D;
using System.Text.Json;

namespace Ekip.Uygulama;

// "Son kullanılanlar" listesi: %AppData%\Ekip[-profil]\son-emojiler.json (en yeni başta, en çok 36).
// Ayarlar.Klasor özel olduğu için yol mantığı burada aynen tekrarlanır.
public static class SonEmojiler
{
    public const int EnCok = 36;
    private static readonly object _kilit = new();
    private static List<string>? _liste;

    private static string Dosya => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Ayarlar.ProfilAdi is null ? "Ekip" : "Ekip-" + Ayarlar.ProfilAdi, "son-emojiler.json");

    public static IReadOnlyList<string> Liste
    {
        get { lock (_kilit) return Yukle().ToList(); }
    }

    // Emojiyi listenin başına alır ve kaydeder (zaten varsa öne taşır).
    public static void Ekle(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return;
        lock (_kilit)
        {
            var l = Yukle();
            l.RemoveAll(x => EmojiVeri.Anahtar(x) == EmojiVeri.Anahtar(emoji));
            l.Insert(0, emoji);
            if (l.Count > EnCok) l.RemoveRange(EnCok, l.Count - EnCok);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Dosya)!);
                File.WriteAllText(Dosya, JsonSerializer.Serialize(l));
            }
            catch { /* Yazılamazsa sadece bu oturumda hatırlanır. */ }
        }
    }

    private static List<string> Yukle()
    {
        if (_liste != null) return _liste;
        try { _liste = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Dosya)) ?? []; }
        catch { _liste = []; }
        _liste.RemoveAll(string.IsNullOrWhiteSpace);
        return _liste;
    }
}

// Emoji seçici paneli (~360x400): arama, kategori sekmeleri, sanallaştırılmış ızgara, üzerine gelince ad önizlemesi.
//
//   var s = new EmojiSecici();
//   s.Secildi += emoji => kutu.SelectedText = emoji;
//   s.Goster(form, kutu.PointToScreen(new Point(kutu.Width, 0)));   // düğmenin üst-sağ köşesi
//
// Klavye: oklar gezinir, Enter seçer, Esc kapatır, yazınca arar. Shift basılıyken seçim paneli kapatmaz.
// Her örnek tek kullanımlıktır (kapanınca Dispose edilir); tekrar açmak için yenisini oluştur.
public sealed class EmojiSecici : AcilirPanel
{
    private abstract record Satir(int Y, int H);
    private sealed record BaslikSatiri(int Y, int H, string Metin) : Satir(Y, H);
    private sealed record EmojiSatiri(int Y, int H, int Ilk, int Adet) : Satir(Y, H);

    private readonly AramaKutusu _ara;
    private List<EmojiKaydi> _liste = [];
    private List<Satir> _satirlar = [];
    private int[] _bolumY = [];                 // sekmeye karşılık gelen bölümün Y'si
    private (int Kat, string Simge)[] _sekmeler = []; // Kat: -1 son kullanılanlar, 0.. kategori
    private int _toplamH, _kaydirma, _odak = -1, _sekmeUzerinde = -1, _aktifSekme;
    private int? _surukleY;
    private int _surukleKaydirma;
    private bool _aramaModu;

    // Seçilen emoji (mesaja yazılacak tam hali).
    public event Action<string>? Secildi;

    // true: seçince panel kapanır (Shift basılıysa kapanmaz).
    public bool SecinceKapat { get; set; } = true;

    private int Hucre => Tema.S(36);
    private int Pad => Tema.S(10);
    private int KaydirmaBosluk => Tema.S(10);
    private Rectangle AramaAlani => new(Pad, Pad, Width - Pad * 2, Tema.S(36));
    private Rectangle SekmeAlani => new(Pad, AramaAlani.Bottom + Tema.S(8), Width - Pad * 2, Tema.S(34));
    private Rectangle OnizlemeAlani => new(0, Height - Tema.S(48), Width, Tema.S(48));
    private Rectangle IzgaraAlani => new(0, SekmeAlani.Bottom + Tema.S(6), Width, OnizlemeAlani.Top - SekmeAlani.Bottom - Tema.S(6));
    private int Sutun => Math.Max(1, (Width - Pad * 2 - KaydirmaBosluk) / Hucre);
    private int IzgaraX => Pad + (Width - Pad * 2 - KaydirmaBosluk - Sutun * Hucre) / 2;
    private int MaksKaydirma => Math.Max(0, _toplamH - IzgaraAlani.Height);

    public EmojiSecici() : base(new Size(Tema.S(360), Tema.S(400)))
    {
        EmojiVeri.Hazirla();
        _ara = new AramaKutusu("Emoji ara…");
        _ara.MetinDegisti += AramaDegisti;
        Controls.Add(_ara);
        Yerlestir();
        Olustur();
    }

    private void Yerlestir() => _ara.Bounds = AramaAlani;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_ara == null) return;
        Yerlestir();
        Olustur();
    }

    protected override void Acildi() => _ara.Odakla();

    // ---------------------------------------------------------------- Model

    private void AramaDegisti()
    {
        _kaydirma = 0;
        Olustur();
        _odak = _aramaModu && _liste.Count > 0 ? 0 : -1;
        Invalidate();
    }

    private void Olustur()
    {
        var sorgu = _ara.Metin.Trim();
        _aramaModu = sorgu.Length > 0;
        var satirlar = new List<Satir>();
        var liste = new List<EmojiKaydi>();
        int y = 0, sutun = Sutun;
        int baslikH = Tema.S(30);

        void Bolum(string baslik, IReadOnlyList<EmojiKaydi> ogeler)
        {
            if (baslik.Length > 0) { satirlar.Add(new BaslikSatiri(y, baslikH, baslik)); y += baslikH; }
            for (int i = 0; i < ogeler.Count; i += sutun)
            {
                int adet = Math.Min(sutun, ogeler.Count - i);
                satirlar.Add(new EmojiSatiri(y, Hucre, liste.Count, adet));
                liste.AddRange(ogeler.Skip(i).Take(adet));
                y += Hucre;
            }
            y += Tema.S(6);
        }

        var bolumYler = new List<(int Kat, int Y)>();
        if (_aramaModu)
        {
            var sonuc = EmojiVeri.Ara(sorgu, 180);
            if (sonuc.Count > 0) Bolum($"Arama sonuçları · {sonuc.Count}", sonuc);
            _sekmeler = [];
        }
        else
        {
            var son = SonEmojiler.Liste.Select(EmojiVeri.Bul).OfType<EmojiKaydi>().ToList();
            var sekmeler = new List<(int, string)>();
            if (son.Count > 0) { bolumYler.Add((-1, y)); sekmeler.Add((-1, "")); Bolum("Son kullanılanlar", son); }
            for (int k = 0; k < EmojiVeri.KategoriAdlari.Length; k++)
            {
                var kat = EmojiVeri.Kategori(k);
                if (kat.Count == 0) continue;
                bolumYler.Add((k, y));
                sekmeler.Add((k, EmojiVeri.KategoriSimgeleri[k]));
                Bolum(EmojiVeri.KategoriAdlari[k], kat);
            }
            _sekmeler = sekmeler.ToArray();
        }
        _bolumY = bolumYler.Select(b => b.Y).ToArray();
        _satirlar = satirlar;
        _liste = liste;
        _toplamH = Math.Max(0, y - Tema.S(6)) + Tema.S(6);
        _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
        _odak = Math.Min(_odak, _liste.Count - 1);
        SekmeyiGuncelle();
    }

    private void SekmeyiGuncelle()
    {
        _aktifSekme = 0;
        for (int i = 0; i < _bolumY.Length; i++)
            if (_bolumY[i] <= _kaydirma + Tema.S(12)) _aktifSekme = i;
    }

    // ---------------------------------------------------------------- Çizim

    protected override void IcCiz(Graphics g)
    {
        var izgara = IzgaraAlani;
        var eskiKlip = g.Clip;
        g.SetClip(izgara);
        IzgaraCiz(g, izgara);
        g.Clip = eskiKlip;
        SekmeleriCiz(g);
        using (var ayirac = new Pen(Tema.Kenar)) g.DrawLine(ayirac, 0, izgara.Top - 1, Width, izgara.Top - 1);
        OnizlemeCiz(g);
    }

    private int SatirEkranY(Satir s) => IzgaraAlani.Top + s.Y - _kaydirma;

    private Rectangle HucreAlani(int indeks)
    {
        foreach (var s in _satirlar)
            if (s is EmojiSatiri es && indeks >= es.Ilk && indeks < es.Ilk + es.Adet)
                return new Rectangle(IzgaraX + (indeks - es.Ilk) * Hucre, SatirEkranY(es), Hucre, Hucre);
        return Rectangle.Empty;
    }

    private void IzgaraCiz(Graphics g, Rectangle izgara)
    {
        if (_liste.Count == 0)
        {
            // Boş durum.
            var yazi = _aramaModu ? "Hiç emoji bulunamadı" : "Emoji yok";
            var alt = _aramaModu ? "Başka bir kelime dene (Türkçe ya da İngilizce)." : "";
            TextRenderer.DrawText(g, Glif.Ara, Cizim.Ikon(22), new Rectangle(0, izgara.Top + Tema.S(60), Width, Tema.S(40)), Tema.MetinSoluk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, yazi, Tema.Yazi(11, true), new Rectangle(0, izgara.Top + Tema.S(110), Width, Tema.S(24)), Tema.Metin,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, alt, Tema.Yazi(9.5f), new Rectangle(Pad, izgara.Top + Tema.S(136), Width - Pad * 2, Tema.S(22)), Tema.MetinSoluk,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            return;
        }

        int emojiBoyut = Tema.S(26);
        // Görünen ilk satırı ikili aramayla bul.
        int lo = 0, hi = _satirlar.Count - 1, ilk = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_satirlar[mid].Y + _satirlar[mid].H < _kaydirma) lo = mid + 1; else { ilk = mid; hi = mid - 1; }
        }
        for (int i = ilk; i < _satirlar.Count; i++)
        {
            var s = _satirlar[i];
            int y = SatirEkranY(s);
            if (y > izgara.Bottom) break;
            if (s is BaslikSatiri b)
            {
                TextRenderer.DrawText(g, Tema.Turkce.TextInfo.ToUpper(b.Metin), Tema.Yazi(8.5f, true),
                    new Rectangle(Pad + Tema.S(4), y, Width - Pad * 2 - KaydirmaBosluk, b.H - Tema.S(2)), Tema.MetinIkincil,
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            else if (s is EmojiSatiri es)
            {
                for (int c = 0; c < es.Adet; c++)
                {
                    int idx = es.Ilk + c;
                    var hucre = new Rectangle(IzgaraX + c * Hucre, y, Hucre, Hucre);
                    if (idx == _odak) Cizim.YuvarlakDoldur(g, Rectangle.Inflate(hucre, -Tema.S(1), -Tema.S(1)), Tema.S(6), Tema.Kenar);
                    EmojiCizici.Ciz(g, _liste[idx].Emoji, hucre.X + (Hucre - emojiBoyut) / 2, hucre.Y + (Hucre - emojiBoyut) / 2, emojiBoyut);
                }
            }
        }

        if (MaksKaydirma > 0)
            Cizim.YuvarlakDoldur(g, KaydirmaCubugu(), Tema.S(3), _surukleY != null ? Tema.KenarHover : Tema.Kenar);
    }

    private Rectangle KaydirmaCubugu()
    {
        var iz = IzgaraAlani;
        int h = Math.Max(Tema.S(36), iz.Height * iz.Height / Math.Max(1, _toplamH));
        int y = MaksKaydirma == 0 ? 0 : (iz.Height - h) * _kaydirma / MaksKaydirma;
        return new Rectangle(Width - Tema.S(12), iz.Top + y, Tema.S(6), h);
    }

    private Rectangle SekmeDikdortgeni(int i)
    {
        var a = SekmeAlani;
        int w = a.Width / Math.Max(1, _sekmeler.Length);
        return new Rectangle(a.X + i * w, a.Y, w, a.Height);
    }

    private void SekmeleriCiz(Graphics g)
    {
        var a = SekmeAlani;
        if (_sekmeler.Length == 0) return; // arama sonuçlarında sekme yok
        int ikon = Tema.S(22);
        for (int i = 0; i < _sekmeler.Length; i++)
        {
            var r = SekmeDikdortgeni(i);
            bool aktif = i == _aktifSekme, uzerinde = i == _sekmeUzerinde;
            var kutu = Rectangle.Inflate(r, -Tema.S(2), -Tema.S(2));
            if (aktif) Cizim.YuvarlakDoldur(g, kutu, Tema.S(6), Tema.YuzeyUst);
            else if (uzerinde) Cizim.YuvarlakDoldur(g, kutu, Tema.S(6), Color.FromArgb(120, Tema.YuzeyUst));
            var (kat, simge) = _sekmeler[i];
            if (kat < 0)
                TextRenderer.DrawText(g, Glif.Saat, Cizim.Ikon(12), r, aktif ? Tema.Metin : Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            else
                EmojiCizici.Ciz(g, simge, r.X + (r.Width - ikon) / 2, r.Y + (r.Height - ikon) / 2 - Tema.S(1), ikon);
            if (aktif) Cizim.YuvarlakDoldur(g, new RectangleF(r.X + r.Width / 2f - Tema.S(8), r.Bottom - Tema.S(3), Tema.S(16), Tema.S(3)), Tema.S(1), Tema.Vurgu);
        }
    }

    private void OnizlemeCiz(Graphics g)
    {
        var a = OnizlemeAlani;
        using (var f = new SolidBrush(Tema.YuzeyUst)) g.FillRectangle(f, a);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, a.Left, a.Top, a.Right, a.Top);
        int b = Tema.S(30);
        if (_odak >= 0 && _odak < _liste.Count)
        {
            var e = _liste[_odak];
            EmojiCizici.Ciz(g, e.Emoji, a.X + Pad + Tema.S(2), a.Y + (a.Height - b) / 2, b);
            int tx = a.X + Pad + b + Tema.S(12), tw = a.Width - tx - Pad;
            var ad = Tema.Turkce.TextInfo.ToUpper(e.TrAd[..1]) + e.TrAd[1..];
            TextRenderer.DrawText(g, ad, Tema.Yazi(10.5f, true), new Rectangle(tx, a.Y + Tema.S(7), tw, Tema.S(20)), Tema.Metin,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            var kod = e.KisaKodlar.Length > 0 ? ":" + e.KisaKodlar[0] + ":" : e.EnAd;
            TextRenderer.DrawText(g, kod, Tema.Yazi(9), new Rectangle(tx, a.Y + Tema.S(26), tw, Tema.S(16)), Tema.MetinSoluk,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        else
        {
            TextRenderer.DrawText(g, "Bir emoji seç ya da yazarak ara", Tema.Yazi(9.5f), new Rectangle(a.X + Pad + Tema.S(2), a.Y, a.Width - Pad * 2, a.Height),
                Tema.MetinSoluk, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ---------------------------------------------------------------- Etkileşim

    private int IndeksBul(Point p)
    {
        var iz = IzgaraAlani;
        if (!iz.Contains(p) || p.X < IzgaraX || p.X >= IzgaraX + Sutun * Hucre) return -1;
        foreach (var s in _satirlar)
        {
            if (s is not EmojiSatiri es) continue;
            int y = SatirEkranY(es);
            if (p.Y < y) break;
            if (p.Y < y + es.H)
            {
                int c = (p.X - IzgaraX) / Hucre;
                return c < es.Adet ? es.Ilk + c : -1;
            }
        }
        return -1;
    }

    private int SekmeBul(Point p)
    {
        if (!SekmeAlani.Contains(p)) return -1;
        for (int i = 0; i < _sekmeler.Length; i++) if (SekmeDikdortgeni(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_surukleY is { } bas)
        {
            int izH = IzgaraAlani.Height, cubukH = KaydirmaCubugu().Height;
            int aralik = Math.Max(1, izH - cubukH);
            _kaydirma = Math.Clamp(_surukleKaydirma + (e.Y - bas) * MaksKaydirma / aralik, 0, MaksKaydirma);
            SekmeyiGuncelle();
            Invalidate();
            return;
        }
        int sek = SekmeBul(e.Location);
        if (sek != _sekmeUzerinde) { _sekmeUzerinde = sek; Cursor = sek >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        int idx = IndeksBul(e.Location);
        if (idx >= 0 && idx != _odak) { _odak = idx; Cursor = Cursors.Hand; Invalidate(); }
        else if (idx < 0 && sek < 0 && Cursor != Cursors.Default) Cursor = Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_sekmeUzerinde != -1) { _sekmeUzerinde = -1; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (MaksKaydirma > 0 && Rectangle.Inflate(KaydirmaCubugu(), Tema.S(4), 0).Contains(e.Location))
        {
            _surukleY = e.Y;
            _surukleKaydirma = _kaydirma;
            return;
        }
        int sek = SekmeBul(e.Location);
        if (sek >= 0)
        {
            _kaydirma = Math.Clamp(_bolumY[sek], 0, MaksKaydirma);
            SekmeyiGuncelle();
            Invalidate();
            return;
        }
        int idx = IndeksBul(e.Location);
        if (idx >= 0) Sec(idx);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_surukleY != null) { _surukleY = null; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Tekerlek(e.Delta); }

    protected override void Tekerlek(int delta)
    {
        _kaydirma = Math.Clamp(_kaydirma - delta * Tema.S(72) / 120, 0, MaksKaydirma);
        SekmeyiGuncelle();
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Right: Gez(+1, 0); return true;
            case Keys.Left: Gez(-1, 0); return true;
            case Keys.Down: Gez(0, +1); return true;
            case Keys.Up: Gez(0, -1); return true;
            case Keys.PageDown: Gez(0, +5); return true;
            case Keys.PageUp: Gez(0, -5); return true;
            case Keys.Enter or Keys.Enter | Keys.Shift:
                if (_odak >= 0) Sec(_odak);
                else if (_aramaModu && _liste.Count > 0) Sec(0);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Klavyeyle odağı taşı: yatay ±1, dikey satır sayısı.
    private void Gez(int yatay, int dikey)
    {
        if (_liste.Count == 0) return;
        if (_odak < 0) { _odak = 0; GorunureKaydir(); Invalidate(); return; }
        var emojiSatirlari = _satirlar.OfType<EmojiSatiri>().ToList();
        int satirNo = emojiSatirlari.FindIndex(s => _odak >= s.Ilk && _odak < s.Ilk + s.Adet);
        if (satirNo < 0) return;
        int sut = _odak - emojiSatirlari[satirNo].Ilk;
        if (yatay != 0) _odak = Math.Clamp(_odak + yatay, 0, _liste.Count - 1);
        if (dikey != 0)
        {
            int hedef = Math.Clamp(satirNo + dikey, 0, emojiSatirlari.Count - 1);
            var s = emojiSatirlari[hedef];
            _odak = s.Ilk + Math.Min(sut, s.Adet - 1);
        }
        GorunureKaydir();
        Invalidate();
    }

    private void GorunureKaydir()
    {
        var iz = IzgaraAlani;
        foreach (var s in _satirlar)
        {
            if (s is not EmojiSatiri es || _odak < es.Ilk || _odak >= es.Ilk + es.Adet) continue;
            // Bölüm başlığı da görünsün: satır bölümün ilk satırıysa başlığa kadar çık.
            int ust = es.Y, alt = es.Y + es.H;
            int idx = _satirlar.IndexOf(es);
            if (idx > 0 && _satirlar[idx - 1] is BaslikSatiri bs) ust = bs.Y;
            if (ust < _kaydirma) _kaydirma = ust;
            else if (alt > _kaydirma + iz.Height) _kaydirma = alt - iz.Height;
            _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
            SekmeyiGuncelle();
            return;
        }
    }

    private void Sec(int idx)
    {
        if (idx < 0 || idx >= _liste.Count) return;
        var emoji = _liste[idx].Emoji;
        SonEmojiler.Ekle(emoji);
        bool tut = !SecinceKapat || (ModifierKeys & Keys.Shift) != 0;
        Secildi?.Invoke(emoji);
        if (!tut) Kapat();
        else { Olustur(); Invalidate(); }
    }
}
