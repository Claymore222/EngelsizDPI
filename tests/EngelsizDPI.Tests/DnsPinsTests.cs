using EngelsizDPI.Core;

namespace EngelsizDPI.Tests;

public sealed class DnsPinsTests
{
    private const string UserHosts = "# kullanıcı\r\n127.0.0.1 ornek.local\r\n";

    [Fact]
    public void Block_is_added_replaced_and_removed_without_touching_user_lines()
    {
        var added = DnsPins.ReplaceBlock(UserHosts, [("1.2.3.4", "tr.rbxcdn.com")]);
        Assert.StartsWith(UserHosts, added);
        Assert.Equal([("1.2.3.4", "tr.rbxcdn.com")], DnsPins.ParseBlock(added));

        var replaced = DnsPins.ReplaceBlock(added, [("5.6.7.8", "tr.rbxcdn.com")]);
        Assert.Equal([("5.6.7.8", "tr.rbxcdn.com")], DnsPins.ParseBlock(replaced));
        Assert.DoesNotContain("1.2.3.4", replaced);

        Assert.Equal(UserHosts, DnsPins.ReplaceBlock(replaced, []));
    }

    [Fact]
    public void Removing_from_hosts_without_block_changes_nothing()
    {
        Assert.Equal(UserHosts, DnsPins.ReplaceBlock(UserHosts, []));
    }

    [Fact]
    public void Doh_answer_keeps_only_ipv4_a_records()
    {
        const string json = """
            {"Status":0,"Answer":[
              {"name":"tr.rbxcdn.com","type":5,"data":"trns1.rbxcdn.com."},
              {"name":"a1831.dscd.akamai.net","type":1,"data":"2.18.79.144"},
              {"name":"a1831.dscd.akamai.net","type":1,"data":"2.18.79.141"},
              {"name":"a1831.dscd.akamai.net","type":1,"data":"2.18.79.140"}]}
            """;
        Assert.Equal(["2.18.79.144", "2.18.79.141"], DnsPins.ParseDohAnswer(json));
        Assert.Empty(DnsPins.ParseDohAnswer("""{"Status":2}"""));
    }

    [Fact]
    public void Pin_outside_pack_domains_is_rejected()
    {
        const string json = """
            {"revision":1,"packs":[{"id":"x","name":"X","test":"https://x.com/","domains":["x.com"],"pin":["microsoft.com"]}]}
            """;
        Assert.ThrowsAny<FormatException>(() => SitePacks.Parse(json));
    }
}
