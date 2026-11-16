using System.Diagnostics;
using System.Runtime.InteropServices;
using Ekip.Ortak;

namespace Ekip.Uygulama;

// Windows pencereleriyle ilgili küçük yardımcılar (oyun penceresini bulmak, öne getirmek).
public static class Pencereler
{
    public record Pencere(IntPtr Hwnd, string Baslik, string Surec);

    public static List<Pencere> Listele()
    {
        var liste = new List<Pencere>();
        int benimPid = Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
            if ((GetWindowLong(h, -20) & 0x80 /*WS_EX_TOOLWINDOW*/) != 0) return true;
            if (DwmGetWindowAttribute(h, 14 /*DWMWA_CLOAKED*/, out int gizli, 4) == 0 && gizli != 0) return true;
            var baslik = Baslik(h);
            if (baslik.Length == 0 || baslik == "Program Manager") return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == benimPid) return true;
            var surec = SurecAdi(h);
            if (surec is null or "TextInputHost") return true;
            liste.Add(new Pencere(h, baslik, surec));
            return true;
        }, IntPtr.Zero);
        return liste;
    }

    public static string Baslik(IntPtr h)
    {
        var sb = new System.Text.StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string? SurecAdi(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch { return null; }
    }

    public static IntPtr OnPencere() => GetForegroundWindow();

    // Görünür bir pencereyi öne getirmeden yalnız göster (perde için).
    public static void Goster(IntPtr h) => ShowWindow(h, 4 /*SW_SHOWNOACTIVATE*/);

    public static Rectangle Alan(IntPtr h)
    {
        if (DwmGetWindowAttribute(h, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out RECT r, Marshal.SizeOf<RECT>()) != 0)
            GetWindowRect(h, out r);
        return Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
    }

    // Windows başka programın odağı çalmasını engeller. Öndeki pencerenin iş parçacığına geçici bağlanınca izin veriyor.
    public static void OneGetir(IntPtr h)
    {
        if (h == IntPtr.Zero || !IsWindow(h)) return;
        if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
        var on = GetForegroundWindow();
        uint onThread = GetWindowThreadProcessId(on, out _), benim = GetCurrentThreadId();
        bool bagli = onThread != 0 && onThread != benim && AttachThreadInput(benim, onThread, true);
        BringWindowToTop(h);
        SetForegroundWindow(h);
        if (bagli) AttachThreadInput(benim, onThread, false);
    }

    public static bool Gecerli(IntPtr h) => h != IntPtr.Zero && IsWindow(h) && IsWindowVisible(h);

    public static bool BizimMi(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid);
        return pid == Environment.ProcessId;
    }

    // Oyun gibi tüm ekranı kaplayan pencere mi? (Masaüstü ve görev çubuğu hariç.)
    public static bool TamEkran(IntPtr h)
    {
        if (!Gecerli(h)) return false;
        var sinif = new System.Text.StringBuilder(64);
        GetClassName(h, sinif, sinif.Capacity);
        if (sinif.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        GetWindowRect(h, out var r);
        var ekran = Screen.FromHandle(h).Bounds;
        return r.L <= ekran.Left && r.T <= ekran.Top && r.R >= ekran.Right && r.B >= ekran.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT v, int s);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool bagla);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
}

// Sistem geneli klavye kancası: kısayollarımızı (overlay, mini sohbet, Esc) oyun öndeyken de yakalar.
// Karar veren fonksiyon true dönerse tuş yutulur (oyun/tarayıcı görmez), false ise normal çalışır.
// Her tuşta çalışır, o yüzden karar fonksiyonu hızlı olmalı (sadece birkaç karşılaştırma).
public sealed class KlavyeKancasi : IDisposable
{
    private readonly HookProc _geriCagri; // Çöp toplayıcı silmesin diye alanda.
    private IntPtr _kanca;
    public Func<Keys, bool, bool, bool, bool>? TusBasildi { get; set; } // (tuş, ctrl, alt, shift) → yut?

