using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WisperTranslator.Core.Audio;

/// <summary>
/// Cattura una sorgente (audio di sistema o microfono) e la normalizza a
/// 16 kHz mono float32, pronta per VAD e ASR.
/// </summary>
public sealed class VoiceCapture : IAudioSource, IDisposable
{
    public const int SampleRate = 16000;

    private readonly MMDevice _device;
    private readonly IWaveIn _capture;
    private readonly ISampleProvider _pipeline;
    private readonly FloatRingBuffer _buffer;
    private readonly float[] _scratch = new float[8192];
    private long _resampleRemainder;
    private bool _disposed;

    public VoiceCapture(SourceKind kind, string? deviceIdOrName = null, int bufferSeconds = 5)
    {
        Kind = kind;
        _device = AudioDevices.Open(kind, deviceIdOrName);
        Name = _device.FriendlyName;
        _capture = kind == SourceKind.System
            ? new WasapiLoopbackCapture(_device)
            : new WasapiCapture(_device);

        CapturedFormat = _capture.WaveFormat;
        _pipeline = new WdlResamplingSampleProvider(
            new DownmixToMonoSampleProvider(new WaveInProvider(_capture).ToSampleProvider()),
            SampleRate);
        _buffer = new FloatRingBuffer(SampleRate * bufferSeconds);
        _capture.DataAvailable += OnDataAvailable;
    }

    public SourceKind Kind { get; }

    public string Name { get; }

    public bool Enabled { get; set; } = true;

    public float Gain { get; set; } = 1f;

    /// <summary>Livello RMS dell'ultimo blocco, con decadimento: usato dai meter della UI.</summary>
    public float Level { get; private set; }

    public bool IsRunning { get; private set; }

    public WaveFormat CapturedFormat { get; }

    public double BufferedSeconds => _buffer.Count / (double)SampleRate;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _buffer.Clear();
        _resampleRemainder = 0;
        _capture.StartRecording();
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _capture.StopRecording();
        IsRunning = false;
    }

    public int Read(Span<float> destination)
    {
        var read = _buffer.Read(destination);
        if (!Enabled)
        {
            destination[..read].Clear();
        }

        return read;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        // La catena è pull-based: quanti campioni a 16 kHz corrispondono a questo blocco?
        var frames = args.BytesRecorded / Math.Max(1, CapturedFormat.BlockAlign);
        var scaled = frames * (long)SampleRate + _resampleRemainder;
        var wanted = (int)(scaled / CapturedFormat.SampleRate);
        _resampleRemainder = scaled % CapturedFormat.SampleRate;
        if (wanted <= 0)
        {
            return;
        }

        var produced = 0;
        double energy = 0;
        while (produced < wanted)
        {
            var chunk = Math.Min(_scratch.Length, wanted - produced);
            var read = _pipeline.Read(_scratch, 0, chunk);
            if (read <= 0)
            {
                break;
            }

            var samples = _scratch.AsSpan(0, read);
            if (!Enabled)
            {
                samples.Clear();
            }

            foreach (var sample in samples)
            {
                energy += sample * (double)sample;
            }

            _buffer.Write(samples);
            produced += read;
        }

        Level = produced > 0
            ? Math.Max((float)Math.Sqrt(energy / produced), Level * 0.8f)
            : Level * 0.8f;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capture.DataAvailable -= OnDataAvailable;
        Stop();
        _capture.Dispose();
        _device.Dispose();
    }
}
