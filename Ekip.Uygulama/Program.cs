namespace Ekip.Uygulama;

static class Program
{
    private static Mutex? _kilit;                       // Süreç boyunca tutulur (tek kopya kilidi).
    public static EventWaitHandle? Goster { get; private set; } // Başka kopya "beni öne getir" der.

    [STAThread]
    static void Main(string[] args)
    {
        int i = Array.IndexOf(args, "--profil");
        if (i >= 0 && i + 1 < args.Length && args[i + 1].All(char.IsLetterOrDigit)) Ayarlar.ProfilAdi = args[i + 1];
        int g = Array.IndexOf(args, "--guncelleme");
        if (g >= 0 && g + 1 < args.Length && Uri.IsWellFormedUriString(args[g + 1], UriKind.Absolute)) Ayarlar.GuncellemeAdresi = args[g + 1];
        // Tek kopya: uygulama zaten çalışıyorsa (ör. tepside) ikinci kopya açılmaz, var olan pencere öne gelir.
        // Güncelleme sonrası yeniden açılışta ("--yeniden") eski kopyanın kapanması biraz beklenir.
        var ad = @"Local\Uygulama-" + (Ayarlar.ProfilAdi ?? "ana");
        _kilit = new Mutex(true, ad + "-kilit", out bool ilk);
        if (!ilk)
        {
            bool aldi;
            try { aldi = _kilit.WaitOne(args.Contains("--yeniden") ? 15000 : 1500); }
            catch (AbandonedMutexException) { aldi = true; }
            if (!aldi)
            {
                try { EventWaitHandle.OpenExisting(ad + "-goster").Set(); } catch { }
                return;
            }
        }
        Goster = new EventWaitHandle(false, EventResetMode.AutoReset, ad + "-goster");

        Guncelleyici.EskileriTemizle();

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Tema.OlcekBelirle();
        _ = Task.Run(EmojiVeri.Hazirla);
        Application.ThreadException += (_, e) =>
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ekip-hata.log"), $"{DateTime.Now:HH:mm:ss} {e.Exception}\n"); } catch { }
        };
        Application.Run(new UygulamaBaglami());
    }
}

// Giriş ekranı ile ana pencere arasında geçişi yönetir.
class UygulamaBaglami : ApplicationContext
{
    private readonly Ayarlar _ayarlar = Ayarlar.Yukle();
    private readonly GirisFormu _giris;
    private AnaForm? _ana;

    public UygulamaBaglami()
    {
        _giris = new GirisFormu(_ayarlar);
        GosterIsteginiDinle();
        _giris.GirisYapildi += (api, profil) =>
        {
            _ayarlar.SonProfil = profil;
            _ayarlar.Kaydet();
            var ana = _ana = new AnaForm(api, profil, _ayarlar);
            ana.CikisYapildi += () =>
            {
                _ayarlar.Token = null;
                _ayarlar.SonProfil = null;
                _ayarlar.Kaydet();
                _giris.Sifirla(ana.CikisSebebi);
                _giris.Show();
                ana.Hide();
                ana.Dispose();
            };
            ana.FormClosed += (_, _) => { if (!_giris.Visible) ExitThread(); };
            ana.Show();
            _giris.Hide();
        };
        _giris.FormClosed += (_, _) => ExitThread();
        _giris.Show();
    }

    // İkinci kez açılmaya çalışılınca: ana pencere (tepside olsa bile) ya da giriş ekranı öne gelir.
    private void GosterIsteginiDinle()
    {
        if (Program.Goster is not { } olay) return;
        var ui = SynchronizationContext.Current!;
        new Thread(() =>
        {
            while (olay.WaitOne())
                ui.Post(_ =>
                {
                    if (_ana is { IsDisposed: false } a) a.PencereyiGoster();
                    else if (!_giris.IsDisposed) { _giris.Show(); if (_giris.WindowState == FormWindowState.Minimized) _giris.WindowState = FormWindowState.Normal; _giris.Activate(); }
                }, null);
        }) { IsBackground = true, Name = "Tek kopya" }.Start();
    }
}
