using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;

namespace Ekip.Uygulama;

// Sohbet mesajı metni için yerleşim + çizim motoru.
//
//   var d = ZenginMetin.Yerlestir(mesaj.Metin, Tema.Yazi(10.5f), genislik, secenekler);   // ölç (pahalı: sonucu önbellekle)
//   int h = d.Yukseklik;                                                                    // satırların toplam yüksekliği
//   d.Ciz(g, x, y, renk);                                                                   // çiz (ucuz; ekran dışı parçaları atlar)
//   var vurgu = d.Bul(new Point(fare.X - x, fare.Y - y));                                   // bağlantı/bahsetme tıklaması
//
// Desteklenenler: **kalın**, *italik*, ~~çizili~~, `kod`, http(s) bağlantıları, @kullaniciadi / @herkes bahsetmeleri,
// renkli emoji (EmojiCizici), \* ile kaçış. Satırlar genislik'i ASLA aşmaz (uzun kelimeler harf harf bölünür).
// Sadece 1-3 emojiden oluşan mesaj büyük ("jumbo") çizilir.
// Ölçüm ve çizim aynı TextRenderer bayraklarını kullanır, yani birbirleriyle birebir uyumludur.

public enum ZenginVurguTuru { Baglanti, Bahsetme }

// Bul()'un sonucu. Baglanti: Deger = adres. Bahsetme: Deger = kullanıcı adı ("@" olmadan, yazıldığı gibi).
public readonly record struct ZenginVurgu(ZenginVurguTuru Tur, string Deger);

public sealed class ZenginSecenekler
{
    // Bahsetme olarak tanınacak kullanıcı adları (küçük harf, "@" olmadan). null ise sadece @herkes tanınır.
    public ISet<string>? Kullanicilar { get; set; }

    // Ben kimim (küçük harf kullanıcı adı): bana yapılan bahsetme ve @herkes daha güçlü vurgulanır.
    public string? Ben { get; set; }

    // Sadece 1-3 emoji içeren mesajı büyük çiz.
    public bool BuyukEmoji { get; set; } = true;

    public Color BaglantiRengi { get; set; } = ColorTranslator.FromHtml("#9E8BFF");
    public Color BahsetmeYazi { get; set; } = ColorTranslator.FromHtml("#B9ABFF");
    public Color BahsetmeZemin { get; set; } = Color.FromArgb(64, 124, 92, 255);
}

public sealed class ZenginDuzen
{
    internal sealed class Parca
    {
        public int Tur;            // 0 yazı, 1 emoji, 2 bahsetme, 3 bağlantı, 4 boşluk (sadece kod arka planı için)
        public string Metin = "";
        public Font Font = null!;
        public int X, Y, W, H;     // Y: yazı kutusunun üstü
        public int Satir;
        public bool Kod, KodBas, KodSon;
        public int KodId;
        public bool Guclu;         // bahsetme: ben / @herkes
        public string? Deger;
    }

    internal readonly List<Parca> Parcalar = [];
    internal readonly List<(Rectangle Alan, ZenginVurgu Vurgu)> Vurgular = [];
    internal readonly List<Rectangle> KodArkalari = [];
    internal ZenginSecenekler Secenek = new();
    internal int SatirH, EmojiBoyutu, Genislik;

    public int Yukseklik { get; internal set; }
    // En uzun satırın genişliği (baloncuk gibi içeriğe sığan kutular için).
    public int KullanilanGenislik { get; internal set; }
    public bool BuyukEmoji { get; internal set; }
    // Biçimlendirme işaretleri atılmış düz metin (bildirim, kopyalama için).
    public string DuzMetin { get; internal set; } = "";

