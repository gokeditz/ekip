namespace Ekip.Uygulama;

// Çok satırlı metni en fazla n satıra sığdırır, taşarsa son satırı "…" ile bitirir (GDI'nin çok satırlı EndEllipsis'i güvenilmez).
// Dönen metin "\n" ile satırlara bölünmüştür; NoPadding ile olduğu gibi çizilir. Sonuçlar önbelleğe alınır (hover'da tekrar ölçülmesin).
public static class MetinYardimci
{
    private static readonly Dictionary<(string, float, int, int), string> _onbellek = [];

    private static int Olc(string s, Font f) => TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.NoPadding).Width;

    private static string SonSatir(string s, Font f, int w)
    {
        s = s.Trim();
        if (Olc(s, f) <= w) return s;
        while (s.Length > 1 && Olc(s.TrimEnd() + "…", f) > w) s = s[..^1];
        return s.TrimEnd() + "…";
    }

    public static string Satirla(string? metin, Font f, int genislik, int maksSatir)
    {
        if (string.IsNullOrWhiteSpace(metin) || genislik <= 0 || maksSatir <= 0) return "";
        var anahtar = (metin, f.Size, genislik, maksSatir);
        if (_onbellek.TryGetValue(anahtar, out var kayitli)) return kayitli;

        var kelimeler = new Queue<string>(metin.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        var satirlar = new List<string>();
        string su = "";
        bool bitti = false;
        while (kelimeler.Count > 0)
        {
            var k = kelimeler.Peek();
            var deneme = su.Length == 0 ? k : su + " " + k;
            if (Olc(deneme, f) <= genislik) { su = deneme; kelimeler.Dequeue(); continue; }
            if (su.Length == 0)
            {
                // Tek kelime bile sığmıyor: harf harf kes, kalanı sonraki satıra bırak.
                int n = k.Length;
                while (n > 1 && Olc(k[..n], f) > genislik) n--;
                kelimeler.Dequeue();
                su = k[..n];
                if (n < k.Length)
                {
                    var yeni = new Queue<string>();
                    yeni.Enqueue(k[n..]);
                    foreach (var x in kelimeler) yeni.Enqueue(x);
                    kelimeler = yeni;
                }
            }
            if (satirlar.Count == maksSatir - 1)
            {
                var hepsi = su + (kelimeler.Count > 0 ? " " + string.Join(" ", kelimeler) : "");
                satirlar.Add(SonSatir(hepsi, f, genislik));
                su = "";
                bitti = true;
                break;
            }
            satirlar.Add(su);
            su = "";
        }
        if (!bitti && su.Length > 0) satirlar.Add(su);
        var sonuc = string.Join("\n", satirlar);
        if (_onbellek.Count > 400) _onbellek.Clear();
        _onbellek[anahtar] = sonuc;
        return sonuc;
    }
}

// Düğmeyi kendi zemin rengindeki bir kabın içine alır: Dugme köşelerini ebeveyninin rengiyle temizlediği için,
// farklı renkli kartların üstündeki düğmelerin köşeleri çirkin görünmesin diye.
public sealed class ZeminliDugme : Panel
{
    public Dugme Dugme { get; }

    public ZeminliDugme(Dugme dugme, Color zemin)
    {
        BackColor = zemin;
        Dugme = dugme;
        dugme.Dock = DockStyle.Fill;
        Controls.Add(dugme);
    }
}
