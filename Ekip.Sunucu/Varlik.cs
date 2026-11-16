using System.Collections.Concurrent;
using System.Security.Claims;
using Ekip.Ortak;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ekip.Sunucu;

// Kim çevrimiçi? Bir kullanıcının birden fazla bağlantısı olabilir (iki bilgisayar), o yüzden sayıyoruz.
public class Varlik
{
    private readonly ConcurrentDictionary<long, int> _baglantilar = new();

    public bool Baglandi(long id) => _baglantilar.AddOrUpdate(id, 1, (_, n) => n + 1) == 1;

    public bool Ayrildi(long id)
    {
        while (_baglantilar.TryGetValue(id, out var n))
        {
            if (n <= 1) { if (_baglantilar.TryRemove(new(id, n))) return true; }
            else if (_baglantilar.TryUpdate(id, n - 1, n)) return false;
        }
        return false;
    }

    public bool Cevrimici(long id) => _baglantilar.ContainsKey(id);

    // Başkalarının göreceği durum: bağlı değilse ya da görünmezse çevrimdışı.
    public Durum Gorunen(long id, Durum secilen) =>
        !Cevrimici(id) || secilen == Durum.Gorunmez ? Durum.Cevrimdisi : secilen;

    public Profil Baskasina(Profil p) => p with { Durum = Gorunen(p.Id, p.Durum) };
}

// Uygulamalar buraya bağlanır; sunucudan anlık olaylar bu yolla gider.
[Authorize]
public class EkipHub(Veritabani db, Varlik varlik, Bildirim bildirim, PartiYoneticisi parti) : Hub
{
    private long Ben => Context.User!.Id();

    public override async Task OnConnectedAsync()
    {
        if (varlik.Baglandi(Ben))
        {
            var p = db.ProfilGetir(Ben);
            if (p != null && p.Durum != Durum.Gorunmez) await bildirim.DurumYay(Ben, p.Durum);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (varlik.Ayrildi(Ben))
        {
            await bildirim.DurumYay(Ben, Durum.Cevrimdisi);
            await PartiGuncellemesiGonder(parti.Ayril(Ben)); // Tamamen çıkınca partiden de düşer.
        }
        await base.OnDisconnectedAsync(exception);
    }

    // ---- Parti ----

    private async Task PartiGuncellemesiGonder(List<long> uyeler)
    {
        if (uyeler.Count == 0) return;
        await Clients.Users(uyeler.Select(x => x.ToString())).SendAsync(HubOlaylari.PartiGuncellendi, parti.Bilgisi(uyeler[0]));
    }

    public PartiDurumu? PartiDurumum() => parti.Durum(Ben);

    public PartiDurumu PartiKur() => parti.Kur(Ben);

    public async Task<string?> PartiyeDavetEt(long kimi)
    {
        var hata = parti.DavetEt(Ben, kimi, out var partiId);
        if (hata != null) return hata;
        var ben = db.ProfilGetir(Ben);
        await Clients.User(kimi.ToString()).SendAsync(HubOlaylari.PartiDaveti, partiId, varlik.Baskasina(ben!));
        return null;
    }

    public async Task<PartiDurumu?> DaveteKatil(string partiId)
    {
        var (durum, eskiUyeler) = parti.Katil(Ben, partiId);
        if (durum is null) return null;
        await PartiGuncellemesiGonder(eskiUyeler);
        // Diğer cihazlarım da dahil herkese yeni üye listesi.
        await Clients.Users(durum.Bilgi.Uyeler.Select(x => x.Id.ToString())).SendAsync(HubOlaylari.PartiGuncellendi, durum.Bilgi);
        return durum;
    }

    public void DavetiReddet(string partiId) => parti.Reddet(Ben, partiId);

    public async Task PartidenAyril()
    {
        await PartiGuncellemesiGonder(parti.Ayril(Ben));
        await Clients.User(Ben.ToString()).SendAsync(HubOlaylari.PartiGuncellendi, (PartiBilgisi?)null);
    }

    public async Task OyunSec(string? oyun)
    {
        var b = parti.OyunSec(Ben, oyun);
        if (b != null) await Clients.Users(b.Uyeler.Select(x => x.Id.ToString())).SendAsync(HubOlaylari.PartiGuncellendi, b);
    }

    public async Task PartiMesajiGonder(string metin)
    {
        var (m, alicilar) = parti.MesajEkle(Ben, metin);
        if (m != null) await Clients.Users(alicilar.Select(x => x.ToString())).SendAsync(HubOlaylari.PartiMesaji, m);
    }

    public async Task DurumAyarla(Durum durum)
    {
        if (durum is Durum.Cevrimdisi || !Enum.IsDefined(durum)) return;
        db.DurumAyarla(Ben, durum);
        await bildirim.DurumYay(Ben, varlik.Gorunen(Ben, durum));
        await bildirim.Kendine(Ben, HubOlaylari.DurumDegisti, Ben, durum); // Diğer cihazlarım da bilsin.
    }

    // "X yazıyor…" göstergesi. Uygulama bunu en fazla birkaç saniyede bir çağırır.
    public async Task Yaziyor(long karsi)
    {
        if (db.IliskiGetir(Ben, karsi) == Veritabani.Iliski.Arkadas)
            await Clients.User(karsi.ToString()).SendAsync(HubOlaylari.Yaziyor, Ben);
    }
}

// HTTP uçlarından ve hub'dan olay göndermek için tek yer.
public class Bildirim(IHubContext<EkipHub> hub, Veritabani db, Varlik varlik)
{
    public Task DurumYay(long id, Durum durum) =>
        hub.Clients.Users(db.ArkadasIdleri(id).Select(x => x.ToString())).SendAsync(HubOlaylari.DurumDegisti, id, durum);

    public Task ArkadaslarDegisti(params long[] idler) =>
        hub.Clients.Users(idler.Select(x => x.ToString())).SendAsync(HubOlaylari.ArkadaslarDegisti);

    public async Task ProfilDegisti(long id)
    {
        var p = db.ProfilGetir(id);
        if (p is null) return;
        await hub.Clients.Users(db.ArkadasIdleri(id).Select(x => x.ToString()))
            .SendAsync(HubOlaylari.ProfilDegisti, varlik.Baskasina(p));
        await hub.Clients.User(id.ToString()).SendAsync(HubOlaylari.ProfilDegisti, p);
    }

    // Alıcıya ve gönderenin kendi (diğer) cihazlarına.
    public Task MesajGonder(Mesaj m) =>
        hub.Clients.Users([m.GonderenId.ToString(), m.AliciId.ToString()]).SendAsync(HubOlaylari.MesajGeldi, m);

    public Task Kendine(long id, string olay, params object[] args) =>
        hub.Clients.User(id.ToString()).SendCoreAsync(olay, args);
}

public static class KullaniciUzantisi
{
    public static long Id(this ClaimsPrincipal u) => long.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
