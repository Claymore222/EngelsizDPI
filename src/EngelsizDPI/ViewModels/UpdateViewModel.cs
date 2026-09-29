using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EngelsizDPI.Core;

namespace EngelsizDPI.ViewModels;

/// <summary>
/// Güncelleme denetimi: açılıştan kısa süre sonra ve ardından 6 saatte bir GitHub'a bakar. Otomatik güncelleme
/// açıksa yeni sürümü sessizce indirir; kurulum bir sonraki açılışta ya da kullanıcı "Yeniden başlat"a
/// bastığında yapılır. Böylece kullanıcı bir Discord görüşmesinin ortasında bağlantısını kaybetmez.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly AppSettings _settings;
    private UpdateInfo? _info;
    private string? _downloadedPath;
    private bool _loading = true;

    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _bannerText = "";
    [ObservableProperty] private string? _checkMessage;
    [ObservableProperty] private bool _autoUpdate;

    /// <summary>İndirilmiş güncellemenin yolu; App bunu alıp uygulamayı yeniden başlatır.</summary>
    public event Action<string>? RestartRequested;

    public bool CanCheck => UpdateService.CanCheck;
    public string ActionLabel => IsReady ? "Yeniden başlat" : UpdateService.CanSelfUpdate ? "Güncelle" : "İndir";

    public UpdateViewModel(AppSettings settings)
    {
        _settings = settings;
        _autoUpdate = settings.AutoUpdate;
        _loading = false;
    }

    partial void OnIsReadyChanged(bool value) => OnPropertyChanged(nameof(ActionLabel));

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
        if (IsDownloading || IsReady) return;
        if (manual) CheckMessage = "Denetleniyor…";

        try
        {
            var info = await UpdateService.CheckAsync();
            if (info is null)
            {
                if (manual) CheckMessage = $"En güncel sürümü kullanıyorsunuz (v{UpdateService.CurrentVersion}).";
                return;
            }

            _info = info;
            IsAvailable = true;
            BannerText = $"Yeni sürüm hazır: v{info.Version}";
            CheckMessage = null;

            if (AutoUpdate && UpdateService.CanSelfUpdate) await DownloadAsync();
        }
        catch (Exception)
        {
            // Arka plan denetimindeki ağ hataları kullanıcıyı rahatsız etmemeli.
            if (manual) CheckMessage = "Güncelleme sunucusuna ulaşılamadı.";
        }
    }

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (_info is null) return;

        if (!UpdateService.CanSelfUpdate)
        {
            OpenReleasePage();
            return;
        }

        if (!IsReady) await DownloadAsync();
        if (IsReady && _downloadedPath is not null) RestartRequested?.Invoke(_downloadedPath);
    }

    private async Task DownloadAsync()
    {
        if (_info is null || IsDownloading) return;
        IsDownloading = true;
        Progress = 0;
        BannerText = $"v{_info.Version} indiriliyor…";
        try
        {
            var progress = new Progress<double>(p => Progress = p * 100);
            _downloadedPath = await UpdateService.DownloadAsync(_info, progress);
            IsReady = true;
            BannerText = $"v{_info.Version} indirildi";
        }
        catch (Exception e)
        {
            BannerText = "Güncelleme indirilemedi: " + e.Message;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    public void ReportApplyFailure(Exception e)
    {
        IsReady = false;
        _downloadedPath = null;
        BannerText = "Güncelleme uygulanamadı: " + e.Message;
    }

    private void OpenReleasePage()
    {
        if (_info is not null) Shell.OpenUrl(_info.ReleaseUrl);
    }
}
