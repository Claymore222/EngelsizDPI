using System.Diagnostics;
using System.Security;
using System.Text;

namespace EngelsizDPI.Core;

/// <summary>
/// Windows oturum açılışında başlatma. Uygulama yönetici izni gerektirdiği için Run kayıt anahtarı
/// yerine "en yüksek yetkiyle" çalışan bir zamanlanmış görev kullanılır (her açılışta UAC sorulmaz).
/// Görev XML ile oluşturulur çünkü schtasks'ın varsayılanları pilde başlatmayı engeller ve 72 saat
/// sonra görevi sonlandırır.
/// </summary>
public static class AutoStart
{
    private const string TaskName = "EngelsizDPI";

    public static bool IsEnabled() => OperatingSystem.IsWindows() && RunSchtasks("/query", "/tn", TaskName) == 0;

    public static void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;

        if (!enabled)
        {
            RunSchtasks("/delete", "/tn", TaskName, "/f");
            return;
        }

        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>EngelsizDPI - oturum açılışında başlat</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT5S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>--tray</Arguments></Exec>
              </Actions>
            </Task>
            """;

        var xmlPath = Path.Combine(Path.GetTempPath(), $"engelsizdpi-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            if (RunSchtasks("/create", "/tn", TaskName, "/xml", xmlPath, "/f") != 0)
                throw new InvalidOperationException("Başlangıç görevi oluşturulamadı.");
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
