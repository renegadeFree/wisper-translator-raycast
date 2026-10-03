using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace WisperTranslator.Core.Asr;

/// <summary>
/// Corsia istantanea NeMo via WebSocket: il server decodifica blocchi da 160 ms e manda i
/// parziali mentre si parla. È molto più reattivo delle richieste HTTP ripetute sull'intero
/// enunciato, che pagavano una decodifica completa a ogni tentativo.
/// </summary>
public sealed class NeMoRealtimeClient : IAsyncDisposable
{
    private readonly int _port;
    private readonly Channel<byte[]> _send = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cancellation;
    private Task? _receiveLoop;
    private Task? _sendLoop;
    private int _disposed;

    public NeMoRealtimeClient(int port) => _port = port;

    public int Port => _port;

    /// <summary>Testo in arrivo dal server: delta o frase completata dal suo endpointing.</summary>
    public event Action<string, bool>? Update;

    public bool IsFaulted { get; private set; }

    public async Task StartAsync(string? language, CancellationToken cancellationToken = default)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var socket = new ClientWebSocket();
        _socket = socket;

        try
        {
            await socket.ConnectAsync(
                new Uri($"ws://127.0.0.1:{_port}/v1/audio/transcriptions/realtime"),
                _cancellation.Token).ConfigureAwait(false);

            var session = JsonSerializer.Serialize(new
            {
                type = "session.update",
                session = new
                {
                    sample_rate = 16000,
                    language,
                    automatic_punctuation = true,
                    word_timestamps = false,
                    speaker_diarization = false,
                },
            });
            await socket.SendAsync(
                Encoding.UTF8.GetBytes(session),
                WebSocketMessageType.Text,
                endOfMessage: true,
                _cancellation.Token).ConfigureAwait(false);

            _sendLoop = Task.Run(() => SendLoopAsync(socket, _cancellation.Token), CancellationToken.None);
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(socket, _cancellation.Token), CancellationToken.None);
        }
        catch (Exception)
        {
            IsFaulted = true;
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Accoda un blocco di campioni float: la conversione e l'invio restano fuori dal loop audio.</summary>
    public void Push(ReadOnlySpan<float> samples)
    {
        if (IsFaulted || _disposed != 0)
        {
            return;
        }

        var bytes = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
        {
            var value = (short)Math.Clamp(samples[index] * short.MaxValue, short.MinValue, short.MaxValue);
            bytes[index * 2] = (byte)(value & 0xFF);
            bytes[(index * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        _send.Writer.TryWrite(bytes);
    }

    private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var block in _send.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (socket.State != WebSocketState.Open)
                {
                    continue;
                }

                await socket.SendAsync(
                    block,
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            IsFaulted = true;
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    IsFaulted = true;
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (message.Length > 1024 * 1024)
                {
                    throw new InvalidOperationException("Messaggio streaming troppo grande.");
                }

                var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                HandleEvent(json);
            }
        }
        catch (OperationCanceledException)
        {
            // arresto richiesto
        }
        catch (Exception)
        {
            IsFaulted = true;
        }
    }

    private void HandleEvent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("type", out var type))
            {
                return;
            }

            var kind = type.GetString() ?? string.Empty;
            if (kind.EndsWith(".delta", StringComparison.Ordinal))
            {
                var delta = Text(document.RootElement, "delta")
                            ?? Text(document.RootElement, "text")
                            ?? Text(document.RootElement, "transcript");
                if (!string.IsNullOrWhiteSpace(delta))
                {
                    Update?.Invoke(delta, false);
                }

                return;
            }

            if (kind.EndsWith(".completed", StringComparison.Ordinal))
            {
                var text = Text(document.RootElement, "text")
                           ?? Text(document.RootElement, "transcript")
                           ?? Text(document.RootElement, "delta");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    Update?.Invoke(text.Trim(), true);
                }
            }
        }
        catch (JsonException)
        {
            // evento non riconosciuto: la sessione continua
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _send.Writer.TryComplete();
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (_socket is { State: WebSocketState.Open })
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "stop",
                    closeTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // il server può aver già chiuso
            }
        }

        foreach (var task in new[] { _sendLoop, _receiveLoop })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // lo stop non deve restare appeso a una socket
            }
        }

        _socket?.Dispose();
        _socket = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
