using Avalonia;
using EngelsizDPI.Core;

namespace EngelsizDPI;

internal static class Program
{
    private const string InstanceMutexName = "Local\\EngelsizDPI.Instance";
    private const string ShowEventName = "Local\\EngelsizDPI.Show";
    private const string ExitEventName = "Local\\EngelsizDPI.Exit";

    /// <summary>Oturum açılışında --tray ile başlatıldığında pencere gösterilmez.</summary>
    public static bool StartInTray { get; private set; }

    /// <summary>Güncelleme sonrası yeniden başlatmada, öncesinde bağlıysa yeniden bağlanılır.</summary>
    public static bool ForceConnect { get; private set; }

    /// <summary>İndirilen exe kendini kurduktan sonra kurulu kopya bu bayrakla açılır.</summary>
    public static bool JustInstalled { get; private set; }

    /// <summary>İkinci bir kopya açılmaya çalışıldığında tetiklenir (mevcut pencere öne getirilir).</summary>
    public static event Action? ShowRequested;

    /// <summary>Kurulum/kaldırma, çalışan kopyanın kapanmasını istediğinde tetiklenir.</summary>
    public static event Action? ExitRequested;

    [STAThread]
    public static void Main(string[] args)
    {
        StartInTray = args.Contains("--tray");
        ForceConnect = args.Contains("--connect");
        JustInstalled = args.Contains("--installed");

        using var mutex = new Mutex(false, InstanceMutexName);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);

        if (OperatingSystem.IsWindows())
        {
            if (args.Contains("--uninstall"))
            {
                RunUninstall(mutex, exitEvent, quiet: args.Contains("--quiet"));
                return;
            }
            if (HandleInstall(args, mutex, showEvent, exitEvent)) return;
        }

        // Güncellemeden/kurulumdan sonra başlatılan kopya, öncekinin kapanmasını bekler.
        if (!AcquireInstance(mutex, args.Contains("--updated") ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
        {
            showEvent.Set();
            return;
        }

        // Daha önce arka planda indirilmiş bir güncelleme varsa arayüz açılmadan uygulanır.
        if (TryApplyPendingUpdate(args)) return;

        if (OperatingSystem.IsWindows()) Installer.RefreshInstalledState();

        StartListener(showEvent, () => ShowRequested?.Invoke());
        StartListener(exitEvent, () => ExitRequested?.Invoke());

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// İndirilen exe kurulu kopya değilse: gerekirse kendini kurar ve kurulu kopyayı başlatır.
    /// true dönerse bu süreç işini bitirmiştir ve kapanmalıdır.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool HandleInstall(string[] args, Mutex mutex, EventWaitHandle showEvent, EventWaitHandle exitEvent)
    {
        var action = Installer.Decide();
        if (action == LaunchAction.RunHere) return false;

        var forward = args.Where(a => a is not ("--updated" or "--installed")).ToList();

        if (action == LaunchAction.LaunchInstalled)
        {
            // Kurulu kopya zaten açıksa öne getir, değilse başlat.
            if (AcquireInstance(mutex, TimeSpan.Zero))
            {
                mutex.ReleaseMutex();
                Installer.LaunchInstalled(forward);
            }
            else
            {
                showEvent.Set();
            }
            return true;
        }

        // Eski sürüm açıksa kapanmasını iste; dosyası serbest kalsın ve iki kopya aynı anda çalışmasın.
        if (!AcquireInstance(mutex, TimeSpan.Zero))
        {
            exitEvent.Set();
            if (!AcquireInstance(mutex, TimeSpan.FromSeconds(15)))
            {
                Installer.Inform("EngelsizDPI şu anda açık ve kapatılamadı. Tepsiden çıkış yapıp tekrar deneyin.");
                return true;
            }
            // Sinyal kimse dinlemeden açık kaldıysa, birazdan açılacak kurulu kopya onu alıp hemen kapanmasın.
            exitEvent.Reset();
        }

        try
        {
            Installer.Install();
        }
        catch (Exception e)
        {
            Log.Write("Kurulum başarısız: " + e.Message);
            mutex.ReleaseMutex();
            Installer.Inform("EngelsizDPI kurulamadı:\n" + e.Message + "\n\nUygulama kurulmadan bu konumdan açılacak.");
            return false;
        }

        mutex.ReleaseMutex();
        Installer.LaunchInstalled(forward.Append("--installed"));
        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RunUninstall(Mutex mutex, EventWaitHandle exitEvent, bool quiet)
    {
        if (!quiet && !Installer.Confirm("EngelsizDPI bilgisayarınızdan kaldırılsın mı?\n\nAyarlarınız ve site listeniz de silinecek."))
            return;

        if (!AcquireInstance(mutex, TimeSpan.Zero))
        {
            exitEvent.Set();
            AcquireInstance(mutex, TimeSpan.FromSeconds(15));
            exitEvent.Reset();
        }

        Installer.Uninstall();
        if (!quiet) Installer.Inform("EngelsizDPI kaldırıldı.");
    }

    private static void StartListener(EventWaitHandle handle, Action onSignal) =>
        new Thread(() =>
        {
            while (handle.WaitOne()) onSignal();
        }) { IsBackground = true }.Start();

    private static bool AcquireInstance(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // Önceki kopya kapanırken mutex'i serbest bırakmadı (ör. güncelleme sonrası çıkış); artık bizim.
            return true;
        }
    }

    private static bool TryApplyPendingUpdate(string[] args)
    {
        if (!UpdateService.CanSelfUpdate || !AppSettings.Load().AutoUpdate) return false;
        var pending = UpdateService.FindPendingUpdate();
        if (pending is null) return false;

        try
        {
            UpdateService.ApplyAndRestart(pending, args.Where(a => a != "--updated"));
            return true;
        }
        catch (Exception)
        {
            // Güncelleme uygulanamazsa mevcut sürümle normal şekilde devam edilir.
            return false;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
