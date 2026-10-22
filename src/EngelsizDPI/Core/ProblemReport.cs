using System.Runtime.InteropServices;
using System.Text;

namespace EngelsizDPI.Core;

/// <summary>Sorun bildirimine eklenen durum bilgisi.</summary>
public sealed record ProblemSnapshot(
    string Status,
    string StatusDetail,
    BypassProfile Profile,
    bool DnsSetting,
    bool DnsActive,
    IReadOnlyList<(string Name, string Result)> Packs,
    int CustomSiteCount,
    string? EngineOutput,
    string? Conflicts);

/// <summary>
/// "Sorun bildir": GitHub'da, uygulamanın durumuyla önceden doldurulmuş bir sorun formu açar. Form herkese açık
/// olduğu için kişisel bilgi eklenmez: kullanıcının eklediği sitelerin adları yerine yalnızca sayısı yazılır,
/// Windows kullanıcı adı metinden silinir. Kullanıcı göndermeden önce her şeyi görür ve düzenleyebilir.
/// </summary>
public static class ProblemReport
{
    public const string Template = "sorun-bildirimi.yml";

    /// <summary>Formdaki "Durum bilgisi" alanının metni.</summary>
    public static string Describe(ProblemSnapshot s, Version appVersion)
    {
        var dns = !s.DnsSetting ? "kapalı" : s.DnsActive ? "açık" : "açık ama sunucuya ulaşılamadı, normal DNS kullanılıyor";
        var text = new StringBuilder()
            .AppendLine($"EngelsizDPI v{appVersion} · {RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.OSArchitecture})")
            .AppendLine($"Durum: {s.Status} · {s.StatusDetail}")
            .AppendLine($"Yöntem: {s.Profile.Name} ({s.Profile.Arguments})")
            .AppendLine($"Güvenli DNS: {dns}")
            .AppendLine("Siteler: " + (s.Packs.Count == 0 ? "yok" : string.Join(", ", s.Packs.Select(p => $"{p.Name}={p.Result}"))));
        if (s.CustomSiteCount > 0) text.AppendLine($"Eklenen özel site sayısı: {s.CustomSiteCount}");
        // Adres çok uzarsa tarayıcılar ve GitHub reddedebilir; serbest metinler kısaltılır.
        if (!string.IsNullOrWhiteSpace(s.Conflicts)) text.AppendLine($"Çakışma: {Shorten(s.Conflicts, 300)}");
        if (!string.IsNullOrWhiteSpace(s.EngineOutput)) text.AppendLine($"GoodbyeDPI son çıktı: {Shorten(s.EngineOutput, 200)}");
        return Scrub(text.ToString().TrimEnd(), Environment.UserName);
    }

    public static string IssueUrl(string repo, string title, string details) =>
        $"https://github.com/{repo}/issues/new?template={Template}" +
        $"&title={Uri.EscapeDataString(title)}&durum={Uri.EscapeDataString(details)}";

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    /// <summary>Dosya yollarındaki Windows kullanıcı adını (C:\Users\ad\...) siler.</summary>
    public static string Scrub(string text, string userName) =>
        userName.Length == 0 ? text : text.Replace($@"\Users\{userName}", @"\Users\<kullanıcı>", StringComparison.OrdinalIgnoreCase);
}
