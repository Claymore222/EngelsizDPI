using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EngelsizDPI.Core;

namespace EngelsizDPI.ViewModels;

public enum ConnectionState { Off, Busy, On, Warning, Error }

public enum PackStatus { None, Testing, Open, Blocked }

public sealed partial class PackItem(SitePack pack, bool enabled, bool isCustom = false) : ObservableObject
{
    public SitePack Pack { get; } = pack;
    public bool IsCustom { get; } = isCustom;
    public string Name => Pack.Name;
    public string Initial => Pack.Name[..1].ToUpperInvariant();
    public string Subtitle => IsCustom ? "Özel site" : $"{Pack.Category} · {Pack.Domains.Count} alan adı";

    [ObservableProperty] private bool _enabled = enabled;
    [ObservableProperty] private PackStatus _status;

    public bool Matches(string query) =>
        query.Length == 0 ||
        Pack.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Pack.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Pack.Domains.Any(d => d.Contains(query, StringComparison.OrdinalIgnoreCase));

    public static PackItem Custom(string domain) =>
        new(new SitePack("custom:" + domain, domain, "Özel", $"https://{domain}/", [domain]), true, isCustom: true);
}

public sealed partial class ScanSuggestion(string domain) : ObservableObject
{
    public string Domain { get; } = domain;
    [ObservableProperty] private bool _selected = true;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBypassEngine? _engine;
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loading = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsConnected), nameof(ToggleLabel), nameof(SwitchOn))]
    private ConnectionState _state = ConnectionState.Off;

    [ObservableProperty] private string _statusText = "Bağlı değil";
    [ObservableProperty] private string _statusDetail = "Başlatmak için anahtarı açın";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private BypassProfile _selectedProfile;
    [ObservableProperty] private bool _dnsRedirect;
    [ObservableProperty] private bool _connectOnLaunch;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasConflict))] private string? _conflictText;

    /// <summary>Kapatılabilir bilgi bandı (ör. kurulum tamamlandı).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNotice))] private string? _notice;

    // Site listesi, arama ve ekleme paneli
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isAddOpen;
    [ObservableProperty] private string _newDomain = "";
    [ObservableProperty] private string? _addMessage;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _lastAddedDomain;

    public IReadOnlyList<BypassProfile> Profiles { get; } = Core.Profiles.All;
    public UpdateViewModel Update { get; }

    /// <summary>Paketler ve özel siteler tek listede; özel siteler en altta.</summary>
    public ObservableCollection<PackItem> Packs { get; } = [];

    /// <summary>Arama kutusuna göre süzülmüş liste (arayüz bunu gösterir).</summary>
    public ObservableCollection<PackItem> VisiblePacks { get; } = [];

    public ObservableCollection<ScanSuggestion> ScanResults { get; } = [];

    public bool IsConnected => State is ConnectionState.On or ConnectionState.Warning;
    public bool SwitchOn => State is ConnectionState.On or ConnectionState.Warning or ConnectionState.Busy;
    public bool HasConflict => ConflictText is not null;
    public bool HasNotice => Notice is not null;
    public bool HasScanResults => ScanResults.Count > 0;
    public string ToggleLabel => IsConnected ? "Bağlantıyı kes" : "Bağlan";
    public string AppVersion => "v" + UpdateService.CurrentVersion;
    public bool CanSuggest => UpdateService.Repo is not null;

    public MainViewModel(IBypassEngine? engine, AppSettings settings)
    {
        _engine = engine;
        _settings = settings;
        Update = new UpdateViewModel(settings);

        _selectedProfile = Core.Profiles.ById(settings.ProfileId);
        _dnsRedirect = settings.DnsRedirect;
        _connectOnLaunch = settings.ConnectOnLaunch;
        _startWithWindows = SafeAutoStartState();

        RebuildPacks();
        ScanResults.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasScanResults));
        SitePacks.Changed += _ => Dispatcher.UIThread.Post(OnCatalogChanged);

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

    // ---- Site listesi ----

    private void RebuildPacks()
    {
        foreach (var item in Packs) item.PropertyChanged -= OnPackChanged;
        Packs.Clear();
        foreach (var pack in SitePacks.All) Packs.Add(new PackItem(pack, _settings.IsPackEnabled(pack)));
        foreach (var domain in _settings.CustomDomains) Packs.Add(PackItem.Custom(domain));
        foreach (var item in Packs) item.PropertyChanged += OnPackChanged;
        ApplyFilter();
    }

    private void OnCatalogChanged()
    {
        var before = _settings.BuildHostList(SitePacks.All);
        RebuildPacks();
        if (IsConnected && !before.SequenceEqual(_settings.BuildHostList(SitePacks.All))) SaveAndReconnect();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        VisiblePacks.Clear();
        foreach (var item in Packs.Where(p => p.Matches(query))) VisiblePacks.Add(item);
    }

    private void OnPackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName != nameof(PackItem.Enabled) || sender is not PackItem { IsCustom: false } item) return;
        _settings.SetPackEnabled(item.Pack, item.Enabled);
        SaveAndReconnect();
    }

    [RelayCommand]
    private void ToggleAddPanel()
    {
        IsAddOpen = !IsAddOpen;
        AddMessage = null;
    }

    [RelayCommand]
    private async Task AddDomainAsync()
    {
        var domain = NormalizeDomain(NewDomain);
        if (domain is null)
        {
            AddMessage = "Geçerli bir alan adı yazın, ör. reddit.com";
            return;
        }
        NewDomain = "";
        LastAddedDomain = domain;

        if (SiteScanner.IsCovered(domain, _settings.BuildHostList(SitePacks.All)))
        {
            AddMessage = $"{domain} zaten listede.";
            return;
        }

        _settings.CustomDomains.Add(domain);
        _settings.Save();
        var item = PackItem.Custom(domain);
        item.PropertyChanged += OnPackChanged;
        Packs.Add(item);
        ApplyFilter();

        if (!IsConnected)
        {
            AddMessage = $"{domain} eklendi. Bağlandığınızda bu sitenin kullandığı diğer adresleri de tarayabilirsiniz.";
            return;
        }

        await ConnectAsync();
        await ScanAsync();
    }

    /// <summary>Son eklenen sitenin kullandığı ve engelli görünen diğer alan adlarını bulur.</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        if (LastAddedDomain is not { } domain) return;
        if (!IsConnected)
        {
            AddMessage = "Taramak için önce bağlanın; aksi halde engelli site açılamaz.";
            return;
        }

        IsScanning = true;
        ScanResults.Clear();
        AddMessage = $"{domain} taranıyor…";
        try
        {
            var found = await SiteScanner.FindBlockedRelatedDomainsAsync(domain, _settings.BuildHostList(SitePacks.All));
            foreach (var d in found) ScanResults.Add(new ScanSuggestion(d));
            AddMessage = found.Count == 0
                ? $"{domain} için engelli başka adres bulunmadı."
                : $"{domain} şu adresleri de kullanıyor ve engelli görünüyor:";
            Log.Write($"Tarama ({domain}): {(found.Count == 0 ? "yok" : string.Join(", ", found))}");
        }
        catch (Exception e)
        {
            AddMessage = $"{domain} açılamadı, taranamadı. Yöntemi değiştirip tekrar deneyin.";
            Log.Write($"Tarama hatası ({domain}): {e.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void AddSuggestions()
    {
        var chosen = ScanResults.Where(s => s.Selected).Select(s => s.Domain).ToList();
        foreach (var d in chosen.Where(d => !_settings.CustomDomains.Contains(d)))
        {
            _settings.CustomDomains.Add(d);
            var item = PackItem.Custom(d);
            item.PropertyChanged += OnPackChanged;
            Packs.Add(item);
        }
        ScanResults.Clear();
        ApplyFilter();
        AddMessage = chosen.Count > 0 ? $"{chosen.Count} adres eklendi." : null;
        SaveAndReconnect();
    }

    [RelayCommand]
    private void RemoveCustom(PackItem item)
    {
        if (!item.IsCustom) return;
        item.PropertyChanged -= OnPackChanged;
        _settings.CustomDomains.Remove(item.Pack.Domains[0]);
        Packs.Remove(item);
        ApplyFilter();
        SaveAndReconnect();
    }

    /// <summary>Son eklenen siteyi (ve bulunan adreslerini) GitHub'da herkes için öneri olarak açar.</summary>
    [RelayCommand]
    private void SuggestToEveryone()
    {
        if (UpdateService.Repo is not { } repo || LastAddedDomain is not { } domain) return;
        var related = _settings.CustomDomains.Where(d => d != domain).ToList();
        var domains = string.Join("\n", new[] { domain }.Concat(ScanResults.Select(s => s.Domain)).Concat(related).Distinct());
        var url = $"https://github.com/{repo}/issues/new?template=site-onerisi.yml" +
                  $"&title={Uri.EscapeDataString("Site önerisi: " + domain)}" +
                  $"&site={Uri.EscapeDataString(domain)}&domains={Uri.EscapeDataString(domains)}";
        Shell.OpenUrl(url);
    }

    // ---- Ayarlar ----

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

    // ---- Bağlantı ----

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
                var score = await ConnectCoreAsync(lastTried, $"{lastTried.Name} deneniyor ({i + 1}/{Profiles.Count})");
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
        if (!File.Exists(Log.FilePath)) Log.Write("Günlük oluşturuldu");
        Shell.OpenFile(Log.FilePath);
    }

    /// <summary>Motoru verilen yöntemle başlatır ve siteleri test eder; erişilebilen paket sayısını döner.</summary>
    private async Task<int> ConnectCoreAsync(BypassProfile profile, string? busyText = null)
    {
        var hosts = _settings.BuildHostList(SitePacks.All);
        if (hosts.Count == 0)
        {
            await _engine!.StopAsync();
            SetOff();
            StatusDetail = "En az bir site açın veya ekleyin.";
            return 0;
        }

        var active = ActivePacks();
        IsBusy = true;
        State = ConnectionState.Busy;
        StatusText = "Bağlanıyor…";
        StatusDetail = busyText ?? $"{profile.Name} yöntemi başlatılıyor";
        foreach (var p in Packs) p.Status = p.Enabled ? PackStatus.Testing : PackStatus.None;

        try
        {
            await _engine!.StartAsync(new EngineOptions(profile, hosts, DnsRedirect));

            if (busyText is null) StatusDetail = "Siteler test ediliyor";
            var results = await ConnectivityTester.TestPacksAsync(active.Select(p => p.Pack));
            foreach (var p in active) p.Status = results[p.Pack] ? PackStatus.Open : PackStatus.Blocked;
            Log.Write($"Test ({profile.Name}): " + string.Join(", ", results.Select(r => $"{r.Key.Name}={(r.Value ? "açık" : "kapalı")}")));

            var ok = results.Count(r => r.Value);
            if (ok == active.Count)
            {
                SetOn(ConnectionState.On, "Bağlı", $"{profile.Name} yöntemi · {ok} site açık");
            }
            else
            {
                var failed = string.Join(", ", results.Where(r => !r.Value).Select(r => r.Key.Name));
                SetOn(ConnectionState.Warning, "Kısmen açık", $"{failed} açılmadı · Otomatik bul'u deneyin");
            }
            return ok;
        }
        catch (Exception e)
        {
            Log.Write($"Bağlanma hatası ({profile.Name}): {e.Message}");
            foreach (var p in Packs) p.Status = PackStatus.None;
            State = ConnectionState.Error;
            StatusText = "Bağlanamadı";
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

    private void SetOn(ConnectionState state, string text, string detail)
    {
        State = state;
        StatusText = text;
        StatusDetail = detail;
    }

    private void SetOff()
    {
        State = ConnectionState.Off;
        StatusText = "Bağlı değil";
        StatusDetail = "Başlatmak için anahtarı açın";
        foreach (var p in Packs) p.Status = PackStatus.None;
    }

    private async void SaveAndReconnect()
    {
        _settings.Save();
        if (IsConnected) await ConnectAsync();
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
