using Ekip.Ortak;
using Microsoft.AspNetCore.SignalR.Client;

namespace Ekip.Uygulama;

// Sunucuyla sürekli açık bağlantı (SignalR). Kopunca kendi kendine yeniden bağlanır.
// Bütün olaylar arayüz iş parçacığında tetiklenir, formlar doğrudan kullanabilir.
public sealed class Canli : IAsyncDisposable
{
    private readonly HubConnection _baglanti;
    private readonly SynchronizationContext _ui;
    private bool _kapandi;

    public event Action? ArkadaslarDegisti;
    public event Action<Profil>? ProfilDegisti;
    public event Action<long, Durum>? DurumDegisti;
    public event Action<bool>? BaglantiDegisti;
    public event Action<Mesaj>? MesajGeldi;
    public event Action<long>? Yaziyor;
    public event Action<long>? SohbetOkundu;
    public event Action<string, Profil>? PartiDaveti;
    public event Action<PartiBilgisi?>? PartiGuncellendi;
    public event Action<PartiMesaji>? PartiMesajiGeldi;
    public event Action<string>? OturumSonlandi;
    public event Action<Mesaj>? MesajGuncellendi;              // düzenlendi / silindi / tepki
    public event Action<KonusmaKimligi, long>? KonusmaYaziyor;  // kanal/grupta (konuşma, kim)
    public event Action<KonusmaKimligi>? KonusmaOkundu;        // kanal/grubu başka cihazımda okudum
    public event Action? SunucularDegisti;
    public event Action<long>? SunucuGuncellendi;
    public event Action? GruplarDegisti;

    public bool Bagli => _baglanti.State == HubConnectionState.Connected;

    public Canli(Api api)
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Arayüz iş parçacığında oluşturulmalı.");
        _baglanti = new HubConnectionBuilder()
            .WithUrl(api.SunucuAdresi + "/hub", o => o.AccessTokenProvider = () => Task.FromResult(api.Token))
            .WithAutomaticReconnect(new SonsuzDeneme())
            .Build();

        _baglanti.On(HubOlaylari.ArkadaslarDegisti, () => Ui(() => ArkadaslarDegisti?.Invoke()));
        _baglanti.On<Profil>(HubOlaylari.ProfilDegisti, p => Ui(() => ProfilDegisti?.Invoke(p)));
        _baglanti.On<long, Durum>(HubOlaylari.DurumDegisti, (id, d) => Ui(() => DurumDegisti?.Invoke(id, d)));
        _baglanti.On<Mesaj>(HubOlaylari.MesajGeldi, m => Ui(() => MesajGeldi?.Invoke(m)));
        _baglanti.On<long>(HubOlaylari.Yaziyor, id => Ui(() => Yaziyor?.Invoke(id)));
        _baglanti.On<long>(HubOlaylari.SohbetOkundu, id => Ui(() => SohbetOkundu?.Invoke(id)));
        _baglanti.On<string, Profil>(HubOlaylari.PartiDaveti, (id, p) => Ui(() => PartiDaveti?.Invoke(id, p)));
        _baglanti.On<PartiBilgisi?>(HubOlaylari.PartiGuncellendi, b => Ui(() => PartiGuncellendi?.Invoke(b)));
        _baglanti.On<PartiMesaji>(HubOlaylari.PartiMesaji, m => Ui(() => PartiMesajiGeldi?.Invoke(m)));
        _baglanti.On<string>(HubOlaylari.OturumSonlandi, s => Ui(() => OturumSonlandi?.Invoke(s)));
        _baglanti.On<Mesaj>(HubOlaylari.MesajGuncellendi, m => Ui(() => MesajGuncellendi?.Invoke(m)));
        _baglanti.On<string, long>(HubOlaylari.KonusmaYaziyor, (k, kim) =>
        {
            if (KonusmaKimligi.Coz(k) is { } kk) Ui(() => KonusmaYaziyor?.Invoke(kk, kim));
        });
        _baglanti.On<string>(HubOlaylari.KonusmaOkundu, k =>
        {
            if (KonusmaKimligi.Coz(k) is { } kk) Ui(() => KonusmaOkundu?.Invoke(kk));
        });
        _baglanti.On(HubOlaylari.SunucularDegisti, () => Ui(() => SunucularDegisti?.Invoke()));
        _baglanti.On<long>(HubOlaylari.SunucuGuncellendi, id => Ui(() => SunucuGuncellendi?.Invoke(id)));
        _baglanti.On(HubOlaylari.GruplarDegisti, () => Ui(() => GruplarDegisti?.Invoke()));

