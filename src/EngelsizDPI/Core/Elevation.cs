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
    /// <param name="args">Uygulamanın argümanları (<see cref="AutoStart.FromTaskArg"/> çıkarılmış).</param>
    /// <param name="fromTask">Bu süreci Launch görevi başlattı; yine de yönetici değilsek görev bize yetki veremiyor.</param>
    public static void Elevate(string[] args, bool fromTask)
    {
        // Görev değişken argüman taşıyamaz; bu yüzden yalnızca argümansız normal açılışta kullanılır. Kurulu kopyanın
        // kendisi bozuk sayılsa da görevle açılır: geri dönüşü (StartupHealth) yönetici olarak o yapar.
        if (!fromTask && args.Length == 0 &&
            (Installer.IsRunningInstalled || Installer.IsInstalledAndCurrent()) &&
            AutoStart.TaskExists(AutoStart.LauncherTask) && AutoStart.Run(AutoStart.LauncherTask))
            return;

        if (fromTask) Log.Write("Başlatma görevi yönetici yetkisi vermedi (hesap yönetici değil mi?); UAC isteniyor");
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
