namespace Ekip.Uygulama;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        int i = Array.IndexOf(args, "--profil");
        if (i >= 0 && i + 1 < args.Length && args[i + 1].All(char.IsLetterOrDigit)) Ayarlar.ProfilAdi = args[i + 1];
        int g = Array.IndexOf(args, "--guncelleme");
        if (g >= 0 && g + 1 < args.Length && Uri.IsWellFormedUriString(args[g + 1], UriKind.Absolute)) Ayarlar.GuncellemeAdresi = args[g + 1];
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

    public UygulamaBaglami()
    {
        _giris = new GirisFormu(_ayarlar);
        _giris.GirisYapildi += (api, profil) =>
        {
            var ana = new AnaForm(api, profil, _ayarlar);
            ana.CikisYapildi += () =>
            {
                _ayarlar.Token = null;
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
}
