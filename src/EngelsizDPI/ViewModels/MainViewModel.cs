using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EngelsizDPI.Core;

namespace EngelsizDPI.ViewModels;

public enum ConnectionState { Off, Busy, On, Warning, Error }

public sealed partial class PackItem(SitePack pack, bool enabled) : ObservableObject
{
    public SitePack Pack { get; } = pack;
    public string Name => Pack.Name;

    [ObservableProperty] private bool _enabled = enabled;

    /// <summary>Son bağlantı testinin sonucu; test yapılmadıysa null.</summary>
    [ObservableProperty] private bool? _reachable;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBypassEngine? _engine;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loading = true;

    [ObservableProperty] private ConnectionState _state = ConnectionState.Off;
    [ObservableProperty] private string _statusText = "Bağlı değil";
    [ObservableProperty] private string _statusDetail = "Başlamak için düğmeye dokunun";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private BypassProfile _selectedProfile;
    [ObservableProperty] private bool _dnsRedirect;
    [ObservableProperty] private bool _connectOnLaunch;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _newDomain = "";
    [ObservableProperty] private string? _conflictText;

    /// <summary>Kapatılabilir bilgi bandı (ör. kurulum tamamlandı).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNotice))] private string? _notice;

    public IReadOnlyList<BypassProfile> Profiles { get; } = Core.Profiles.All;
    public UpdateViewModel Update { get; }
    public ObservableCollection<PackItem> Packs { get; }
    public ObservableCollection<string> CustomDomains { get; }

    public bool IsConnected => State is ConnectionState.On or ConnectionState.Warning;
    public bool HasConflict => ConflictText is not null;
    public bool HasNotice => Notice is not null;
    public string ToggleLabel => IsConnected ? "Bağlantıyı kes" : "Bağlan";
    public string AppVersion => "v" + UpdateService.CurrentVersion;

