using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace Ekip.Uygulama;

// Bütün ağ bağlantıları buradan kurulur.
// Sebep: bazı evlerde IPv6 "var gibi görünüp" çalışmıyor. .NET önce IPv6 adresine bağlanmayı deniyor ve
// orada uzun süre takılıyor; açılışta oturum kontrolü zaman aşımına düşüp kullanıcı giriş ekranında kalıyordu.
// Burada önce IPv4 adresleri, her biri kısa bir süre denenir; olmazsa IPv6'ya geçilir.
public static class Ag
{
    private static readonly TimeSpan AdresBasina = TimeSpan.FromSeconds(4);

    // Tek, paylaşılan bağlantı havuzu (her HttpClient kendi havuzunu açmasın).
    public static readonly SocketsHttpHandler Isleyici = new()
    {
        ConnectCallback = Baglan,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
    };

    public static HttpClient Istemci(TimeSpan zamanAsimi) => new(Isleyici, disposeHandler: false) { Timeout = zamanAsimi };

    private static async ValueTask<Stream> Baglan(SocketsHttpConnectionContext c, CancellationToken iptal)
    {
        var adresler = await Dns.GetHostAddressesAsync(c.DnsEndPoint.Host, iptal);
        var sirali = adresler.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Concat(adresler.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6)).ToList();
        if (sirali.Count == 0) throw new HttpRequestException($"{c.DnsEndPoint.Host} bulunamadı.");

        Exception? son = null;
        foreach (var adres in sirali)
        {
            var soket = new Socket(adres.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var sure = CancellationTokenSource.CreateLinkedTokenSource(iptal);
            sure.CancelAfter(AdresBasina);
            try
            {
                await soket.ConnectAsync(new IPEndPoint(adres, c.DnsEndPoint.Port), sure.Token);
                return new NetworkStream(soket, ownsSocket: true);
            }
            catch (Exception e) when (!iptal.IsCancellationRequested)
            {
                son = e;
                soket.Dispose();
            }
        }
        throw new HttpRequestException("Sunucuya bağlanılamadı.", son);
    }

    // SignalR'ın WebSocket bağlantısı da aynı yoldan kurulsun (oturum anahtarı başlıkta gider).
    public static async ValueTask<WebSocket> WebSocketAc(Uri adres, string? token, CancellationToken iptal)
    {
        var ws = new ClientWebSocket();
        if (!string.IsNullOrEmpty(token)) ws.Options.SetRequestHeader("Authorization", new AuthenticationHeaderValue("Bearer", token).ToString());
        await ws.ConnectAsync(adres, new HttpMessageInvoker(Isleyici, disposeHandler: false), iptal);
        return ws;
    }
}