    // (x, y) yerleşimin sol üst köşesi. renk: normal yazı rengi. Ekran dışındaki parçalar çizilmez.
    public void Ciz(Graphics g, int x, int y, Color renk)
    {
        if (Parcalar.Count == 0) return;
        var klip = g.ClipBounds;
        int ust = (int)klip.Top - y, alt = (int)Math.Ceiling(klip.Bottom) - y;
        const TextFormatFlags Bicim = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping;
        var yaz = Secenek;

        // 1) Arka planlar: kod kutuları ve bahsetme hapları.
        var eski = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var r in KodArkalari)
        {
            if (r.Bottom < ust || r.Y > alt) continue;
            var kr = new RectangleF(x + r.X + 0.5f, y + r.Y + 0.5f, r.Width - 1, r.Height - 1);
            using var yol = Tema.YuvarlakDikdortgen(kr, Tema.S(4));
            using var d = new SolidBrush(ColorTranslator.FromHtml("#0E0F13"));
            using var k = new Pen(ColorTranslator.FromHtml("#2A2C35"));
            g.FillPath(d, yol);
            g.DrawPath(k, yol);
        }
        foreach (var p in Parcalar)
        {
            if (p.Tur != 2 || p.Y + p.H < ust || p.Y > alt) continue;
            var pr = HapAlani(p);
            Cizim.YuvarlakDoldur(g, new RectangleF(x + pr.X, y + pr.Y, pr.Width, pr.Height), Tema.S(4), p.Guclu ? Tema.Vurgu : yaz.BahsetmeZemin);
        }
        g.SmoothingMode = eski;

        // 2) Yazılar ve emojiler.
        foreach (var p in Parcalar)
        {
            if (p.Y + p.H < ust || p.Y > alt || p.Tur == 4) continue;
            switch (p.Tur)
            {
                case 1:
                    EmojiCizici.Ciz(g, p.Metin, x + p.X, y + p.Y, p.W);
                    break;
                case 2:
                {
                    var pr = HapAlani(p);
                    var kutu = new Rectangle(x + pr.X + Tema.S(3), y + p.Y, pr.Width - Tema.S(6), p.H);
                    TextRenderer.DrawText(g, p.Metin, p.Font, kutu, p.Guclu ? Color.White : yaz.BahsetmeYazi, Bicim | TextFormatFlags.EndEllipsis);
                    break;
                }
                case 3:
                    TextRenderer.DrawText(g, p.Metin, p.Font, new Point(x + p.X, y + p.Y), yaz.BaglantiRengi, Bicim);
                    break;
                default:
                    TextRenderer.DrawText(g, p.Metin, p.Font, new Point(x + p.X, y + p.Y), renk, Bicim);
                    break;
            }
        }
    }

    // Hapın çizildiği dikdörtgen (yerleşim koordinatında).
    private Rectangle HapAlani(Parca p) => new(p.X, p.Y - (SatirH - p.H) / 2 + Tema.S(1), p.W, SatirH - Tema.S(2));

    // p: yerleşimin sol üstüne göre nokta. Bağlantı/bahsetme üzerindeyse onu, değilse null verir.
    public ZenginVurgu? Bul(Point p)
    {
        foreach (var (alan, vurgu) in Vurgular)
            if (alan.Contains(p)) return vurgu;
        return null;
    }
}

public static class ZenginMetin
{
    private const TextFormatFlags OlcBicimi = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    // ---------------------------------------------------------------- Dışa açık

    public static ZenginDuzen Yerlestir(string? metin, Font font, int genislik, ZenginSecenekler? secenekler = null)
    {
        secenekler ??= new ZenginSecenekler();
        var d = new ZenginDuzen { Secenek = secenekler, Genislik = Math.Max(Tema.S(20), genislik) };
        var belirtecler = Ayristir(metin ?? "", secenekler);
        d.DuzMetin = string.Concat(belirtecler.Select(b => b.Tur == T.SatirSonu ? "\n" : b.Metin));
        if (belirtecler.Count == 0) return d;
        new Yerlestirici(d, font, belirtecler).Calistir();
        return d;
    }

    // ---------------------------------------------------------------- Ayrıştırma

    private enum T { Kelime, Bosluk, SatirSonu, Emoji, Bahsetme, Baglanti }

    private sealed class Belirtec
    {
        public T Tur;
        public string Metin = "";
        public bool Kalin, Italik, Cizili, Kod;
        public int KodId;
        public bool KodBas, KodSon;
        public string? Deger;
        public bool Guclu;
    }

