using System.Net;
using System.Net.Sockets;
using EngelsizDPI.Core;

namespace EngelsizDPI.Tests;

public sealed class SecureDnsTests
{
    [Fact]
    public void Query_is_a_recursive_A_lookup()
    {
        var q = SecureDns.BuildQuery(0x1234, "discord.com");
        byte[] expected =
        [
            0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0,
            7, (byte)'d', (byte)'i', (byte)'s', (byte)'c', (byte)'o', (byte)'r', (byte)'d',
            3, (byte)'c', (byte)'o', (byte)'m', 0,
            0, 1, 0, 1,
        ];
        Assert.Equal(expected, q);
    }

    [Theory]
    [InlineData(new byte[] { 0x12, 0x34, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 }, true)]   // yanıt, hata yok
    [InlineData(new byte[] { 0x12, 0x35, 0x81, 0x80, 0, 1, 0, 1, 0, 0, 0, 0 }, false)]  // başka sorgunun yanıtı
    [InlineData(new byte[] { 0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 }, false)]  // yanıt değil, sorgu
    [InlineData(new byte[] { 0x12, 0x34, 0x81, 0x82, 0, 1, 0, 0, 0, 0, 0, 0 }, false)]  // SERVFAIL
    [InlineData(new byte[] { 0x12, 0x34, 0x81 }, false)]                                 // kısa paket
    public void Only_successful_answers_to_our_query_count(byte[] reply, bool expected) =>
        Assert.Equal(expected, SecureDns.IsAnswerTo(reply, 0x1234));

    [Fact]
    public async Task Probe_succeeds_against_a_server_that_answers()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)server.Client.LocalEndPoint!;
        var serve = Task.Run(async () =>
        {
            var request = await server.ReceiveAsync();
            var reply = request.Buffer.ToArray();
            reply[2] |= 0x80; // QR
            await server.SendAsync(reply, request.RemoteEndPoint);
        });

        Assert.True(await SecureDns.CanResolveAsync(endpoint, "discord.com", TimeSpan.FromSeconds(2)));
        await serve;
    }

    [Fact]
    public async Task Probe_fails_when_nobody_answers()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var endpoint = (IPEndPoint)silent.Client.LocalEndPoint!;
        Assert.False(await SecureDns.CanResolveAsync(endpoint, "discord.com", TimeSpan.FromMilliseconds(200)));
    }
}
