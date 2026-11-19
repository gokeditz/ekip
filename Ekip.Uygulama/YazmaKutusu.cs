using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Mesaj yazma alanı: çok satırlı kutu, sol "+" (dosya ekle), sağda GIF ve emoji düğmeleri; üstte yanıt/düzenleme çubuğu
// ve gönderilmeyi bekleyen ek kartları; ":em" emoji ve "@" üye önerileri.
// Kısayollar: Enter gönder, Shift+Enter satır, ↑ (boşken) son mesajı düzenle, Esc yanıt/düzenleme iptal,
// Ctrl+E emoji, Ctrl+G GIF, Ctrl+B/Ctrl+I kalın/italik, Ctrl+V panodan resim/dosya.
public sealed class YazmaKutusu : Control
{
    private sealed class YaziKutusu : TextBox
    {
        public bool TabYakala;
        protected override bool IsInputKey(Keys tus) => (tus == Keys.Tab && TabYakala) || base.IsInputKey(tus);
    }

    private sealed class Kart
    {
        public required GonderilecekDosya Dosya;
        public Bitmap? Kucuk;
        public Rectangle Alan, Kapat;
    }

    private readonly YaziKutusu _kutu = new()
    {
        BorderStyle = BorderStyle.None, BackColor = Tema.Giris, ForeColor = Tema.Metin, Font = Tema.Yazi(11),
        Multiline = true, AcceptsReturn = true, WordWrap = true, ScrollBars = ScrollBars.None, MaxLength = Kurallar.MesajMax,
    };
    private readonly List<Kart> _kartlar = [];
    private readonly OneriPaneli _oneri = new() { Visible = false };
    private (long Id, string Ad)? _yanit;
    private long? _duzenle;
    private string? _taslak;
    private string _uzerinde = "";
    private int _kutuH;
    private bool _yukseklikBekliyor, _programMetni;
    private int _oneriBas = -1;
    private string _oneriTuru = "";

    public Api? Api { get; set; }
    public AvatarOnbellek? Avatarlar { get => _oneri.Avatarlar; set => _oneri.Avatarlar = value; }
    public bool BasitMod { get; set; }
    public bool HerkesBahsedilebilir { get; set; }       // @herkes önerilsin (kanal/grup)
    public IReadOnlyList<Profil> Uyeler { get; set; } = [];
    public long BenId { get; set; }
    public string IpucuYazisi { get => _kutu.PlaceholderText; set => _kutu.PlaceholderText = value; }

    public event Action<string, List<GonderilecekDosya>>? Gonder;
    public event Action<long, string>? DuzenlemeKaydet;
    public event Action<GifSonucu>? GifSecildi;
    public event Action? SonMesajiDuzenle;
    public event Action? Yaziyor;
    public event Action? ModKapandi;
    public event Action? YukseklikDegisti;
    public event Action<string, bool>? Bildirim;
    public event Action<int>? TekerlekIstendi;     // kutu üstünde tekerlek: listeye ilet (delta)

    public long? YanitId => _yanit?.Id;
    public long? DuzenlenenId => _duzenle;
    public bool Duzenleniyor => _duzenle != null;
    public int KartSayisi => _kartlar.Count;

    public string Metin
    {
        get => _kutu.Text;
        set { _programMetni = true; _kutu.Text = value; _programMetni = false; YuksekligiHesapla(); OneriKapat(); }
    }

