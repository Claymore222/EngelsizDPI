using Avalonia;
using EngelsizDPI.Core;

namespace EngelsizDPI;

internal static class Program
{
    private const string InstanceMutexName = "Local\\EngelsizDPI.Instance";
    private const string ShowEventName = "Local\\EngelsizDPI.Show";

    /// <summary>Oturum açılışında --tray ile başlatıldığında pencere gösterilmez.</summary>
    public static bool StartInTray { get; private set; }

    /// <summary>Güncelleme sonrası yeniden başlatmada, öncesinde bağlıysa yeniden bağlanılır.</summary>
    public static bool ForceConnect { get; private set; }

    /// <summary>İkinci bir kopya açılmaya çalışıldığında tetiklenir (mevcut pencere öne getirilir).</summary>
    public static event Action? ShowRequested;

    [STAThread]
    public static void Main(string[] args)
    {
        StartInTray = args.Contains("--tray");
        ForceConnect = args.Contains("--connect");

        using var mutex = new Mutex(false, InstanceMutexName);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        // Güncellemeden sonra başlatılan yeni sürüm, eski sürümün kapanmasını bekler.
        if (!AcquireInstance(mutex, args.Contains("--updated") ? TimeSpan.FromSeconds(20) : TimeSpan.Zero))
        {
            showEvent.Set();
            return;
        }

        // Daha önce arka planda indirilmiş bir güncelleme varsa arayüz açılmadan uygulanır.
        if (TryApplyPendingUpdate(args)) return;

        var listener = new Thread(() =>
        {
            while (showEvent.WaitOne()) ShowRequested?.Invoke();
        }) { IsBackground = true };
        listener.Start();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

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
