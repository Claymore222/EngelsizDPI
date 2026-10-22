using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace EngelsizDPI.Core;

/// <summary>
/// "En yüksek ayrıcalıklarla" çalışan zamanlanmış görevler (eski DNSChanger'daki schtasks /rl highest ile aynı
/// yöntem). Yönetici olarak oluşturulan böyle bir görev, sonradan UAC sorulmadan yönetici yetkisiyle başlar:
///
/// - EngelsizDPI\Launch-&lt;SID&gt;: tetikleyicisi yok; uygulama normal kullanıcı olarak açıldığında kendini bununla
///   yeniden başlatır (bkz. <see cref="Elevation"/>). Kurulumda ve her açılışta oluşturulur.
/// - EngelsizDPI\Autostart-&lt;SID&gt;: oturum açılır açılmaz, beklemeden --tray ile başlatır. Ayarlardan açılıp kapanır.
///
/// Görevler kullanıcıya özeldir (adın sonunda kullanıcının SID'i): bir görev yalnızca oluşturan kullanıcının
/// oturumunda çalışır, ortak bir ad olsaydı aynı bilgisayardaki ikinci kullanıcı birincinin görevini ezerdi.
///
/// Görevler XML ile oluşturulur çünkü schtasks'ın varsayılanları pilde başlatmayı engeller ve 72 saat sonra
/// görevi sonlandırır.
/// </summary>
public static partial class AutoStart
{
    private const string Folder = "EngelsizDPI";

    /// <summary>
    /// Launch görevinin exe'ye verdiği argüman. Görevle başlatılan kopya yine de yönetici değilse (hesap artık
    /// yönetici değil) görevi tekrar çalıştırmak sonsuz döngü olurdu; bu argümanı gören kopya UAC'ye düşer.
    /// </summary>
    public const string FromTaskArg = "--from-task";

    // Bütün kullanıcıların ortak kullandığı eski adlar: v1.2.0–v1.3.x ve v1.1.0 öncesi (kök klasör).
    private static readonly string[] SharedLauncherTasks = [Folder + @"\Launch"];
    private static readonly string[] SharedAutostartTasks = [Folder + @"\Autostart", "EngelsizDPI"];

    private static string UserSid { get; } = OperatingSystem.IsWindows()
        ? WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName
        : Environment.UserName;

    public static string LauncherTask => $@"{Folder}\Launch-{UserSid}";
    private static string AutostartTask => $@"{Folder}\Autostart-{UserSid}";

    public static bool IsEnabled() =>
        OperatingSystem.IsWindows() && (TaskExists(AutostartTask) || SharedAutostartTasks.Any(IsOwnTask));

    public static void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        // Bu kullanıcıya ait eski ortak görev taşınır; başka kullanıcınınkine dokunulmaz.
        foreach (var task in SharedAutostartTasks.Where(IsOwnTask)) Delete(task);

        if (!enabled)
        {
            Delete(AutostartTask);
            return;
        }
        CreateTask(AutostartTask, "Oturum açılışında başlat", CurrentExe(), logonTrigger: true, arguments: "--tray");
    }

    /// <summary>UAC'siz başlatma görevini (yeniden) oluşturur.</summary>
    public static void EnsureLauncher(string exe)
    {
        CreateTask(LauncherTask, "Uygulamayı UAC sormadan başlat", exe, logonTrigger: false, arguments: FromTaskArg);
        // Ortak eski görev hangi kullanıcıya aitse onun oturumunda çalışıyordu; kullanıcılar ilk açılışta kendi
        // görevlerini oluşturur.
        foreach (var task in SharedLauncherTasks) Delete(task);
    }

    public static bool TaskExists(string name) => RunSchtasks("/query", "/tn", name).ExitCode == 0;

    /// <summary>Görevi hemen çalıştırır; başarılıysa true.</summary>
    public static bool Run(string name) => RunSchtasks("/run", "/tn", name).ExitCode == 0;

    /// <summary>Bütün kullanıcıların görevlerini siler (kaldırma).</summary>
    public static void RemoveAll()
    {
        var (_, csv) = RunSchtasks("/query", "/fo", "csv", "/nh");
        var ours = csv.Split('\n')
            .Select(line => line.Split(',')[0].Trim().Trim('"'))
            .Where(name => name.StartsWith($@"\{Folder}\", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var task in ours.Concat(SharedLauncherTasks).Concat(SharedAutostartTasks)) Delete(task);
    }

    /// <summary>Görev bu kullanıcı adına mı çalışıyor? (Görev XML'indeki UserId, SID ya da DOMAIN\kullanıcı olabilir.)</summary>
    private static bool IsOwnTask(string name)
    {
        var (code, xml) = RunSchtasks("/query", "/tn", name, "/xml");
        if (code != 0) return false;
        var owner = UserIdRegex().Match(xml).Groups[1].Value.Trim();
        return owner.Equals(UserSid, StringComparison.OrdinalIgnoreCase) ||
               owner.Equals($@"{Environment.UserDomainName}\{Environment.UserName}", StringComparison.OrdinalIgnoreCase) ||
               owner.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase);
    }

    private static void Delete(string name) => RunSchtasks("/delete", "/tn", name, "/f");

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
            if (RunSchtasks("/create", "/tn", name, "/xml", xmlPath, "/f").ExitCode != 0)
                throw new InvalidOperationException($"Zamanlanmış görev oluşturulamadı: {name}");
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static (int ExitCode, string Output) RunSchtasks(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output);
    }

    [GeneratedRegex(@"<UserId>([^<]*)</UserId>")]
    private static partial Regex UserIdRegex();
}
