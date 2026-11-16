using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ekip.Sunucu;

// "Authorization: Bearer <token>" başlığını (ya da SignalR için ?access_token=) okuyup kullanıcıyı bulur.
public class TokenDogrulama(
    IOptionsMonitor<AuthenticationSchemeOptions> secenekler,
    ILoggerFactory logger,
    UrlEncoder encoder,
    Veritabani db) : AuthenticationHandler<AuthenticationSchemeOptions>(secenekler, logger, encoder)
{
    public const string Sema = "EkipToken";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = TokenAl(Request);
        if (token is null) return Task.FromResult(AuthenticateResult.NoResult());

        var oturum = db.OturumdanKullanici(token);
        if (oturum is null) return Task.FromResult(AuthenticateResult.Fail("Geçersiz oturum"));
        var (k, admin) = oturum.Value;

        var kimlik = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, k.Id.ToString()),
            new Claim(ClaimTypes.Name, k.KullaniciAdi),
            new Claim("gorunen_ad", k.GorunenAd),
            new Claim("admin", admin ? "1" : "0"),
        ], Sema);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(kimlik), Sema)));
    }

    public static string? TokenAl(HttpRequest istek)
    {
        var baslik = istek.Headers.Authorization.ToString();
        if (baslik.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return baslik["Bearer ".Length..].Trim();
        if (istek.Path.StartsWithSegments("/hub") && istek.Query.TryGetValue("access_token", out var q))
            return q.ToString();
        return null;
    }
}
