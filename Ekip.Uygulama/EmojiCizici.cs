using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Ekip.Uygulama;

// Renkli emoji çizimi. GDI/TextRenderer renkli emoji çizemediği için gömülü Twemoji resimleri kullanılır;
// kümede olmayan emoji Segoe UI Emoji glifiyle (tek renk) çizilir.
// Küçültülmüş resimler (emoji, boyut) çiftine göre önbelleğe alınır; her biri boyut*boyut*4 bayt (20 px için 1,6 KB).
public static class EmojiCizici
{
    // ---------------------------------------------------------------- Dizi bulma

    // s[i]'den başlayan emoji dizisinin UTF-16 uzunluğu (ZWJ birleşimleri, FE0F, ten rengi, tuş takımı, bayrak dahil).
    // Orada emoji yoksa 0. Düz yazı olarak kalan karakterler (FE0F'siz © gibi) emoji sayılmaz.
    public static int Uzunluk(string s, int i)
    {
        if (i < 0 || i >= s.Length) return 0;
        int cp = Kod(s, i, out int n);
        if (cp < 0x80 && !(cp is '#' or '*' or (>= '0' and <= '9'))) return 0; // hızlı çıkış: düz ASCII

        // Bayrak: iki bölgesel gösterge.
        if (cp is >= 0x1F1E6 and <= 0x1F1FF)
        {
            if (i + n < s.Length && Kod(s, i + n, out int n2) is >= 0x1F1E6 and <= 0x1F1FF) return n + n2;
            return 0;
        }

        // Tuş takımı: [0-9#*] FE0F? 20E3
        if (cp is '#' or '*' or (>= '0' and <= '9'))
        {
            int j = i + 1;
            if (j < s.Length && s[j] == '️') j++;
            return j < s.Length && s[j] == '⃣' ? j + 1 - i : 0;
        }

        if (!Temel(cp)) return 0;
        int son = i + n;
        bool fe0f = false, zwj = false;
        int ilkCp = cp;
        while (true)
        {
            if (son < s.Length && s[son] == '️') { fe0f = true; son++; }
            else if (son < s.Length && s[son] == '︎' && !zwj) return 0; // açıkça düz yazı istenmiş
            if (son < s.Length && Kod(s, son, out int nm) is >= 0x1F3FB and <= 0x1F3FF) son += nm;
            // Alt bölge bayrakları (siyah bayrak + etiket harfleri + iptal etiketi).
            if (ilkCp == 0x1F3F4 && !zwj)
                while (son < s.Length && Kod(s, son, out int nt) is >= 0xE0020 and <= 0xE007F) son += nt;
            if (son + 1 < s.Length && s[son] == '‍' && Kod(s, son + 1, out int nz) is int sonraki && Temel(sonraki))
            {
                zwj = true;
                son += 1 + nz;
                continue;
            }
            break;
        }

        // BMP'deki "metin görünümü varsayılan" karakterler FE0F ya da ZWJ olmadan emoji sayılmaz.
        if (ilkCp < 0x1F000 && !fe0f && !zwj && !EmojiGorunumlu(ilkCp)) return 0;
        return son - i;
    }

    // Dizedeki tüm emoji dizilerinin (başlangıç, uzunluk) listesi.
    public static List<(int Basla, int Uzunluk)> Bul(string metin)
    {
        var sonuc = new List<(int, int)>();
        for (int i = 0; i < metin.Length;)
        {
            int u = Uzunluk(metin, i);
            if (u > 0) { sonuc.Add((i, u)); i += u; }
            else i++;
        }
        return sonuc;
    }

    private static int Kod(string s, int i, out int uzunluk)
    {
        if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { uzunluk = 2; return char.ConvertToUtf32(s[i], s[i + 1]); }
        uzunluk = 1;
        return s[i];
    }

    private static bool Temel(int cp) => cp switch
    {
        0x1F004 or 0x1F0CF or 0x1F18E or 0x1F21A or 0x1F22F => true,
        >= 0x1F170 and <= 0x1F171 or 0x1F17E or 0x1F17F or >= 0x1F191 and <= 0x1F19A => true,
        >= 0x1F201 and <= 0x1F202 or >= 0x1F232 and <= 0x1F23A or >= 0x1F250 and <= 0x1F251 => true,
        >= 0x1F300 and <= 0x1FAFF => true,
        0xA9 or 0xAE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x24C2 or 0x3030 or 0x303D or 0x3297 or 0x3299 => true,
        >= 0x2194 and <= 0x2199 or 0x21A9 or 0x21AA or 0x231A or 0x231B or 0x2328 or 0x23CF => true,
        >= 0x23E9 and <= 0x23F3 or >= 0x23F8 and <= 0x23FA or 0x25AA or 0x25AB or 0x25B6 or 0x25C0 => true,
        >= 0x25FB and <= 0x25FE or >= 0x2600 and <= 0x27BF or 0x2934 or 0x2935 or >= 0x2B05 and <= 0x2B07 => true,
        0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 => true,
        _ => false,
    };

