using System.Diagnostics;
using System.Net.Http.Json;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Gestisce il processo del server di traduzione locale (MTranServer, Apache-2.0):
/// lo scarica se manca, lo avvia nascosto, aspetta che sia pronto e lo spegne alla chiusura.
/// </summary>
public sealed class TranslationServer : IDisposable
{
    private const string Version = "4.0.33";
    private const string AssetName = $"mtranserver-{Version}-windows-amd64.exe";
    private const string DownloadUrl =
        $"https://github.com/xxnuo/MTranServer/releases/download/v{Version}/{AssetName}";
    private const string Sha256SumsUrl =
        $"https://github.com/xxnuo/MTranServer/releases/download/v{Version}/SHA256SUMS";

    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly string _executablePath;
    private readonly string _modelDirectory;
    private readonly Queue<string> _log = new();
    private Process? _process;
    private bool _disposed;

    public TranslationServer(string? executablePath = null, string? modelDirectory = null, int port = 8989)
    {
        _executablePath = executablePath ?? DefaultExecutablePath;
        _modelDirectory = modelDirectory ?? DefaultModelDirectory;
        Port = port;
    }

    public int Port { get; }

    /// <summary>True se il processo è stato avviato da questa istanza (e va quindi fermato da noi).</summary>
    public bool StartedByUs => _process is { HasExited: false };

    public IReadOnlyCollection<string> RecentLog
    {
        get
        {
            lock (_log)
            {
                return [.. _log];
            }
        }
    }

    /// <summary>
    /// Cerca il binario accanto all'eseguibile (installazione) e poi nella cartella utente
    /// (sviluppo o download a runtime). Il download finisce sempre nella cartella utente,
    /// perché quella dell'app può essere in sola lettura.
    /// </summary>
    public static string DefaultExecutablePath
    {
        get
        {
            var installed = Path.Combine(AppContext.BaseDirectory, "tools", "mtranserver.exe");
            if (File.Exists(installed))
            {
                return installed;
            }

            var local = Path.Combine(Core.AppPaths.Root, "tools", "mtranserver.exe");
            return File.Exists(local) ? local : Path.Combine(Core.AppPaths.Root, "tools", "mtranserver.exe");
        }
    }

    public static string DefaultModelDirectory =>
        Path.Combine(Core.AppPaths.ModelsDirectory, "mt");

    /// <summary>Scarica e verifica il binario se non è già presente accanto all'app.</summary>
    public static async Task<string> EnsureExecutableAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = DefaultExecutablePath;
        if (File.Exists(path) && new FileInfo(path).Length > 1024)
        {
            return path;
        }

        var temporary = path + ".part";
        await Models.HttpDownload.DownloadAsync(DownloadUrl, temporary, progress, cancellationToken);

        var actual = await Models.HttpDownload.ComputeSha256Async(temporary, cancellationToken);
        var sums = await Models.HttpDownload.DownloadStringAsync(Sha256SumsUrl, cancellationToken);
        if (!Models.HttpDownload.VerifySha256(sums, AssetName, actual))
        {
            File.Delete(temporary);
            throw new InvalidOperationException(
                $"Il binario del server di traduzione non supera la verifica SHA-256 ({actual}).");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>Avvia il server se non c'è già qualcuno in ascolto sulla porta.</summary>
    public async Task<bool> EnsureStartedAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (await IsReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (!File.Exists(_executablePath))
        {
            return false;
        }

        Directory.CreateDirectory(_modelDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--port", Port.ToString(), "--model-dir", _modelDirectory,
            "--offline", "--no-ui", "--no-check-update", "--log-level", "warn" })
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        _process = process;
        process.OutputDataReceived += (_, args) => AppendLog(args.Data);
        process.ErrorDataReceived += (_, args) => AppendLog(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                return false;
            }

            if (await IsReadyAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Scarica i modelli per le coppie indicate (formato MTranServer: <c>it-en</c>).</summary>
    public async Task<bool> DownloadModelsAsync(
        IEnumerable<string> pairs,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_executablePath))
        {
            return false;
        }

        Directory.CreateDirectory(_modelDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--download");
        foreach (var pair in pairs) startInfo.ArgumentList.Add(pair);
        foreach (var argument in new[] { "--model-dir", _modelDirectory, "--no-ui", "--no-check-update" })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        process.OutputDataReceived += (_, args) => AppendLog(args.Data);
        process.ErrorDataReceived += (_, args) => AppendLog(args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var cancellation = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await ProbeClient
                .PostAsJsonAsync(
                    $"http://127.0.0.1:{Port}/translate",
                    new { from = "en", to = "it", text = "ok" },
                    cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_log)
        {
            _log.Enqueue(line.Trim());
            while (_log.Count > 30)
            {
                _log.Dequeue();
            }
        }
    }

    public void Stop()
    {
        if (_process is not { } process)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception)
        {
            // il processo era già terminato
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
