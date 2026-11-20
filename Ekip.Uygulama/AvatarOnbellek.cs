using Ekip.Ortak;

namespace Ekip.Uygulama;

// Profil resimleri bir kere indirilir, sürüm değişene kadar bellekte tutulur.
public class AvatarOnbellek(Api api)
{
    private readonly Dictionary<long, (int surum, Image? resim)> _resimler = [];
    private readonly HashSet<(long, int)> _iniyor = [];

    public event Action? Yuklendi;

    public Image? Al(Profil p)
    {
        if (p.AvatarSurum == 0) return null;
        if (_resimler.TryGetValue(p.Id, out var k) && k.surum == p.AvatarSurum) return k.resim;
        if (_iniyor.Add((p.Id, p.AvatarSurum))) _ = Indir(p.Id, p.AvatarSurum);
        return _resimler.TryGetValue(p.Id, out k) ? k.resim : null; // Yenisi gelene kadar eskisi görünsün.
    }

    // Kendi yüklediğimiz resmi tekrar indirmeye gerek yok.
    public void Koy(long id, int surum, Image? resim)
    {
        if (_resimler.TryGetValue(id, out var eski) && eski.resim != resim) eski.resim?.Dispose();
        _resimler[id] = (surum, resim);
        Yuklendi?.Invoke();
    }

    private async Task Indir(long id, int surum)
    {
        var veri = await api.AvatarIndir(id, surum);
        _iniyor.Remove((id, surum));
        // Bozuk/eksik resim olursa null saklarız; baş harfli avatar gösterilir ve tekrar tekrar indirilmez.
        Koy(id, surum, veri is null ? null : Cizim.ResimCoz(veri));
    }
}