    // Unicode'da "Emoji_Presentation" olan BMP karakterleri (FE0F olmadan da emoji çıkar).
    private static bool EmojiGorunumlu(int cp) => cp switch
    {
        0x231A or 0x231B or >= 0x23E9 and <= 0x23EC or 0x23F0 or 0x23F3 or 0x25FD or 0x25FE => true,
        0x2614 or 0x2615 or >= 0x2648 and <= 0x2653 or 0x267F or 0x2693 or 0x26A1 or 0x26AA or 0x26AB => true,
        0x26BD or 0x26BE or 0x26C4 or 0x26C5 or 0x26CE or 0x26D4 or 0x26EA or 0x26F2 or 0x26F3 or 0x26F5 => true,
        0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 or 0x274C or 0x274E => true,
        >= 0x2753 and <= 0x2755 or 0x2757 or >= 0x2795 and <= 0x2797 or 0x27B0 or 0x27BF => true,
        0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 => true,
        _ => false,
    };

    // ---------------------------------------------------------------- Resim

    private static readonly object _kilit = new();
    private static readonly Dictionary<string, string?> _cozum = [];
    private static readonly Dictionary<(string, int), Bitmap> _resimler = [];
    private static readonly Dictionary<int, Font> _glifYazilari = [];

    // Emoji için Twemoji anahtarı. Tam eşleşme yoksa ten rengi atılır, o da yoksa ZWJ dizisinin ilk parçası denenir
    // (ör. kümede olmayan bir ten rengi/aile birleşimi yine tanınır halde görünür). Hiçbiri yoksa null.
    private static string? Coz(string emoji)
    {
        lock (_kilit)
        {
            if (_cozum.TryGetValue(emoji, out var k)) return k;
            k = EmojiVeri.Anahtar(emoji);
            if (!EmojiVeri.PngVar(k))
            {
                var parcalar = k.Split('-').Where(p => !(p.Length == 5 && string.CompareOrdinal(p, "1f3fb") >= 0 && string.CompareOrdinal(p, "1f3ff") <= 0)).ToArray();
                var ten = string.Join('-', parcalar);
                if (EmojiVeri.PngVar(ten)) k = ten;
                else
                {
                    int z = Array.IndexOf(parcalar, "200d");
                    var ilk = z > 0 ? string.Join('-', parcalar[..z]) : null;
                    k = ilk != null && EmojiVeri.PngVar(ilk) ? ilk : null;
                }
            }
            return _cozum[emoji] = k;
        }
    }

    // Kümede bu emoji için resim var mı (yoksa font glifine düşülür).
    public static bool Var(string emoji) => Coz(emoji) != null;

    // Emojinin boyut x boyut piksellik, yüksek kaliteyle küçültülmüş resmi (önbellekli). Yoksa null.
    // Dönen resmi Dispose ETME (önbellek paylaşıyor).
    public static Bitmap? Resim(string emoji, int boyut)
    {
        boyut = Math.Clamp(boyut, 8, 256);
        var anahtar = Coz(emoji);
        if (anahtar is null) return null;
        lock (_kilit)
        {
            if (_resimler.TryGetValue((anahtar, boyut), out var b)) return b;
        }
        using var kaynak = EmojiVeri.PngAc(anahtar);
        if (kaynak is null) return null;
        var hedef = new Bitmap(boyut, boyut, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(hedef))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(kaynak, new Rectangle(0, 0, boyut, boyut), 0, 0, kaynak.Width, kaynak.Height, GraphicsUnit.Pixel);
        }
        lock (_kilit)
        {
            // Çok şişerse bırak (Dispose edilmez: çizen olabilir, çöp toplayıcı temizler).
            if (_resimler.Count > 3000) _resimler.Clear();
            _resimler[(anahtar, boyut)] = hedef;
        }
        return hedef;
    }

    // Emojiyi (x, y) sol üstte olacak şekilde boyut x boyut kareye çizer.
    public static void Ciz(Graphics g, string emoji, int x, int y, int boyut)
    {
        var resim = Resim(emoji, boyut);
        if (resim != null)
        {
            var eskiI = g.InterpolationMode;
            var eskiP = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor; // resim zaten bu boyutta: yeniden örnekleme yok
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(resim, new Rectangle(x, y, boyut, boyut), 0, 0, resim.Width, resim.Height, GraphicsUnit.Pixel);
            g.InterpolationMode = eskiI;
            g.PixelOffsetMode = eskiP;
            return;
        }
        // Yedek: Segoe UI Emoji glifi (tek renk).
        Font f;
        lock (_kilit)
        {
            if (!_glifYazilari.TryGetValue(boyut, out f!))
                _glifYazilari[boyut] = f = new Font("Segoe UI Emoji", boyut * 0.72f, GraphicsUnit.Pixel);
        }
        TextRenderer.DrawText(g, emoji, f, new Rectangle(x, y, boyut + boyut / 2, boyut), Tema.Metin,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.NoClipping);
    }

    // Emojiyi verilen alanın ortasına, en uzun kenara sığacak kare olarak çizer.
    public static void Ciz(Graphics g, string emoji, Rectangle alan)
    {
        int b = Math.Min(alan.Width, alan.Height);
        Ciz(g, emoji, alan.X + (alan.Width - b) / 2, alan.Y + (alan.Height - b) / 2, b);
    }
}
