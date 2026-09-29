using System.Diagnostics;

namespace EngelsizDPI.Core;

/// <summary>
/// Tarayıcı ve dosya açma. Uygulama yönetici olarak çalıştığı için doğrudan başlatılan tarayıcı da yönetici
/// yetkisi alırdı; bağlantılar Explorer üzerinden, normal kullanıcı yetkisiyle açılır.
/// </summary>
public static class Shell
{
    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        Start(OperatingSystem.IsWindows() ? "explorer.exe" : "open", uri.AbsoluteUri);
    }

    public static void OpenFile(string path) =>
        Start(OperatingSystem.IsWindows() ? "notepad.exe" : "open", path);

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
