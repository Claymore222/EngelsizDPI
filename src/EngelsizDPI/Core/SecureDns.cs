using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EngelsizDPI.Core;

/// <summary>
/// "Güvenli DNS": GoodbyeDPI, 53. porttaki bütün DNS sorgularını buraya yönlendirir. Yandex DNS'in standart dışı
/// portu kullanılır çünkü bazı operatörler 53. porttaki DNS trafiğine araya girip kendileri yanıt verir.
///
/// Yönlendirme bütün sorguları kapsadığı için bu sunucu düşerse ya da operatör portu kapatırsa bilgisayarda hiçbir
/// alan adı çözülmez. Bu yüzden bağlanmadan önce ve sağlık denetimlerinde sunucuya doğrudan (DNS'e ihtiyaç
/// duymadan, IP ile) gerçek bir sorgu gönderilip yanıt verdiği doğrulanır.
/// </summary>
public static class SecureDns
{
    public const string ServerV4 = "77.88.8.8", ServerV6 = "2a02:6b8::feed:0ff";
    public const int Port = 1253;

    private const string ProbeName = "discord.com";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);
    private static (DateTime At, bool Ok)? _last;

    /// <summary>Sunucu yanıt veriyor mu? Sonuç kısa süre önbellekte tutulur (Otomatik bul her yöntemde sormasın).</summary>
    public static async Task<bool> IsReachableAsync(bool fresh = false, CancellationToken ct = default)
    {
        if (!fresh && _last is { } last && DateTime.UtcNow - last.At < CacheFor) return last.Ok;
        var ok = await CanResolveAsync(new IPEndPoint(IPAddress.Parse(ServerV4), Port), ProbeName, TimeSpan.FromSeconds(2), ct);
        _last = (DateTime.UtcNow, ok);
        return ok;
    }

    /// <summary>Sunucuya A kaydı sorgusu gönderir; iki denemeden birinde başarılı yanıt gelirse true.</summary>
    public static async Task<bool> CanResolveAsync(IPEndPoint server, string name, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var udp = new UdpClient(server.AddressFamily);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
                await udp.SendAsync(BuildQuery(id, name), server, ct);

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(timeout);
                try
                {
                    while (true)
                    {
                        var reply = await udp.ReceiveAsync(wait.Token);
                        if (IsAnswerTo(reply.Buffer, id)) return true;
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Zaman aşımı: bir kez daha denenir.
                }
            }
        }
        catch (SocketException)
        {
            // Ağ yok ya da ICMP "port kapalı" yanıtı.
        }
        return false;
    }

    /// <summary>Özyinelemeli, tek sorulu A kaydı sorgusu (RFC 1035).</summary>
    public static byte[] BuildQuery(ushort id, string name)
    {
        var packet = new List<byte>(32 + name.Length);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100); // RD: özyineleme istenir
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // QDCOUNT
        packet.AddRange(header.ToArray());
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }
        packet.AddRange([0, 0, 1, 0, 1]); // kök etiketi, QTYPE=A, QCLASS=IN
        return packet.ToArray();
    }

    /// <summary>Paket, verilen kimlikli sorgunun hatasız (RCODE=0) yanıtı mı?</summary>
    public static bool IsAnswerTo(ReadOnlySpan<byte> reply, ushort id) =>
        reply.Length >= 12 &&
        BinaryPrimitives.ReadUInt16BigEndian(reply) == id &&
        (reply[2] & 0x80) != 0 &&  // QR: yanıt
        (reply[3] & 0x0F) == 0;    // RCODE: hata yok

    /// <summary>
    /// İnternet bağlantısı var mı? DNS'e ihtiyaç duymadan bilinen IP'lere TCP bağlantısı denenir; böylece güvenli
    /// DNS ya da site testleri başarısız olduğunda sorunun internetin kendisinde olup olmadığı anlaşılır.
    /// </summary>
    public static async Task<bool> IsOnlineAsync(CancellationToken ct = default)
    {
        foreach (var ip in new[] { "1.1.1.1", "8.8.8.8" })
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Parse(ip), 443, wait.Token);
                return true;
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
            }
        }
        return false;
    }
}
