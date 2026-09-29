using System.Net;

namespace EngelsizDPI.Core;

public static class ConnectivityTester
{
    /// <summary>
    /// Adrese TLS bağlantısı kurulup HTTP yanıtı alınabiliyorsa true döner. Yanıtın durum kodu
    /// önemsizdir (403/429 da erişim var demektir); DPI engeli bağlantı sıfırlama, zaman aşımı veya
    /// sahte IP'ye bağlı sertifika hatası olarak görünür.
    /// </summary>
    public static async Task<bool> CanReachAsync(string url, CancellationToken ct = default)
    {
        // Her testte yeni handler: yöntemler arasında eski (engelsiz ya da engelli) bağlantı yeniden kullanılmasın.
        using var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.Zero,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) EngelsizDPI/1.0");

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public static async Task<Dictionary<SitePack, bool>> TestPacksAsync(IEnumerable<SitePack> packs, CancellationToken ct = default)
    {
        var list = packs.ToList();
        var results = await Task.WhenAll(list.Select(p => CanReachAsync(p.TestUrl, ct)));
        return list.Zip(results).ToDictionary(x => x.First, x => x.Second);
    }
}
