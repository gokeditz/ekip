using System.Security.Claims;
using System.Threading.RateLimiting;
using Ekip.Ortak;
using Ekip.Sunucu;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

var dbYolu = builder.Configuration["VeritabaniYolu"] ?? Path.Combine(AppContext.BaseDirectory, "veri", "ekip.db");
var veritabani = new Veritabani(dbYolu);
builder.Services.AddSingleton(veritabani);

// "--AdminYap kullaniciadi" ile başlatınca o kullanıcı admin olur (ör. ilk admin hesabını kaybedersen).
if (builder.Configuration["AdminYap"] is { Length: > 0 } adminYap)
    Console.WriteLine(veritabani.AdminYapKullaniciAdiyla(adminYap) ? $"{adminYap} artık admin." : $"{adminYap} adında kullanıcı yok.");

builder.Services.AddAuthentication(TokenDogrulama.Sema)
    .AddScheme<AuthenticationSchemeOptions, TokenDogrulama>(TokenDogrulama.Sema, null);
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireClaim("admin", "1")));

// Sunucunun önünde Caddy/Nginx (HTTPS) olunca gerçek istemci IP'si buradan gelir; deneme sınırı doğru kişiye uygulansın.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto);

// Şifre deneme saldırılarına karşı: IP başına dakikada 10 giriş/kayıt denemesi (test için "HesapLimiti" ile değişir).
var hesapLimiti = builder.Configuration.GetValue("HesapLimiti", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("hesap", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = hesapLimiti, Window = TimeSpan.FromMinutes(1) }));
    // Arkadaşlık isteği spam'ine karşı: kullanıcı başına dakikada 20.
    o.AddPolicy("istek", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    // Mesaj spam'ine karşı: kullanıcı başına 10 saniyede 20 mesaj.
    o.AddPolicy("mesaj", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromSeconds(10) }));
});

builder.Services.AddSingleton<Varlik>();
builder.Services.AddSingleton<Bildirim>();
builder.Services.AddSingleton<PartiYoneticisi>();
builder.Services.AddSignalR();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/", () => "Ekip sunucusu çalışıyor.");

var hesap = app.MapGroup("/api").RequireRateLimiting("hesap");

hesap.MapPost("/kayit", (KayitIstegi i, Veritabani db) =>
{
    var hata = Kurallar.KullaniciAdiKontrol(i.KullaniciAdi)
               ?? Kurallar.GorunenAdKontrol(i.GorunenAd)
               ?? Kurallar.SifreKontrol(i.Sifre);
    if (hata is not null) return Results.BadRequest(new HataYaniti(hata));

    var s = db.KayitOl(i.KullaniciAdi, i.GorunenAd.Trim(), i.Sifre, i.Kod);
    if (s.Kullanici is null)
        return Results.Json(new HataYaniti(s.Hata!, s.Alan), statusCode: s.Alan == "kullaniciAdi" ? 409 : 400);

    return Results.Ok(new OturumYaniti(db.OturumAc(s.Kullanici.Id), s.Kullanici));
});

hesap.MapPost("/giris", (GirisIstegi i, Veritabani db) =>
{
    if (string.IsNullOrWhiteSpace(i.KullaniciAdi) || string.IsNullOrEmpty(i.Sifre) || i.Sifre.Length > Kurallar.SifreMax)
        return Results.BadRequest(new HataYaniti("Kullanıcı adı ve şifre gerekli."));

    var k = db.GirisDogrula(i.KullaniciAdi, i.Sifre, out bool yasakli);
    if (yasakli) return Results.Json(new HataYaniti("Bu hesap yasaklandı."), statusCode: 403);
    if (k is null) return Results.Json(new HataYaniti("Kullanıcı adı veya şifre yanlış."), statusCode: 401);

    return Results.Ok(new OturumYaniti(db.OturumAc(k.Id), k));
});

app.ProfilUclari();
app.ArkadasUclari();
app.MesajUclari();
app.YonetimUclari();
app.MapHub<EkipHub>("/hub");

app.MapPost("/api/cikis", (HttpRequest r, Veritabani db) =>
{
    db.OturumKapat(TokenDogrulama.TokenAl(r)!);
    return Results.Ok();
}).RequireAuthorization();

app.Run();
