using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EngelsizDPI.Core;

/// <summary>
/// Tek dosyalık .NET uygulaması yerel kütüphanelerini (SkiaSharp, HarfBuzz, ANGLE) açılışta varsayılan olarak
/// %TEMP%\.net altına çıkarır ve oradan yükler. O klasöre kullanıcının bütün programları yazabilir; uygulama ise
/// UAC sormadan yönetici olarak başlayabildiği için oradaki bir DLL'i değiştiren zararlı yazılım yönetici yetkisi
/// kazanabilirdi.
///
/// Çıkarma klasörü yalnızca süreç başlamadan önce bir ortam değişkeniyle seçilebilir. Bu yüzden yönetici kopya,
/// değişken ayarlı değilse kendini bir kez, değişken yalnızca yöneticilerin yazabildiği Program Files altını
/// gösterecek şekilde yeniden başlatır. İlk süreç Avalonia'yı hiç başlatmadığı için %TEMP%'teki kütüphaneleri
/// yüklemez.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeExtraction
{
    private const string Variable = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";

    public static string SecureDir => Path.Combine(Installer.InstallDir, "runtime");

    /// <summary>Gerekiyorsa kendini güvenli çıkarma klasörüyle yeniden başlatır; true dönerse bu süreç kapanmalıdır.</summary>
    public static bool RelaunchIfNeeded(string[] args, bool alreadyRelaunched)
    {
        if (!IsSingleFile || IsSecureDirSet()) return false;
        if (alreadyRelaunched)
        {
            Log.Write("Güvenli çıkarma klasörü ayarlanamadı; yerel kütüphaneler varsayılan klasörden yüklenecek");
            return false;
        }
        try
        {
            Directory.CreateDirectory(SecureDir);
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(Program.RelaunchArg); // Değişken ulaşmazsa ikinci kez yeniden başlatılmaz.
            psi.Environment[Variable] = SecureDir;
            // Bu süreç ön plana gelme hakkını yeni sürece devreder; yoksa pencere arkada açılabilir.
            AllowSetForegroundWindow(-1);
            Process.Start(psi);
            return true;
        }
        catch (Exception e)
        {
            Log.Write("Güvenli klasörle yeniden başlatılamadı: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Eski sürümlerin çıkardığı klasörleri siler. Bu sürecin kullandığı klasöre dokunmaz: DLL'ler ancak gerektiğinde
    /// yüklenir (HarfBuzz ilk metin çizildiğinde), tepside başlayan kopyada henüz kilitli olmadıkları için silinirlerdi
    /// ve pencere açılınca uygulama DllNotFoundException ile çökerdi.
    /// </summary>
    public static void CleanupOldVersions()
    {
        var root = Path.Combine(SecureDir, "EngelsizDPI");
        if (!IsSecureDirSet() || !Directory.Exists(root)) return;
        var current = CurrentExtractionDir(root);
        if (current is null) return; // Hangisinin bizim olduğunu bilmeden hiçbirini silme.
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(Path.TrimEndingDirectorySeparator(dir), current, StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Bu sürecin yüklediği bir DLL'in bulunduğu, <paramref name="root"/> altındaki ilk seviye klasör.</summary>
    private static string? CurrentExtractionDir(string root)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        try
        {
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                var path = m.FileName;
                if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var rest = path[prefix.Length..];
                var sep = rest.IndexOf(Path.DirectorySeparatorChar);
                if (sep > 0) return prefix + rest[..sep];
            }
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        return null;
    }

    private static bool IsSecureDirSet() =>
        Environment.GetEnvironmentVariable(Variable) is { } current &&
        string.Equals(Path.TrimEndingDirectorySeparator(current), SecureDir, StringComparison.OrdinalIgnoreCase);

#pragma warning disable IL3000 // Tek dosyalık yayında Location'ın boş olması tam olarak aradığımız işaret.
    private static bool IsSingleFile => string.IsNullOrEmpty(typeof(NativeExtraction).Assembly.Location);
#pragma warning restore IL3000

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