    private static readonly Regex BaglantiRegex = new(@"https?://[^\s<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static List<Belirtec> Ayristir(string ham, ZenginSecenekler sec)
    {
        // Normalleştir: satır sonları, sekmeler, kontrol karakterleri.
        var sb = new StringBuilder(ham.Length);
        foreach (var c in ham.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            if (c == '\t') sb.Append(' ');
            else if (c == '\n' || c >= ' ' && c != '\u007f' && !(c >= '​' && c <= '‌') && c != '﻿') sb.Append(c);
        }
        var s = sb.ToString().TrimEnd();
        var sonuc = new List<Belirtec>();
        if (s.Length == 0) return sonuc;

        // Korunan aralıklar: kod ve bağlantılar (içlerinde biçim işareti aranmaz).
        var korunan = new List<(int Bas, int Son, bool Kod)>();
        for (int i = 0; i < s.Length;)
        {
            if (s[i] != '`') { i++; continue; }
            int n = 1;
            while (i + n < s.Length && s[i + n] == '`') n++;
            int j = i + n, bulunan = -1;
            while (j < s.Length)
            {
                int k = s.IndexOf('`', j);
                if (k < 0) break;
                int m = 1;
                while (k + m < s.Length && s[k + m] == '`') m++;
                if (m == n) { bulunan = k; break; }
                j = k + m;
            }
            if (bulunan > i + n) { korunan.Add((i, bulunan + n, true)); i = bulunan + n; }
            else i += n;
        }
        foreach (Match m in BaglantiRegex.Matches(s))
        {
            if (korunan.Any(k => m.Index < k.Son && m.Index + m.Length > k.Bas)) continue;
            int len = BaglantiUzunlugu(m.Value);
            if (len > 8) korunan.Add((m.Index, m.Index + len, false));
        }
        korunan.Sort((a, b) => a.Bas.CompareTo(b.Bas));

        bool kalin = false, italik = false, cizili = false;
        var duz = new StringBuilder();
        int kodSayac = 0;

        void Bosalt()
        {
            if (duz.Length == 0) return;
            DuzMetniBol(duz.ToString(), kalin, italik, cizili, sec, sonuc);
            duz.Clear();
        }

        // Kapanış işareti var mı: işaretten önce boşluk olmayan bir karakter olmalı.
        bool Kapanir(string isaret, int baslangic)
        {
            for (int k = baslangic; k < s.Length; k++)
            {
                k = s.IndexOf(isaret, k, StringComparison.Ordinal);
                if (k < 0) return false;
                if (k > baslangic && !char.IsWhiteSpace(s[k - 1]) && !korunan.Any(p => k >= p.Bas && k < p.Son)) return true;
            }
            return false;
        }

        for (int i = 0; i < s.Length;)
        {
            var kor = korunan.FirstOrDefault(p => p.Bas == i && p.Son > p.Bas);
            if (kor.Son > kor.Bas)
            {
                Bosalt();
                var parca = s[kor.Bas..kor.Son];
                if (kor.Kod)
                {
                    int n = 0;
                    while (n < parca.Length && parca[n] == '`') n++;
                    var ic = parca[n..^n].Trim('\n');
                    if (ic.StartsWith(' ') && ic.EndsWith(' ') && ic.Trim().Length > 0) ic = ic[1..^1];
                    KodBelirteclerine(ic, ++kodSayac, kalin, italik, cizili, sonuc);
                }
                else sonuc.Add(new Belirtec { Tur = T.Baglanti, Metin = parca, Deger = parca, Kalin = kalin, Italik = italik, Cizili = cizili });
                i = kor.Son;
                continue;
            }

            char c = s[i];
            if (c == '\\' && i + 1 < s.Length && char.IsPunctuation(s[i + 1]) || c == '\\' && i + 1 < s.Length && s[i + 1] is '*' or '~' or '`' or '\\')
            {
                duz.Append(s[i + 1]);
                i += 2;
                continue;
            }
            if (c == '*' && i + 1 < s.Length && s[i + 1] == '*')
            {
                if (kalin ? i > 0 && !char.IsWhiteSpace(s[i - 1]) : i + 2 < s.Length && !char.IsWhiteSpace(s[i + 2]) && Kapanir("**", i + 2))
                { Bosalt(); kalin = !kalin; i += 2; continue; }
            }
            else if (c == '~' && i + 1 < s.Length && s[i + 1] == '~')
            {
                if (cizili ? i > 0 && !char.IsWhiteSpace(s[i - 1]) : i + 2 < s.Length && !char.IsWhiteSpace(s[i + 2]) && Kapanir("~~", i + 2))
                { Bosalt(); cizili = !cizili; i += 2; continue; }
            }
            else if (c == '*')
            {
                if (italik ? i > 0 && !char.IsWhiteSpace(s[i - 1]) : i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1]) && Kapanir("*", i + 1))
                { Bosalt(); italik = !italik; i++; continue; }
            }
            duz.Append(c);
            i++;
        }
        Bosalt();
        return sonuc;
    }

    // Bağlantının sonundaki noktalama/işaretleri at ("bak https://x.com." → nokta bağlantıya dahil değil).
    private static int BaglantiUzunlugu(string url)
    {
        int len = url.Length;
        while (len > 0)
        {
            char c = url[len - 1];
            if (c is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"' or '*' or '~' or '_' or ']' or '}' or '>') len--;
            else if (c == ')' && url[..len].Count(ch => ch == '(') < url[..len].Count(ch => ch == ')')) len--;
            else break;
        }
        return len;
    }

    private static void KodBelirteclerine(string ic, int kodId, bool kalin, bool italik, bool cizili, List<Belirtec> sonuc)
    {
        int bas = sonuc.Count;
        foreach (var satirParcasi in ic.Split('\n').Select((p, n) => (p, n)))
        {
            if (satirParcasi.n > 0) sonuc.Add(new Belirtec { Tur = T.SatirSonu, Kod = true, KodId = kodId });
            int i = 0;
            var p = satirParcasi.p;
            while (i < p.Length)
            {
                int j = i;
                bool bosluk = p[i] == ' ';
                while (j < p.Length && (p[j] == ' ') == bosluk) j++;
                sonuc.Add(new Belirtec { Tur = bosluk ? T.Bosluk : T.Kelime, Metin = p[i..j], Kod = true, KodId = kodId });
                i = j;
            }
        }
        if (sonuc.Count > bas)
        {
            sonuc[bas].KodBas = true;
            sonuc[^1].KodSon = true;
        }
    }

    // Biçim içermeyen düz parçayı kelime / boşluk / satır sonu / emoji / bahsetme belirteçlerine böler.
    private static void DuzMetniBol(string p, bool kalin, bool italik, bool cizili, ZenginSecenekler sec, List<Belirtec> sonuc)
    {
        Belirtec Yeni(T tur, string metin) => new() { Tur = tur, Metin = metin, Kalin = kalin, Italik = italik, Cizili = cizili };
        int i = 0;
        var kelime = new StringBuilder();
        void KelimeBitir() { if (kelime.Length > 0) { sonuc.Add(Yeni(T.Kelime, kelime.ToString())); kelime.Clear(); } }

        while (i < p.Length)
        {
            char c = p[i];
            if (c == '\n') { KelimeBitir(); sonuc.Add(Yeni(T.SatirSonu, "\n")); i++; continue; }
            if (c == ' ')
            {
                KelimeBitir();
                int j = i;
                while (j < p.Length && p[j] == ' ') j++;
                sonuc.Add(Yeni(T.Bosluk, p[i..j]));
                i = j;
                continue;
            }
            int eu = EmojiCizici.Uzunluk(p, i);
            if (eu > 0)
            {
                KelimeBitir();
                sonuc.Add(Yeni(T.Emoji, p.Substring(i, eu)));
                i += eu;
                continue;
            }
            if (c == '@' && (i == 0 || !(char.IsLetterOrDigit(p[i - 1]) || p[i - 1] == '_')))
            {
                int j = i + 1;
                while (j < p.Length && j - i - 1 < 20 && (p[j] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '.')) j++;
                while (j > i + 1 && p[j - 1] == '.') j--;
                var ad = p[(i + 1)..j];
                var kucuk = ad.ToLowerInvariant();
                bool herkes = kucuk == "herkes";
                if (ad.Length > 0 && (herkes || sec.Kullanicilar?.Contains(kucuk) == true))
                {
                    KelimeBitir();
                    var b = Yeni(T.Bahsetme, "@" + ad);
                    b.Deger = ad;
                    b.Guclu = herkes || (sec.Ben != null && kucuk == sec.Ben.ToLowerInvariant());
                    sonuc.Add(b);
                    i = j;
                    continue;
                }
            }
            kelime.Append(c);
            i++;
        }
        KelimeBitir();
    }

    // ---------------------------------------------------------------- Yerleşim

    private static readonly Dictionary<(Font, string), int> _genislikler = [];
    private static readonly Dictionary<Font, (int H, int Asc)> _metrikler = [];
    private static readonly Dictionary<Font, int> _bosluklar = [];
    private static readonly Dictionary<(Font, int), Font> _yazilar = [];
    private static readonly object _kilit = new();

    private static int Ol(string metin, Font f)
    {
        lock (_kilit)
        {
            if (_genislikler.TryGetValue((f, metin), out var w)) return w;
            w = TextRenderer.MeasureText(metin, f, Size.Empty, OlcBicimi).Width;
            if (_genislikler.Count > 40000) _genislikler.Clear();
            return _genislikler[(f, metin)] = w;
        }
    }

    private static int BoslukGenisligi(Font f)
    {
        lock (_kilit)
        {
            if (!_bosluklar.TryGetValue(f, out var w))
                _bosluklar[f] = w = Math.Max(1, TextRenderer.MeasureText("a a", f, Size.Empty, OlcBicimi).Width - TextRenderer.MeasureText("aa", f, Size.Empty, OlcBicimi).Width);
            return w;
        }
    }

    // Yazı kutusu yüksekliği ve taban çizgisine uzaklık (farklı fontları aynı taban çizgisine hizalamak için).
    private static (int H, int Asc) Metrik(Font f)
    {
        lock (_kilit)
        {
            if (_metrikler.TryGetValue(f, out var m)) return m;
            int h = TextRenderer.MeasureText("Ay", f, Size.Empty, OlcBicimi).Height;
            int asc = h * 4 / 5;
            try
            {
                var ff = f.FontFamily;
                double a = ff.GetCellAscent(f.Style), d = ff.GetCellDescent(f.Style);
                asc = (int)Math.Round(h * a / (a + d));
            }
            catch { /* Aile stili desteklemiyorsa tahmini değer kalır. */ }
            return _metrikler[f] = (h, asc);
        }
    }

    private static Font Yazi(Font baz, Belirtec b)
    {
        bool kalin = b.Kalin, italik = b.Italik;
        if (b.Kod)
        {
            int kodAnahtar = 0x1000 | (b.Kalin ? 1 : 0) | (b.Italik ? 2 : 0) | (b.Cizili ? 4 : 0);
            lock (_kilit)
            {
                if (!_yazilar.TryGetValue((baz, kodAnahtar), out var kf))
                {
                    var stil = (b.Kalin ? FontStyle.Bold : 0) | (b.Italik ? FontStyle.Italic : 0) | (b.Cizili ? FontStyle.Strikeout : 0);
                    _yazilar[(baz, kodAnahtar)] = kf = new Font("Consolas", baz.SizeInPoints * 0.93f, stil);
                }
                return kf;
            }
        }

        var st = baz.Style & ~FontStyle.Bold;
        if (italik) st |= FontStyle.Italic;
        if (b.Cizili) st |= FontStyle.Strikeout;
        if (b.Tur == T.Baglanti) st |= FontStyle.Underline;
        if (b.Tur == T.Bahsetme) kalin = true;
        string aile = baz.FontFamily.Name;
        if (aile == Tema.YaziAilesi || aile == Tema.YaziAilesiKalin)
            return Tema.Yazi(baz.SizeInPoints, kalin || aile == Tema.YaziAilesiKalin || baz.Bold, st);

        int anahtar = (int)st | (kalin ? 0x100 : 0);
        lock (_kilit)
        {
            if (!_yazilar.TryGetValue((baz, anahtar), out var f))
                _yazilar[(baz, anahtar)] = f = new Font(baz, kalin ? st | FontStyle.Bold : st);
            return f;
        }
    }

    private sealed class Yerlestirici(ZenginDuzen d, Font baz, List<Belirtec> belirtecler)
    {
        private int x, satir, satirBas;
        private int satirH, kenarBos, emoji, tabanAsc;

        public void Calistir()
        {
            var (lh, asc) = Metrik(baz);
            kenarBos = Tema.S(1);
            satirH = lh + kenarBos * 2;
            emoji = Math.Max(Tema.S(14), lh);
            tabanAsc = asc;
            d.SatirH = satirH;
            d.EmojiBoyutu = emoji;

            // Sadece 1-3 emoji (arada boşluk olabilir) → büyük.
            var anlamli = belirtecler.Where(b => b.Tur != T.Bosluk).ToList();
            if (d.Secenek.BuyukEmoji && anlamli.Count is >= 1 and <= 3 && anlamli.All(b => b.Tur == T.Emoji))
            {
                Buyuk(anlamli);
                return;
            }

            foreach (var b in belirtecler) Yerles(b);
            if (x > 0 || satir == 0) { KodArkalariniTopla(); satir++; } // son (yarım) satırı da say
            d.Yukseklik = satir * satirH;
        }

        private void Buyuk(List<Belirtec> liste)
        {
            d.BuyukEmoji = true;
            int boyut = Math.Max(Tema.S(32), (int)Math.Round(satirH * 2.2));
            int ara = Tema.S(4), cx = 0, cy = 0, satirSay = 1, enGen = 0;
            foreach (var b in liste)
            {
                if (cx > 0 && cx + boyut > d.Genislik) { cx = 0; cy += boyut + ara; satirSay++; }
                int w = Math.Min(boyut, d.Genislik);
                d.Parcalar.Add(new ZenginDuzen.Parca { Tur = 1, Metin = b.Metin, X = cx, Y = cy, W = w, H = w, Satir = satirSay - 1 });
                cx += w + ara;
                enGen = Math.Max(enGen, cx - ara);
            }
            d.KullanilanGenislik = enGen;
            d.Yukseklik = cy + boyut + Tema.S(2);
        }

        private void Yerles(Belirtec b)
        {
            switch (b.Tur)
            {
                case T.SatirSonu:
                    SatirBitir();
                    break;
                case T.Bosluk:
                {
                    if (x == 0 && !b.Kod) break; // satır başındaki boşluk (sarmadan kalan) atılır
                    var f = Yazi(baz, b);
                    int w = BoslukGenisligi(f) * b.Metin.Length;
                    if (x + w > d.Genislik && !b.Kod) { SatirBitir(); break; }
                    w = Math.Min(w, Math.Max(0, d.Genislik - x));
                    Ekle(new ZenginDuzen.Parca { Tur = 4, Metin = b.Metin, Font = f, W = w, Kod = b.Kod, KodId = b.KodId }, f);
                    break;
                }
                case T.Emoji:
                {
                    if (x > 0 && x + emoji > d.Genislik) SatirBitir();
                    Ekle(new ZenginDuzen.Parca { Tur = 1, Metin = b.Metin, W = Math.Min(emoji, d.Genislik), H = emoji }, null);
                    break;
                }
                case T.Bahsetme:
                {
                    var f = Yazi(baz, b);
                    int pad = Tema.S(3), w = Ol(b.Metin, f) + pad * 2;
                    if (x > 0 && x + w > d.Genislik) SatirBitir();
                    w = Math.Min(w, d.Genislik);
                    var p = new ZenginDuzen.Parca { Tur = 2, Metin = b.Metin, Font = f, W = w, Guclu = b.Guclu, Deger = b.Deger };
                    Ekle(p, f);
                    d.Vurgular.Add((new Rectangle(p.X, p.Y - kenarBos, p.W, satirH), new ZenginVurgu(ZenginVurguTuru.Bahsetme, b.Deger!)));
                    break;
                }
                default:
                    Kelime(b);
                    break;
            }
        }

        private void Kelime(Belirtec b)
        {
            var f = Yazi(baz, b);
            int kodPad = b.Kod ? Tema.S(3) : 0;
            string kalan = b.Metin;
            bool ilk = true;
            while (kalan.Length > 0)
            {
                bool kodBas = ilk && b.KodBas;
                int sol = kodBas ? kodPad : 0, sag = b.KodSon ? kodPad : 0;
                int w = Ol(kalan, f);
                if (x + sol + w + sag <= d.Genislik) { Yerlestir(kalan, f, b, kodBas, b.KodSon); return; }

                // Sığmıyor: satırın ortasındaysak yeni satırda tekrar dene.
                if (x > 0) { SatirBitir(); continue; }

                // Satır başında bile sığmıyor: harf harf böl.
                int n = Sigan(kalan, f, d.Genislik - sol);
                if (n <= 0) n = char.IsHighSurrogate(kalan[0]) && kalan.Length > 1 ? 2 : 1;
                var parca = kalan[..n];
                kalan = kalan[n..];
                Yerlestir(parca, f, b, kodBas, false);
                ilk = false;
                if (kalan.Length > 0) SatirBitir();
            }
        }

        private void Yerlestir(string metin, Font f, Belirtec b, bool kodBas, bool kodSon)
        {
            int pad = b.Kod ? Tema.S(3) : 0;
            if (kodBas) x += pad;
            var p = new ZenginDuzen.Parca
            {
                Tur = b.Tur == T.Baglanti ? 3 : 0, Metin = metin, Font = f, W = Ol(metin, f),
                Kod = b.Kod, KodBas = kodBas, KodSon = kodSon, KodId = b.KodId,
            };
            Ekle(p, f);
            if (kodSon) x += pad;
            if (b.Tur == T.Baglanti)
                d.Vurgular.Add((new Rectangle(p.X, p.Y - kenarBos, p.W, satirH), new ZenginVurgu(ZenginVurguTuru.Baglanti, b.Deger!)));
        }

        // En çok kaç karakter genislige sığar (ikili arama; vekil çiftini bölmez).
        private static int Sigan(string metin, Font f, int genislik)
        {
            int lo = 0, hi = metin.Length;
            while (lo < hi)
            {
                int orta = (lo + hi + 1) / 2;
                if (Ol(metin[..orta], f) <= genislik) lo = orta; else hi = orta - 1;
            }
            if (lo > 0 && lo < metin.Length && char.IsHighSurrogate(metin[lo - 1])) lo--;
            return lo;
        }

        private void Ekle(ZenginDuzen.Parca p, Font? f)
        {
            p.X = x;
            p.Satir = satir;
            if (p.Tur == 1)
            {
                p.Y = satir * satirH + (satirH - p.H) / 2;
            }
            else
            {
                var (h, asc) = Metrik(f!);
                p.H = h;
                // Taban çizgisini ana yazıyla hizala.
                p.Y = satir * satirH + kenarBos + (tabanAsc - asc);
            }
            x += p.W;
            d.Parcalar.Add(p);
            d.KullanilanGenislik = Math.Max(d.KullanilanGenislik, x);
        }

        // Satırı bitirir (kod arka planlarını toplar) ve yeni satıra geçer.
        private void SatirBitir()
        {
            KodArkalariniTopla();
            satir++;
            satirBas = d.Parcalar.Count;
            x = 0;
        }

        // O satırdaki ardışık kod parçalarını tek arka plana birleştir.
        private void KodArkalariniTopla()
        {
            int pad = Tema.S(3);
            for (int i = satirBas; i < d.Parcalar.Count; i++)
            {
                var p = d.Parcalar[i];
                if (!p.Kod) continue;
                int j = i;
                while (j + 1 < d.Parcalar.Count && d.Parcalar[j + 1].Kod && d.Parcalar[j + 1].KodId == p.KodId) j++;
                var son = d.Parcalar[j];
                int sol = Math.Max(0, p.X - pad), sag = Math.Min(d.Genislik, son.X + son.W + pad);
                d.KodArkalari.Add(Rectangle.FromLTRB(sol, satir * satirH + Tema.S(1), sag, (satir + 1) * satirH - Tema.S(1)));
                i = j;
            }
        }
    }
}
