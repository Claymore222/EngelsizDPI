using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace EngelsizDPI.Core;

public enum LaunchAction
{
    /// <summary>Kurulu kopyadan ya da geliştirme ortamından çalışıyoruz; normal açılış.</summary>
    RunHere,
    /// <summary>İndirilen exe kurulu sürümden yeni (ya da kurulum yok): kendini kur, kurulu kopyayı başlat.</summary>
    InstallAndLaunch,
    /// <summary>Kurulu sürüm aynı ya da daha yeni: indirilen exe'yi değil kurulu kopyayı başlat.</summary>
    LaunchInstalled,
}

/// <summary>
/// Kurulumsuz tek exe'nin kendini kurması: indirilen EngelsizDPI.exe ilk açılışta kendini
/// C:\Program Files\EngelsizDPI altına kopyalar, Başlat menüsü ve masaüstü kısayolu oluşturur, Windows'un
/// "Yüklü uygulamalar" listesine kaydolur ve kurulu kopyayı başlatır. Böylece kullanıcı exe'yi nereye
/// indirdiğini düşünmek zorunda kalmaz; otomatik güncelleme ve "Windows açılınca başlat" hep aynı yolu kullanır.
///
/// Program Files seçildi çünkü uygulama yönetici olarak çalışıyor: bu klasöre yalnızca yöneticiler yazabilir,
/// böylece sıradan bir süreç yönetici yetkisiyle çalışacak exe'yi değiştiremez.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class Installer
{
    public const string AppName = "EngelsizDPI";
    private const string Publisher = "Claymore222";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName;

    public static string InstallDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    public static string InstalledExe => Path.Combine(InstallDir, AppName + ".exe");

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk");

    /// <summary>Kurulu kopyadan mı çalışıyoruz?</summary>
    public static bool IsRunningInstalled =>
        Environment.ProcessPath is { } exe && PathsEqual(exe, InstalledExe);

    /// <summary>Kurulu kopya var ve bu exe'den eski değil (yani kurulu kopyayı başlatmak yeterli).</summary>
    public static bool IsInstalledAndCurrent() =>
        ReadVersion(InstalledExe) is { } installed && installed >= UpdateService.CurrentVersion;

    /// <summary>Bu açılışta ne yapılacağına karar verir.</summary>
    public static LaunchAction Decide() => Decide(
        canInstall: UpdateService.CanSelfUpdate,
        currentExe: Environment.ProcessPath ?? "",
        installedExe: InstalledExe,
        currentVersion: UpdateService.CurrentVersion,
        installedVersion: ReadVersion(InstalledExe));

    /// <summary>Karar mantığı (yan etkisiz; test edilebilsin diye ayrı).</summary>
    public static LaunchAction Decide(bool canInstall, string currentExe, string installedExe,
        Version currentVersion, Version? installedVersion)
    {
        if (!canInstall || PathsEqual(currentExe, installedExe)) return LaunchAction.RunHere;
        return installedVersion is not null && installedVersion >= currentVersion
            ? LaunchAction.LaunchInstalled
            : LaunchAction.InstallAndLaunch;
    }

    /// <summary>Çalışan exe'yi kurulum klasörüne kopyalar ve kısayolları, kaldırma kaydını oluşturur.</summary>
    public static void Install()
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");
        Directory.CreateDirectory(InstallDir);

        if (File.Exists(InstalledExe))
        {
            // Kurulu (daha eski) sürüm çalışıyorsa dosyası kilitlidir; yeniden adlandırma buna rağmen çalışır.
            var old = InstalledExe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(InstalledExe, old);
            try
            {
                File.Copy(source, InstalledExe);
            }
            catch
            {
                File.Move(old, InstalledExe);
                throw;
            }
        }
        else
        {
            File.Copy(source, InstalledExe);
        }

        CreateShortcut(StartMenuShortcut);
        CreateShortcut(DesktopShortcut);
        RegisterUninstallEntry();
        AutoStart.EnsureLauncher(InstalledExe);
        Log.Write($"Kuruldu: {InstalledExe} (v{UpdateService.CurrentVersion})");
    }

    /// <summary>Kurulu kopya her açıldığında çağrılır: güncellemeden sonra sürüm bilgisini ve başlangıç görevini tazeler.</summary>
    public static void RefreshInstalledState()
    {
        if (!IsRunningInstalled) return;
        try
        {
            RegisterUninstallEntry();
            if (!File.Exists(StartMenuShortcut)) CreateShortcut(StartMenuShortcut);
            AutoStart.EnsureLauncher(InstalledExe);
            // Görev eski adla (v1.1.0) ya da kurulumdan önce indirilen exe'nin yoluyla kayıtlı olabilir.
            if (AutoStart.IsEnabled()) AutoStart.SetEnabled(true);

        }
        catch (Exception e)
        {
            Log.Write("Kurulum bilgisi tazelenemedi: " + e.Message);
        }

        // v1.1.0 motor dosyalarını ve güncellemeleri kullanıcının yazabildiği klasörlerde tutuyordu. Eski
        // WinDivert64.sys, sürücü bellekte kaldığı için yeniden başlatmaya kadar kilitli olabilir; her klasör
        // ayrı denenir ve silinemeyen bir sonraki açılışta tekrar denenir.
        foreach (var legacy in AppPaths.LegacyWritableDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(legacy, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Her şeyi kaldırır. Kurulum klasörü, bu süreç kapandıktan sonra silinir.</summary>
    public static void Uninstall()
    {
        Log.Write("Kaldırılıyor");
        TryRun(AutoStart.RemoveAll);
        TryRun(() => File.Delete(StartMenuShortcut));
        TryRun(() => File.Delete(DesktopShortcut));
        TryRun(() => Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false));

        // Motor dosyaları, günlük, ayarlar ve indirilmiş güncellemeler.
        foreach (var dir in new[] { AppPaths.DataDir, Path.GetDirectoryName(AppPaths.SettingsFile)! }.Concat(AppPaths.LegacyWritableDirs))
        {
            TryRun(() => { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); });
        }

        if (!Directory.Exists(InstallDir)) return;
        if (!IsRunningInstalled)
        {
            TryRun(() => Directory.Delete(InstallDir, recursive: true));
            return;
        }

        // Çalışan exe kendini silemez; kapandıktan birkaç saniye sonra klasörü silen bir komut bırakılır.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{InstallDir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    public static void LaunchInstalled(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(InstalledExe) { UseShellExecute = false, WorkingDirectory = InstallDir };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    private static void RegisterUninstallEntry()
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", UpdateService.CurrentVersion.ToString());
        key.SetValue("Publisher", Publisher);
        key.SetValue("DisplayIcon", InstalledExe + ",0");
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --quiet");
        if (UpdateService.Repo is { } repo) key.SetValue("URLInfoAbout", $"https://github.com/{repo}");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        if (File.Exists(InstalledExe))
            key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    private static void CreateShortcut(string path)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell yok.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = InstalledExe;
            link.WorkingDirectory = InstallDir;
            link.IconLocation = InstalledExe + ",0";
            link.Description = "Discord ve Roblox'a VPN'siz erişim";
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static Version? ReadVersion(string exe) =>
        File.Exists(exe) && UpdateService.TryParseVersion(FileVersionInfo.GetVersionInfo(exe).FileVersion, out var v) ? v : null;

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static void TryRun(Action action)
    {
        try { action(); }
        catch (Exception e) { Log.Write("Kaldırma adımı atlandı: " + e.Message); }
    }

    /// <summary>Kaldırma onayı için basit Windows iletişim kutusu (arayüz açılmadan kullanılır).</summary>
    public static bool Confirm(string text) =>
        MessageBoxW(IntPtr.Zero, text, AppName, 0x4 /* MB_YESNO */ | 0x20 /* MB_ICONQUESTION */) == 6 /* IDYES */;

    public static void Inform(string text) => MessageBoxW(IntPtr.Zero, text, AppName, 0x40 /* MB_ICONINFORMATION */);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
