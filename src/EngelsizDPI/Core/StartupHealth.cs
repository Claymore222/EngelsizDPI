namespace EngelsizDPI.Core;

/// <summary>
/// Kurulu sürümün sağlıklı açılıp açılmadığını izler; bozuk bir sürüm yayınlanırsa kullanıcı onda takılı kalmasın.
///
/// Her açılışta durum dosyasına "sürüm deneme-sayısı" yazılır; uygulama bir süre sorunsuz çalışınca ya da düzgün
/// kapatılınca dosya silinir. Böylece dosyadaki sayı, üst üste sağlıklı bitmeyen açılışları gösterir:
/// - Güncellemeden sonra yeni sürüm üst üste açılamazsa saklanan önceki sürüme (EngelsizDPI.exe.old) dönülür.
/// - Kullanıcı eski bir exe indirip açarsa kurulu sürüm bozuk sayıldığı için eski sürüm onun üzerine kurulur.
/// İki durumda da bozuk sürüm atlanır: bir daha otomatik indirilmez, ondan yeni bir düzeltme gelince güncellenir.
/// </summary>
public sealed class StartupHealth(string stateFile, string skipFile)
{
    /// <summary>Bu kadar açılış üst üste sağlıklı bitmezse sürüm bozuk sayılır.</summary>
    public const int BrokenThreshold = 2;

    /// <summary>Kurulu kopyanın durumu; dosyalar yalnızca yöneticinin yazabildiği kurulum klasöründe durur.</summary>
    public static StartupHealth? Installed { get; } = OperatingSystem.IsWindows()
        ? new(Path.Combine(Installer.InstallDir, "startup-state"), Path.Combine(Installer.InstallDir, "skipped-version"))
        : null;

    /// <summary>Açılışı kaydeder ve bu sürümün o ana kadar üst üste sağlıklı bitmeyen açılış sayısını döner.</summary>
    public int BeginStartup(Version version)
    {
        var previous = ReadFailures(version);
        TryWrite(stateFile, $"{version} {previous + 1}");
        return previous;
    }

    public void MarkHealthy() => TryDelete(stateFile);

    public bool IsBroken(Version version) => ReadFailures(version) >= BrokenThreshold;

    /// <summary>Sürümü bozuk olarak işaretler: otomatik güncellemede bir daha kurulmaz.</summary>
    public void Skip(Version version)
    {
        TryWrite(skipFile, version.ToString());
        TryDelete(stateFile);
    }

    public bool IsSkipped(Version version) =>
        TryRead(skipFile) is { } text && UpdateService.TryParseVersion(text, out var skipped) && skipped == version;

    /// <summary>Kurulu kopyadan çalışıyorsak bu açılışı sağlıklı sayar.</summary>
    public static void MarkCurrentHealthy()
    {
        if (OperatingSystem.IsWindows() && Installer.IsRunningInstalled) Installed?.MarkHealthy();
    }

    private int ReadFailures(Version version)
    {
        var parts = TryRead(stateFile)?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is [var v, var n] && UpdateService.TryParseVersion(v, out var recorded) && recorded == version &&
               int.TryParse(n, out var count) ? count : 0;
    }

    private static string? TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void TryWrite(string path, string text)
    {
        try { File.WriteAllText(path, text); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Açılış durumu yazılamadı: " + e.Message); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
