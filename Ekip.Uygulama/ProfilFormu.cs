using Ekip.Ortak;

namespace Ekip.Uygulama;

// Profil düzenleme (Ayarlar > Profil sekmesi): solda alanlar, sağda canlı önizleme. Ayrı pencere değil.
public class ProfilDuzenleyici : Panel
{
    private readonly Api _api;
    private Profil _ilk;
    private readonly AvatarOnbellek _avatarlar;

    private byte[]? _yeniAvatar;
    private bool _avatarKaldir;
    private Image? _onizlemeResmi;
    private string _renk;

    private readonly Label _baslik = new() { AutoSize = true, Text = "Profilini düzenle", Font = Tema.Yazi(16, kalin: true), ForeColor = Tema.Metin };
    private readonly Label _altBaslik = new() { AutoSize = true, Text = "Arkadaşların seni böyle görecek.", Font = Tema.Yazi(10), ForeColor = Tema.MetinIkincil };
    private readonly Label _resimEtiket = Etiket("PROFİL RESMİ");
    private readonly Dugme _resimYukle = new() { Text = "Resim yükle", Height = Tema.S(36) };
    private readonly Dugme _resimKaldir = new() { Text = "Kaldır", Tur = Dugme.Turler.Ikincil, Height = Tema.S(36) };
    private readonly GirisKutusu _gorunenAd = new() { Etiket = "Görünen ad", MaksUzunluk = Kurallar.GorunenAdMax };
    private readonly GirisKutusu _durumMesaji = new() { Etiket = "Durum mesajı", MaksUzunluk = Kurallar.DurumMesajiMax, IpucuYazisi = "Ne yapıyorsun? ör. Ranked kasıyorum" };
    private readonly GirisKutusu _hakkimda = new() { Etiket = "Hakkımda", MaksUzunluk = Kurallar.HakkimdaMax, CokSatir = true, IpucuYazisi = "Kendinden biraz bahset" };
    private readonly Label _renkEtiket = Etiket("PROFİL RENGİ");
    private readonly RenkSecici _renkler = new();
    private readonly Label _onizlemeEtiket = Etiket("ÖNİZLEME");
    private readonly ProfilKarti _onizleme = new();
    private readonly Panel _alt = new() { BackColor = Tema.Yuzey };
    private readonly Label _hata = new() { AutoSize = false, Font = Tema.Yazi(9.5f, kalin: true), ForeColor = Tema.Hata, BackColor = Tema.Yuzey, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dugme _vazgec = new() { Text = "Geri al", Tur = Dugme.Turler.Ikincil };
    private readonly Dugme _kaydet = new() { Text = "Kaydet" };

    public event Action<Profil>? Kaydedildi;

    private static Label Etiket(string s) => new() { AutoSize = true, Text = s, Font = Tema.Yazi(8.5f, kalin: true), ForeColor = Tema.MetinIkincil };

    public ProfilDuzenleyici(Api api, Profil ben, AvatarOnbellek avatarlar)
    {
        _api = api;
        _ilk = ben;
        _avatarlar = avatarlar;
        _renk = ben.ProfilRengi;
        BackColor = Tema.Arkaplan;
        ForeColor = Tema.Metin;
        _hakkimda.Height = Tema.S(24) + Tema.S(104);

        _alt.Controls.AddRange([_hata, _vazgec, _kaydet]);
        Controls.AddRange([_baslik, _altBaslik, _resimEtiket, _resimYukle, _resimKaldir, _gorunenAd, _durumMesaji, _hakkimda,
            _renkEtiket, _renkler, _onizlemeEtiket, _onizleme, _alt]);

        foreach (var k in new[] { _gorunenAd, _durumMesaji, _hakkimda }) k.TextChanged += (_, _) => Onizle();
        _renkler.Degisti += r => { _renk = r; Onizle(); };
        _resimYukle.Click += (_, _) => ResimSec();
        _resimKaldir.Click += (_, _) =>
        {
            _yeniAvatar = null;
            _avatarKaldir = true;
            _onizlemeResmi = null;
            Onizle();
        };
        _vazgec.Click += (_, _) => Yukle(_ilk);
        _kaydet.Click += async (_, _) => await Kaydet();

        Yukle(ben);
    }

    // Alanları verilen profille doldurur (ilk açılış, "Geri al", kayıttan sonra).
    public void Yukle(Profil ben)
    {
        _ilk = ben;
        _renk = ben.ProfilRengi;
        _yeniAvatar = null;
        _avatarKaldir = false;
        if (_onizlemeResmi != null && _onizlemeResmi != _avatarlar.Al(ben)) _onizlemeResmi.Dispose();
        _onizlemeResmi = _avatarlar.Al(ben);
        _gorunenAd.Text = ben.GorunenAd;
        _durumMesaji.Text = ben.DurumMesaji;
        _hakkimda.Text = ben.Hakkimda;
        _gorunenAd.Hata = null;
        _renkler.Secili = _renk;
        Mesgul(false);
        Onizle();
    }

    private ProfilGuncelleme Guncelleme() => new(_gorunenAd.Text.Trim(), _hakkimda.Text.Trim(), _durumMesaji.Text.Trim(), _renk);

    private void Onizle()
    {
        var g = Guncelleme();
        _onizleme.Profil = _ilk with
        {
            GorunenAd = string.IsNullOrWhiteSpace(g.GorunenAd) ? _ilk.KullaniciAdi : g.GorunenAd,
            Hakkimda = g.Hakkimda,
            DurumMesaji = g.DurumMesaji,
            ProfilRengi = g.ProfilRengi,
        };
        _onizleme.Resim = _onizlemeResmi;
        _resimKaldir.Visible = _onizlemeResmi != null;
        _hata.Text = "";
        PerformLayout();
    }

    private void ResimSec()
    {
        using var d = new OpenFileDialog
        {
            Title = "Profil resmi seç",
            Filter = "Resimler|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tüm dosyalar|*.*",
        };
        if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(d.FileName).Length > 20 * 1024 * 1024) { _hata.Text = "Bu resim çok büyük (en fazla 20 MB)."; return; }
            _yeniAvatar = Cizim.AvatarHazirla(d.FileName);
            _avatarKaldir = false;
            if (_onizlemeResmi != null && _onizlemeResmi != _avatarlar.Al(_ilk)) _onizlemeResmi.Dispose();
            _onizlemeResmi = Cizim.ResimCoz(_yeniAvatar);
            Onizle();
        }
        catch
        {
            _hata.Text = "Bu dosya açılamadı. PNG veya JPG bir resim seç.";
        }
    }

    private async Task Kaydet()
    {
        var g = Guncelleme();
        _gorunenAd.Hata = Kurallar.GorunenAdKontrol(g.GorunenAd);
        if (_gorunenAd.Hata != null) { _gorunenAd.Focus(); return; }
        if (Kurallar.ProfilKontrol(g) is { } h) { _hata.Text = h; return; }

        Mesgul(true);
        Profil? son = null;
        if (_avatarKaldir && _ilk.AvatarSurum != 0)
        {
            var s = await _api.AvatarSil();
            if (!s.Basarili) { Hata(s.Hata!); return; }
            _avatarlar.Koy(_ilk.Id, 0, null);
        }
        if (_yeniAvatar != null)
        {
            var s = await _api.AvatarYukle(_yeniAvatar);
            if (!s.Basarili) { Hata(s.Hata!); return; }
            _avatarlar.Koy(_ilk.Id, s.Veri!.AvatarSurum, Cizim.ResimCoz(_yeniAvatar)); // Tekrar indirmeye gerek yok.
            son = s.Veri;
        }
        bool degisti = g.GorunenAd != _ilk.GorunenAd || g.Hakkimda != _ilk.Hakkimda || g.DurumMesaji != _ilk.DurumMesaji || g.ProfilRengi != _ilk.ProfilRengi;
        if (degisti || son is null)
        {
            var s = await _api.ProfilGuncelle(g);
            if (!s.Basarili) { Hata(s.Hata!); return; }
            son = s.Veri;
        }
        Yukle(son!);
        Kaydedildi?.Invoke(son!);
    }

    private void Hata(string mesaj) { Mesgul(false); _hata.Text = mesaj; }

    private void Mesgul(bool m)
    {
        foreach (Control c in new Control[] { _gorunenAd, _durumMesaji, _hakkimda, _renkler, _resimYukle, _resimKaldir, _kaydet, _vazgec })
            c.Enabled = !m;
        _kaydet.Text = m ? "Kaydediliyor…" : "Kaydet";
        UseWaitCursor = m;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_alt is null) return;
        int w = ClientSize.Width, h = ClientSize.Height;
        int x = Tema.S(28), y = Tema.S(22);
        int solW = Tema.S(390);

        _baslik.Location = new Point(x - Tema.S(5), y); y = _baslik.Bottom;
        _altBaslik.Location = new Point(x - Tema.S(3), y); y = _altBaslik.Bottom + Tema.S(20);

        _resimEtiket.Location = new Point(x - Tema.S(3), y); y = _resimEtiket.Bottom + Tema.S(6);
        _resimYukle.Bounds = new Rectangle(x, y, Tema.S(130), Tema.S(36));
        _resimKaldir.Bounds = new Rectangle(_resimYukle.Right + Tema.S(10), y, Tema.S(90), Tema.S(36));
        y = _resimYukle.Bottom + Tema.S(12);

        foreach (var k in new[] { _gorunenAd, _durumMesaji, _hakkimda })
        {
            k.Bounds = new Rectangle(x, y, solW, k.Height);
            y = k.Bottom + Tema.S(12);
        }
        _renkEtiket.Location = new Point(x - Tema.S(3), y); y = _renkEtiket.Bottom + Tema.S(6);
        _renkler.Bounds = new Rectangle(x, y, solW, Tema.S(36));

        int sagX = x + solW + Tema.S(36), sagW = w - sagX - Tema.S(28);
        int oy = _resimEtiket.Top;
        _onizlemeEtiket.Location = new Point(sagX - Tema.S(3), oy);
        int kartY = _onizlemeEtiket.Bottom + Tema.S(6);
        _onizleme.Bounds = new Rectangle(sagX, kartY, sagW, Math.Min(_onizleme.GerekenYukseklik(sagW), h - Tema.S(68) - kartY - Tema.S(16)));

        _alt.Bounds = new Rectangle(0, h - Tema.S(68), w, Tema.S(68)); // Kaydet çubuğu sayfanın altında
        _kaydet.Bounds = new Rectangle(w - Tema.S(28) - Tema.S(120), Tema.S(14), Tema.S(120), Tema.S(40));
        _vazgec.Bounds = new Rectangle(_kaydet.Left - Tema.S(10) - Tema.S(100), Tema.S(14), Tema.S(100), Tema.S(40));
        _hata.Bounds = new Rectangle(Tema.S(28), 0, _vazgec.Left - Tema.S(40), Tema.S(68));
    }
}