    public KlavyeKancasi()
    {
        _geriCagri = Isle;
        _kanca = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, _geriCagri, GetModuleHandle(null), 0);
    }

    private IntPtr Isle(int kod, IntPtr wParam, IntPtr lParam)
    {
        if (kod >= 0 && (wParam == 0x100 || wParam == 0x104) /*KEYDOWN, SYSKEYDOWN*/)
        {
            var tus = (Keys)Marshal.ReadInt32(lParam);
            if (tus == Keys.Escape || !Kisayol.Gecersiz(tus))
            {
                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0, alt = (GetAsyncKeyState(0x12) & 0x8000) != 0, shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                if (TusBasildi?.Invoke(tus, ctrl, alt, shift) == true) return 1;
            }
        }
        return CallNextHookEx(_kanca, kod, wParam, lParam);
    }

    public void Dispose()
    {
        if (_kanca != IntPtr.Zero) UnhookWindowsHookEx(_kanca);
        _kanca = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int kod, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int kod, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? ad);
}

// Steam overlay'i gibi: oyunun üstünü karartan yarı saydam perde. Tıklanınca overlay kapanır.
public class OverlayPerdesi : Form
{
    public event Action? Tiklandi;

    public OverlayPerdesi()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = ColorTranslator.FromHtml("#05060A");
        Opacity = 0.72;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/; return cp; }
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Tiklandi?.Invoke(); }
}

// Overlay'in içeriği: perdenin üstünde, şeffaf zeminli pencere. Sadece kartlar görünür, aradaki boşluk perdeye tıklar.
public class OverlayFormu : Form
{
    public enum Sekme { Parti, Dm }

    private static readonly Color Seffaf = Color.FromArgb(255, 1, 2, 3); // Bu renk pencerede delik olur.

    private readonly OverlayUstSerit _ust = new();
    private readonly KartPaneli _sol = new();
    private readonly KartPaneli _sag = new();
    private readonly OverlayPartiKarti _partiKarti = new();
    private readonly Label _arkadasBaslik = new() { AutoSize = true, Font = Tema.Yazi(8.5f, kalin: true), ForeColor = Tema.MetinIkincil, BackColor = Tema.Arkaplan };
    private readonly ArkadasListe _arkadaslar = new();
    private readonly OverlaySekmeler _sekmeler = new();
    private readonly SohbetPaneli _dm = new();
    private readonly SohbetPaneli _parti = new() { Ipucu = "Partiye yaz, Enter ile gönder", BaslikGizli = true };
    private readonly BosDurum _bos = new() { Glif = Uygulama.Glif.Mesaj, Baslik = "Bir sohbet seç", Aciklama = "Soldan bir arkadaşına tıkla ya da parti kurup arkadaşlarını davet et." };
    private readonly System.Windows.Forms.Timer _saat = new() { Interval = 20000 };

    public AvatarOnbellek? Avatarlar { set { _arkadaslar.Avatarlar = _dm.Avatarlar = _parti.Avatarlar = _partiKarti.Avatarlar = value; } }
    public Sekme Secili { get; private set; } = Sekme.Parti;
    public string TusYazisi { get => _ust.TusYazisi; set { _ust.TusYazisi = value; _ust.Invalidate(); } }
    public Profil? DmKisi { get; private set; }
    public bool Acik => Visible;

    public event Action? KapatIstendi;
    public event Action<long>? DmSecildi;
    public event Action<long, string>? DmGonder;
    public event Action<string>? PartiGonder;
    public event Action<long>? DmEskiIste;
    public event Action<Profil>? Davet;
    public event Action? PartiKur;
    public event Action? PartidenAyril;
    public event Action? SabitleTik; // Açık sekmeyi (parti ya da DM) oyuna mini sohbet olarak sabitle/kaldır.

    // Ana pencere hangi sohbetin sabitli olduğunu bilir; sekme değişince buna bakıp düğme yazısını ayarlar.
    public Func<Sekme, long?, bool>? SabitliMi { get; set; }

