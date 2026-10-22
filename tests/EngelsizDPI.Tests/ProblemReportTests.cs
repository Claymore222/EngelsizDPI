using EngelsizDPI.Core;

namespace EngelsizDPI.Tests;

public sealed class ProblemReportTests
{
    private static ProblemSnapshot Snapshot(int customSites = 0, bool dns = true, bool dnsActive = true, string? output = null) => new(
        "Kısmen açık", "Roblox açılmadı · Otomatik bul'u deneyin", Profiles.Default, dns, dnsActive,
        [("Discord", "açık"), ("Roblox", "açılmadı")], customSites, output, null);

    [Fact]
    public void Describe_contains_version_state_method_and_sites()
    {
        var text = ProblemReport.Describe(Snapshot(), new Version(1, 4, 0));
        Assert.Contains("EngelsizDPI v1.4.0", text);
        Assert.Contains("Durum: Kısmen açık · Roblox açılmadı", text);
        Assert.Contains("Yöntem: Standart (-5 --set-ttl 5)", text);
        Assert.Contains("Güvenli DNS: açık", text);
        Assert.Contains("Siteler: Discord=açık, Roblox=açılmadı", text);
    }

    [Fact]
    public void Custom_sites_are_counted_not_named()
    {
        var text = ProblemReport.Describe(Snapshot(customSites: 2), new Version(1, 4, 0));
        Assert.Contains("Eklenen özel site sayısı: 2", text);
        Assert.DoesNotContain("Eklenen özel site sayısı", ProblemReport.Describe(Snapshot(), new Version(1, 4, 0)));
    }

    [Theory]
    [InlineData(false, false, "Güvenli DNS: kapalı")]
    [InlineData(true, false, "Güvenli DNS: açık ama sunucuya ulaşılamadı")]
    public void Dns_state_is_explained(bool setting, bool active, string expected) =>
        Assert.Contains(expected, ProblemReport.Describe(Snapshot(dns: setting, dnsActive: active), new Version(1, 4, 0)));

    [Fact]
    public void User_name_in_paths_is_removed()
    {
        var scrubbed = ProblemReport.Scrub(@"Hata: C:\Users\Ahmet\AppData\x ve Ahmet adı", "ahmet");
        Assert.Equal(@"Hata: C:\Users\<kullanıcı>\AppData\x ve Ahmet adı", scrubbed);
    }

    [Fact]
    public void Issue_url_targets_the_form_and_escapes_fields()
    {
        var url = ProblemReport.IssueUrl("u/r", "Sorun: Roblox açılmıyor", "a=b & c\nd");
        Assert.StartsWith("https://github.com/u/r/issues/new?template=sorun-bildirimi.yml&title=", url);
        Assert.Contains("&durum=a%3Db%20%26%20c%0Ad", url);
        Assert.Equal("https", new Uri(url).Scheme);
    }
}