// Profil rengi seçmek için yuvarlak renk düğmeleri.
public class RenkSecici : Control
{
    private string _secili = Kurallar.ProfilRenkleri[0];
    private int _uzerinde = -1;
    public event Action<string>? Degisti;

    public string Secili { get => _secili; set { _secili = value; Invalidate(); } }

    public RenkSecici()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    private Rectangle Alan(int i)
    {
        int cap = Tema.S(30), ara = Tema.S(10);
        return new Rectangle(Tema.S(3) + i * (cap + ara), (Height - cap) / 2, cap, cap);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int i = 0; i < Kurallar.ProfilRenkleri.Length; i++)
        {
            var r = Alan(i);
            var renk = ColorTranslator.FromHtml(Kurallar.ProfilRenkleri[i]);
            bool secili = Kurallar.ProfilRenkleri[i] == _secili;
            if (secili || i == _uzerinde)
            {
                var halka = Rectangle.Inflate(r, Tema.S(3), Tema.S(3));
                using var p = new Pen(secili ? Color.White : Tema.KenarHover, Tema.S(2));
                g.DrawEllipse(p, halka);
            }
            using var b = new SolidBrush(Enabled ? renk : Color.FromArgb(90, renk));
            g.FillEllipse(b, r);
            if (secili)
                TextRenderer.DrawText(g, Glif.Onay, Cizim.Ikon(9), r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private int Bul(Point p)
    {
        for (int i = 0; i < Kurallar.ProfilRenkleri.Length; i++) if (Alan(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = Bul(e.Location);
        if (i == _uzerinde) return;
        _uzerinde = i;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = -1; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int i = Bul(e.Location);
        if (i < 0 || !Enabled) return;
        _secili = Kurallar.ProfilRenkleri[i];
        Invalidate();
        Degisti?.Invoke(_secili);
    }
}
