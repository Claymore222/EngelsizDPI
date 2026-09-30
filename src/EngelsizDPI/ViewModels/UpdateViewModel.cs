using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EngelsizDPI.Core;

namespace EngelsizDPI.ViewModels;

/// <summary>
/// Güncellemeler. Kullanıcı sormadan kurulum yapılmaz:
/// - "Güncellemeleri denetle" yalnızca haber verir; kullanıcı "Şimdi yükle" derse indirilip kurulur,
///   "Sonra" derse bant kapanır ve tepsi menüsünden istediği zaman kurabilir.
/// - Arka planda (açılıştan kısa süre sonra ve 6 saatte bir) yeni sürüm bulunursa, "Otomatik güncelle" açıksa
///   sessizce indirilir ve bir sonraki açılışta kurulur; kapalıysa yalnızca haber verilir.
/// Böylece kullanıcı bir Discord görüşmesinin ortasında bağlantısını kaybetmez.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly AppSettings _settings;
    private UpdateInfo? _info;
    private string? _downloadedPath;
    private bool _loading = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowBanner), nameof(TrayLabel))] private bool _isAvailable;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowBanner))] private bool _isDismissed;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PrimaryLabel), nameof(TrayLabel))] private bool _isReady;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private string _bannerDetail = "";
    [ObservableProperty] private string _statusText = "Güncellemeleri denetle";
    [ObservableProperty] private bool _autoUpdate;

    /// <summary>İndirilmiş güncellemenin yolu; App bunu alıp uygulamayı yeniden başlatır.</summary>
    public event Action<string>? RestartRequested;

    public bool CanCheck => UpdateService.CanCheck;
    public bool ShowBanner => IsAvailable && !IsDismissed;
    public string PrimaryLabel => !UpdateService.CanSelfUpdate ? "İndir" : IsReady ? "Şimdi kur" : "Şimdi yükle";
    public string TrayLabel => _info is null ? "" : IsReady ? $"v{_info.Version} güncellemesini kur" : $"v{_info.Version} güncellemesini yükle";

    public UpdateViewModel(AppSettings settings)
    {
        _settings = settings;
        _autoUpdate = settings.AutoUpdate;
        _loading = false;
    }

    partial void OnAutoUpdateChanged(bool value)
    {
        if (_loading) return;
        _settings.AutoUpdate = value;
        _settings.Save();
    }

    public void StartBackgroundChecks()
    {
        if (!UpdateService.CanCheck) return;
        _ = Task.Run(async () =>
        {
            await UpdateService.CleanupAsync();
            await Task.Delay(FirstCheckDelay);
            using var timer = new PeriodicTimer(CheckInterval);
            do
            {
                await Dispatcher.UIThread.InvokeAsync(() => CheckCoreAsync(manual: false));
            } while (await timer.WaitForNextTickAsync());
        });
    }

    [RelayCommand]
    private Task CheckAsync() => CheckCoreAsync(manual: true);

    private async Task CheckCoreAsync(bool manual)
    {
        if (IsDownloading) return;

        // Zaten bulunmuş bir güncelleme varsa yeniden sormaya gerek yok; kapatılmış bandı tekrar göster.
        if (IsAvailable)
        {
            if (manual) IsDismissed = false;
            return;
        }

        if (manual) StatusText = "Denetleniyor…";
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info is null)
            {
                StatusText = $"Güncel · v{UpdateService.CurrentVersion}";
                return;
            }

            _info = info;
            StatusText = $"v{info.Version} mevcut";
            IsDismissed = false;
            ShowAvailable();
            IsAvailable = true;

            if (!manual && AutoUpdate && UpdateService.CanSelfUpdate) await DownloadAsync();
        }
        catch (Exception)
        {
            // Arka plan denetimindeki ağ hataları kullanıcıyı rahatsız etmemeli.
            if (manual) StatusText = "Sunucuya ulaşılamadı";
        }
    }

    /// <summary>İndirilmemişse indirir, sonra kurup uygulamayı yeniden başlatır.</summary>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_info is null) return;

        if (!UpdateService.CanSelfUpdate)
        {
            Shell.OpenUrl(_info.ReleaseUrl);
            return;
        }

        IsDismissed = false;
        if (!IsReady) await DownloadAsync();
        if (IsReady && _downloadedPath is not null)
        {
            BannerText = $"v{_info.Version} kuruluyor…";
            BannerDetail = "Uygulama birkaç saniye içinde yeniden açılacak.";
            RestartRequested?.Invoke(_downloadedPath);
        }
    }

    [RelayCommand]
    private void Dismiss() => IsDismissed = true;

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        if (_info is not null) Shell.OpenUrl(_info.ReleaseUrl);
    }

    private async Task DownloadAsync()
    {
        if (_info is null || IsDownloading) return;
        IsDownloading = true;
        Progress = 0;
        BannerText = $"v{_info.Version} indiriliyor…";
        BannerDetail = "İndirme bitene kadar uygulamayı kullanmaya devam edebilirsiniz.";
        try
        {
            var progress = new Progress<double>(p => Progress = p * 100);
            _downloadedPath = await UpdateService.DownloadAsync(_info, progress);
            IsReady = true;
            BannerText = $"EngelsizDPI v{_info.Version} kurulmaya hazır";
            BannerDetail = "Bir sonraki açılışta kendiliğinden kurulur. İsterseniz şimdi kurabilirsiniz; bağlantı birkaç saniye kesilir.";
        }
        catch (Exception e)
        {
            ShowAvailable();
            BannerDetail = "İndirilemedi: " + e.Message;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void ShowAvailable()
    {
        BannerText = $"EngelsizDPI v{_info!.Version} mevcut";
        BannerDetail = UpdateService.CanSelfUpdate
            ? "Şimdi yükleyebilir ya da istediğiniz zaman tepsi menüsünden yükleyebilirsiniz."
            : "Yeni sürümü indirme sayfasından alabilirsiniz.";
    }

    public void ReportApplyFailure(Exception e)
    {
        IsReady = false;
        _downloadedPath = null;
        ShowAvailable();
        BannerDetail = "Kurulamadı: " + e.Message;
    }
}
