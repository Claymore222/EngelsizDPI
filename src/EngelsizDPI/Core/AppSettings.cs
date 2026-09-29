using System.Text.Json;

namespace EngelsizDPI.Core;

public sealed class AppSettings
{
    public string ProfileId { get; set; } = Profiles.Default.Id;
    /// <summary>Kullanıcının açıkça açtığı paketler.</summary>
    public List<string> EnabledPacks { get; set; } = [];

    /// <summary>
    /// Kullanıcının açıkça kapattığı paketler. Listede olmayan paketler kendi varsayılanını kullanır; böylece
    /// uzaktan eklenen yeni bir paket kullanıcının seçimlerini bozmadan varsayılan haliyle gelir.
    /// null: bu alan eklenmeden önce kaydedilmiş ayar dosyası (bkz. <see cref="Load"/>).
    /// </summary>
    public List<string>? DisabledPacks { get; set; }
    public List<string> CustomDomains { get; set; } = [];
    public bool DnsRedirect { get; set; } = true;
    public bool ConnectOnLaunch { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;

    /// <summary>"system", "light" ya da "dark" (bkz. <see cref="AppTheme"/>).</summary>
    public string Theme { get; set; } = AppTheme.System;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new();
                // Eski dosyalarda yalnızca açık paketler tutuluyordu; o zaman bilinen ve açık olmayanlar kapatılmıştı.
                settings.DisabledPacks ??= SitePacks.All.Select(p => p.Id).Except(settings.EnabledPacks).ToList();
                return settings;
            }
        }
        catch (Exception)
        {
            // Bozuk ayar dosyası uygulamayı açılmaz hale getirmemeli; varsayılanlarla devam edilir.
        }
        return new() { DisabledPacks = [] };
    }

    public bool IsPackEnabled(SitePack pack) =>
        EnabledPacks.Contains(pack.Id) || (pack.Default && !(DisabledPacks ?? []).Contains(pack.Id));

    public void SetPackEnabled(SitePack pack, bool enabled)
    {
        DisabledPacks ??= [];
        EnabledPacks.Remove(pack.Id);
        DisabledPacks.Remove(pack.Id);
        // Yalnızca varsayılandan farklı seçim saklanır.
        if (enabled != pack.Default) (enabled ? EnabledPacks : DisabledPacks).Add(pack.Id);
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Etkin paketlerin ve özel sitelerin birleşik, tekrarsız alan adı listesi.</summary>
    public IReadOnlyList<string> BuildHostList(IEnumerable<SitePack> catalog) =>
        catalog.Where(IsPackEnabled).SelectMany(p => p.Domains)
            .Concat(CustomDomains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public static class AppPaths
{
    public static string SettingsFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EngelsizDPI", "settings.json");

    /// <summary>Günlük gibi paylaşılan veriler (C:\ProgramData\EngelsizDPI).</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EngelsizDPI");

    /// <summary>
    /// Yönetici olarak çalıştırılan motor dosyaları. Kuruluyken yalnızca yöneticilerin yazabildiği Program Files
    /// altında durur: uygulama UAC sormadan yönetici olarak başlayabildiği için, sıradan bir sürecin
    /// değiştirebileceği bir klasörden exe/dll çalıştırmak yetki yükseltme açığı olurdu.
    /// </summary>
    public static string EngineDir { get; } = OperatingSystem.IsWindows() && Installer.IsRunningInstalled
        ? Path.Combine(Installer.InstallDir, "engine")
        : Path.Combine(DataDir, "engine");

    /// <summary>v1.1.0'ın kullanıcının yazabildiği yerlerde bıraktığı ve artık kullanılmayan klasörler.</summary>
    public static IEnumerable<string> LegacyWritableDirs =>
    [
        Path.Combine(DataDir, "engine"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngelsizDPI"),
    ];

    public static string HostListFile => Path.Combine(EngineDir, "hosts.txt");
}
