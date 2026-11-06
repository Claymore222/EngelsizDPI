using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace EngelsizDPI.Core;

/// <summary>
/// Bazı adlar bu hatlarda UDP DNS ile hiç çözülmüyor: ör. Roblox'un avatar/küçük resim adresi tr.rbxcdn.com,
/// Yandex dahil denenen bütün UDP sunucularında boş yanıt veriyor (TCP ve DoH ile çözülüyor). Studio ve site
/// bu adresten resim istediği için görseller gelmiyordu.
///
/// Paketlerdeki "pin" adları bağlanınca DNS-over-HTTPS (Cloudflare, olmazsa Google; IP ile, DNS'e gerek yok) ile
/// çözülür ve hosts dosyasında yalnızca bu uygulamanın işaretli bölümüne yazılır. Bölüm sağlık denetimlerinde
/// yenilenir, bağlantı kesilince silinir; hosts dosyasının geri kalanına dokunulmaz.
/// </summary>
public static partial class DnsPins
{
    public const string BeginMarker = "# >>> EngelsizDPI (otomatik eklenir, elle düzenlemeyin)";
    public const string EndMarker = "# <<< EngelsizDPI";

    private static readonly string[] DohUrls =
    [
        "https://1.1.1.1/dns-query?type=A&name=",
        "https://8.8.8.8/resolve?type=A&name=",
    ];

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _written; // Son yazılan bölüm; aynıysa dosyaya tekrar yazılmaz.

    public static string HostsFile => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

    /// <summary>Adları çözer ve hosts bölümünü günceller. Çözülemeyen ad için eski kayıt korunur. Hata fırlatmaz.</summary>
    public static async Task ApplyAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var previous = ParseBlock(ReadHosts());
            var entries = new List<(string Ip, string Name)>();
            foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var ips = await ResolveAsync(name, ct);
                if (ips.Count == 0) ips = previous.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Ip).ToList();
                if (ips.Count == 0) Log.Write($"DNS onarımı: {name} çözülemedi");
                entries.AddRange(ips.Select(ip => (ip, name)));
            }
            Write(entries);
        }
        catch (Exception e)
        {
            Log.Write("DNS onarımı uygulanamadı: " + e.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Uygulamanın hosts bölümünü kaldırır. Hata fırlatmaz.</summary>
    public static void Remove()
    {
        Gate.Wait();
        try { Write([]); }
        catch (Exception e) { Log.Write("DNS onarımı kaldırılamadı: " + e.Message); }
        finally { Gate.Release(); }
    }

    private static void Write(IReadOnlyList<(string Ip, string Name)> entries)
    {
        var original = ReadHosts();
        var updated = ReplaceBlock(original, entries);
        if (updated == original) { _written = updated; return; }
        File.WriteAllText(HostsFile, updated, new UTF8Encoding(false));
        _written = updated;
        try { DnsFlushResolverCache(); } catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
        Log.Write(entries.Count == 0 ? "DNS onarımı kaldırıldı" :
            "DNS onarımı: " + string.Join(", ", entries.Select(e => $"{e.Name}={e.Ip}")));
    }

    private static string ReadHosts() => File.Exists(HostsFile) ? File.ReadAllText(HostsFile) : "";

    /// <summary>Metindeki uygulama bölümünü verilen kayıtlarla değiştirir (kayıt yoksa bölümü siler).</summary>
    public static string ReplaceBlock(string hosts, IReadOnlyList<(string Ip, string Name)> entries)
    {
        var newline = hosts.Contains("\r\n") || hosts.Length == 0 ? "\r\n" : "\n";
        var lines = hosts.Replace("\r\n", "\n").Split('\n').ToList();
        var start = lines.FindIndex(l => l.Trim() == BeginMarker);
        if (start >= 0)
        {
            var end = lines.FindIndex(start, l => l.Trim() == EndMarker);
            lines.RemoveRange(start, (end < 0 ? lines.Count - 1 : end) - start + 1);
        }
        while (lines.Count > 0 && lines[^1].Trim() == "") lines.RemoveAt(lines.Count - 1);

        if (entries.Count > 0)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add(BeginMarker);
            lines.AddRange(entries.Select(e => $"{e.Ip} {e.Name}"));
            lines.Add(EndMarker);
        }
        return lines.Count == 0 ? "" : string.Join(newline, lines) + newline;
    }

    /// <summary>Uygulama bölümündeki kayıtlar.</summary>
    public static List<(string Ip, string Name)> ParseBlock(string hosts)
    {
        var result = new List<(string, string)>();
        var inside = false;
        foreach (var raw in hosts.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line == BeginMarker) { inside = true; continue; }
            if (line == EndMarker) break;
            if (!inside) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && IPAddress.TryParse(parts[0], out _) && SitePacks.IsValidDomain(parts[1]))
                result.Add((parts[0], parts[1]));
        }
        return result;
    }

    private static async Task<List<string>> ResolveAsync(string name, CancellationToken ct)
    {
        foreach (var url in DohUrls)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var json = await Http.GetStringAsync(url + Uri.EscapeDataString(name), timeout.Token);
                var ips = ParseDohAnswer(json);
                if (ips.Count > 0) return ips;
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                // Sonraki sağlayıcı denenir.
            }
        }
        return [];
    }

    /// <summary>DoH JSON yanıtındaki A kayıtları (en fazla 2).</summary>
    public static List<string> ParseDohAnswer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var ips = new List<string>();
        if (!doc.RootElement.TryGetProperty("Status", out var status) || status.GetInt32() != 0) return ips;
        if (!doc.RootElement.TryGetProperty("Answer", out var answers) || answers.ValueKind != JsonValueKind.Array) return ips;
        foreach (var a in answers.EnumerateArray())
        {
            if (a.TryGetProperty("type", out var type) && type.GetInt32() == 1 &&
                a.TryGetProperty("data", out var data) && IPAddress.TryParse(data.GetString(), out var ip) &&
                ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                ips.Add(ip.ToString());
                if (ips.Count == 2) break;
            }
        }
        return ips;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-json"));
        return client;
    }

    [LibraryImport("dnsapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCache();
}