        _baglanti.Reconnecting += _ => { Ui(() => BaglantiDegisti?.Invoke(false)); return Task.CompletedTask; };
        _baglanti.Reconnected += _ =>
        {
            // Kopukken kaçırdığımız olaylar olabilir: listeyi tazele.
            Ui(() => { BaglantiDegisti?.Invoke(true); ArkadaslarDegisti?.Invoke(); });
            return Task.CompletedTask;
        };
        _baglanti.Closed += async _ =>
        {
            if (_kapandi) return;
            Ui(() => BaglantiDegisti?.Invoke(false));
            await Baslat();
        };
    }

    private void Ui(Action a) => _ui.Post(_ => { if (!_kapandi) a(); }, null);

    // İlk bağlantı otomatik yeniden denemeye dahil değil; sunucu kapalıysa burada bekleyip tekrar deneriz.
    public async Task Baslat()
    {
        var bekle = 1;
        while (!_kapandi)
        {
            try
            {
                await _baglanti.StartAsync();
                Ui(() => BaglantiDegisti?.Invoke(true));
                return;
            }
            catch
            {
                Ui(() => BaglantiDegisti?.Invoke(false));
                await Task.Delay(TimeSpan.FromSeconds(bekle));
                bekle = Math.Min(bekle * 2, 15);
            }
        }
    }

    public async Task DurumAyarla(Durum d)
    {
        if (Bagli) await _baglanti.InvokeAsync("DurumAyarla", d);
    }

    public async Task YaziyorBildir(long karsi)
    {
        try { if (Bagli) await _baglanti.InvokeAsync("Yaziyor", karsi); }
        catch { /* Önemsiz: gösterge gitmezse bir şey olmaz. */ }
    }

    // Kanal/grupta "yazıyor" (DM için YaziyorBildir).
    public async Task KonusmaYaziyorBildir(KonusmaKimligi k)
    {
        try { if (Bagli) await _baglanti.InvokeAsync(HubMetotlari.KonusmaYaziyor, k.ToString()); }
        catch { }
    }

    // ---- Parti ---- (bağlantı yoksa null/hata döner, arayüz bunu gösterir)

    private async Task<T?> Cagir<T>(string ad, params object?[] args)
    {
        if (!Bagli) return default;
        try { return await _baglanti.InvokeCoreAsync<T>(ad, args); }
        catch { return default; }
    }

    private async Task Gonder(string ad, params object?[] args)
    {
        try { if (Bagli) await _baglanti.InvokeCoreAsync(ad, args); }
        catch { /* Bağlantı koptuysa yeniden bağlanınca durum tazelenir. */ }
    }

    public Task<PartiDurumu?> PartiDurumum() => Cagir<PartiDurumu?>("PartiDurumum");
    public Task<PartiDurumu?> PartiKur() => Cagir<PartiDurumu?>("PartiKur");
    public async Task<string?> PartiyeDavetEt(long kimi) =>
        Bagli ? await Cagir<string?>("PartiyeDavetEt", kimi) : "Sunucuya bağlı değilsin.";
    public Task<PartiDurumu?> DaveteKatil(string partiId) => Cagir<PartiDurumu?>("DaveteKatil", partiId);
    public Task DavetiReddet(string partiId) => Gonder("DavetiReddet", partiId);
    public Task PartidenAyril() => Gonder("PartidenAyril");
    public Task OyunSec(string? oyun) => Gonder("OyunSec", oyun);
    public Task PartiMesajiGonder(string metin) => Gonder("PartiMesajiGonder", metin);

    public async ValueTask DisposeAsync()
    {
        _kapandi = true;
        await _baglanti.DisposeAsync();
    }

    private class SonsuzDeneme : IRetryPolicy
    {
        private static readonly int[] Saniyeler = [0, 2, 5, 10];
        public TimeSpan? NextRetryDelay(RetryContext c) =>
            TimeSpan.FromSeconds(c.PreviousRetryCount < Saniyeler.Length ? Saniyeler[c.PreviousRetryCount] : 15);
    }
}

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
