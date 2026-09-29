using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace EngelsizDPI.Core;

/// <summary>
/// Bir sitenin sayfasında geçen diğer alan adlarını (resim, script, API sunucuları) bulur ve hangilerinin
/// engelli göründüğünü test eder. Örn. reddit.com eklenince i.redd.it ve redditstatic.com'u önerir.
///
/// Engelli sayılma ölçütü: alan adı DNS'te çözülüyor ama HTTPS bağlantısı kurulamıyor. Hiç çözülmeyen adlar
/// (yazım hatası, iç ağ) önerilmez.
/// </summary>
public static partial class SiteScanner
{
    private const int MaxHosts = 30;

    public static async Task<IReadOnlyList<string>> FindBlockedRelatedDomainsAsync(
        string domain, IReadOnlyCollection<string> alreadyCovered, CancellationToken ct = default)
    {
        string html;
        using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) })
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
            html = await client.GetStringAsync($"https://{domain}/", ct);
        }

        var candidates = ExtractRegistrableDomains(html)
            .Where(d => !IsCovered(d, alreadyCovered.Append(domain)))
            .Take(MaxHosts)
            .ToList();

        var blocked = new List<string>();
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(candidates.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (await ResolvesAsync(d, ct) && !await ConnectivityTester.CanReachAsync($"https://{d}/", ct))
                    lock (blocked) blocked.Add(d);
            }
            finally
            {
                gate.Release();
            }
        }));
        return blocked.Order().ToList();
    }

    /// <summary>HTML içindeki mutlak URL'lerden kayıtlı alan adlarını (ör. cdn.x.com → x.com) çıkarır.</summary>
    public static IReadOnlyList<string> ExtractRegistrableDomains(string html) =>
        UrlHostRegex().Matches(html)
            .Select(m => m.Groups[1].Value.ToLowerInvariant().TrimEnd('.'))
            .Where(SitePacks.IsValidDomain)
            .Select(RegistrableDomain)
            .Distinct()
            .ToList();

    /// <summary>Alt alan adını atıp kaydedilebilir adı döner: a.b.example.com → example.com, x.gov.tr → x.gov.tr.</summary>
    public static string RegistrableDomain(string host)
    {
        var labels = host.Split('.');
        if (labels.Length <= 2) return host;
        // "com.tr", "co.uk" gibi ikinci düzey uzantılarda üç etiket alınır.
        var twoLevelSuffix = labels[^2] is "com" or "co" or "org" or "net" or "gov" or "edu" or "gen" or "web" or "bel" or "k12" or "ac"
                             && labels[^1].Length == 2;
        return string.Join('.', labels[^(twoLevelSuffix ? 3 : 2)..]);
    }

    /// <summary>Alan adı listedeki bir addır ya da onun alt alan adıdır (GoodbyeDPI eşleştirmesiyle aynı mantık).</summary>
    public static bool IsCovered(string domain, IEnumerable<string> list) =>
        list.Any(l => domain.Equals(l, StringComparison.OrdinalIgnoreCase) ||
                      domain.EndsWith("." + l, StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> ResolvesAsync(string domain, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, ct);
            return addresses.Length > 0;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"(?:https?:)?//([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlHostRegex();
}
