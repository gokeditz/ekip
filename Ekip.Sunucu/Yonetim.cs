using System.Security.Claims;
using Ekip.Ortak;
using Microsoft.AspNetCore.SignalR;

namespace Ekip.Sunucu;

// Admin paneli uçları. Hepsi "Admin" yetkisi ister: admin olmayan 403 alır.
public static class Yonetim
{
    public static void YonetimUclari(this WebApplication app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        g.MapGet("/ozet", (Veritabani db, Varlik varlik) =>
        {
            var k = db.Kullanicilar();
            var simdi = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int aktif = db.Kodlar().Count(x => !x.Iptal && x.KalanKullanim != 0 && (x.SonKullanma is null || x.SonKullanma > simdi));
            return new YonetimOzeti(k.Count, k.Count(x => varlik.Cevrimici(x.id)), aktif);
        });

        g.MapGet("/kodlar", (Veritabani db) => db.Kodlar());

        g.MapPost("/kodlar", (KodOlusturma i, ClaimsPrincipal u, Veritabani db) =>
        {
            if (i.Kullanim is < 0 or > 1000 || i.Gun is < 0 or > 3650) return Results.BadRequest(new HataYaniti("Geçersiz kod ayarı."));
            return Results.Ok(db.KodOlustur(u.Id(), i.Kullanim, i.Gun));
        });

        g.MapDelete("/kodlar/{kod}", (string kod, Veritabani db) =>
            db.KodIptal(kod) ? Results.Ok() : Results.NotFound(new HataYaniti("Böyle bir kod yok.")));

        g.MapGet("/kullanicilar", (Veritabani db, Varlik varlik) =>
            db.Kullanicilar().Select(x => new YonetimKullanicisi(x.id, x.ad, x.gorunen, x.renk, x.avatar, x.kayit,
                x.admin, x.yasakli, varlik.Cevrimici(x.id), x.kod)).ToList());

        g.MapPost("/kullanicilar/{id:long}/admin", (long id, DegerAyari i, ClaimsPrincipal u, Veritabani db) =>
        {
            if (id == u.Id() && !i.Deger) return Results.BadRequest(new HataYaniti("Kendi adminliğini kaldıramazsın."));
            db.AdminAyarla(id, i.Deger);
            return Results.Ok();
        });

        g.MapPost("/kullanicilar/{id:long}/yasak", async (long id, DegerAyari i, ClaimsPrincipal u, Veritabani db, IHubContext<EkipHub> hub) =>
        {
            if (id == u.Id()) return Results.BadRequest(new HataYaniti("Kendini yasaklayamazsın."));
            if (i.Deger && db.AdminMi(id)) return Results.BadRequest(new HataYaniti("Önce adminliğini kaldır, sonra yasakla."));
            db.YasakAyarla(id, i.Deger);
            // Açık uygulaması varsa hemen çıkış yaptır (oturumları zaten silindi).
            if (i.Deger) await hub.Clients.User(id.ToString()).SendAsync(HubOlaylari.OturumSonlandi, "Hesabın yasaklandı.");
            return Results.Ok();
        });
    }
}