    public OverlayFormu()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = TransparencyKey = Seffaf;
        Font = Tema.Yazi(10);
        KeyPreview = true;

        _sol.Controls.AddRange([_partiKarti, _arkadasBaslik, _arkadaslar]);
        _sag.Controls.AddRange([_sekmeler, _dm, _parti, _bos]);
        Controls.AddRange([_ust, _sol, _sag]);

        _ust.Kapat += () => KapatIstendi?.Invoke();
        _arkadaslar.Eylem += (s, eylem, alan) =>
        {
            if (eylem is "mesaj" or "profil") DmSecildi?.Invoke(s.Profil.Id);
            else if (eylem == "menu")
            {
                var m = new KoyuMenu();
                m.Ekle("Mesaj gönder", () => DmSecildi?.Invoke(s.Profil.Id));
                if (s.Profil.Durum != Durum.Cevrimdisi) m.Ekle("Partiye davet et", () => Davet?.Invoke(s.Profil));
                m.Closed += (_, _) => BeginInvoke(m.Dispose);
                m.Show(alan.Left, alan.Bottom + Tema.S(4));
            }
        };
        _partiKarti.PartiKur += () => PartiKur?.Invoke();
        _partiKarti.Ayril += () => PartidenAyril?.Invoke();
        _sekmeler.Secildi += s => { Secili = s; SekmeyiGoster(); };
        _sekmeler.SabitleTik += () => SabitleTik?.Invoke();
        _dm.Gonder += metin => { if (DmKisi != null) DmGonder?.Invoke(DmKisi.Id, metin); };
        _dm.EskiIste += () => { if (DmKisi != null) DmEskiIste?.Invoke(DmKisi.Id); };
        _parti.Gonder += metin => PartiGonder?.Invoke(metin);
        _saat.Tick += (_, _) => _ust.Invalidate();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/; return cp; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { e.Handled = e.SuppressKeyPress = true; KapatIstendi?.Invoke(); }
    }

    public void Ac(Rectangle ekran, string oyunBasligi)
    {
        Bounds = ekran;
        _ust.OyunBasligi = oyunBasligi;
        _saat.Start();
        SekmeyiGoster();
        if (!Visible) Show();
        Pencereler.OneGetir(Handle);
        Activate();
        (Secili == Sekme.Parti && _parti.Visible ? _parti : _dm).Odaklan();
    }

    public void Kapat()
    {
        _saat.Stop();
        Hide();
    }

    public void ArkadaslariAyarla(List<Profil> arkadaslar)
    {
        var sirali = arkadaslar.OrderBy(p => p.Durum == Durum.Cevrimdisi ? 1 : 0).ThenBy(p => p.GorunenAd, StringComparer.Create(Tema.Turkce, true)).ToList();
        int cevrimici = sirali.Count(p => p.Durum != Durum.Cevrimdisi);
        _arkadasBaslik.Text = $"ARKADAŞLAR — {cevrimici} ÇEVRİMİÇİ";
        _arkadaslar.Ayarla(sirali.Select(p => new Satir(p, SatirTuru.Arkadas)).ToList());
    }

    public void PartiAyarla(PartiBilgisi? p)
    {
        _partiKarti.Parti = p;
        _sekmeler.PartiVar = p != null;
        if (p is null && Secili == Sekme.Parti && DmKisi != null) Secili = Sekme.Dm;
        SekmeyiGoster();
        PerformLayout(); // Parti kartının yüksekliği değişti: arkadaş listesi yukarı/aşağı kaysın.
    }

    public void DmAc(Profil kisi)
    {
        DmKisi = kisi;
        Secili = Sekme.Dm;
        _sekmeler.DmAdi = kisi.GorunenAd;
        SekmeyiGoster();
        _dm.Odaklan();
    }

    public void DmGoster(Profil ben, Profil kisi, SohbetVerisi v, HashSet<string> basarisiz)
    {
        if (DmKisi?.Id != kisi.Id) return;
        DmKisi = kisi;
        _dm.Goster(ben, kisi, v, basarisiz);
    }

    public void PartiSohbetiGoster(Profil ben, Profil baslik, SohbetVerisi v, Func<long, Profil?> cozucu)
    {
        _parti.Cozucu = cozucu;
        _parti.Goster(ben, baslik, v, []);
    }

    public void PartiAlta() => _parti.Alta();
    public void DmAlta() => _dm.Alta();

    private void SekmeyiGoster()
    {
        _sekmeler.Secili = Secili;
        bool parti = Secili == Sekme.Parti && _partiKarti.Parti != null;
        bool dm = Secili == Sekme.Dm && DmKisi != null;
        _parti.Visible = parti;
        _dm.Visible = dm;
        _bos.Visible = !parti && !dm;
        if (_bos.Visible) _bos.Invalidate();
        _sekmeler.SabitButonu = parti || dm;
        _sekmeler.Sabitli = (parti || dm) && SabitliMi?.Invoke(Secili, DmKisi?.Id) == true;
    }

    public void SabitDurumunuYenile() => SekmeyiGoster();

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_ust is null) return;
        int w = ClientSize.Width, h = ClientSize.Height;
        _ust.Bounds = new Rectangle(0, 0, w, Tema.S(60));

        int bosluk = Tema.S(40), ustY = _ust.Bottom + Tema.S(32);
        int kartH = Math.Min(h - ustY - bosluk, Tema.S(780));
        int solW = Tema.S(360), sagW = Math.Min(Tema.S(820), w - bosluk * 2 - solW - Tema.S(24));
        int toplam = solW + Tema.S(24) + sagW;
        int x = (w - toplam) / 2;
        _sol.Bounds = new Rectangle(x, ustY, solW, kartH);
        _sag.Bounds = new Rectangle(_sol.Right + Tema.S(24), ustY, sagW, kartH);

        int ic = Tema.S(14);
        _partiKarti.Bounds = new Rectangle(ic, ic, solW - ic * 2, _partiKarti.Yukseklik);
        _arkadasBaslik.Location = new Point(ic + Tema.S(2), _partiKarti.Bottom + Tema.S(18));
        int ly = _arkadasBaslik.Bottom + Tema.S(8);
        _arkadaslar.Bounds = new Rectangle(Tema.S(4), ly, solW - Tema.S(8), kartH - ly - Tema.S(4));

        _sekmeler.Bounds = new Rectangle(1, 1, sagW - 2, Tema.S(52));
        var icerik = new Rectangle(1, _sekmeler.Bottom, sagW - 2, kartH - _sekmeler.Bottom - 1);
        _dm.Bounds = _parti.Bounds = _bos.Bounds = icerik;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _saat.Dispose();
        base.Dispose(disposing);
    }
}

