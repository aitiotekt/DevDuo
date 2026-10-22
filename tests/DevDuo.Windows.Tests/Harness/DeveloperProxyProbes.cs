using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace DevDuo.Windows.Tests;

internal sealed class DeveloperProxyProbe : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource cancellation = new();
    private readonly bool socks;
    internal ConcurrentQueue<string> Requests { get; } = new();
    internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    internal DeveloperProxyProbe(bool socks)
    {
        this.socks = socks;
        listener.Start();
        _ = Accept();
    }
    private async Task Accept()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
                _ = Respond(await listener.AcceptTcpClientAsync(cancellation.Token));
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task Respond(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var stream = client.GetStream();
                if (socks)
                {
                    var greeting = new byte[2];
                    await stream.ReadExactlyAsync(greeting, timeout.Token);
                    if (greeting[0] != 5) return;
                    await stream.ReadExactlyAsync(new byte[greeting[1]], timeout.Token);
                    await stream.WriteAsync(new byte[] { 5, 0 }, timeout.Token);
                    var connect = new byte[4];
                    await stream.ReadExactlyAsync(connect, timeout.Token);
                    if (connect[0] != 5 || connect[1] != 1) return;
                    var size = connect[3] switch { 1 => 4, 4 => 16, 3 => await ReadOne(stream, timeout.Token), _ => 0 };
                    var host = new byte[size];
                    await stream.ReadExactlyAsync(host, timeout.Token);
                    await stream.ReadExactlyAsync(new byte[2], timeout.Token);
                    Requests.Enqueue(connect[3] == 3 ? Encoding.ASCII.GetString(host) : new IPAddress(host).ToString());
                    await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 }, timeout.Token);
                }
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                var first = await reader.ReadLineAsync(timeout.Token);
                if (first is null) return;
                Requests.Enqueue(first);
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }
                var body = Encoding.UTF8.GetBytes("<!doctype html><title>Native proxy acceptance</title><h1>Native proxy response</h1><input>" +
                    (first.Contains("/devduo-", StringComparison.Ordinal) ? "<img src='/asset-proof'><script>fetch('/fetch-proof').then(r=>{if(r.ok)document.documentElement.dataset.proxyFetch='ok'})</script>" : ""));
                var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        }
    }
    private static async Task<int> ReadOne(NetworkStream stream, CancellationToken token)
    {
        var value = new byte[1];
        await stream.ReadExactlyAsync(value, token);
        return value[0];
    }
    public void Dispose()
    {
        cancellation.Cancel();
        listener.Stop();
        cancellation.Dispose();
    }
}

internal sealed class DeveloperTlsProxyProbe : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource cancellation = new();
    private readonly X509Certificate2 certificate;
    private int clientHello;
    internal bool ClientHelloObserved => Volatile.Read(ref clientHello) != 0;
    internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    internal DeveloperTlsProxyProbe()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        listener.Start();
        _ = Accept();
    }
    private async Task Accept()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
                _ = Authenticate(await listener.AcceptTcpClientAsync(cancellation.Token));
        }
        catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task Authenticate(TcpClient client)
    {
        using (client)
        using (var tls = new SslStream(client.GetStream()))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ServerCertificateSelectionCallback = (_, _) => { Volatile.Write(ref clientHello, 1); return certificate; },
                }, timeout.Token);
            }
            catch (Exception error) when (error is AuthenticationException or IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
    public void Dispose()
    {
        cancellation.Cancel();
        listener.Stop();
        certificate.Dispose();
        cancellation.Dispose();
    }
}
