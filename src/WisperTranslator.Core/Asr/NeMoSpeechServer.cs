using System.Diagnostics;

namespace WisperTranslator.Core.Asr;

/// <summary>
/// Server locale di NeMo-Speech.cpp (Apache-2.0): processo figlio che espone ASR via HTTP.
/// Girando fuori dal nostro processo, un suo eventuale crash non porta giù l'applicazione.
/// </summary>
public sealed class NeMoSpeechServer : IDisposable
{
    public const int DefaultPort = 8123;

    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly string _executablePath;
    private readonly string _modelPath;
    private Process? _process;
    private bool _disposed;

    public NeMoSpeechServer(string executablePath, string modelPath, int port = DefaultPort)
    {
        _executablePath = executablePath;
        _modelPath = modelPath;
        Port = port;
    }

    public int Port { get; }

    public bool StartedByUs => _process is { HasExited: false };

    /// <summary>Cartella dell'utente dove viene estratto il runtime scaricato.</summary>
    public static string UserToolsRoot => Path.Combine(AppPaths.Root, "tools", "nemo-speech");

    /// <summary>Cerca il runtime accanto all'app (installazione) e poi nella cartella utente.</summary>
    public static string? FindExecutable()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "nemo-speech", "bin", "nemo-speech.exe");
        if (File.Exists(bundled))
        {
            return bundled;
        }

        if (Models.ModelStore.FindFile(Models.ModelCatalog.NeMoRuntime, "nemo-speech.exe") is { } installed)
        {
            return installed;
        }

        if (!Directory.Exists(UserToolsRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(UserToolsRoot, "nemo-speech.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>Scarica ed estrae il runtime CPU (circa 5,5 MB compressi) se manca.</summary>
    public static async Task<string> EnsureExecutableAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (FindExecutable() is { } existing)
        {
            return existing;
        }

        var entry = Models.ModelCatalog.NeMoRuntime;
        await Models.ModelStore.EnsurePackAsync(entry, progress, cancellationToken).ConfigureAwait(false);

        return Models.ModelStore.FindFile(entry, "nemo-speech.exe")
               ?? throw new InvalidOperationException("Il runtime NeMo-Speech non contiene nemo-speech.exe.");
    }

    public async Task<bool> EnsureStartedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (await IsReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (!File.Exists(_executablePath) || !File.Exists(_modelPath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--asr-model");
        startInfo.ArgumentList.Add(_modelPath);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(Port.ToString());
        startInfo.ArgumentList.Add("--no-ui");
        startInfo.ArgumentList.Add("--quiet");

        var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        _process = process;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
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

            try
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await ProbeClient
                .GetAsync($"http://127.0.0.1:{Port}/v1/models", cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
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
