using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EngelsizDPI.Core;

namespace EngelsizDPI.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("engelsizdpi-test-").FullName;

    public UpdateServiceTests() => UpdateService.UpdateDir = Path.Combine(_dir, "update");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("v2", 2, 0, 0)]
    [InlineData("v1.4.0-beta.1", 1, 4, 0)]
    public void TryParseVersion_accepts_tag_formats(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateService.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void TryParseVersion_rejects_garbage(string? tag) => Assert.False(UpdateService.TryParseVersion(tag, out _));

    [Fact]
    public void ParseRelease_returns_newer_version_with_digest()
    {
        var info = UpdateService.ParseRelease(Release("v1.1.0", Asset("EngelsizDPI.exe", "sha256:" + new string('a', 64))), new Version(1, 0, 0));

        Assert.NotNull(info);
        Assert.Equal(new Version(1, 1, 0), info.Version);
        Assert.Equal(new string('a', 64), info.Sha256);
        Assert.EndsWith("/EngelsizDPI.exe", info.DownloadUrl);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    public void ParseRelease_ignores_same_or_older_version(string tag) =>
        Assert.Null(UpdateService.ParseRelease(Release(tag, Asset("EngelsizDPI.exe", "sha256:" + new string('a', 64))), new Version(1, 0, 0)));

    [Fact]
    public void ParseRelease_falls_back_to_checksum_file()
    {
        var info = UpdateService.ParseRelease(Release("v1.1.0", Asset("EngelsizDPI.exe", null), Asset("EngelsizDPI.exe.sha256", null)), new Version(1, 0, 0));

        Assert.NotNull(info);
        Assert.Null(info.Sha256);
        Assert.EndsWith("/EngelsizDPI.exe.sha256", info.Sha256Url);
    }

    [Fact]
    public void ParseRelease_refuses_unverifiable_or_missing_exe()
    {
        Assert.Null(UpdateService.ParseRelease(Release("v1.1.0", Asset("EngelsizDPI.exe", null)), new Version(1, 0, 0)));
        Assert.Null(UpdateService.ParseRelease(Release("v1.1.0", Asset("Other.zip", "sha256:" + new string('a', 64))), new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789  EngelsizDPI.exe", true)]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\n", true)]
    [InlineData("not-a-hash  EngelsizDPI.exe", false)]
    [InlineData("", false)]
    public void ParseChecksumFile_reads_sha256sum_format(string content, bool valid) =>
        Assert.Equal(valid, UpdateService.ParseChecksumFile(content) is not null);

    [Fact]
    public void ReplaceExecutable_swaps_files_and_keeps_old_copy()
    {
        var current = Write("EngelsizDPI.exe", "old");
        var fresh = Write("new.exe", "new");

        UpdateService.ReplaceExecutable(current, fresh);

        Assert.Equal("new", File.ReadAllText(current));
        Assert.Equal("old", File.ReadAllText(current + ".old"));
        Assert.False(File.Exists(fresh));
    }

    [Fact]
    public void ReplaceExecutable_restores_original_when_copy_fails()
    {
        var current = Write("EngelsizDPI.exe", "old");

        Assert.ThrowsAny<IOException>(() => UpdateService.ReplaceExecutable(current, Path.Combine(_dir, "missing.exe")));

        Assert.Equal("old", File.ReadAllText(current));
        Assert.False(File.Exists(current + ".old"));
    }

    [Fact]
    public void ReplaceExecutable_works_while_current_exe_is_open()
    {
        // Çalışan bir exe'yi taklit eder: Windows, açık (paylaşım modunda silmeye izin veren) dosyanın
        // yeniden adlandırılmasına izin verir.
        var current = Write("EngelsizDPI.exe", "old");
        var fresh = Write("new.exe", "new");
        using var running = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

        UpdateService.ReplaceExecutable(current, fresh);

        Assert.Equal("new", File.ReadAllText(current));
    }

    [Fact]
    public async Task DownloadAsync_verifies_hash()
    {
        var payload = Encoding.UTF8.GetBytes("engelsizdpi update payload");
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        using var server = new OneShotServer(payload);

        var path = await UpdateService.DownloadAsync(new UpdateInfo(new Version(9, 0, 0), server.Url, hash, null, ""), ct: TestContext.Current.CancellationToken);

        Assert.Equal(payload, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.EndsWith("EngelsizDPI-9.0.0.exe", path);
    }

    [Fact]
    public async Task DownloadAsync_rejects_tampered_file_and_leaves_nothing_behind()
    {
        using var server = new OneShotServer(Encoding.UTF8.GetBytes("tampered"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateService.DownloadAsync(new UpdateInfo(new Version(9, 0, 0), server.Url, new string('0', 64), null, ""), ct: TestContext.Current.CancellationToken));

        Assert.Contains("SHA-256", error.Message);
        Assert.Empty(Directory.EnumerateFiles(UpdateService.UpdateDir));
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static JsonElement Release(string tag, params object[] assets) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["tag_name"] = tag,
            ["html_url"] = $"https://github.com/o/r/releases/tag/{tag}",
            ["assets"] = assets,
        });

    private static Dictionary<string, object?> Asset(string name, string? digest) => new()
    {
        ["name"] = name,
        ["browser_download_url"] = $"https://github.com/o/r/releases/download/v/{name}",
        ["digest"] = digest,
    };

    /// <summary>Tek bir isteğe sabit içerikle cevap veren minimal HTTP sunucusu.</summary>
    private sealed class OneShotServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public string Url { get; }

        public OneShotServer(byte[] body)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/EngelsizDPI.exe";
            _ = Task.Run(async () =>
            {
                using var client = await _listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var buffer = new byte[4096];
                await stream.ReadAtLeastAsync(buffer, 1, throwOnEndOfStream: false);
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
            });
        }

        public void Dispose() => _listener.Stop();
    }
}
