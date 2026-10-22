using System.Diagnostics;

namespace EngelsizDPI.Core;

/// <summary>
/// Tarayıcı ve dosya açma. Uygulama yönetici olarak çalıştığı için doğrudan başlatılan tarayıcı da yönetici
/// yetkisi alırdı; bağlantılar Explorer üzerinden, normal kullanıcı yetkisiyle açılır.
/// </summary>
public static class Shell
{
    /// <summary>
    /// explorer.exe argümanı bir dosya yolu gibi okur: MAX_PATH'ten (260) uzun bir adres verilirse hata vermeden
    /// Belgeler klasörünü açar. Uzun adresler (ör. önceden doldurulmuş sorun formu) bu yüzden geçici bir internet
    /// kısayoluna (.url) yazılıp o açılır; kısayolu da Explorer, normal kullanıcı yetkisiyle açar.
    /// </summary>
    private const int MaxExplorerArgument = 200;

    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        var target = uri.AbsoluteUri;
        if (!OperatingSystem.IsWindows())
        {
            Start("open", target);
            return;
        }
        if (target.Length > MaxExplorerArgument && TryWriteShortcut(target) is { } shortcut) target = shortcut;
        Start("explorer.exe", target);
    }

    private static string? TryWriteShortcut(string url)
    {
        try
        {
            // Yönetici süreci kullanıcının yazabildiği bir klasöre (ör. %TEMP%) yazmamalı: oraya konan bir bağlantı
            // (junction) yazmayı başka bir yere yönlendirebilir. Kuruluyken kurulum klasörü kullanılır.
            var dir = OperatingSystem.IsWindows() && Installer.IsRunningInstalled
                ? Installer.InstallDir
                : Path.Combine(Path.GetTempPath(), "EngelsizDPI");
            Directory.CreateDirectory(dir);
            // Explorer dosyayı arka planda okur; hemen silinemez. Aynı ad her seferinde üzerine yazılır.
            var path = Path.Combine(dir, "baglanti.url");
            File.WriteAllText(path, $"[InternetShortcut]\r\nURL={url}\r\n");
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("Bağlantı kısayolu yazılamadı: " + e.Message);
            return null;
        }
    }

    /// <summary>Dosyayı varsayılan programıyla, Explorer üzerinden normal kullanıcı yetkisiyle açar.</summary>
    public static void OpenFile(string path) =>
        Start(OperatingSystem.IsWindows() ? "explorer.exe" : "open", path);

    private static void Start(string exe, string arg)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add(arg);
            Process.Start(psi);
        }
        catch (Exception e)
        {
            Log.Write($"Açılamadı ({exe}): {e.Message}");
        }
    }
}
