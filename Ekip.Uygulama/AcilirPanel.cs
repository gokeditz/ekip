using System.Drawing.Drawing2D;

namespace Ekip.Uygulama;

// Emoji/GIF seçici gibi küçük açılır paneller için ortak taban. Panel ayrı bir pencere DEĞİL, formun içine
// eklenen bir kontrol (IcModal gibi): odak/etkinlik sorunu yok, form dışına taşamaz, arama kutusuna yazı normal çalışır.
// Dışarı tıklayınca, Esc'e basınca ya da form etkin olmaktan çıkınca kapanır; kapanınca odak eski kontrole döner.
public abstract class AcilirPanel : Control
{
    private static long _sonKapanma;
    private Control? _oncekiOdak;
    private Form? _form;
    private Filtre? _filtre;
    private bool _kapaniyor;

    // Panel dışına tıklanınca kapandı mı (az önce)? Paneli açan düğmenin tıklamasında "açık panel varsa sadece kapat"
    // davranışı için: if (AcilirPanel.YeniKapandi) return;
    public static bool YeniKapandi => Environment.TickCount64 - _sonKapanma < 300;

    public event Action? Kapandi;

    protected AcilirPanel(Size boyut)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        BackColor = Tema.Yuzey;
        Size = boyut;
        TabStop = false;
    }

    // ekranNoktasi: paneli açan düğmenin ÜST-SAĞ köşesi (ekran koordinatı). Panel önce o noktanın üstüne, sağ kenarı
    // o noktada olacak şekilde yerleşir; yukarıda yer yoksa altına açılır. Her durumda form içine kıstırılır.
    public void Goster(Control sahip, Point ekranNoktasi)
    {
        var form = sahip.FindForm() ?? throw new InvalidOperationException("Sahip kontrol bir forma bağlı olmalı.");
        _form = form;
        _oncekiOdak = form.ActiveControl;

        int bosluk = Tema.S(8);
        var kap = form.ClientSize;
        int w = Math.Min(Width, Math.Max(Tema.S(200), kap.Width - bosluk * 2));
        int h = Math.Min(Height, Math.Max(Tema.S(200), kap.Height - bosluk * 2));
        var p = form.PointToClient(ekranNoktasi);
        int sol = p.X - w, ust = p.Y - bosluk - h;
        if (ust < bosluk) ust = p.Y + bosluk; // yukarıda sığmıyor: aşağı aç
        sol = Math.Clamp(sol, bosluk, Math.Max(bosluk, kap.Width - w - bosluk));
        ust = Math.Clamp(ust, bosluk, Math.Max(bosluk, kap.Height - h - bosluk));
        Bounds = new Rectangle(sol, ust, w, h);

        form.Controls.Add(this);
        BringToFront();
        _filtre = new Filtre(this);
        Application.AddMessageFilter(_filtre);
        form.Deactivate += FormPasif;
        form.FormClosing += FormKapaniyor;
        form.Resize += FormBoyut;
        Acildi();
    }

    public void Kapat()
    {
        if (_kapaniyor || IsDisposed) return;
        _kapaniyor = true;
        _sonKapanma = Environment.TickCount64;
        if (_filtre != null) Application.RemoveMessageFilter(_filtre);
        _filtre = null;
        var form = _form;
        if (form != null)
        {
            form.Deactivate -= FormPasif;
            form.FormClosing -= FormKapaniyor;
            form.Resize -= FormBoyut;
            form.Controls.Remove(this);
        }
        Kapandi?.Invoke();
        // Odağı eski yerine ver (sohbet kutusu): yazmaya devam edilebilsin.
        if (form != null && !form.IsDisposed && _oncekiOdak is { IsDisposed: false, Visible: true } o) { try { o.Focus(); } catch { } }
        Dispose();
    }

    // Açıldıktan sonra çağrılır: arama kutusuna odak vermek için.
    protected virtual void Acildi() { }

    // Panelin üzerindeyken fare tekerleği (filtre iletir; odak arama kutusunda olsa bile çalışsın).
    protected virtual void Tekerlek(int delta) { }

    // İçeriği çiz (zemin ve kenarlık taban sınıfta).
    protected abstract void IcCiz(Graphics g);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Tema.Yuzey);
        IcCiz(g);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var kalem = new Pen(Tema.Kenar);
        using var yol = Tema.YuvarlakDikdortgen(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Tema.S(8));
        g.DrawPath(kalem, yol);
    }

    // Köşeler yuvarlak: bölge ile kırp.
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var yol = Tema.YuvarlakDikdortgen(new RectangleF(0, 0, Width, Height), Tema.S(8));
        var eski = Region;
        Region = new Region(yol);
        eski?.Dispose();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Kapat(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void FormPasif(object? s, EventArgs e) => Kapat();
    private void FormKapaniyor(object? s, FormClosingEventArgs e) => Kapat();
    private void FormBoyut(object? s, EventArgs e) => Kapat();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_filtre != null) Application.RemoveMessageFilter(_filtre);
            _filtre = null;
            Region?.Dispose();
        }
        base.Dispose(disposing);
    }

    // Panel dışındaki tıklamaları yakalar (kapat) ve panelin üstündeki tekerleği panele iletir.
    private sealed class Filtre(AcilirPanel panel) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message m)
        {
            if (panel.IsDisposed) return false;
            switch (m.Msg)
            {
                case 0x201 or 0x204 or 0x207 or 0xA1 or 0xA4: // sol/sağ/orta tık, başlık/çerçeve tıkı
                {
                    var c = Control.FromChildHandle(m.HWnd);
                    if (c == null || !(c == panel || panel.Contains(c))) panel.Kapat();
                    break;
                }
                case 0x20A: // WM_MOUSEWHEEL
                {
                    if (panel.RectangleToScreen(panel.ClientRectangle).Contains(Control.MousePosition))
                    {
                        int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                        panel.Tekerlek(delta);
                        return true;
                    }
                    break;
                }
            }
            return false;
        }
    }
}

