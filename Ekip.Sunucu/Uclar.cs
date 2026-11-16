using System.Security.Claims;
using Ekip.Ortak;

namespace Ekip.Sunucu;

// Profil ve arkadaşlık HTTP uçları.
public static class Uclar
{
    public static void ProfilUclari(this WebApplication app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();

        g.MapGet("/ben", (ClaimsPrincipal u, Veritabani db) =>
            db.ProfilGetir(u.Id()) is { } p ? Results.Ok(p) : Results.Unauthorized());

        g.MapPut("/profil", async (ProfilGuncelleme p, ClaimsPrincipal u, Veritabani db, Bildirim bildirim) =>
        {
            if (Kurallar.ProfilKontrol(p) is { } hata) return Results.BadRequest(new HataYaniti(hata));
            db.ProfilGuncelle(u.Id(), p);
            await bildirim.ProfilDegisti(u.Id());
            return Results.Ok(db.ProfilGetir(u.Id()));
        });

        g.MapPut("/profil/avatar", async (HttpRequest istek, ClaimsPrincipal u, Veritabani db, Bildirim bildirim) =>
        {
            if (istek.ContentLength is > Kurallar.AvatarMaxBayt) return Results.BadRequest(new HataYaniti("Resim çok büyük."));
            using var ms = new MemoryStream();
            var tampon = new byte[16 * 1024];
            int okunan;
            while ((okunan = await istek.Body.ReadAsync(tampon)) > 0)
            {
                ms.Write(tampon, 0, okunan);
                if (ms.Length > Kurallar.AvatarMaxBayt) return Results.BadRequest(new HataYaniti("Resim çok büyük."));
            }
            var veri = ms.ToArray();
            if (ResimTuru(veri) is null) return Results.BadRequest(new HataYaniti("Sadece PNG veya JPEG yüklenebilir."));

            db.AvatarAyarla(u.Id(), veri);
            await bildirim.ProfilDegisti(u.Id());
            return Results.Ok(db.ProfilGetir(u.Id()));
        });

        g.MapDelete("/profil/avatar", async (ClaimsPrincipal u, Veritabani db, Bildirim bildirim) =>
        {
            db.AvatarAyarla(u.Id(), null);
            await bildirim.ProfilDegisti(u.Id());
            return Results.Ok(db.ProfilGetir(u.Id()));
        });

        g.MapGet("/avatar/{id:long}", (long id, Veritabani db, HttpResponse yanit) =>
        {
            var veri = db.AvatarGetir(id);
            if (veri is null || ResimTuru(veri) is not { } tur) return Results.NotFound();
            yanit.Headers.CacheControl = "private, max-age=86400";
            return Results.File(veri, tur);
        });
    }

