using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace WisperTranslator.Core.Models;

public sealed record DownloadOutcome(long SizeBytes, bool Resumed);

/// <summary>Download in streaming: ripresa Range, verifica completa e sostituzione atomica.</summary>
public static class HttpDownload
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<DownloadOutcome> DownloadVerifiedAsync(string url, string destination,
        long expectedSizeBytes, string expectedSha256, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("SHA-256 del catalogo non valido.", nameof(expectedSha256));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var part = destination + ".part";
        var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (expectedSizeBytes > 0 && offset > expectedSizeBytes)
        {
            File.Delete(part);
            offset = 0;
        }

        // Un'interruzione può avvenire dopo l'ultimo byte e prima del rename.
        if (expectedSizeBytes <= 0 || offset != expectedSizeBytes)
        {
            using var networkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            networkTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, networkTimeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var resumed = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (response.StatusCode == HttpStatusCode.PartialContent
                && response.Content.Headers.ContentRange?.From != offset)
                throw new InvalidOperationException("Il server ha restituito un intervallo di download errato.");
            if (!resumed) offset = 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(part, resumed ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81920];
            var lastReport = Environment.TickCount64;
            var written = offset;
            while (true)
            {
                // Limite di inattività, non sulla durata complessiva dei modelli più grandi.
                networkTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                var read = await input.ReadAsync(buffer, networkTimeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                written += read;
                if (expectedSizeBytes > 0 && written > expectedSizeBytes)
                {
                    throw new InvalidOperationException("Il download supera la dimensione del catalogo.");
                }
                if (Environment.TickCount64 - lastReport >= 100)
                {
                    progress?.Report(written);
                    lastReport = Environment.TickCount64;
                }
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(written);
        }

        var size = new FileInfo(part).Length;
        if (expectedSizeBytes > 0 && size != expectedSizeBytes)
            throw new IOException($"Download incompleto: {size} byte, attesi {expectedSizeBytes}.");
        var hash = await ComputeSha256Async(part, cancellationToken).ConfigureAwait(false);
        if (!hash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidOperationException("Il file scaricato non supera la verifica SHA-256.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(part, destination, overwrite: true);
        return new DownloadOutcome(size, offset > 0);
    }

    public static async Task DownloadAsync(string url, string destination, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        using var networkTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        networkTimeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, networkTimeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var total = response.Content.Headers.ContentLength;
        var buffer = new byte[81920];
        long written = 0;
        var lastReport = Environment.TickCount64;
        while (true)
        {
            networkTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            var read = await input.ReadAsync(buffer, networkTimeout.Token).ConfigureAwait(false);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            if (Environment.TickCount64 - lastReport >= 100)
            {
                if (total > 0) progress?.Report(written / (double)total.Value);
                lastReport = Environment.TickCount64;
            }
        }
        if (total is { } length && written != length) throw new IOException("Download incompleto.");
        progress?.Report(1);
    }

    public static async Task<string> DownloadStringAsync(string url, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        return await Client.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    public static bool VerifySha256(string checksums, string fileName, string actual) =>
        checksums.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(line =>
        {
            var fields = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return fields.Length == 2 && fields[0].Equals(actual, StringComparison.OrdinalIgnoreCase)
                && fields[1].TrimStart('*').Equals(fileName, StringComparison.Ordinal);
        });
}
