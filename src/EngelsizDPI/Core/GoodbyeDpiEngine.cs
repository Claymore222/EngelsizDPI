using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

namespace EngelsizDPI.Core;

/// <summary>
/// goodbyedpi.exe'yi Windows servisi yerine uygulamanın alt süreci olarak çalıştırır. Süreç bir
/// Job Object'e bağlandığı için EngelsizDPI çökse bile GoodbyeDPI arkada kalmaz; sistem ayarı da
/// değiştirilmediğinden geri alınacak bir şey yoktur.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GoodbyeDpiEngine : IBypassEngine
{
    private const string ResourcePrefix = "engine.win-x64.";
    private const string LegacyServiceName = "GoodbyeDPI";
    private const string LegacyStartupTask = "DNSChangerApp_Startup";

    // Yandex DNS'in standart dışı portu: bazı ISS'ler 53. porttaki DNS trafiğini ele geçirir.
    private const string DnsV4 = "77.88.8.8", DnsV6 = "2a02:6b8::feed:0ff", DnsPort = "1253";

    private readonly KillOnCloseJob _job = new();
    private readonly WinDivertKeepAlive _driver = new();
    private readonly Queue<string> _output = new();
    private Process? _process;
    private bool _stopping;

    public bool IsRunning => _process is { HasExited: false };

    public event EventHandler? UnexpectedExit;

    private static string ExePath => Path.Combine(AppPaths.EngineDir, "goodbyedpi.exe");

    public async Task StartAsync(EngineOptions options, CancellationToken ct = default)
    {
        await StopAsync();
        await Task.Run(ExtractEngineFiles, ct);

        await File.WriteAllLinesAsync(AppPaths.HostListFile, options.Hosts, ct);

        // Sürücüyü açık tut: GoodbyeDPI yeniden başlatılırken sürücü kaldırılıp 1058 hatasına düşülmesin.
        // Açılamıyorsa GoodbyeDPI'ı hiç başlatmadan gerçek nedeni bildir.
        var driverError = await OpenDriverAsync(ct);
        if (driverError is { } code)
        {
            Log.Write($"WinDivert açılamadı: hata {code}");
            throw new EngineException(WinDivertKeepAlive.Describe(code) + $" (hata kodu {code})");
        }

        var psi = new ProcessStartInfo(ExePath)
        {
            WorkingDirectory = AppPaths.EngineDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in options.Profile.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            psi.ArgumentList.Add(arg);
        if (options.DnsRedirect)
        {
            foreach (var arg in new[] { "--dns-addr", DnsV4, "--dns-port", DnsPort, "--dnsv6-addr", DnsV6, "--dnsv6-port", DnsPort })
                psi.ArgumentList.Add(arg);
        }
        if (options.Hosts.Count > 0)
        {
            psi.ArgumentList.Add("--blacklist");
            psi.ArgumentList.Add(AppPaths.HostListFile);
        }

        Log.Write($"GoodbyeDPI başlatılıyor ({options.Profile.Name}): {string.Join(' ', psi.ArgumentList)}");
        lock (_output) _output.Clear();

        var process = Process.Start(psi) ?? throw new EngineException("GoodbyeDPI başlatılamadı.");
        _job.Assign(process);
        // Çıktı okunmazsa pipe tamponu dolduğunda süreç kilitlenir. GoodbyeDPI çıktısı tamponlandığı için
        // satırlar genellikle süreç kapanırken toplu gelir; hata ayıklama için günlüğe yazılır.
        process.OutputDataReceived += (_, e) => OnOutput(e.Data);
        process.ErrorDataReceived += (_, e) => OnOutput(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;
        _process = process;

        // WinDivert sürücüsü açılamazsa (antivirüs, uyumsuz sürüm) GoodbyeDPI hata verip kapanır.
        await Task.Delay(1500, ct);
        if (process.HasExited)
        {
            _process = null;
            process.WaitForExit(); // Kalan çıktının okunmasını bekler.
            Log.Write($"GoodbyeDPI hemen kapandı, çıkış kodu {process.ExitCode}");
            throw new EngineException("GoodbyeDPI hemen kapandı: " + (LastOutputLine() ?? $"çıkış kodu {process.ExitCode}"));
        }
        Log.Write("GoodbyeDPI çalışıyor");
    }

    private async Task<int?> OpenDriverAsync(CancellationToken ct)
    {
        try
        {
            return await _driver.EnsureOpenAsync(AppPaths.EngineDir, TimeSpan.FromSeconds(15), ct);
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Log.Write("WinDivert.dll yüklenemedi: " + e.Message);
            return 2;
        }
    }

    private void OnOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Log.Write("[goodbyedpi] " + line);
        lock (_output)
        {
            _output.Enqueue(line);
            while (_output.Count > 50) _output.Dequeue();
        }
    }

    /// <summary>GoodbyeDPI'ın son anlamlı çıktı satırı (hata mesajı çoğunlukla sondadır).</summary>
    public string? LastOutputLine()
    {
        lock (_output)
            return _output.LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? _output.LastOrDefault();
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process is null) return;

        _stopping = true;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException)
        {
            // Süreç zaten kapanmış.
        }
        finally
        {
            process.Dispose();
            _stopping = false;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopping || !ReferenceEquals(sender, _process)) return;
        _process = null;
        if (sender is Process p)
        {
            p.WaitForExit(); // Tamponda kalan çıktı (hata mesajı) okunsun.
            Log.Write($"GoodbyeDPI beklenmedik şekilde kapandı, çıkış kodu {p.ExitCode}");
        }
        UnexpectedExit?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> DetectConflicts()
    {
        var conflicts = new List<string>();

        if (RunTool("sc.exe", "query", LegacyServiceName) == 0)
            conflicts.Add("Eski bir GoodbyeDPI servisi kurulu (GoodbyeDPI-Turkey veya DNSChanger).");
        if (RunTool("schtasks.exe", "/query", "/tn", LegacyStartupTask) == 0)
            conflicts.Add("Eski DNSChanger uygulaması Windows açılışında başlayacak şekilde ayarlı.");

        var ownPid = _process?.Id;
        if (Process.GetProcessesByName("goodbyedpi").Any(p => p.Id != ownPid))
            conflicts.Add("Başka bir goodbyedpi.exe çalışıyor.");

        return conflicts;
    }

    public Task RemoveConflictsAsync() => Task.Run(() =>
    {
        RunTool("sc.exe", "stop", LegacyServiceName);
        RunTool("sc.exe", "delete", LegacyServiceName);
        RunTool("schtasks.exe", "/delete", "/tn", LegacyStartupTask, "/f");
        // Eski uygulama indirilirken "DNSChangerApp (1).exe" gibi farklı adlarla kaydedilmiş olabilir.
        foreach (var p in Process.GetProcesses().Where(p => p.ProcessName.StartsWith("DNSChangerApp", StringComparison.OrdinalIgnoreCase)))
        {
            try { p.Kill(); } catch (Exception) { /* Zaten kapanmış. */ }
        }

        var ownPid = _process?.Id;
        foreach (var p in Process.GetProcessesByName("goodbyedpi").Where(p => p.Id != ownPid))
        {
            try { p.Kill(); p.WaitForExit(3000); }
            catch (Exception) { /* Başka bir kullanıcıya ait ya da zaten kapanmış. */ }
        }

        // Eski araçtan kalan WinDivert sürücüsü kaldırılır; ancak sürücüyü biz tutuyorsak durdurulmaz, çünkü
        // kullanımdaki sürücüyü durdurmak bağlantıyı koparır ve sürücü kaldırılana kadar yeniden açılamaz.
        if (!IsRunning && !_driver.IsOpen) RunTool("sc.exe", "stop", "WinDivert");
        Log.Write("Çakışmalar kaldırıldı");
    });

    private static int RunTool(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    /// <summary>Gömülü motor dosyalarını, diskteki kopya farklıysa (yeni sürüm) yeniden yazar.</summary>
    private static void ExtractEngineFiles()
    {
        Directory.CreateDirectory(AppPaths.EngineDir);
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix)))
        {
            var target = Path.Combine(AppPaths.EngineDir, name[ResourcePrefix.Length..]);
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var content = ms.ToArray();

            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(content)) continue;
            try
            {
                File.WriteAllBytes(target, content);
            }
            catch (IOException) when (File.Exists(target))
            {
                // WinDivert64.sys yüklü sürücü tarafından kilitli olabilir; mevcut kopya kullanılır.
            }
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _driver.Dispose();
        _job.Dispose();
    }
}
