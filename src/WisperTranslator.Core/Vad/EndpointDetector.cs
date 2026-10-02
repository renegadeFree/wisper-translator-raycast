namespace WisperTranslator.Core.Vad;

public sealed class EndpointOptions
{
    public int SampleRate { get; init; } = 16000;

    public int FrameSamples { get; init; } = 512;

    /// <summary>Probabilità oltre la quale il frame è considerato parlato.</summary>
    public double SpeechThreshold { get; init; } = 0.5;

    /// <summary>Probabilità sotto la quale il frame è considerato silenzio (isteresi).</summary>
    public double SilenceThreshold { get; init; } = 0.35;

    /// <summary>Parlato minimo perché l'enunciato venga aperto: filtra i click.</summary>
    public double MinSpeechSeconds { get; init; } = 0.25;

    /// <summary>Silenzio necessario per chiudere l'enunciato (endpoint).</summary>
    public double MinSilenceSeconds { get; init; } = 0.5;

    /// <summary>
    /// Durata massima di un enunciato: oltre, viene chiuso d'ufficio. Su parlato continuo
    /// (video, lezioni) un tetto alto fa ridecodificare ogni volta molti secondi di audio e
    /// manda il decoder in loop di ripetizione: 8 s è il compromesso misurato.
    /// </summary>
    public double MaxUtteranceSeconds { get; init; } = 8.0;

    /// <summary>Coda di silenzio conservata per non troncare l'ultima parola.</summary>
    public double PadSeconds { get; init; } = 0.1;

    public double FrameSeconds => FrameSamples / (double)SampleRate;
}

public enum EndpointSignal
{
    None,
    SpeechStart,
    SpeechEnd,
}

/// <summary>
/// Macchina a stati pura (nessun audio, nessun modello): decide quando un enunciato
/// inizia e finisce a partire dalla probabilità di voce frame per frame.
/// </summary>
public sealed class EndpointDetector
{
    private readonly EndpointOptions _options;
    private int _speechFrames;
    private int _silenceFrames;
    private int _utteranceFrames;

    public EndpointDetector(EndpointOptions? options = null) => _options = options ?? new EndpointOptions();

    public EndpointOptions Options => _options;

    public bool InSpeech { get; private set; }

    /// <summary>Durata dell'enunciato in corso (o appena chiuso).</summary>
    public TimeSpan UtteranceDuration => TimeSpan.FromSeconds(_utteranceFrames * _options.FrameSeconds);

    /// <summary>Frame di parlato accumulati prima dell'apertura dell'enunciato.</summary>
    public int PendingSpeechFrames => _speechFrames;

    public int TrailingSilenceFrames => _silenceFrames;

    public EndpointSignal Process(double speechProbability)
    {
        if (!InSpeech)
        {
            _speechFrames = speechProbability >= _options.SpeechThreshold ? _speechFrames + 1 : 0;
            if (_speechFrames * _options.FrameSeconds >= _options.MinSpeechSeconds)
            {
                InSpeech = true;
                _utteranceFrames = _speechFrames;
                _silenceFrames = 0;
                return EndpointSignal.SpeechStart;
            }

            return EndpointSignal.None;
        }

        _utteranceFrames++;
        _silenceFrames = speechProbability < _options.SilenceThreshold ? _silenceFrames + 1 : 0;

        var tooLong = _utteranceFrames * _options.FrameSeconds >= _options.MaxUtteranceSeconds;
        var silence = _silenceFrames * _options.FrameSeconds >= _options.MinSilenceSeconds;
        if (!tooLong && !silence)
        {
            return EndpointSignal.None;
        }

        InSpeech = false;
        _speechFrames = 0;
        _silenceFrames = 0;
        return EndpointSignal.SpeechEnd;
    }

    public void Reset()
    {
        InSpeech = false;
        _speechFrames = 0;
        _silenceFrames = 0;
        _utteranceFrames = 0;
    }
}
