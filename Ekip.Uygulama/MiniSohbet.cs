using System.Runtime.InteropServices;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Oyuna sabitlenen küçük sohbet penceresi.
// Pasif: yarı saydam, tıklamalar içinden oyuna geçer, odak almaz.
// Etkileşimli (T'ye basınca ya da overlay açıkken): yazılır, başlıktan taşınır, kenarlardan boyutlanır, tekerlekle kayar.
public class MiniSohbet : Form
{
    public record Satir(string Ad, Color Renk, string Metin, long Zaman);

    private string _baslik = "";
    private List<Satir> _satirlar = [];
    private bool _etkilesim, _kapatUzerinde;
    private int _kaydirma, _icerikH; // _kaydirma: en alttan yukarı kaç piksel kaydırıldı
    private readonly GirisKutusu _girdi = new() { CokSatir = false, MaksUzunluk = Kurallar.MesajMax, Visible = false };

    public event Action? KapatIstendi;
    public event Action<string>? Gonder;
    public event Action<bool>? YazmaBitti;      // true: oyuna geri dön (Enter/Esc), false: kullanıcı başka yere tıkladı
    public event Action<Rectangle>? AlanDegisti; // taşındı ya da boyutlandı: ayarlara kaydedilsin
    public bool Etkilesimli => _etkilesim;
    public string TusYazisi { get; set; } = "T";

    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;
    private const TextFormatFlags Bicim = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    public MiniSohbet()
    {
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = ColorTranslator.FromHtml("#101116");
        DoubleBuffered = true;
        Size = new Size(Tema.S(340), Tema.S(240));
        MinimumSize = new Size(Tema.S(240), Tema.S(160));
        Opacity = 0.86;
        _girdi.IpucuYazisi = "Mesaj yaz, Enter ile gönder";
        Controls.Add(_girdi);

        _girdi.IcKutu.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                var m = _girdi.Text.Trim();
                _girdi.Text = "";
                if (m.Length > 0) Gonder?.Invoke(m);
                YazmaBitti?.Invoke(true); // Oyunlardaki gibi: gönder, oyuna dön.
            }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; YazmaBitti?.Invoke(true); }
        };
    }

    protected override bool ShowWithoutActivation => !_etkilesim;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int yuvarlak = 2; // Windows 11'de yuvarlak köşe; 10'da yok sayılır.
        DwmSetWindowAttribute(Handle, 33, ref yuvarlak, sizeof(int));
    }

    public void Ayarla(string baslik, List<Satir> satirlar)
    {
        int eskiH = _icerikH;
        _baslik = baslik;
        _satirlar = satirlar;
        Olc();
        // Yukarı kaydırmışken yeni mesaj gelirse okuduğu yer kaymasın.
        if (_kaydirma > 0) _kaydirma = Math.Clamp(_kaydirma + (_icerikH - eskiH), 0, MaksKaydirma);
        Invalidate();
    }

    public void EtkilesimAyarla(bool etkilesim)
    {
        _etkilesim = etkilesim;
        int s = GetWindowLong(Handle, -20);
        s = etkilesim ? s & ~WS_EX_TRANSPARENT & ~WS_EX_NOACTIVATE : s | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLong(Handle, -20, s);
        Opacity = etkilesim ? 0.97 : 0.86;
        _girdi.Visible = etkilesim;
        if (!etkilesim) { _kaydirma = 0; _girdi.Text = ""; }
        PerformLayout();
        Olc();
        Invalidate();
    }

    public void YaziKutusunaOdaklan() => _girdi.Focus();

    // Overlay perdesinin ve oyunun üstüne çık.
    public void EnUsteGetir() => SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x40);

    private int BaslikH => Tema.S(34);
    private int AltH => _etkilesim ? Tema.S(56) : Tema.S(24);
    private Rectangle KapatAlani => new(Width - Tema.S(34), (BaslikH - Tema.S(26)) / 2, Tema.S(26), Tema.S(26));
    private Rectangle MesajAlani => new(Tema.S(12), BaslikH + Tema.S(6), Width - Tema.S(24) - Tema.S(6), Height - BaslikH - AltH - Tema.S(8));
    private int MaksKaydirma => Math.Max(0, _icerikH - MesajAlani.Height);
    private const string HapYazisi = "↓ En alta in";
    private Rectangle HapAlani
    {
        get
        {
            int w = TextRenderer.MeasureText(HapYazisi, Tema.Yazi(8, kalin: true)).Width + Tema.S(12), h = Tema.S(20);
            return new Rectangle((Width - w) / 2, MesajAlani.Bottom - h - Tema.S(2), w, h);
        }
    }

    private int MesajYuksekligi(Satir s, int genislik) =>
        TextRenderer.MeasureText(s.Metin, Tema.Yazi(10), new Size(genislik, int.MaxValue), Bicim).Height + Tema.S(18) + Tema.S(8);

    private void Olc()
    {
        int w = MesajAlani.Width;
        _icerikH = w <= 0 ? 0 : _satirlar.Sum(s => MesajYuksekligi(s, w));
        _kaydirma = Math.Clamp(_kaydirma, 0, MaksKaydirma);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_girdi is null) return;
        _girdi.Bounds = new Rectangle(Tema.S(10), Height - Tema.S(52), Width - Tema.S(20), Tema.S(44));
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Olc(); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // Önce mesajlar (yazı çizimi kırpmaya uymuyor; taşan kısımları sonra başlık ve alt şerit örter).
        var alan = MesajAlani;
        if (_satirlar.Count == 0)
            TextRenderer.DrawText(g, "Henüz mesaj yok.", Tema.Yazi(9.5f), alan, Tema.MetinSoluk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        else
        {
            var metinF = Tema.Yazi(10);
            var adF = Tema.Yazi(9, kalin: true);
            int y = alan.Bottom + _kaydirma;
            bool kaydirildi = _kaydirma > 0;
            g.SetClip(alan);
            for (int i = _satirlar.Count - 1; i >= 0 && y > alan.Top; i--)
            {
                var s = _satirlar[i];
                int h = TextRenderer.MeasureText(g, s.Metin, metinF, new Size(alan.Width, int.MaxValue), Bicim).Height;
                // Pasifken (kaydırma yok) sığmayan eski mesajı yarım çizme; kaydırırken yarım görünmesi normal.
                if (!kaydirildi && i < _satirlar.Count - 1 && y - h - Tema.S(18) < alan.Top) break;
                y -= h;
                if (y < alan.Bottom) TextRenderer.DrawText(g, s.Metin, metinF, new Rectangle(alan.X, y, alan.Width, h + 2), Tema.Metin, Bicim);
                y -= Tema.S(18);
                if (y < alan.Bottom)
                {
                    int adW = TextRenderer.MeasureText(g, s.Ad, adF, Size.Empty, TextFormatFlags.NoPadding).Width;
                    TextRenderer.DrawText(g, s.Ad, adF, new Point(alan.X, y), Acik(s.Renk), TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, Zaman.Kisa(s.Zaman), Tema.Yazi(8), new Point(alan.X + adW + Tema.S(6), y + Tema.S(1)), Tema.MetinSoluk, TextFormatFlags.NoPadding);
                }
                y -= Tema.S(8);
            }
            g.ResetClip();

            // Kaydırma çubuğu (sadece etkileşimliyken ve içerik taşıyorsa)
            if (_etkilesim && MaksKaydirma > 0)
            {
                int cubukH = Math.Max(Tema.S(24), alan.Height * alan.Height / Math.Max(1, _icerikH));
                int cubukY = alan.Top + (alan.Height - cubukH) * (MaksKaydirma - _kaydirma) / MaksKaydirma;
                Cizim.YuvarlakDoldur(g, new Rectangle(Width - Tema.S(10), cubukY, Tema.S(4), cubukH), Tema.S(2), Tema.KenarHover);
            }
        }

        // Mesaj alanının dışına taşanları ört: üstteki boşluk ve alttaki şerit.
        using (var zemin = new SolidBrush(BackColor))
        {
            g.FillRectangle(zemin, 1, BaslikH, Width - 2, alan.Top - BaslikH);
            g.FillRectangle(zemin, 1, alan.Bottom, Width - 2, Height - alan.Bottom - 1);
        }

        // Kaydırılmışken altta küçük bir hap: yeni mesajlar aşağıda.
        if (_kaydirma > 0)
        {
            var hap = HapAlani;
            Cizim.YuvarlakDoldur(g, hap, hap.Height / 2f, Tema.Vurgu);
            TextRenderer.DrawText(g, HapYazisi, Tema.Yazi(8, kalin: true), hap, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Başlık en son: altına taşan mesaj kalmasın.
        using (var b = new SolidBrush(ColorTranslator.FromHtml("#17181F"))) g.FillRectangle(b, 1, 1, Width - 2, BaslikH - 1);
        TextRenderer.DrawText(g, Glif.Mesaj, Cizim.Ikon(9.5f), new Rectangle(Tema.S(10), 0, Tema.S(20), BaslikH), Tema.Vurgu, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _baslik, Tema.Yazi(9.5f, kalin: true), new Rectangle(Tema.S(34), 0, Width - Tema.S(80), BaslikH), Tema.Metin,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        if (_etkilesim)
        {
            var k = KapatAlani;
            if (_kapatUzerinde) Cizim.YuvarlakDoldur(g, k, Tema.S(4), Color.FromArgb(60, Tema.Hata));
            TextRenderer.DrawText(g, Glif.Kapat, Cizim.Ikon(8.5f), k, _kapatUzerinde ? Tema.Hata : Tema.MetinIkincil, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        using (var p = new Pen(_etkilesim ? Tema.Vurgu : ColorTranslator.FromHtml("#2A2C36"))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

        if (!_etkilesim)
            TextRenderer.DrawText(g, $"{TusYazisi} ile yaz", Tema.Yazi(8), new Rectangle(0, Height - Tema.S(24), Width - Tema.S(12), Tema.S(20)), Tema.MetinSoluk,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static Color Acik(Color c) => Color.FromArgb((c.R + 255) / 2, (c.G + 255) / 2, (c.B + 255) / 2);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_etkilesim) return;
        _kaydirma = Math.Clamp(_kaydirma + e.Delta * Tema.S(40) / 120, 0, MaksKaydirma);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool u = _etkilesim && KapatAlani.Contains(e.Location);
        Cursor = u ? Cursors.Hand : Cursors.Default;
        if (u != _kapatUzerinde) { _kapatUzerinde = u; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_kapatUzerinde) { _kapatUzerinde = false; Invalidate(); } }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_etkilesim || e.Button != MouseButtons.Left) return;
        if (KapatAlani.Contains(e.Location)) KapatIstendi?.Invoke();
        else if (_kaydirma > 0 && HapAlani.Contains(e.Location)) { _kaydirma = 0; Invalidate(); }
    }

    // Taşıma ve boyutlandırmayı Windows'a bırakıyoruz: başlık = sürükleme, kenarlar = boyut.
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84, WM_EXITSIZEMOVE = 0x232;
        if (m.Msg == WM_NCHITTEST && _etkilesim)
        {
            var p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            int k = Tema.S(7);
            bool sol = p.X < k, sag = p.X >= Width - k, ust = p.Y < k, alt = p.Y >= Height - k;
            int sonuc = ust && sol ? 13 : ust && sag ? 14 : alt && sol ? 16 : alt && sag ? 17 : sol ? 10 : sag ? 11 : ust ? 12 : alt ? 15
                : p.Y < BaslikH && !KapatAlani.Contains(p) ? 2 /*HTCAPTION*/ : 1 /*HTCLIENT*/;
            m.Result = sonuc;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_EXITSIZEMOVE) AlanDegisti?.Invoke(Bounds);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr sonra, int x, int y, int w, int hh, uint f);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
}

// Öndeki pencere değişince haber verir. Windows olayına abone olur, sürekli kontrol (zamanlayıcı) yok.
public sealed class OnPencereIzleyici : IDisposable
{
    private readonly WinEventProc _geriCagri; // Çöp toplayıcı silmesin diye alanda.
    private IntPtr _kanca;
    public event Action<IntPtr>? Degisti;

    public OnPencereIzleyici()
    {
        _geriCagri = (_, _, h, _, _, _, _) => Degisti?.Invoke(h);
        _kanca = SetWinEventHook(3 /*EVENT_SYSTEM_FOREGROUND*/, 3, IntPtr.Zero, _geriCagri, 0, 0, 0 /*OUTOFCONTEXT*/);
    }

    public void Dispose()
    {
        if (_kanca != IntPtr.Zero) UnhookWinEvent(_kanca);
        _kanca = IntPtr.Zero;
    }

    private delegate void WinEventProc(IntPtr kanca, uint olay, IntPtr h, int nesne, int cocuk, uint thread, uint zaman);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc p, uint pid, uint tid, uint bayrak);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr h);
}
