namespace EngelsizDPI.Core;

/// <summary>
/// Tek tıkla açılıp kapatılabilen hazır site grupları. <see cref="TestUrl"/> bağlantı testinde
/// ve otomatik yöntem bulmada kullanılır.
/// </summary>
public sealed record SitePack(string Id, string Name, string TestUrl, IReadOnlyList<string> Domains);

public static class SitePacks
{
    public static readonly IReadOnlyList<SitePack> All =
    [
        new("discord", "Discord", "https://discord.com/api/v9/gateway",
        [
            "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media",
            "discord.co", "discord.new", "discord.gift", "dis.gd",
            "discord-attachments-uploads-prd.storage.googleapis.com",
        ]),
        new("roblox", "Roblox", "https://www.roblox.com/",
        [
            "roblox.com", "rbxcdn.com",
        ]),
    ];
}
