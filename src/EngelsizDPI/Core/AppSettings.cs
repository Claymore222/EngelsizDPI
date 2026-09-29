using System.Text.Json;

namespace EngelsizDPI.Core;

public sealed class AppSettings
{
    public string ProfileId { get; set; } = Profiles.Default.Id;
    public List<string> EnabledPacks { get; set; } = SitePacks.All.Select(p => p.Id).ToList();
    public List<string> CustomDomains { get; set; } = [];
    public bool DnsRedirect { get; set; } = true;
    public bool ConnectOnLaunch { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new();
        }
        catch (Exception)
        {
            // Bozuk ayar dosyası uygulamayı açılmaz hale getirmemeli; varsayılanlarla devam edilir.
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Etkin paketlerin ve özel sitelerin birleşik, tekrarsız alan adı listesi.</summary>
    public IReadOnlyList<string> BuildHostList() =>
        SitePacks.All.Where(p => EnabledPacks.Contains(p.Id)).SelectMany(p => p.Domains)
            .Concat(CustomDomains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public static class AppPaths
{
    public static string SettingsFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EngelsizDPI", "settings.json");

    /// <summary>Motor dosyaları; servis/sürücü yolu kullanıcıdan bağımsız olsun diye ProgramData'da.</summary>
    public static string EngineDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EngelsizDPI", "engine");

    public static string HostListFile => Path.Combine(EngineDir, "hosts.txt");
}
