using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace EngelsizDPI.Core;

public sealed record UpdateInfo(Version Version, string DownloadUrl, string? Sha256, string? Sha256Url, string ReleaseUrl);

/// <summary>
/// GitHub Releases üzerinden kendi kendini güncelleme.
///
/// Akış: en son sürüm sorgulanır → EngelsizDPI.exe indirilir ve SHA-256 ile doğrulanır → çalışan exe
/// "EngelsizDPI.exe.old" olarak yeniden adlandırılır (Windows çalışan exe'nin silinmesine izin vermez ama
/// yeniden adlandırılmasına izin verir) → yeni exe yerine kopyalanır → yeni sürüm başlatılır ve eskisi kapanır.
/// Uygulama zaten yönetici olarak çalıştığından yeni süreç de UAC sorulmadan yönetici olarak açılır.
/// </summary>
public static class UpdateService
{
    public const string AssetName = "EngelsizDPI.exe";
    private const string ChecksumAssetName = AssetName + ".sha256";

    /// <summary>"kullanıcı/depo"; derleme sırasında GitHubRepo özelliğinden gelir (CI'da otomatik doldurulur).</summary>
    public static string? Repo { get; } = ReadRepo();

    public static Version CurrentVersion { get; } = Normalize(typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0));

    // CurrentVersion'dan sonra tanımlanmalı: statik alanlar yazıldıkları sırayla başlatılır.
    private static readonly HttpClient Http = CreateClient();

    /// <summary>
    /// Yalnızca yayınlanmış tek dosyalık exe kendini değiştirebilir; `dotnet run` ile geliştirme sırasında
    /// bin klasöründeki dosyaların üzerine yazılmaz.
    /// </summary>
#pragma warning disable IL3000 // Tek dosyalık yayında Location'ın boş olması tam olarak aradığımız işaret.
    public static bool CanSelfUpdate =>
        OperatingSystem.IsWindows() && Repo is not null && Environment.ProcessPath is not null &&
        string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location);
