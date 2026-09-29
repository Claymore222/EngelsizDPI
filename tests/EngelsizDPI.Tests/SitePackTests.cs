using EngelsizDPI.Core;
using EngelsizDPI.ViewModels;

namespace EngelsizDPI.Tests;

public sealed class SitePackTests
{
    private static string RepoPacksJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "packs", "packs.json"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "packs", "packs.json"));
    }

    [Fact]
    public void Repo_packs_json_is_valid_and_contains_defaults()
    {
        var catalog = SitePacks.Parse(RepoPacksJson());

        Assert.True(catalog.Revision > 0);
        Assert.Contains(catalog.Packs, p => p.Id == "discord" && p.Default);
        Assert.Contains(catalog.Packs, p => p.Id == "roblox" && p.Default);
    }

    [Fact]
    public void Bundled_copy_matches_repo_file() =>
        Assert.Equal(SitePacks.Parse(RepoPacksJson()).Packs.Select(p => p.Id), SitePacks.All.Select(p => p.Id));

    [Theory]
    [InlineData("""{"revision":1,"packs":[]}""")]
    [InlineData("""{"revision":1,"packs":[{"id":"x","name":"X","test":"http://x.com/","domains":["x.com"]}]}""")]
    [InlineData("""{"revision":1,"packs":[{"id":"x","name":"X","test":"https://x.com/","domains":[]}]}""")]
    [InlineData("""{"revision":1,"packs":[{"id":"x","name":"X","test":"https://x.com/","domains":["not a domain"]}]}""")]
    [InlineData("""{"revision":1,"packs":[{"id":"x","name":"X","test":"https://x.com/","domains":["x.com"]},{"id":"x","name":"Y","test":"https://y.com/","domains":["y.com"]}]}""")]
    public void Parse_rejects_invalid_catalogs(string json) =>
        Assert.ThrowsAny<Exception>(() => SitePacks.Parse(json));

    [Fact]
    public void Parse_normalizes_domains_and_defaults_category()
    {
        var pack = SitePacks.Parse("""{"revision":2,"packs":[{"id":"X","name":"X","test":"https://x.com/","domains":[" X.com ","x.com"]}]}""").Packs[0];

        Assert.Equal("x", pack.Id);
        Assert.Equal(["x.com"], pack.Domains);
        Assert.Equal("Diğer", pack.Category);
        Assert.False(pack.Default);
    }

    [Fact]
    public void Pack_selection_only_stores_differences_from_default()
    {
        var def = new SitePack("d", "D", "C", "https://d.com/", ["d.com"], Default: true);
        var opt = new SitePack("o", "O", "C", "https://o.com/", ["o.com"]);
        var s = new AppSettings { DisabledPacks = [] };

        Assert.True(s.IsPackEnabled(def));
        Assert.False(s.IsPackEnabled(opt));

        s.SetPackEnabled(def, false);
        s.SetPackEnabled(opt, true);
        Assert.False(s.IsPackEnabled(def));
        Assert.True(s.IsPackEnabled(opt));
        Assert.Equal(["d"], s.DisabledPacks);
        Assert.Equal(["o"], s.EnabledPacks);

        s.SetPackEnabled(def, true);
        s.SetPackEnabled(opt, false);
        Assert.Empty(s.DisabledPacks!);
        Assert.Empty(s.EnabledPacks);
    }

    [Fact]
    public void Host_list_merges_enabled_packs_and_custom_domains()
    {
        var catalog = new[]
        {
            new SitePack("a", "A", "C", "https://a.com/", ["a.com", "a2.com"], Default: true),
            new SitePack("b", "B", "C", "https://b.com/", ["b.com"]),
        };
        var s = new AppSettings { DisabledPacks = [], CustomDomains = ["c.com", "A.com"] };

        Assert.Equal(["a.com", "a2.com", "c.com"], s.BuildHostList(catalog));
    }

    [Fact]
    public void Scanner_extracts_registrable_domains_from_html()
    {
        const string html = """
            <img src="https://i.redd.it/x.png"><script src="//www.redditstatic.com/a.js"></script>
            <a href="https://sub.example.com.tr/p">x</a> <link href="https://i.redd.it/y.png"> // not.a.url.js
            """;

        var domains = SiteScanner.ExtractRegistrableDomains(html);

        Assert.Contains("redd.it", domains);
        Assert.Contains("redditstatic.com", domains);
        Assert.Contains("example.com.tr", domains);
        Assert.Equal(domains.Count, domains.Distinct().Count());
    }

    [Theory]
    [InlineData("cdn.discordapp.com", true)]
    [InlineData("discord.com", true)]
    [InlineData("notdiscord.com", false)]
    [InlineData("discord.com.evil.org", false)]
    public void Covered_matches_domain_and_subdomains_only(string domain, bool covered) =>
        Assert.Equal(covered, SiteScanner.IsCovered(domain, ["discord.com", "discordapp.com"]));

    [Theory]
    [InlineData("https://www.Reddit.com/r/x", "reddit.com")]
    [InlineData("old.reddit.com", "old.reddit.com")]
    [InlineData("reddit", null)]
    [InlineData("", null)]
    public void NormalizeDomain_cleans_user_input(string input, string? expected) =>
        Assert.Equal(expected, MainViewModel.NormalizeDomain(input));
}