    public MainViewModel(IBypassEngine? engine, AppSettings settings)
    {
        _engine = engine;
        _settings = settings;
        Update = new UpdateViewModel(settings);

        _selectedProfile = Core.Profiles.ById(settings.ProfileId);
        _dnsRedirect = settings.DnsRedirect;
        _connectOnLaunch = settings.ConnectOnLaunch;
        _startWithWindows = SafeAutoStartState();

        Packs = new(SitePacks.All.Select(p => new PackItem(p, settings.EnabledPacks.Contains(p.Id))));
        foreach (var item in Packs) item.PropertyChanged += OnPackChanged;
        CustomDomains = new(settings.CustomDomains);

        if (_engine is null)
        {
            State = ConnectionState.Error;
            StatusText = "Desteklenmiyor";
            StatusDetail = "Bu işletim sistemi henüz desteklenmiyor.";
        }
        else
        {
            _engine.UnexpectedExit += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                State = ConnectionState.Error;
                StatusText = "Bağlantı koptu";
                StatusDetail = "GoodbyeDPI beklenmedik şekilde kapandı" +
                    (_engine.LastOutputLine() is { } line ? $": {line}" : ". Tekrar bağlanmayı deneyin.");
            });
            RefreshConflicts();
        }

        _loading = false;
    }

    /// <summary>Oturum açılışında / uygulama başlarken ayar açıksa bağlanır.</summary>
    public async Task StartupAsync()
    {
        if (!(ConnectOnLaunch || Program.ForceConnect) || _engine is null) return;

        // Oturum açılışında ağ bağdaştırıcısı birkaç saniye geç hazır olabilir; aksi halde test boşuna başarısız olur.
        for (var i = 0; i < 30 && !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); i++)
            await Task.Delay(1000);

        await ConnectAsync();
    }

    partial void OnStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ToggleLabel));
    }

    partial void OnConflictTextChanged(string? value) => OnPropertyChanged(nameof(HasConflict));

    partial void OnSelectedProfileChanged(BypassProfile value)
    {
        if (_loading) return;
        _settings.ProfileId = value.Id;
        SaveAndReconnect();
    }

    partial void OnDnsRedirectChanged(bool value)
    {
        if (_loading) return;
        _settings.DnsRedirect = value;
        SaveAndReconnect();
    }

    partial void OnConnectOnLaunchChanged(bool value)
    {
        if (_loading) return;
        _settings.ConnectOnLaunch = value;
        _settings.Save();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) return;
        try
        {
            AutoStart.SetEnabled(value);
        }
        catch (Exception e)
        {
            StatusDetail = "Başlangıç ayarı değiştirilemedi: " + e.Message;
            _loading = true;
            StartWithWindows = !value;
            _loading = false;
        }
    }

    private void OnPackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PackItem.Enabled)) return;
        _settings.EnabledPacks = Packs.Where(p => p.Enabled).Select(p => p.Pack.Id).ToList();
        SaveAndReconnect();
    }

    [RelayCommand]
    private void AddDomain()
    {
        var domain = NormalizeDomain(NewDomain);
        if (domain is null)
        {
            StatusDetail = "Geçerli bir alan adı girin (ör. reddit.com).";
            return;
        }
        NewDomain = "";
        if (CustomDomains.Contains(domain, StringComparer.OrdinalIgnoreCase)) return;

        CustomDomains.Add(domain);
        _settings.CustomDomains = CustomDomains.ToList();
        SaveAndReconnect();
    }

    [RelayCommand]
    private void RemoveDomain(string domain)
    {
        CustomDomains.Remove(domain);
        _settings.CustomDomains = CustomDomains.ToList();
        SaveAndReconnect();
    }

    [RelayCommand]
    private Task ToggleAsync() => IsConnected || State == ConnectionState.Busy ? DisconnectAsync() : ConnectAsync();

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        if (_engine is null) return;
        await _gate.WaitAsync();
        try
        {
            await _engine.StopAsync();
            SetOff();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ConnectAsync()
    {
        if (_engine is null) return;
        await _gate.WaitAsync();
        try
        {
            await ConnectCoreAsync(SelectedProfile);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Yöntemleri sırayla dener ve etkin sitelerin hepsini açan ilkini seçer.</summary>
    [RelayCommand]
    private async Task AutoFindAsync()
    {
        if (_engine is null) return;
        await _gate.WaitAsync();
        try
        {
            BypassProfile? best = null, lastTried = null;
            var bestScore = -1;
            for (var i = 0; i < Profiles.Count; i++)
            {
                lastTried = Profiles[i];
                var score = await ConnectCoreAsync(lastTried, $"Deneniyor: {lastTried.Name} ({i + 1}/{Profiles.Count})");
                if (score > bestScore) (best, bestScore) = (lastTried, score);
                if (score == ActivePacks().Count) break;
            }

            if (best is null) return;
            if (best != SelectedProfile)
            {
                _loading = true;
                SelectedProfile = best;
                _loading = false;
                _settings.ProfileId = best.Id;
                _settings.Save();
            }
            // Döngü tam başarı olmadan bittiyse motor son denenen yöntemle çalışıyordur.
            if (best != lastTried) await ConnectCoreAsync(best);
        }
        finally
        {
            _gate.Release();
        }
    }

    [RelayCommand]
    private async Task FixConflictsAsync()
    {
        if (_engine is null) return;
        var wasConnected = IsConnected;
        IsBusy = true;
        try
        {
            await _engine.RemoveConflictsAsync();
        }
        finally
        {
            IsBusy = false;
            RefreshConflicts();
        }
        // Eski araç kaldırılınca motor temiz bir sürücüyle yeniden başlatılır ve siteler yeniden test edilir.
        if (wasConnected) await ConnectAsync();
    }

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    [RelayCommand]
    private static void OpenLog()
    {
        try
        {
            if (!File.Exists(Log.FilePath)) Log.Write("Günlük oluşturuldu");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", Log.FilePath) { UseShellExecute = false });
        }
        catch (Exception) { /* Not Defteri açılamadı. */ }
    }

    /// <summary>Motoru verilen yöntemle başlatır ve siteleri test eder; erişilebilen paket sayısını döner.</summary>
    private async Task<int> ConnectCoreAsync(BypassProfile profile, string? busyText = null)
    {
        var hosts = _settings.BuildHostList();
        if (hosts.Count == 0)
        {
            await _engine!.StopAsync();
            SetOff();
            StatusDetail = "En az bir site seçin veya ekleyin.";
            return 0;
        }

        IsBusy = true;
        State = ConnectionState.Busy;
        StatusText = busyText ?? "Bağlanıyor…";
        StatusDetail = profile.Name;
        foreach (var p in Packs) p.Reachable = null;

        try
        {
            await _engine!.StartAsync(new EngineOptions(profile, hosts, DnsRedirect));

            var active = ActivePacks();
            if (active.Count == 0)
            {
                SetOn(ConnectionState.On, $"{profile.Name} · {hosts.Count} site");
                return 0;
            }

            if (busyText is null) StatusText = "Test ediliyor…";
            var results = await ConnectivityTester.TestPacksAsync(active.Select(p => p.Pack));
            foreach (var p in active) p.Reachable = results[p.Pack];
            Log.Write($"Test ({profile.Name}): " + string.Join(", ", results.Select(r => $"{r.Key.Name}={(r.Value ? "açık" : "kapalı")}")));

            var ok = results.Count(r => r.Value);
            if (ok == active.Count)
                SetOn(ConnectionState.On, $"{profile.Name} · tüm siteler erişilebilir");
            else
                SetOn(ConnectionState.Warning,
                    $"{string.Join(", ", results.Where(r => !r.Value).Select(r => r.Key.Name))} açılmadı. \"Otomatik bul\"u deneyin.");
            return ok;
        }
        catch (Exception e)
        {
            Log.Write($"Bağlanma hatası ({profile.Name}): {e.Message}");
            State = ConnectionState.Error;
            StatusText = "Başlatılamadı";
            StatusDetail = e.Message;
            RefreshConflicts();
            return -1;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<PackItem> ActivePacks() => Packs.Where(p => p.Enabled).ToList();

    private void SetOn(ConnectionState state, string detail)
    {
        State = state;
        StatusText = state == ConnectionState.On ? "Aktif" : "Kısmen aktif";
        StatusDetail = detail;
    }

    private void SetOff()
    {
        State = ConnectionState.Off;
        StatusText = "Bağlı değil";
        StatusDetail = "Başlamak için düğmeye dokunun";
        foreach (var p in Packs) p.Reachable = null;
    }

    private async void SaveAndReconnect()
    {
        _settings.Save();
        if (IsConnected || State == ConnectionState.Warning)
            await ConnectAsync();
    }

    private void RefreshConflicts()
    {
        var conflicts = _engine?.DetectConflicts() ?? [];
        ConflictText = conflicts.Count == 0 ? null : string.Join(" ", conflicts);
    }

    private static bool SafeAutoStartState()
    {
        try { return AutoStart.IsEnabled(); }
        catch (Exception) { return false; }
    }

    /// <summary>"https://www.Reddit.com/r/x" gibi girdileri "reddit.com" haline getirir.</summary>
    public static string? NormalizeDomain(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        if (!s.Contains("://")) s = "http://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.HostNameType != UriHostNameType.Dns) return null;

        var host = uri.IdnHost;
        if (host.StartsWith("www.")) host = host[4..];
        return host.Contains('.') ? host : null;
    }
}
