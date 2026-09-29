using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace EngelsizDPI.Core;

/// <summary>
/// Uygulama normal kullanıcı olarak başlar (manifest: asInvoker) ve yönetici yetkisini şöyle alır:
/// kuruluysa "EngelsizDPI\Launch" zamanlanmış görevini çalıştırır (UAC sorulmaz); kurulu değilse ya da görev
/// yoksa bir kereliğine UAC ile kendini yönetici olarak yeniden başlatır (kurulum o sırada görevi oluşturur).
/// </summary>
[SupportedOSPlatform("windows")]
public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Yönetici olmayan süreçte çağrılır: uygun yoldan yönetici kopyayı başlatır. Bu süreç ardından kapanmalıdır.
    /// </summary>
    public static void Elevate(string[] args)
    {
        // Görev argüman taşıyamaz; bu yüzden yalnızca argümansız, kurulu kopyanın normal açılışında kullanılır.
        if (args.Length == 0 && Installer.IsInstalledAndCurrent() && AutoStart.TaskExists(AutoStart.LauncherTask) &&
            AutoStart.Run(AutoStart.LauncherTask))
            return;

        RelaunchWithUac(args);
    }

    private static void RelaunchWithUac(string[] args)
    {
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = string.Join(' ', args.Select(Quote)),
        };
        try
        {
            Process.Start(psi);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // Kullanıcı UAC penceresinde "Hayır" dedi.
        }
    }

    private static string Quote(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;
}
