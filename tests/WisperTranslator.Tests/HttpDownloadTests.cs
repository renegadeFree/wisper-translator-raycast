using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using WisperTranslator.Core.Models;

namespace WisperTranslator.Tests;

/// <summary>
/// Verifica il percorso di download dei modelli contro un server HTTP minimo in-process:
/// niente rete esterna, ma copre hash, troncamento e ripresa da un download interrotto.
/// </summary>
public class HttpDownloadTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "wisper-tests-" + Guid.NewGuid().ToString("N"));

    public HttpDownloadTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ScaricaEVerificaIlHash()
    {
        var content = Build(64 * 1024);
        using var server = new TinyHttpServer(content);
        var destination = Path.Combine(_directory, "model.bin");

        var outcome = await HttpDownload.DownloadVerifiedAsync(
            server.Url,
            destination,
            content.Length,
            Hash(content));

        Assert.Equal(content.Length, outcome.SizeBytes);
        Assert.False(outcome.Resumed);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task RifiutaUnFileConHashDiverso()
    {
        var content = Build(32 * 1024);
        using var server = new TinyHttpServer(content);
        var destination = Path.Combine(_directory, "corrotto.bin");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HttpDownload.DownloadVerifiedAsync(
                server.Url,
                destination,
                content.Length,
                Hash(Build(16))));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task RiprendeUnDownloadInterrotto()
    {
        var content = Build(200 * 1024);
        var destination = Path.Combine(_directory, "ripreso.bin");

        // Primo tentativo: il server chiude la connessione a metà.
        using (var broken = new TinyHttpServer(content, cutAfter: 80 * 1024))
        {
            // HttpClient segnala la connessione interrotta con HttpIOException (derivata di IOException).
            await Assert.ThrowsAnyAsync<IOException>(() =>
                HttpDownload.DownloadVerifiedAsync(broken.Url, destination, content.Length, Hash(content)));
        }

        Assert.False(File.Exists(destination));
        Assert.True(File.Exists(destination + ".part"));
        Assert.Equal(80 * 1024, new FileInfo(destination + ".part").Length);

        // Secondo tentativo: il server risponde alla Range e il file si completa.
        using var server = new TinyHttpServer(content);
        var outcome = await HttpDownload.DownloadVerifiedAsync(
            server.Url,
            destination,
            content.Length,
            Hash(content));

        Assert.True(outcome.Resumed);
        Assert.Equal(80 * 1024, server.LastRangeStart);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task RiparteDaZeroSeIlServerIgnoraLaRange()
    {
        var content = Build(100 * 1024);
        var destination = Path.Combine(_directory, "senza-range.bin");
        await File.WriteAllBytesAsync(destination + ".part", content[..(40 * 1024)]);

        using var server = new TinyHttpServer(content, supportRange: false);
        var outcome = await HttpDownload.DownloadVerifiedAsync(
            server.Url,
            destination,
            content.Length,
            Hash(content));

        Assert.False(outcome.Resumed);
        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    private static byte[] Build(int size)
    {
        var buffer = new byte[size];
        for (var index = 0; index < size; index++)
        {
            buffer[index] = (byte)(index % 251);
        }

        return buffer;
    }

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception)
        {
            // cartella temporanea già rimossa
        }
    }

    /// <summary>Server HTTP minimo: serve un solo file e registra l'ultima Range ricevuta.</summary>
    private sealed class TinyHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly byte[] _content;
        private readonly bool _supportRange;
        private readonly int _cutAfter;

        public TinyHttpServer(byte[] content, bool supportRange = true, int cutAfter = 0)
        {
            _content = content;
            _supportRange = supportRange;
            _cutAfter = cutAfter;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/model.bin";

        public long LastRangeStart { get; private set; } = -1;

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var header = new StringBuilder();
                    var buffer = new byte[1];
                    while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        if (await stream.ReadAsync(buffer) == 0)
                        {
                            return;
                        }

                        header.Append((char)buffer[0]);
                    }

                    var request = header.ToString();
                    var start = 0L;
                    var partial = false;
                    if (_supportRange)
                    {
                        var rangeLine = request
                            .Split("\r\n")
                            .FirstOrDefault(line => line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                        if (rangeLine is not null)
                        {
                            var value = rangeLine.Split('=')[1].Trim().TrimEnd('-');
                            if (long.TryParse(value, out var parsed))
                            {
                                start = parsed;
                                partial = true;
                            }
                        }
                    }

                    LastRangeStart = partial ? start : 0;

                    var body = _content.AsMemory((int)start);
                    var toSend = _cutAfter > 0 ? Math.Min(_cutAfter - start, body.Length) : body.Length;
                    var headers =
                        (partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n")
                        + $"Content-Length: {body.Length}\r\n"
                        + (partial
                            ? $"Content-Range: bytes {start}-{_content.Length - 1}/{_content.Length}\r\n"
                            : string.Empty)
                        + "Connection: close\r\n\r\n";

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(headers));
                    if (toSend > 0)
                    {
                        await stream.WriteAsync(body[..(int)Math.Max(0, toSend)]);
                    }

                    await stream.FlushAsync();
                }
                catch (Exception)
                {
                    // connessione interrotta dal test
                }
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