#pragma warning restore IL3000

    public static bool CanCheck => Repo is not null;

    /// <summary>İndirilmiş ve doğrulanmış güncellemeler; kullanıcıya ait klasörde tutulur.</summary>
    /// <remarks>
    /// Kuruluyken Program Files altında (yalnızca yönetici yazabilir): buradaki exe, sonraki açılışta yönetici
    /// olarak kurulur; kullanıcının yazabildiği bir klasör olsaydı herhangi bir program oraya kendi exe'sini koyup
    /// yetki kazanabilirdi.
    /// </remarks>
    public static string UpdateDir { get; internal set; } = OperatingSystem.IsWindows() && Installer.IsRunningInstalled
        ? Path.Combine(Installer.InstallDir, "updates")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngelsizDPI", "update");

    /// <summary>Yeni sürüm varsa bilgisini, yoksa null döner.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (Repo is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // Henüz hiç sürüm yok.
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseRelease(json.RootElement, CurrentVersion);
    }

    /// <summary>GitHub "latest release" yanıtını yorumlar; test edilebilsin diye ayrı tutuldu.</summary>
    public static UpdateInfo? ParseRelease(JsonElement release, Version current)
    {
        if (!TryParseVersion(release.GetProperty("tag_name").GetString(), out var version) || version <= current)
            return null;

        string? url = null, sha = null, shaUrl = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var assetUrl = asset.GetProperty("browser_download_url").GetString();
            if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
            {
                url = assetUrl;
                if (asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d &&
                    d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = d["sha256:".Length..];
            }
            else if (string.Equals(name, ChecksumAssetName, StringComparison.OrdinalIgnoreCase))
            {
                shaUrl = assetUrl;
            }
        }

        // Doğrulanamayan dosya asla çalıştırılmaz.
        if (url is null || (sha is null && shaUrl is null)) return null;
        return new UpdateInfo(version, url, sha, shaUrl, release.GetProperty("html_url").GetString() ?? "");
    }

    /// <summary>Güncellemeyi indirir, SHA-256'yı doğrular ve doğrulanmış dosyanın yolunu döner.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var expected = info.Sha256 ?? ParseChecksumFile(await Http.GetStringAsync(info.Sha256Url!, ct));
        if (expected is null) throw new InvalidOperationException("Güncellemenin doğrulama bilgisi okunamadı.");

        Directory.CreateDirectory(UpdateDir);
        var target = PendingPath(info.Version);
        var partial = target + ".part";

        using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using (var file = File.Create(partial))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    if (total > 0) progress?.Report((double)done / total.Value);
                }
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                throw new InvalidOperationException("İndirilen güncelleme doğrulanamadı (SHA-256 uyuşmuyor).");
            }
        }

        // Elle yayınlanan bir sürümde etiket (ör. v1.0.1) yükseltilip projedeki <Version> unutulursa, kurulan
        // exe kendini yine eski sürüm sanar ve aynı güncellemeyi sonsuza kadar tekrar kurardı.
        if (TryParseVersion(FileVersionInfo.GetVersionInfo(partial).FileVersion, out var exeVersion) && exeVersion <= CurrentVersion)
        {
            File.Delete(partial);
            throw new InvalidOperationException(
                $"Yayındaki exe'nin sürümü ({exeVersion}) etiketle ({info.Version}) uyuşmuyor; güncelleme atlandı.");
        }

        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>Daha önce indirilip doğrulanmış, şu ankinden yeni bir sürüm varsa yolunu döner.</summary>
    public static string? FindPendingUpdate()
    {
        if (!Directory.Exists(UpdateDir)) return null;
        return Directory.EnumerateFiles(UpdateDir, "EngelsizDPI-*.exe")
            .Select(path => (path, ok: TryParseVersion(Path.GetFileNameWithoutExtension(path)["EngelsizDPI-".Length..], out var v), v))
            .Where(x => x.ok && x.v > CurrentVersion)
            .OrderByDescending(x => x.v)
            .Select(x => x.path)
            .FirstOrDefault();
    }

    /// <summary>
    /// <paramref name="newExe"/>'yi <paramref name="currentExe"/>'nin yerine koyar. Hata olursa eski exe geri
    /// yüklenir; kullanıcı hiçbir durumda çalışmayan bir uygulamayla kalmaz.
    /// </summary>
    public static void ReplaceExecutable(string currentExe, string newExe)
    {
        var old = currentExe + ".old";
        if (File.Exists(old)) File.Delete(old);

        File.Move(currentExe, old);
        try
        {
            File.Copy(newExe, currentExe);
        }
        catch
        {
            if (File.Exists(currentExe)) File.Delete(currentExe);
            File.Move(old, currentExe);
            throw;
        }
        File.Delete(newExe);
    }

    /// <summary>Exe'yi değiştirir ve yeni sürümü başlatır. Çağıran taraf ardından uygulamayı kapatmalıdır.</summary>
    public static void ApplyAndRestart(string newExe, IEnumerable<string> args)
    {
        var current = Environment.ProcessPath!;
        ReplaceExecutable(current, newExe);

        var psi = new ProcessStartInfo(current) { UseShellExecute = false };
        psi.ArgumentList.Add("--updated");
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>Güncelleme sonrası artıkları temizler: .old exe ve artık gereksiz indirmeler.</summary>
    public static async Task CleanupAsync()
    {
        if (Environment.ProcessPath is { } exe)
        {
            // Eski süreç kapanırken dosya birkaç saniye daha kilitli kalabilir.
            for (var i = 0; i < 10 && File.Exists(exe + ".old"); i++)
            {
                try { File.Delete(exe + ".old"); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Task.Delay(1000); }
            }
        }

        if (!Directory.Exists(UpdateDir)) return;
        foreach (var file in Directory.EnumerateFiles(UpdateDir))
        {
            var name = Path.GetFileNameWithoutExtension(file.Replace(".part", ""));
            var stale = !name.StartsWith("EngelsizDPI-") || !TryParseVersion(name["EngelsizDPI-".Length..], out var v) || v <= CurrentVersion;
            if (stale || file.EndsWith(".part"))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('v', 'V');
        var dash = s.IndexOfAny(['-', '+']);
        if (dash >= 0) s = s[..dash];
        if (!Version.TryParse(s.Contains('.') ? s : s + ".0", out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>"sha256sum" biçimindeki ("HASH  dosya") ya da yalnızca hash içeren dosyadan hash'i okur.</summary>
    public static string? ParseChecksumFile(string content)
    {
        var token = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token : null;
    }

    private static string PendingPath(Version v) => Path.Combine(UpdateDir, $"EngelsizDPI-{v}.exe");

    // 1.2 ile 1.2.0 aynı sayılsın; 4. hane (revision) kullanılmaz.
    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static string? ReadRepo()
    {
        var repo = typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GitHubRepo")?.Value;
        return repo is { Length: > 0 } && repo.Count(c => c == '/') == 1 && !repo.StartsWith("KULLANICI") ? repo : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"EngelsizDPI/{CurrentVersion}");
        return client;
    }
}