    public YazmaKutusu()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Tema.Arkaplan;
        AllowDrop = true;
        _kutuH = Tema.S(48);
        Controls.Add(_kutu);
        _kutu.AllowDrop = true;
        _kutu.GotFocus += (_, _) => Invalidate();
        _kutu.LostFocus += (_, _) => { Invalidate(); };
        _kutu.TextChanged += (_, _) =>
        {
            YuksekligiHesapla();
            if (_programMetni) return;
            if (_kutu.TextLength > 0 && _duzenle is null) Yaziyor?.Invoke();
            OneriyiGuncelle();
        };
        _kutu.KeyDown += TusBasildi;
        _kutu.KeyUp += (_, e) => { if (e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End) OneriyiGuncelle(); };
        _kutu.MouseUp += (_, _) => OneriyiGuncelle();
        _kutu.MouseWheel += (_, e) => { if (_kutuH < Tema.S(170)) TekerlekIstendi?.Invoke(e.Delta); };
        _kutu.DragEnter += SurukleGirdi;
        _kutu.DragDrop += SurukleBirakildi;
        DragEnter += SurukleGirdi;
        DragDrop += SurukleBirakildi;
        _oneri.Secildi += OneriKabul;
        Cursor = Cursors.Default;
    }

    // ---------------------------------------------------------------- Ölçüler

    private int BarH => _yanit != null || _duzenle != null ? Tema.S(36) : 0;
    private int KartW => Tema.S(200);
    private int KartH => Tema.S(56);
    private int KartSutun => Math.Max(1, (Width - Tema.S(24) + Tema.S(8)) / (KartW + Tema.S(8)));
    private int KartAlaniH => _kartlar.Count == 0 ? 0 : ((_kartlar.Count + KartSutun - 1) / KartSutun) * (KartH + Tema.S(8)) + Tema.S(8);
    private int UstH => BarH + KartAlaniH;
    private Rectangle KutuAlani => new(0, UstH, Width, _kutuH);
    private int DugmeY => KutuAlani.Bottom - Tema.S(48) + Tema.S(7);
    private bool ArtiVar => !BasitMod && _duzenle is null;
    private Rectangle ArtiAlani => new(Tema.S(8), DugmeY, Tema.S(34), Tema.S(34));
    private Rectangle EmojiAlani => new(Width - Tema.S(8) - Tema.S(34), DugmeY, Tema.S(34), Tema.S(34));
    private bool GifVar => !BasitMod && _duzenle is null;
    private Rectangle GifAlani => new(EmojiAlani.Left - Tema.S(2) - Tema.S(40), DugmeY, Tema.S(40), Tema.S(34));
    private Rectangle YanitKapatAlani => new(Width - Tema.S(36), Tema.S(6), Tema.S(26), Tema.S(24));
    private int SolBosluk => ArtiVar ? Tema.S(48) : Tema.S(16);
    private int SagBosluk => (GifVar ? Tema.S(40) + Tema.S(2) : 0) + Tema.S(34) + Tema.S(12);

    public int GerekenYukseklik => UstH + _kutuH;

    private int KutuYuksekligiOlc()
    {
        var olcu = TextRenderer.MeasureText(_kutu.Text.Length == 0 ? "A" : _kutu.Text + "A", _kutu.Font,
            new Size(Math.Max(10, _kutu.Width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
        return Math.Clamp(olcu.Height + Tema.S(26), Tema.S(48), Tema.S(170));
    }

    private void YuksekligiHesapla()
    {
        int h = KutuYuksekligiOlc();
        if (h == _kutuH) return;
        _kutuH = h;
        Yerlestir();
        Invalidate();
        YukseklikIste();
    }

    private void YukseklikIste()
    {
        if (_yukseklikBekliyor || !IsHandleCreated) { if (!IsHandleCreated) YukseklikDegisti?.Invoke(); return; }
        _yukseklikBekliyor = true;
        BeginInvoke(() => { _yukseklikBekliyor = false; if (!IsDisposed) YukseklikDegisti?.Invoke(); });
    }

    private void Yerlestir()
    {
        if (_kutu is null) return;
        int x = SolBosluk, w = Math.Max(Tema.S(40), Width - x - SagBosluk);
        var k = KutuAlani;
        _kutu.SetBounds(x, k.Y + Tema.S(13), w, Math.Max(Tema.S(20), k.Height - Tema.S(24)));
        KartlariYerlestir();
        if (_oneri.Visible) OneriyiKonumla();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Yerlestir();
        int h = KutuYuksekligiOlc();
        if (h != _kutuH) { _kutuH = h; Yerlestir(); YukseklikIste(); }
    }

    protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); if (_oneri.Visible) OneriyiKonumla(); }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (Parent is null) _oneri.Parent?.Controls.Remove(_oneri);
    }

    public void Odaklan() => _kutu.Focus();
    public bool OdakVar => _kutu.Focused;

    // ---------------------------------------------------------------- Modlar

    public void YanitModu(Mesaj m, string ad)
    {
        DuzenlemeyiBirak();
        _yanit = (m.Id, ad);
        ModDegisti();
    }

    public void DuzenlemeModu(Mesaj m)
    {
        if (_duzenle is null) _taslak = _kutu.Text;
        _yanit = null;
        _duzenle = m.Id;
        TemizleKartlar();
        Metin = m.Metin;
        _kutu.SelectionStart = _kutu.TextLength;
        ModDegisti();
    }

    private void DuzenlemeyiBirak()
    {
        if (_duzenle is null) return;
        _duzenle = null;
        Metin = _taslak ?? "";
        _taslak = null;
    }

    // Yanıt/düzenleme modundan çık (gönderince, sohbet değişince).
    public void ModuKapat(bool olayVer = false)
    {
        bool vardi = _yanit != null || _duzenle != null;
        _yanit = null;
        DuzenlemeyiBirak();
        if (vardi) ModDegisti();
        if (vardi && olayVer) ModKapandi?.Invoke();
    }

    private void ModDegisti()
    {
        Yerlestir();
        Invalidate();
        YukseklikIste();
        _kutu.Focus();
    }

    public void Temizle()
    {
        Metin = "";
        TemizleKartlar();
        ModuKapat();
    }

    private void TemizleKartlar()
    {
        foreach (var c in _kartlar) c.Kucuk?.Dispose();
        _kartlar.Clear();
        Yerlestir();
        Invalidate();
        YukseklikIste();
    }

    // ---------------------------------------------------------------- Klavye

    private void TusBasildi(object? s, KeyEventArgs e)
    {
        if (_oneri.Visible)
        {
            if (e.KeyCode == Keys.Down) { _oneri.Gezin(1); Bitir(e); return; }
            if (e.KeyCode == Keys.Up) { _oneri.Gezin(-1); Bitir(e); return; }
            if (e.KeyCode is Keys.Enter or Keys.Tab && !e.Shift) { _oneri.Kabul(); Bitir(e); return; }
            if (e.KeyCode == Keys.Escape) { OneriKapat(); Bitir(e); return; }
        }

        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            Bitir(e);
            Gonderilsin();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_yanit != null || _duzenle != null) { ModuKapat(olayVer: true); Bitir(e); }
        }
        else if (e.KeyCode == Keys.Up && !e.Shift && !e.Control && _kutu.TextLength == 0 && _duzenle is null && !BasitMod)
        {
            Bitir(e);
            SonMesajiDuzenle?.Invoke();
        }
        else if (e.Control && e.KeyCode == Keys.E) { Bitir(e); EmojiAc(); }
        else if (e.Control && e.KeyCode == Keys.G && GifVar) { Bitir(e); GifAc(); }
        else if (e.Control && e.KeyCode == Keys.B) { Bitir(e); Sar("**"); }
        else if (e.Control && e.KeyCode == Keys.I) { Bitir(e); Sar("*"); }
        else if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
        {
            if (PanodanEkle()) Bitir(e);
        }
    }


    private static void Bitir(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }

    // Seçili metni işaretle sarar; seçim yoksa işaretleri koyup imleci ortaya alır.
    private void Sar(string isaret)
    {
        int bas = _kutu.SelectionStart, uz = _kutu.SelectionLength;
        if (uz > 0)
        {
            var sec = _kutu.SelectedText;
            _kutu.SelectedText = isaret + sec + isaret;
            _kutu.Select(bas + isaret.Length, sec.Length);
        }
        else
        {
            _kutu.SelectedText = isaret + isaret;
            _kutu.SelectionStart = bas + isaret.Length;
        }
    }

    private void Gonderilsin()
    {
        var metin = _kutu.Text.Trim();
        if (_duzenle is { } id)
        {
            if (metin.Length == 0) return;
            DuzenlemeKaydet?.Invoke(id, metin);
            return;
        }
        if (metin.Length == 0 && _kartlar.Count == 0) return;
        var dosyalar = _kartlar.Select(k => k.Dosya).ToList();
        OneriKapat();
        // Küçük resimleri bırak; gönderim sonrası kutu temiz.
        foreach (var k in _kartlar) k.Kucuk?.Dispose();
        _kartlar.Clear();
        _programMetni = true; _kutu.Clear(); _programMetni = false;
        YuksekligiHesapla();
        Yerlestir();
        Invalidate();
        YukseklikIste();
        Gonder?.Invoke(metin, dosyalar);
    }

    // ---------------------------------------------------------------- Emoji / GIF

    private void EmojiAc()
    {
        if (AcilirPanel.YeniKapandi || FindForm() is null) return;
        var s = new EmojiSecici();
        s.Secildi += emoji =>
        {
            _kutu.SelectedText = emoji;
            _kutu.Focus();
        };
        var a = EmojiAlani;
        s.Goster(this, PointToScreen(new Point(a.Right, a.Top)));
    }

    private void GifAc()
    {
        if (AcilirPanel.YeniKapandi || Api is null || FindForm() is null) return;
        var s = new GifSecici(Api);
        s.Secildi += g => GifSecildi?.Invoke(g);
        var a = GifAlani;
        s.Goster(this, PointToScreen(new Point(EmojiAlani.Right, a.Top)));
    }

    // ---------------------------------------------------------------- Dosyalar

    private static readonly Dictionary<string, string> Mimeler = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".bmp"] = "image/bmp", [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".mov"] = "video/quicktime", [".mkv"] = "video/x-matroska",
        [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg", [".m4a"] = "audio/mp4", [".flac"] = "audio/flac",
        [".pdf"] = "application/pdf", [".zip"] = "application/zip", [".txt"] = "text/plain", [".json"] = "application/json",
    };

    public void DosyalariEkle(IEnumerable<string> yollar, bool gecici = false)
    {
        if (BasitMod || _duzenle != null) return;
        foreach (var yol in yollar)
        {
            if (Directory.Exists(yol)) { Bildirim?.Invoke("Klasörler gönderilemez, dosya seç.", true); continue; }
            FileInfo bilgi;
            try { bilgi = new FileInfo(yol); if (!bilgi.Exists) continue; }
            catch { continue; }
            if (_kartlar.Count >= Kurallar.MesajEkMax) { Bildirim?.Invoke($"Bir mesaja en fazla {Kurallar.MesajEkMax} dosya eklenebilir.", true); break; }
            if (bilgi.Length > Kurallar.DosyaMaxBayt) { Bildirim?.Invoke($"{bilgi.Name} çok büyük (en fazla {Kurallar.DosyaMaxBayt / 1024 / 1024} MB).", true); continue; }
            if (bilgi.Length == 0) { Bildirim?.Invoke($"{bilgi.Name} boş bir dosya.", true); continue; }
            if (_kartlar.Any(k => string.Equals(k.Dosya.Yol, bilgi.FullName, StringComparison.OrdinalIgnoreCase))) continue;

            string mime = Mimeler.GetValueOrDefault(bilgi.Extension, "application/octet-stream");
            int gw = 0, gh = 0;
            if (mime.StartsWith("image/"))
            {
                try
                {
                    using var fs = File.OpenRead(bilgi.FullName);
                    using var img = Image.FromStream(fs, false, false);
                    (gw, gh) = (img.Width, img.Height);
                }
                catch { /* Resim gibi görünen ama açılamayan dosya: dosya olarak gider. */ mime = "application/octet-stream"; }
            }
            var kart = new Kart
            {
                Dosya = new GonderilecekDosya { Yol = bilgi.FullName, Ad = bilgi.Name, Mime = mime, Boyut = bilgi.Length, Genislik = gw, Yukseklik = gh, Gecici = gecici },
            };
            _kartlar.Add(kart);
            if (mime.StartsWith("image/")) _ = KucukResimYap(kart);
        }
        Yerlestir();
        Invalidate();
        YukseklikIste();
        _kutu.Focus();
    }

    private async Task KucukResimYap(Kart kart)
    {
        int b = Tema.S(40);
        var kucuk = await Task.Run(() =>
        {
            try
            {
                using var fs = File.OpenRead(kart.Dosya.Yol);
                using var img = Image.FromStream(fs, false, false);
                int kenar = Math.Min(img.Width, img.Height);
                var kaynak = new Rectangle((img.Width - kenar) / 2, (img.Height - kenar) / 2, kenar, kenar);
                var bmp = new Bitmap(b, b);
                using var g = Graphics.FromImage(bmp);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(img, new Rectangle(0, 0, b, b), kaynak, GraphicsUnit.Pixel);
                return bmp;
            }
            catch { return null; }
        });
        if (IsDisposed || !_kartlar.Contains(kart)) { kucuk?.Dispose(); return; }
        kart.Kucuk = kucuk;
        Invalidate();
    }

    private void DosyaSec()
    {
        using var d = new OpenFileDialog { Multiselect = true, Title = "Gönderilecek dosyaları seç", CheckFileExists = true };
        if (d.ShowDialog(FindForm()) == DialogResult.OK) DosyalariEkle(d.FileNames);
    }

    // Ctrl+V: panoda dosya ya da (yazı olmayan) resim varsa ek yapar. İşlendiyse true.
    private bool PanodanEkle()
    {
        if (BasitMod || _duzenle != null) return false;
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var dosyalar = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (dosyalar.Count > 0) { DosyalariEkle(dosyalar); return true; }
            }
            if (Clipboard.ContainsImage() && !Clipboard.ContainsText())
            {
                using var img = Clipboard.GetImage();
                if (img is null) return false;
                var klasor = Path.Combine(Path.GetTempPath(), "ekip-yapistirilan");
                Directory.CreateDirectory(klasor);
                var yol = Path.Combine(klasor, $"resim-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                img.Save(yol, System.Drawing.Imaging.ImageFormat.Png);
                DosyalariEkle([yol], gecici: true);
                return true;
            }
        }
        catch { /* Pano o an başka programda: normal yapıştırmaya bırak. */ }
        return false;
    }

    private void SurukleGirdi(object? s, DragEventArgs e)
    {
        e.Effect = !BasitMod && _duzenle is null && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void SurukleBirakildi(object? s, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] dosyalar) DosyalariEkle(dosyalar);
    }

    private void KartiKaldir(int i)
    {
        if (i < 0 || i >= _kartlar.Count) return;
        var k = _kartlar[i];
        _kartlar.RemoveAt(i);
        k.Kucuk?.Dispose();
        if (k.Dosya.Gecici) try { File.Delete(k.Dosya.Yol); } catch { }
        _uzerinde = "";
        Yerlestir();
        Invalidate();
        YukseklikIste();
    }

    private void KartlariYerlestir()
    {
        int sutun = KartSutun, x0 = Tema.S(12), y0 = BarH + Tema.S(8);
        for (int i = 0; i < _kartlar.Count; i++)
        {
            var k = _kartlar[i];
            k.Alan = new Rectangle(x0 + (i % sutun) * (KartW + Tema.S(8)), y0 + (i / sutun) * (KartH + Tema.S(8)), KartW, KartH);
            k.Kapat = new Rectangle(k.Alan.Right - Tema.S(26), k.Alan.Y + Tema.S(6), Tema.S(20), Tema.S(20));
        }
    }

    // ---------------------------------------------------------------- Öneriler (":em" emoji, "@" üye)

    private void OneriKapat() { _oneri.Visible = false; _oneriBas = -1; _kutu.TabYakala = false; }

    private static readonly Regex EmojiKalibi = new(@"(?:^|[\s(])(:[\p{L}\p{N}_+\-]{2,})$", RegexOptions.Compiled);
    private static readonly Regex BahsetmeKalibi = new(@"(?:^|\s)(@[\p{L}\p{N}_.]*)$", RegexOptions.Compiled);

    private void OneriyiGuncelle()
    {
        int imlec = _kutu.SelectionStart;
        if (_kutu.SelectionLength > 0 || imlec > _kutu.TextLength) { OneriKapat(); return; }
        var once = _kutu.Text[..imlec];

        var em = EmojiKalibi.Match(once);
        if (em.Success)
        {
            var onek = em.Groups[1].Value[1..];
            var liste = new List<OneriPaneli.Oge>();
            var gorulen = new HashSet<string>();
            foreach (var (e, kod) in EmojiVeri.KisaKodOner(onek, 8))
                if (gorulen.Add(e.Anahtar)) liste.Add(new OneriPaneli.Oge(e.Emoji, $":{kod}:", e.TrAd, null));
            if (liste.Count < 8)
                foreach (var e in EmojiVeri.Ara(onek, 12))
                {
                    if (liste.Count >= 8) break;
                    if (gorulen.Add(e.Anahtar)) liste.Add(new OneriPaneli.Oge(e.Emoji, e.KisaKodlar.Length > 0 ? $":{e.KisaKodlar[0]}:" : e.TrAd, e.TrAd, null));
                }
            if (liste.Count > 0) { OneriGoster("emoji", em.Groups[1].Index, liste); return; }
        }
        else if (!BasitMod)
        {
            var bm = BahsetmeKalibi.Match(once);
            if (bm.Success)
            {
                var onek = bm.Groups[1].Value[1..].ToLowerInvariant();
                var liste = new List<OneriPaneli.Oge>();
                if (HerkesBahsedilebilir && "herkes".StartsWith(onek)) liste.Add(new OneriPaneli.Oge("@herkes ", "herkes", "Herkese bildirim gönder", null));
                var adaylar = Uyeler.Where(p => p.Id != BenId && p.KullaniciAdi.Length > 0)
                    .Select(p => (P: p, Puan: p.KullaniciAdi.StartsWith(onek, StringComparison.OrdinalIgnoreCase) ? 0 : p.GorunenAd.StartsWith(onek, StringComparison.OrdinalIgnoreCase) ? 1
                        : p.KullaniciAdi.Contains(onek, StringComparison.OrdinalIgnoreCase) || p.GorunenAd.Contains(onek, StringComparison.OrdinalIgnoreCase) ? 2 : 9))
                    .Where(x => x.Puan < 9).OrderBy(x => x.Puan).ThenBy(x => x.P.GorunenAd, StringComparer.Create(Tema.Turkce, true)).Take(8 - liste.Count);
                foreach (var (p, _) in adaylar) liste.Add(new OneriPaneli.Oge("@" + p.KullaniciAdi + " ", p.GorunenAd, "@" + p.KullaniciAdi, p));
                if (liste.Count > 0) { OneriGoster("uye", bm.Groups[1].Index, liste); return; }
            }
        }
        OneriKapat();
    }

    private void OneriGoster(string tur, int bas, List<OneriPaneli.Oge> liste)
    {
        _oneriBas = bas;
        _oneriTuru = tur;
        if (Parent != null && _oneri.Parent != Parent) { _oneri.Parent?.Controls.Remove(_oneri); Parent.Controls.Add(_oneri); }
        _oneri.Ayarla(liste, tur == "emoji");
        _oneri.Visible = true;
        _oneri.BringToFront();
        _kutu.TabYakala = true;
        OneriyiKonumla();
    }

    private void OneriyiKonumla()
    {
        if (Parent is null) return;
        int w = Math.Min(Tema.S(440), Math.Max(Tema.S(220), Width));
        int h = _oneri.GerekenYukseklik;
        _oneri.SetBounds(Left, Math.Max(Tema.S(4), Top - h - Tema.S(6)), w, h);
    }

    private void OneriKabul(string metin)
    {
        if (_oneriBas < 0) return;
        int imlec = _kutu.SelectionStart;
        _kutu.Select(_oneriBas, Math.Max(0, imlec - _oneriBas));
        _kutu.SelectedText = metin;
        OneriKapat();
        _kutu.Focus();
    }

    // ---------------------------------------------------------------- Fare

    private string Bul(Point p)
    {
        if (BarH > 0 && YanitKapatAlani.Contains(p)) return "bar";
        for (int i = 0; i < _kartlar.Count; i++)
            if (_kartlar[i].Kapat.Contains(p)) return "kart" + i;
        if (ArtiVar && ArtiAlani.Contains(p)) return "arti";
        if (GifVar && GifAlani.Contains(p)) return "gif";
        if (EmojiAlani.Contains(p)) return "emoji";
        return "";
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var b = Bul(e.Location);
        if (b == _uzerinde) return;
        _uzerinde = b;
        Cursor = b.Length > 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_uzerinde.Length > 0) { _uzerinde = ""; Cursor = Cursors.Default; Invalidate(); } }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) { _kutu.Focus(); return; }
        var b = Bul(e.Location);
        if (b == "bar") ModuKapat(olayVer: true);
        else if (b.StartsWith("kart")) KartiKaldir(int.Parse(b[4..]));
        else if (b == "arti") DosyaSec();
        else if (b == "gif") GifAc();
        else if (b == "emoji") EmojiAc();
        else _kutu.Focus();
    }

    // ---------------------------------------------------------------- Çizim

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int ust = UstH;

        // Üst bölüm (yanıt çubuğu + ek kartları): kutunun arkasından başlayan yuvarlak panel.
        if (ust > 0)
        {
            using var yol = Tema.YuvarlakDikdortgen(new RectangleF(.5f, .5f, Width - 1.5f, ust + Tema.S(16)), Tema.S(10));
            using var b = new SolidBrush(Tema.Yuzey);
            using var p = new Pen(Tema.Kenar);
            g.FillPath(b, yol);
            g.DrawPath(p, yol);
        }

        // Kutu
        var k = KutuAlani;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(.5f, k.Y + .5f, Width - 1.5f, k.Height - 1.5f), Tema.S(8)))
        {
            using var b = new SolidBrush(Tema.Giris);
            using var p = new Pen(_kutu.Focused ? Tema.Vurgu : Tema.Kenar, _kutu.Focused ? Tema.S(2) : 1);
            g.FillPath(b, yol);
            g.DrawPath(p, yol);
        }
        g.SmoothingMode = SmoothingMode.None;

        // Çubuk
        if (BarH > 0)
        {
            string metin = _duzenle != null ? "Mesajı düzenliyorsun — Esc iptal, Enter kaydet" : "";
            if (_duzenle != null)
                TextRenderer.DrawText(g, metin, Tema.Yazi(9.5f), new Rectangle(Tema.S(14), 0, Width - Tema.S(60), BarH), Tema.MetinIkincil,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            else
            {
                var ad = _yanit!.Value.Ad;
                var f1 = Tema.Yazi(9.5f);
                var f2 = Tema.Yazi(9.5f, kalin: true);
                int w2 = Math.Min(TextRenderer.MeasureText(ad, f2, Size.Empty, TextFormatFlags.NoPadding).Width, Width - Tema.S(220));
                TextRenderer.DrawText(g, ad, f2, new Rectangle(Tema.S(14), 0, w2 + 2, BarH), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, " kişisine yanıt veriyorsun", f1, new Rectangle(Tema.S(14) + w2, 0, Width - w2 - Tema.S(60), BarH), Tema.MetinIkincil,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            var kr = YanitKapatAlani;
            bool u = _uzerinde == "bar";
            if (u) Cizim.YuvarlakDoldur(g, kr, Tema.S(4), Tema.Kenar);
            TextRenderer.DrawText(g, Glif.Kapat, Cizim.Ikon(8.5f), kr, u ? Tema.Metin : Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Ek kartları
        for (int i = 0; i < _kartlar.Count; i++) KartCiz(g, _kartlar[i], _uzerinde == "kart" + i);

        // Düğmeler
        if (ArtiVar) DugmeCiz(g, ArtiAlani, "arti");
        if (GifVar) DugmeCiz(g, GifAlani, "gif");
        DugmeCiz(g, EmojiAlani, "emoji");
    }

    private void KartCiz(Graphics g, Kart c, bool kapatUzerinde)
    {
        var r = c.Alan;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), Tema.S(8)))
        {
            using var b = new SolidBrush(Tema.YuzeyUst);
            using var p = new Pen(Tema.Kenar);
            g.FillPath(b, yol);
            g.DrawPath(p, yol);
        }
        int ib = Tema.S(40);
        var ikon = new Rectangle(r.X + Tema.S(8), r.Y + (r.Height - ib) / 2, ib, ib);
        if (c.Kucuk != null)
        {
            using var yol = Tema.YuvarlakDikdortgen(ikon, Tema.S(6));
            using var f = new TextureBrush(c.Kucuk, WrapMode.Clamp);
            f.TranslateTransform(ikon.X, ikon.Y);
            f.ScaleTransform(ikon.Width / (float)c.Kucuk.Width, ikon.Height / (float)c.Kucuk.Height);
            g.FillPath(f, yol);
        }
        else
        {
            Cizim.YuvarlakDoldur(g, ikon, Tema.S(6), Tema.VurguKoyu);
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, c.Dosya.Resim ? "" : "", Cizim.Ikon(13), ikon, Tema.VurguHover, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        g.SmoothingMode = SmoothingMode.None;
        int tx = ikon.Right + Tema.S(10), tw = r.Right - tx - Tema.S(30);
        TextRenderer.DrawText(g, c.Dosya.Ad, Tema.Yazi(9, kalin: true), new Rectangle(tx, r.Y + Tema.S(11), tw, Tema.S(18)), Tema.Metin,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, ResimGoruntuleyici.BoyutYazisi(c.Dosya.Boyut), Tema.Yazi(8.5f), new Rectangle(tx, r.Y + Tema.S(30), tw, Tema.S(16)), Tema.MetinSoluk,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var kr = c.Kapat;
        Cizim.YuvarlakDoldur(g, kr, kr.Height / 2f, kapatUzerinde ? Tema.Hata : Tema.Kenar);
        TextRenderer.DrawText(g, Glif.Kapat, Cizim.Ikon(7f), kr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DugmeCiz(Graphics g, Rectangle r, string ad)
    {
        bool u = _uzerinde == ad;
        var renk = u ? Tema.Metin : Tema.MetinIkincil;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (u) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
        switch (ad)
        {
            case "arti":
            {
                // Yuvarlak içinde artı
                var c = new RectangleF(r.X + (r.Width - Tema.S(22)) / 2f, r.Y + (r.Height - Tema.S(22)) / 2f, Tema.S(22), Tema.S(22));
                using var b = new SolidBrush(u ? Tema.MetinIkincil : Tema.Kenar);
                g.FillEllipse(b, c);
                using var p = new Pen(u ? Tema.Giris : Tema.MetinIkincil, Math.Max(1.6f, Tema.S(2) * 0.9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                float cx = c.X + c.Width / 2, cy = c.Y + c.Height / 2, a = Tema.S(5);
                g.DrawLine(p, cx - a, cy, cx + a, cy);
                g.DrawLine(p, cx, cy - a, cx, cy + a);
                break;
            }
            case "gif":
            {
                var rozet = new Rectangle(r.X + Tema.S(5), r.Y + (r.Height - Tema.S(20)) / 2, r.Width - Tema.S(10), Tema.S(20));
                using var yol = Tema.YuvarlakDikdortgen(new RectangleF(rozet.X + .5f, rozet.Y + .5f, rozet.Width - 1, rozet.Height - 1), Tema.S(5));
                using var p = new Pen(renk, 1.4f);
                g.DrawPath(p, yol);
                g.SmoothingMode = SmoothingMode.None;
                TextRenderer.DrawText(g, "GIF", Tema.Yazi(8, kalin: true), rozet, renk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                break;
            }
            default:
            {
                float s = Tema.S(20), x0 = r.X + (r.Width - s) / 2f, y0 = r.Y + (r.Height - s) / 2f, k = s / 20f;
                using var p = new Pen(renk, Math.Max(1.5f, 1.6f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawEllipse(p, x0 + 1.5f * k, y0 + 1.5f * k, 17 * k, 17 * k);
                using (var b = new SolidBrush(renk))
                {
                    g.FillEllipse(b, x0 + 6.4f * k, y0 + 7.4f * k, 2.2f * k, 2.2f * k);
                    g.FillEllipse(b, x0 + 11.4f * k, y0 + 7.4f * k, 2.2f * k, 2.2f * k);
                }
                g.DrawArc(p, x0 + 5.5f * k, y0 + 5.5f * k, 9f * k, 9f * k, 25, 130);
                break;
            }
        }
        g.SmoothingMode = SmoothingMode.None;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var c in _kartlar) c.Kucuk?.Dispose();
            _kartlar.Clear();
            _oneri.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- Öneri paneli

    private sealed class OneriPaneli : Control
    {
        public sealed record Oge(string Eklenecek, string Baslik, string? Alt, Profil? Profil);

        private List<Oge> _liste = [];
        private bool _emoji;
        private int _secili, _uzerinde = -1;

        public AvatarOnbellek? Avatarlar { get; set; }
        public event Action<string>? Secildi;

        private int SatirH => Tema.S(38);
        public int GerekenYukseklik => _liste.Count * SatirH + Tema.S(10);

        public OneriPaneli()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Tema.YuzeyUst;
            TabStop = false;
        }

        public void Ayarla(List<Oge> liste, bool emoji)
        {
            _liste = liste;
            _emoji = emoji;
            _secili = 0;
            _uzerinde = -1;
            Invalidate();
        }

        public void Gezin(int d)
        {
            if (_liste.Count == 0) return;
            _secili = (_secili + d + _liste.Count) % _liste.Count;
            Invalidate();
        }

        public void Kabul()
        {
            if (_secili >= 0 && _secili < _liste.Count) Secildi?.Invoke(_liste[_secili].Eklenecek);
        }

        private int Satir(Point p) => p.Y < Tema.S(5) ? -1 : (p.Y - Tema.S(5)) / SatirH is var i && i < _liste.Count ? i : -1;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = Satir(e.Location);
            if (i == _uzerinde) return;
            _uzerinde = i;
            if (i >= 0) _secili = i;
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = Satir(e.Location);
            if (i >= 0) { _secili = i; Kabul(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using var yol = Tema.YuvarlakDikdortgen(new RectangleF(0, 0, Width, Height), Tema.S(8));
            var eski = Region;
            Region = new Region(yol);
            eski?.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Tema.YuzeyUst);
            for (int i = 0; i < _liste.Count; i++)
            {
                var o = _liste[i];
                var r = new Rectangle(Tema.S(4), Tema.S(5) + i * SatirH, Width - Tema.S(8), SatirH);
                bool s = i == _secili;
                if (s) Cizim.YuvarlakDoldur(g, r, Tema.S(5), Tema.Vurgu);
                int x = r.X + Tema.S(10);
                if (_emoji)
                {
                    EmojiCizici.Ciz(g, o.Eklenecek, x, r.Y + (r.Height - Tema.S(22)) / 2, Tema.S(22));
                    x += Tema.S(22) + Tema.S(10);
                }
                else if (o.Profil != null)
                {
                    Cizim.Avatar(g, new RectangleF(x, r.Y + (r.Height - Tema.S(26)) / 2f, Tema.S(26), Tema.S(26)), o.Profil, Avatarlar?.Al(o.Profil), s ? Tema.Vurgu : Tema.YuzeyUst, false);
                    x += Tema.S(26) + Tema.S(10);
                }
                else
                {
                    // @herkes: kısa simge
                    var ik = new Rectangle(x, r.Y + (r.Height - Tema.S(26)) / 2, Tema.S(26), Tema.S(26));
                    Cizim.YuvarlakDoldur(g, ik, ik.Width / 2f, s ? Color.FromArgb(90, Color.White) : Tema.Kenar);
                    TextRenderer.DrawText(g, "@", Tema.Yazi(10, kalin: true), ik, Tema.Metin, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += Tema.S(26) + Tema.S(10);
                }
                var renk = s ? Color.White : Tema.Metin;
                var baslikF = Tema.Yazi(10, kalin: true);
                int bw = Math.Min(TextRenderer.MeasureText(o.Baslik, baslikF, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width, r.Right - x - Tema.S(80));
                TextRenderer.DrawText(g, o.Baslik, baslikF, new Rectangle(x, r.Y, bw + 2, r.Height), renk,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (o.Alt != null && x + bw + Tema.S(12) < r.Right - Tema.S(8))
                    TextRenderer.DrawText(g, o.Alt, Tema.Yazi(9), new Rectangle(x + bw + Tema.S(10), r.Y, r.Right - x - bw - Tema.S(18), r.Height),
                        s ? Color.FromArgb(220, Color.White) : Tema.MetinSoluk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var kalem = new Pen(Tema.Kenar);
            using var yol = Tema.YuvarlakDikdortgen(new RectangleF(.5f, .5f, Width - 1.5f, Height - 1.5f), Tema.S(8));
            g.DrawPath(kalem, yol);
        }
    }
}