    public static void ArkadasUclari(this WebApplication app)
    {
        var g = app.MapGroup("/api/arkadaslar").RequireAuthorization();

        g.MapGet("/", (ClaimsPrincipal u, Veritabani db, Varlik varlik) =>
        {
            var (a, gelen, giden) = db.ArkadaslariGetir(u.Id());
            return new ArkadasListesi(
                a.Select(varlik.Baskasina).ToList(),
                gelen.Select(p => p with { Durum = Durum.Cevrimdisi }).ToList(), // Arkadaş olmayanın durumu gizli.
                giden.Select(p => p with { Durum = Durum.Cevrimdisi }).ToList());
        });

        g.MapPost("/istek", async (ArkadasIstegi i, ClaimsPrincipal u, Veritabani db, Bildirim bildirim) =>
        {
            var ben = u.Id();
            var ad = (i.KullaniciAdi ?? "").Trim().TrimStart('@');
            if (ad.Length == 0) return Results.BadRequest(new HataYaniti("Bir kullanıcı adı yaz."));

            if (db.KullaniciIdBul(ad) is not { } o) return Results.NotFound(new HataYaniti($"\"{ad}\" adında biri yok. Yazımı kontrol et."));
            if (o == ben) return Results.BadRequest(new HataYaniti("Kendine istek gönderemezsin 🙂"));

            switch (db.IliskiGetir(ben, o))
            {
                case Veritabani.Iliski.Arkadas:
                    return Results.Conflict(new HataYaniti("Zaten arkadaşsınız."));
                case Veritabani.Iliski.BenGonderdim:
                    return Results.Conflict(new HataYaniti("Bu kişiye zaten istek gönderdin."));
                case Veritabani.Iliski.OGonderdi:
                    // O da bana istek atmış: direkt arkadaş olalım.
                    db.IstekKabulEt(ben, o);
                    await bildirim.ArkadaslarDegisti(ben, o);
                    return Results.Ok(new ArkadasIstegiYaniti("O da sana istek göndermişti, artık arkadaşsınız!", true));
                default:
                    db.IstekEkle(ben, o);
                    await bildirim.ArkadaslarDegisti(ben, o);
                    var adi = db.ProfilGetir(o)?.GorunenAd ?? ad;
                    return Results.Ok(new ArkadasIstegiYaniti($"İstek gönderildi. {adi} kabul edince arkadaş olacaksınız.", false));
            }
        }).RequireRateLimiting("istek");

        g.MapPost("/{id:long}/kabul", async (long id, ClaimsPrincipal u, Veritabani db, Bildirim bildirim) =>
        {
            if (!db.IstekKabulEt(u.Id(), id)) return Results.NotFound(new HataYaniti("Bu istek artık geçerli değil."));
            await bildirim.ArkadaslarDegisti(u.Id(), id);
            return Results.Ok();
        });

        // Arkadaşlıktan çıkar / isteği reddet / gönderdiğim isteği geri çek
        g.MapDelete("/{id:long}", async (long id, ClaimsPrincipal u, Veritabani db, Bildirim bildirim) =>
        {
            if (db.IliskiSil(u.Id(), id)) await bildirim.ArkadaslarDegisti(u.Id(), id);
            return Results.Ok();
        });
    }

    public static void MesajUclari(this WebApplication app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();
        var arkadasDegil = new HataYaniti("Sadece arkadaşlarınla mesajlaşabilirsin.");

        g.MapGet("/sohbetler", (ClaimsPrincipal u, Veritabani db, Varlik varlik) =>
            db.SohbetOzetleri(u.Id()).Select(s => new SohbetOzeti(varlik.Baskasina(s.karsi), s.son, s.okunmamis)).ToList());

        g.MapGet("/mesajlar/{id:long}", (long id, long? once, ClaimsPrincipal u, Veritabani db) =>
            db.IliskiGetir(u.Id(), id) != Veritabani.Iliski.Arkadas
                ? Results.Json(arkadasDegil, statusCode: 403)
                : Results.Ok(db.Mesajlar(u.Id(), id, once, Kurallar.MesajSayfasi)));

        g.MapPost("/mesajlar/{id:long}", async (long id, MesajGonderme i, ClaimsPrincipal u, Veritabani db, Bildirim bildirim) =>
        {
            var metin = (i.Metin ?? "").Trim();
            if (metin.Length == 0) return Results.BadRequest(new HataYaniti("Boş mesaj gönderilemez."));
            if (metin.Length > Kurallar.MesajMax) return Results.BadRequest(new HataYaniti($"Mesaj en fazla {Kurallar.MesajMax} karakter olabilir."));
            if (db.IliskiGetir(u.Id(), id) != Veritabani.Iliski.Arkadas) return Results.Json(arkadasDegil, statusCode: 403);

            var m = db.MesajEkle(u.Id(), id, metin) with { IstemciId = i.IstemciId?.Length <= 64 ? i.IstemciId : null };
            await bildirim.MesajGonder(m);
            return Results.Ok(m);
        }).RequireRateLimiting("mesaj");

        g.MapPost("/mesajlar/{id:long}/okundu", async (long id, ClaimsPrincipal u, Veritabani db, Bildirim bildirim) =>
        {
            if (db.OkunduIsaretle(u.Id(), id) > 0) await bildirim.Kendine(u.Id(), HubOlaylari.SohbetOkundu, id);
            return Results.Ok();
        });
    }

    private static string? ResimTuru(byte[] v) =>
        v.Length > 8 && v[0] == 0x89 && v[1] == 0x50 && v[2] == 0x4E && v[3] == 0x47 ? "image/png"
        : v.Length > 3 && v[0] == 0xFF && v[1] == 0xD8 && v[2] == 0xFF ? "image/jpeg"
        : null;
}
