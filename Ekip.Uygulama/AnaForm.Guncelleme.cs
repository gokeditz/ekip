namespace Ekip.Uygulama;

// Otomatik güncelleme: açılıştan biraz sonra ve 30 dakikada bir yeni sürüm var mı bakılır.
// Pencere açıksa uygulama içinde sorulur; tepsideyse balon bildirimi çıkar, pencere açılınca sorulur.
public partial class AnaForm
{
    private readonly System.Windows.Forms.Timer _guncellemeZamanlayici = new() { Interval = 30 * 60 * 1000 };
    private Guncelleyici.SurumBilgisi? _bekleyenGuncelleme;
    private string? _reddedilenSurum;
    private bool _guncellemeSoruluyor, _guncelleniyor;

    private void GuncellemeyiBaslat()
    {
        if (string.IsNullOrWhiteSpace(Ayarlar.GuncellemeAdresi)) return;
        _guncellemeZamanlayici.Tick += async (_, _) => await GuncellemeKontrol();
        _guncellemeZamanlayici.Start();
        // Açılışta hemen değil, giriş/bağlantı trafiği bitince bak.
        var ilk = new System.Windows.Forms.Timer { Interval = 5000 };
        ilk.Tick += async (_, _) => { ilk.Stop(); ilk.Dispose(); await GuncellemeKontrol(); };
        ilk.Start();
    }

    private async Task GuncellemeKontrol()
    {
        if (_guncelleniyor || IsDisposed) return;
        var b = await Guncelleyici.Kontrol(Ayarlar.GuncellemeAdresi);
        if (b is null || IsDisposed || b.Surum == _reddedilenSurum) return;
        _bekleyenGuncelleme = b;
        if (Visible && WindowState != FormWindowState.Minimized) await GuncellemeSor();
        else _tepsi?.ShowBalloonTip(5000, "Ekip güncellemesi hazır",
            $"Sürüm {b.Surum} çıktı. Güncellemek için Ekip'i aç.", ToolTipIcon.None);
    }

    // Pencere tepsiden açılınca bekleyen güncelleme varsa sor.
    private void BekleyenGuncellemeyiSor()
    {
        if (_bekleyenGuncelleme != null) BeginInvoke(async () => await GuncellemeSor());
    }

    private async Task GuncellemeSor()
    {
        if (_bekleyenGuncelleme is not { } b || _guncellemeSoruluyor || _guncelleniyor) return;
        _guncellemeSoruluyor = true;
        string notlar = string.IsNullOrWhiteSpace(b.Notlar) ? "" : "\n\n" + b.Notlar.Trim();
        bool evet = await OnayKarti.Sor(this, $"Yeni sürüm hazır: {b.Surum}",
            $"Şu an {Guncelleyici.MevcutSurumYazisi} sürümünü kullanıyorsun. Güncelleme birkaç saniye sürer, Ekip kendiliğinden yeniden açılır.{notlar}",
            "Şimdi güncelle", tehlike: false);
        _guncellemeSoruluyor = false;
        if (!evet)
        {
            _reddedilenSurum = b.Surum; // Bu oturumda aynı sürüm için tekrar rahatsız etme.
            _bekleyenGuncelleme = null;
            Bildir("Güncellemeyi Ekip'i bir dahaki açışında yapabilirsin.", hata: false);
            return;
        }
        await GuncellemeyiUygula(b);
    }

    private async Task GuncellemeyiUygula(Guncelleyici.SurumBilgisi b)
    {
        _guncelleniyor = true;
        int sonYuzde = -1;
        Bildir("Güncelleme indiriliyor…", hata: false);
        var ilerleme = new Progress<int>(y =>
        {
            if (y == sonYuzde || IsDisposed) return;
            sonYuzde = y;
            Bildir($"Güncelleme indiriliyor… %{y}", hata: false);
        });
        var hata = await Guncelleyici.Uygula(b, ilerleme, () =>
        {
            // Yeni sürüm açılmadan önce: kısayol kancasını, overlay'i ve tepsi simgesini bırak.
            _ayarlar.Kaydet();
            _gercektenKapat = true;
            PartiTemizle();
        });
        // Buraya sadece hata olursa gelinir (başarılıysa uygulama yeniden başlar).
        _guncelleniyor = false;
        _bekleyenGuncelleme = null;
        if (hata != null && !IsDisposed) Bildir(hata, hata: true);
    }
}
