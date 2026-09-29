using System.Diagnostics;
using System.Security;
using System.Text;

namespace EngelsizDPI.Core;

/// <summary>
/// "En yüksek ayrıcalıklarla" çalışan zamanlanmış görevler (eski DNSChanger'daki schtasks /rl highest ile aynı
/// yöntem). Yönetici olarak oluşturulan böyle bir görev, sonradan UAC sorulmadan yönetici yetkisiyle başlar:
///
/// - EngelsizDPI\Launch: tetikleyicisi yok; uygulama normal kullanıcı olarak açıldığında kendini bununla
///   yeniden başlatır (bkz. <see cref="Elevation"/>). Kurulumda oluşturulur.
/// - EngelsizDPI\Autostart: oturum açılır açılmaz, beklemeden --tray ile başlatır. Ayarlardan açılıp kapanır.
///
/// Görevler XML ile oluşturulur çünkü schtasks'ın varsayılanları pilde başlatmayı engeller ve 72 saat sonra
/// görevi sonlandırır.
/// </summary>
public static class AutoStart
{
    public const string LauncherTask = @"EngelsizDPI\Launch";
    private const string AutostartTask = @"EngelsizDPI\Autostart";
    private const string LegacyAutostartTask = "EngelsizDPI"; // v1.1.0 ve öncesi

    public static bool IsEnabled() =>
        OperatingSystem.IsWindows() && (TaskExists(AutostartTask) || TaskExists(LegacyAutostartTask));

    public static void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        RunSchtasks("/delete", "/tn", LegacyAutostartTask, "/f");

        if (!enabled)
        {
            RunSchtasks("/delete", "/tn", AutostartTask, "/f");
            return;
        }
        CreateTask(AutostartTask, "Oturum açılışında başlat", CurrentExe(), logonTrigger: true, arguments: "--tray");
    }

    /// <summary>UAC'siz başlatma görevini (yeniden) oluşturur.</summary>
    public static void EnsureLauncher(string exe) =>
        CreateTask(LauncherTask, "Uygulamayı UAC sormadan başlat", exe, logonTrigger: false, arguments: null);

    public static bool TaskExists(string name) => RunSchtasks("/query", "/tn", name) == 0;

    /// <summary>Görevi hemen çalıştırır; başarılıysa true.</summary>
    public static bool Run(string name) => RunSchtasks("/run", "/tn", name) == 0;

    public static void RemoveAll()
    {
        RunSchtasks("/delete", "/tn", LauncherTask, "/f");
        RunSchtasks("/delete", "/tn", AutostartTask, "/f");
        RunSchtasks("/delete", "/tn", LegacyAutostartTask, "/f");
    }

    private static string CurrentExe() =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");

    private static void CreateTask(string name, string description, string exe, bool logonTrigger, string? arguments)
    {
        var user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        var trigger = logonTrigger
            ? $"<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>"
            : "";
        var args = arguments is null ? "" : $"<Arguments>{SecurityElement.Escape(arguments)}</Arguments>";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>EngelsizDPI - {SecurityElement.Escape(description)}</Description></RegistrationInfo>
              {trigger}
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <!-- Uygulama tepside çalışırken görev "çalışıyor" sayılır; yeni açılış yine başlamalı ki mevcut pencereyi öne getirsin. -->
                <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command>{args}</Exec>
              </Actions>
            </Task>
            """;

        var xmlPath = Path.Combine(Path.GetTempPath(), $"engelsizdpi-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            if (RunSchtasks("/create", "/tn", name, "/xml", xmlPath, "/f") != 0)
                throw new InvalidOperationException($"Zamanlanmış görev oluşturulamadı: {name}");
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static int RunSchtasks(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}
