namespace EngelsizDPI.Core;

/// <summary>
/// DPI atlatma yöntemi. Argümanlar GoodbyeDPI-Turkey'in test edilmiş .cmd dosyalarından alınmıştır;
/// site listesi (--blacklist) ve DNS yönlendirmesi motor tarafından ayrıca eklenir.
/// </summary>
public sealed record BypassProfile(string Id, string Name, string Description, string Arguments)
{
    public override string ToString() => Name;
}

public static class Profiles
{
    public static readonly IReadOnlyList<BypassProfile> All =
    [
        new("standard", "Standart", "Çoğu operatörde çalışır (Türk Telekom, Vodafone, Türknet).", "-5 --set-ttl 5"),
        new("ttl3", "Alternatif 1", "Superonline için; düşük TTL ile sahte paket.", "--set-ttl 3"),
        new("mode5", "Alternatif 2", "TTL ayarı yok; bazı siteler yavaş açılıyorsa deneyin.", "-5"),
        new("mode9", "Alternatif 3", "Yanlış SEQ + checksum ile sahte paket, QUIC engelli.", "-9"),
        new("mode7", "Alternatif 4", "Yalnızca yanlış checksum ile sahte paket.", "-7"),
    ];

    public static BypassProfile Default => All[0];

    public static BypassProfile ById(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Default;
}
