using Ekip.Ortak;
using Ekip.Uygulama;

namespace Ekip.Mobil;

// Açık oturum: sunucu bağlantısı (Api + Canli), ben ve sık kullanılan listeler. Uygulama boyunca tek tane.
public sealed class Oturum
{
    public const string VarsayilanSunucu = "https://ekip.ekip-bulut.workers.dev";

    public static Oturum? Aktif { get; private set; }

    public Api Api { get; }
    public Canli Canli { get; }
    public Profil Ben { get; set; }
    public ArkadasListesi Arkadaslar { get; set; } = new([], [], []);

    private readonly Dictionary<(long, int), ImageSource?> _avatarlar = [];
    private readonly Dictionary<(long, int), Task<ImageSource?>> _iniyor = [];

    private Oturum(Api api, Profil ben)
    {
        Api = api;
        Ben = ben;
        Canli = new Canli(api); // Ana iş parçacığında oluşturulmalı (olaylar arayüz iş parçacığına gelir).
        Canli.ArkadaslarDegisti += async () => await ArkadaslariYenile();
        Canli.ProfilDegisti += p => { if (p.Id == Ben.Id) Ben = p; };
        Canli.OturumSonlandi += async _ => await Cikis();
    }

    // ---- Kayıtlı oturum (telefonun şifreli deposu) ----

    public static string SunucuAdresi
    {
        get => Preferences.Get("sunucu", VarsayilanSunucu);
        set => Preferences.Set("sunucu", value);
    }

    public static Task<string?> KayitliToken() => SecureStorage.GetAsync("token");

    public static async Task<Oturum> Baslat(Api api, Profil ben, bool kaydet)
    {
        if (kaydet && api.Token is { } t) await SecureStorage.SetAsync("token", t);
        Aktif = new Oturum(api, ben);
        _ = Aktif.Canli.Baslat();
        _ = Aktif.ArkadaslariYenile();
        return Aktif;
    }

    public async Task Cikis()
    {
        try { await Api.CikisYap(); } catch { }
        SecureStorage.Remove("token");
        await Canli.DisposeAsync();
        Aktif = null;
        if (Application.Current?.Windows.FirstOrDefault() is { } w) w.Page = new GirisSayfasi();
    }

    public async Task ArkadaslariYenile()
    {
        var s = await Api.Arkadaslar();
        if (s.Basarili) { Arkadaslar = s.Veri!; ArkadaslarYenilendi?.Invoke(); }
    }

    public event Action? ArkadaslarYenilendi;

    public Profil? ProfilBul(long id) =>
        id == Ben.Id ? Ben : Arkadaslar.Arkadaslar.FirstOrDefault(p => p.Id == id);

    // ---- Profil resimleri (sürüm değişene kadar bellekte) ----

    public Task<ImageSource?> Avatar(Profil p)
    {
        if (p.AvatarSurum == 0) return Task.FromResult<ImageSource?>(null);
        var k = (p.Id, p.AvatarSurum);
        if (_avatarlar.TryGetValue(k, out var hazir)) return Task.FromResult(hazir);
        if (_iniyor.TryGetValue(k, out var t)) return t;
        t = Indir(k);
        _iniyor[k] = t;
        return t;
    }

    private async Task<ImageSource?> Indir((long id, int surum) k)
    {
        var veri = await Api.AvatarIndir(k.id, k.surum);
        ImageSource? r = veri is null ? null : ImageSource.FromStream(() => new MemoryStream(veri));
        _avatarlar[k] = r;
        _iniyor.Remove(k);
        return r;
    }
}