// Paneller için yuvarlak köşeli arama kutusu (büyüteç + yazı + temizle düğmesi).
public sealed class AramaKutusu : Control
{
    private readonly TextBox _kutu;
    private bool _temizleUzerinde;

    public event Action? MetinDegisti;

    public AramaKutusu(string ipucu)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _kutu = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Tema.Giris,
            ForeColor = Tema.Metin,
            Font = Tema.Yazi(10.5f),
            PlaceholderText = ipucu,
            MaxLength = 60,
        };
        _kutu.TextChanged += (_, _) => { Invalidate(); MetinDegisti?.Invoke(); };
        _kutu.GotFocus += (_, _) => Invalidate();
        _kutu.LostFocus += (_, _) => Invalidate();
        Controls.Add(_kutu);
        Height = Tema.S(36);
        Cursor = Cursors.IBeam;
    }

    public string Metin { get => _kutu.Text; set => _kutu.Text = value; }
    public TextBox IcKutu => _kutu;
    public void Odakla() => _kutu.Focus();

    private Rectangle TemizleAlani => new(Width - Tema.S(30), (Height - Tema.S(22)) / 2, Tema.S(22), Tema.S(22));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int sol = Tema.S(34), sag = Tema.S(34);
        _kutu.Width = Math.Max(Tema.S(20), Width - sol - sag);
        _kutu.Location = new Point(sol, (Height - _kutu.Height) / 2);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_kutu.Text.Length > 0 && TemizleAlani.Contains(e.Location)) _kutu.Clear();
        _kutu.Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool u = _kutu.Text.Length > 0 && TemizleAlani.Contains(e.Location);
        if (u != _temizleUzerinde) { _temizleUzerinde = u; Cursor = u ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_temizleUzerinde) { _temizleUzerinde = false; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Tema.Yuzey);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool odak = _kutu.Focused;
        using (var yol = Tema.YuvarlakDikdortgen(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Tema.S(8)))
        using (var firca = new SolidBrush(Tema.Giris))
        using (var kalem = new Pen(odak ? Tema.Vurgu : Tema.Kenar, odak ? Tema.S(2) * 0.75f : 1))
        {
            g.FillPath(firca, yol);
            g.DrawPath(kalem, yol);
        }
        TextRenderer.DrawText(g, Glif.Ara, Cizim.Ikon(10), new Rectangle(Tema.S(8), 0, Tema.S(24), Height), odak ? Tema.MetinIkincil : Tema.MetinSoluk,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (_kutu.Text.Length > 0)
        {
            var t = TemizleAlani;
            if (_temizleUzerinde) Cizim.YuvarlakDoldur(g, t, t.Height / 2f, Tema.Kenar);
            TextRenderer.DrawText(g, Glif.Kapat, Cizim.Ikon(7.5f), t, _temizleUzerinde ? Tema.Metin : Tema.MetinIkincil,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