// Kenarlıklı, opak kart (overlay'deki paneller).
public class KartPaneli : Panel
{
    public KartPaneli()
    {
        BackColor = Tema.Arkaplan;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p = new Pen(Tema.Kenar);
        e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }
}

// Overlay'in üst şeridi: logo, oyun adı, saat, kapat.
public class OverlayUstSerit : Control
{
    private bool _kapatUzerinde;
    public string OyunBasligi { get; set; } = "";
    public string TusYazisi { get; set; } = "Ctrl+Tab";
    public event Action? Kapat;

    public OverlayUstSerit()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = ColorTranslator.FromHtml("#0E0F13");
    }

    private Rectangle KapatAlani => new(Width - Tema.S(176), (Height - Tema.S(36)) / 2, Tema.S(156), Tema.S(36));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var logo = new Rectangle(Tema.S(24), (Height - Tema.S(30)) / 2, Tema.S(30), Tema.S(30));
        Tema.Logo(g, logo);
        TextRenderer.DrawText(g, "ekip", Tema.Yazi(13, kalin: true), new Rectangle(logo.Right + Tema.S(10), 0, Tema.S(80), Height), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int x = logo.Right + Tema.S(84);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, x, Height / 2 - Tema.S(12), x, Height / 2 + Tema.S(12));
        x += Tema.S(16);
        TextRenderer.DrawText(g, Glif.Oyun, Cizim.Ikon(12), new Rectangle(x, 0, Tema.S(24), Height), Tema.MetinIkincil, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += Tema.S(30);
        var saat = DateTime.Now.ToString("HH:mm");
        int saatW = TextRenderer.MeasureText(g, saat, Tema.Yazi(13, kalin: true), Size.Empty, TextFormatFlags.NoPadding).Width;
        int saatX = (Width - saatW) / 2;
        TextRenderer.DrawText(g, OyunBasligi, Tema.Yazi(10.5f), new Rectangle(x, 0, Math.Max(0, saatX - x - Tema.S(20)), Height), Tema.MetinIkincil,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, saat, Tema.Yazi(13, kalin: true), new Rectangle(saatX, 0, saatW + 2, Height), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var k = KapatAlani;
        Cizim.YuvarlakDoldur(g, k, Tema.S(6), _kapatUzerinde ? Tema.Kenar : Tema.YuzeyUst);
        TextRenderer.DrawText(g, $"Kapat   {TusYazisi}", Tema.Yazi(9.5f, kalin: true), k, Tema.Metin, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        using (var p = new Pen(Tema.Kenar)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool u = KapatAlani.Contains(e.Location);
        if (u == _kapatUzerinde) return;
        _kapatUzerinde = u;
        Cursor = u ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _kapatUzerinde = false; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (KapatAlani.Contains(e.Location)) Kapat?.Invoke(); }
}

// Overlay'deki parti kartı: parti yoksa "Parti kur", varsa üyeler ve "Ayrıl".
public class OverlayPartiKarti : Control
{
    private PartiBilgisi? _parti;
    private string? _uzerinde;
    public AvatarOnbellek? Avatarlar { get; set; }
    public PartiBilgisi? Parti { get => _parti; set { _parti = value; Height = Yukseklik; Invalidate(); } }
    public int Yukseklik => _parti is null ? Tema.S(64) : Tema.S(104);
    public event Action? PartiKur;
    public event Action? Ayril;

    public OverlayPartiKarti()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = Yukseklik;
    }

    private Rectangle AyrilAlani => new(Width - Tema.S(70), Tema.S(10), Tema.S(60), Tema.S(28));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Arkaplan);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_parti is null)
        {
            Cizim.YuvarlakDoldur(g, r, Tema.S(8), _uzerinde == "kur" ? Tema.VurguHover : Tema.Vurgu);
            TextRenderer.DrawText(g, Glif.Oyun, Cizim.Ikon(15), new Rectangle(Tema.S(16), 0, Tema.S(30), Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Parti kur", Tema.Yazi(11, kalin: true), new Point(Tema.S(56), Tema.S(12)), Color.White, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Arkadaşlarını davet et, birlikte konuşun", Tema.Yazi(9), new Point(Tema.S(56), Tema.S(35)), ColorTranslator.FromHtml("#E4DDFF"), TextFormatFlags.NoPadding);
            return;
        }
        Cizim.YuvarlakDoldur(g, r, Tema.S(8), Tema.Yuzey);
        TextRenderer.DrawText(g, Glif.Oyun, Cizim.Ikon(12), new Point(Tema.S(14), Tema.S(15)), Tema.Vurgu, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, $"Parti · {_parti.Uyeler.Count} kişi", Tema.Yazi(10.5f, kalin: true), new Point(Tema.S(42), Tema.S(13)), Tema.Metin, TextFormatFlags.NoPadding);
        var a = AyrilAlani;
        if (_uzerinde == "ayril") Cizim.YuvarlakDoldur(g, a, Tema.S(4), Color.FromArgb(50, Tema.Hata));
        TextRenderer.DrawText(g, "Ayrıl", Tema.Yazi(9.5f, kalin: true), a, Tema.Hata, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        for (int i = 0; i < Math.Min(_parti.Uyeler.Count, 8); i++)
        {
            var u = _parti.Uyeler[i];
            Cizim.Avatar(g, new RectangleF(Tema.S(14) + i * Tema.S(38), Tema.S(52), Tema.S(34), Tema.S(34)), u, Avatarlar?.Al(u), Tema.Yuzey, false);
        }
    }

    private string? Bul(Point p) => _parti is null ? "kur" : AyrilAlani.Contains(p) ? "ayril" : null;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var b = Bul(e.Location);
        if (b == _uzerinde) return;
        _uzerinde = b;
        Cursor = b != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = null; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var b = Bul(e.Location);
        if (b == "kur") PartiKur?.Invoke();
        else if (b == "ayril") Ayril?.Invoke();
    }
}

