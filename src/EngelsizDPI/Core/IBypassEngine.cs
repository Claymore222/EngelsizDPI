namespace EngelsizDPI.Core;

public sealed record EngineOptions(BypassProfile Profile, IReadOnlyList<string> Hosts, bool DnsRedirect);

/// <summary>Platforma özel DPI atlatma motoru (Windows: GoodbyeDPI, ileride macOS: byedpi).</summary>
public interface IBypassEngine : IDisposable
{
    bool IsRunning { get; }

    /// <summary>Motor, uygulama tarafından durdurulmadan kapandığında tetiklenir.</summary>
    event EventHandler? UnexpectedExit;

    Task StartAsync(EngineOptions options, CancellationToken ct = default);

    /// <summary>Motorun son anlamlı çıktı satırı (genellikle hata mesajı); yoksa null.</summary>
    string? LastOutputLine();
    Task StopAsync();

    /// <summary>Çakışan başka DPI araçları (eski GoodbyeDPI servisi vb.) için kullanıcıya gösterilecek açıklamalar.</summary>
    IReadOnlyList<string> DetectConflicts();
    Task RemoveConflictsAsync();
}

public sealed class EngineException(string message) : Exception(message);

public static class EngineFactory
{
    public static IBypassEngine Create()
    {
        if (OperatingSystem.IsWindows()) return new GoodbyeDpiEngine();
        throw new PlatformNotSupportedException("Bu platform henüz desteklenmiyor.");
    }
}
