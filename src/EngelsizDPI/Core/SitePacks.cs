using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngelsizDPI.Core;

/// <summary>
/// Tek tıkla açılıp kapatılabilen site grubu. <see cref="TestUrl"/> bağlantı testinde ve otomatik yöntem
/// bulmada kullanılır.
/// </summary>
public sealed record SitePack(
    string Id,
    string Name,
    string Category,
    [property: JsonPropertyName("test")] string TestUrl,
    IReadOnlyList<string> Domains,
    bool Default = false);

public sealed record PackCatalog(int Revision, IReadOnlyList<SitePack> Packs);

/// <summary>
/// Site paketi listesi repodaki packs/packs.json dosyasından gelir; yeni bir site eklemek için uygulama sürümü
/// çıkarmak gerekmez. Sıra: önbellek / exe'ye gömülü kopya (hangisi yeniyse) → arka planda GitHub, olmazsa jsDelivr.
/// </summary>
public static class SitePacks
{
    private const int MaxPacks = 200, MaxDomainsPerPack = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string CacheFile => Path.Combine(Path.GetDirectoryName(AppPaths.SettingsFile)!, "packs.json");

    /// <summary>Şu anki katalog; açılışta yerelden yüklenir, uzaktan yenilenince değişir.</summary>
    public static PackCatalog Current { get; private set; } = LoadLocal();

    public static IReadOnlyList<SitePack> All => Current.Packs;

    /// <summary>Katalog uzaktan güncellendiğinde tetiklenir.</summary>
    public static event Action<PackCatalog>? Changed;

    /// <summary>JSON'u okur ve doğrular; bozuk ya da şüpheli içerikte hata fırlatır.</summary>
    public static PackCatalog Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<RawCatalog>(json, JsonOptions)
                  ?? throw new FormatException("Boş paket listesi.");
        if (raw.Packs is not { Count: > 0 and <= MaxPacks }) throw new FormatException("Paket sayısı geçersiz.");

        var packs = new List<SitePack>();
        foreach (var p in raw.Packs)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name)) throw new FormatException("Paket kimliği/adı eksik.");
            if (!Uri.TryCreate(p.Test, UriKind.Absolute, out var test) || test.Scheme != Uri.UriSchemeHttps)
                throw new FormatException($"{p.Id}: test adresi https olmalı.");
            var domains = (p.Domains ?? []).Select(d => d.Trim().ToLowerInvariant()).Distinct().ToList();
            if (domains.Count is 0 or > MaxDomainsPerPack || domains.Any(d => !IsValidDomain(d)))
                throw new FormatException($"{p.Id}: alan adları geçersiz.");

            packs.Add(new SitePack(p.Id.Trim().ToLowerInvariant(), p.Name.Trim(), p.Category?.Trim() is { Length: > 0 } c ? c : "Diğer",
                test.ToString(), domains, p.Default));
        }
        if (packs.Select(p => p.Id).Distinct().Count() != packs.Count) throw new FormatException("Aynı kimlikte iki paket var.");
        return new PackCatalog(raw.Revision, packs);
    }

    public static bool IsValidDomain(string d) =>
        d.Length is > 3 and < 254 && d.Contains('.') && !d.StartsWith('.') && !d.EndsWith('.') &&
        Uri.CheckHostName(d) == UriHostNameType.Dns;

    /// <summary>Uzak listeyi indirir; yerel sürümden yeniyse kaydeder ve <see cref="Changed"/> tetikler.</summary>
    public static async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        if (UpdateService.Repo is not { } repo) return false;
        string[] urls =
        [
            $"https://raw.githubusercontent.com/{repo}/main/packs/packs.json",
            $"https://cdn.jsdelivr.net/gh/{repo}@main/packs/packs.json",
        ];

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"EngelsizDPI/{UpdateService.CurrentVersion}");
        foreach (var url in urls)
        {
            try
            {
                var json = await client.GetStringAsync(url, ct);
                var remote = Parse(json);
                if (remote.Revision <= Current.Revision) return false;

                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
                await File.WriteAllTextAsync(CacheFile, json, ct);
                Current = remote;
                Log.Write($"Site listesi güncellendi (revizyon {remote.Revision}, {remote.Packs.Count} paket)");
                Changed?.Invoke(remote);
                return true;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException or JsonException)
            {
                Log.Write($"Site listesi alınamadı ({new Uri(url).Host}): {e.Message}");
            }
        }
        return false;
    }

    private static PackCatalog LoadLocal()
    {
        var bundled = Parse(ReadBundled());
        try
        {
            if (File.Exists(CacheFile))
            {
                var cached = Parse(File.ReadAllText(CacheFile));
                if (cached.Revision > bundled.Revision) return cached;
            }
        }
        catch (Exception)
        {
            // Bozuk önbellek yok sayılır; gömülü liste her zaman geçerlidir.
        }
        return bundled;
    }

    private static string ReadBundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("packs.json")
                           ?? throw new InvalidOperationException("Gömülü paket listesi bulunamadı.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class RawCatalog
    {
        public int Revision { get; set; }
        public List<RawPack>? Packs { get; set; }
    }

    private sealed class RawPack
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Category { get; set; }
        public bool Default { get; set; }
        public string? Test { get; set; }
        public List<string>? Domains { get; set; }
    }
}