// Sağ karttaki sekmeler: "Parti sohbeti" ve seçili arkadaşla direkt mesaj.
public class OverlaySekmeler : Control
{
    private OverlayFormu.Sekme _secili;
    private bool _partiVar;
    private string? _dmAdi;
    private OverlayFormu.Sekme? _uzerinde;
    private readonly List<(OverlayFormu.Sekme s, Rectangle r)> _alanlar = [];

    public OverlayFormu.Sekme Secili { get => _secili; set { _secili = value; Invalidate(); } }
    public bool PartiVar { get => _partiVar; set { _partiVar = value; Invalidate(); } }
    public string? DmAdi { get => _dmAdi; set { _dmAdi = value; Invalidate(); } }
    public event Action<OverlayFormu.Sekme>? Secildi;

    public OverlaySekmeler()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Tema.Arkaplan;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        _alanlar.Clear();
        int x = Tema.S(14);
        var f = Tema.Yazi(10, kalin: true);
        void Sekme(OverlayFormu.Sekme s, string glif, string yazi)
        {
            int w = TextRenderer.MeasureText(g, yazi, f, Size.Empty, TextFormatFlags.NoPadding).Width + Tema.S(52);
            var r = new Rectangle(x, (Height - Tema.S(34)) / 2, Math.Min(w, Tema.S(260)), Tema.S(34));
            _alanlar.Add((s, r));
            bool secili = s == _secili;
            if (secili) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.YuzeyUst);
            else if (s == _uzerinde) Cizim.YuvarlakDoldur(g, r, Tema.S(6), Tema.Yuzey);
            var renk = secili || s == _uzerinde ? Tema.Metin : Tema.MetinIkincil;
            TextRenderer.DrawText(g, glif, Cizim.Ikon(10.5f), new Rectangle(r.X + Tema.S(12), r.Y, Tema.S(20), r.Height), renk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, yazi, f, new Rectangle(r.X + Tema.S(38), r.Y, r.Width - Tema.S(46), r.Height), renk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            x = r.Right + Tema.S(8);
        }
        if (_partiVar) Sekme(OverlayFormu.Sekme.Parti, Glif.Oyun, "Parti sohbeti");
        if (_dmAdi != null) Sekme(OverlayFormu.Sekme.Dm, Glif.Mesaj, _dmAdi);
        if (_alanlar.Count == 0)
            TextRenderer.DrawText(g, "Sohbet", Tema.Yazi(10.5f, kalin: true), new Rectangle(Tema.S(20), 0, Tema.S(200), Height), Tema.Metin, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        // Sağda: açık sohbeti oyuna mini pencere olarak sabitle.
        if (_sabitButonu)
        {
            var r = SabitAlani;
            if (_sabitli) Cizim.YuvarlakDoldur(g, r, Tema.S(6), _sabitUzerinde ? Tema.Kenar : Tema.YuzeyUst);
            else Cizim.YuvarlakDoldur(g, r, Tema.S(6), _sabitUzerinde ? Tema.VurguHover : Tema.Vurgu);
            var yazi = _sabitli ? "Sabitlemeyi kaldır" : "Oyuna sabitle";
            TextRenderer.DrawText(g, _sabitli ? Glif.SabitlemeyiKaldir : Glif.Sabitle, Cizim.Ikon(10), new Rectangle(r.X + Tema.S(12), r.Y, Tema.S(20), r.Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, yazi, Tema.Yazi(9.5f, kalin: true), new Rectangle(r.X + Tema.S(36), r.Y, r.Width - Tema.S(40), r.Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        using var p = new Pen(Tema.Kenar);
        g.DrawLine(p, 0, Height - 1, Width, Height - 1);
    }

    private bool _sabitButonu, _sabitli, _sabitUzerinde;
    public bool SabitButonu { get => _sabitButonu; set { _sabitButonu = value; Invalidate(); } }
    public bool Sabitli { get => _sabitli; set { _sabitli = value; Invalidate(); } }
    public event Action? SabitleTik;
    private Rectangle SabitAlani => new(Width - Tema.S(14) - Tema.S(176), (Height - Tema.S(34)) / 2, Tema.S(176), Tema.S(34));

    private OverlayFormu.Sekme? Bul(Point p) => _alanlar.Where(a => a.r.Contains(p)).Select(a => (OverlayFormu.Sekme?)a.s).FirstOrDefault();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var b = Bul(e.Location);
        bool s = _sabitButonu && SabitAlani.Contains(e.Location);
        if (b == _uzerinde && s == _sabitUzerinde) return;
        _uzerinde = b;
        _sabitUzerinde = s;
        Cursor = b != null || s ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _uzerinde = null; _sabitUzerinde = false; Invalidate(); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_sabitButonu && SabitAlani.Contains(e.Location)) SabitleTik?.Invoke();
        else if (Bul(e.Location) is { } s) Secildi?.Invoke(s);
    }
}

// Oyundayken de görünen parti daveti. Odağı çalmaz, 30 saniye sonra kendiliğinden kapanır.
public class DavetPenceresi : Form
{
    private readonly Profil _davetEden;
    private readonly Image? _resim;
    private readonly System.Windows.Forms.Timer _sure = new() { Interval = 30000 };
    private readonly Dugme _katil = new() { Text = "Katıl" };
    private readonly Dugme _reddet = new() { Text = "Reddet", Tur = Dugme.Turler.Ikincil };
    private bool _cevaplandi;

    public event Action? Katil;
    public event Action? Reddet;

    public DavetPenceresi(Profil davetEden, Image? resim, int sira)
    {
        _davetEden = davetEden;
        _resim = resim;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Tema.Yuzey;
        DoubleBuffered = true;
        Size = new Size(Tema.S(340), Tema.S(124));
        var ekran = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(ekran.Right - Width - Tema.S(16), ekran.Bottom - (Height + Tema.S(12)) * (sira + 1));

        _katil.Bounds = new Rectangle(Tema.S(72), Tema.S(72), Tema.S(118), Tema.S(36));
        _reddet.Bounds = new Rectangle(Tema.S(200), Tema.S(72), Tema.S(118), Tema.S(36));
        _katil.Click += (_, _) => { _cevaplandi = true; Katil?.Invoke(); Close(); };
        _reddet.Click += (_, _) => Close();
        Controls.AddRange([_katil, _reddet]);
        _sure.Tick += (_, _) => Close();
        _sure.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x8 /*TOPMOST*/ | 0x08000000 /*NOACTIVATE*/;
            return cp;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _sure.Dispose();
        if (!_cevaplandi) Reddet?.Invoke();
        BeginInvoke(Dispose);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using (var p = new Pen(Tema.Vurgu)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        Cizim.Avatar(g, new RectangleF(Tema.S(16), Tema.S(18), Tema.S(44), Tema.S(44)), _davetEden, _resim, BackColor, false);
        TextRenderer.DrawText(g, _davetEden.GorunenAd, Tema.Yazi(11, kalin: true), new Rectangle(Tema.S(72), Tema.S(18), Width - Tema.S(88), Tema.S(22)), Tema.Metin, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "seni partiye davet etti", Tema.Yazi(9.5f), new Point(Tema.S(72), Tema.S(42)), Tema.MetinIkincil, TextFormatFlags.NoPadding);
    }
}
