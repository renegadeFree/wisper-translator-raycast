using WisperTranslator.Core.Asr;
using WisperTranslator.Core.Audio;
using WisperTranslator.Core.Session;
using WisperTranslator.Core.Vad;

namespace WisperTranslator.Tests;

/// <summary>
/// Regressione del difetto "Ferma chiude il programma": la decodifica in corso non deve
/// poter bloccare lo stop né far liberare il motore mentre sta lavorando.
/// </summary>
public class RealtimeStopTests
{
    [Fact]
    public async Task LoStopNonRestaAppesoSeUnaDecodificaENInCorso()
    {
        var engine = new BlockingEngine();
        using var transcriber = Build(engine, TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();

        // Come nell'applicazione: la pipeline gira su un thread suo, lo stop arriva da fuori.
        var run = Task.Run(() => transcriber.RunAsync(cancellation.Token));
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(6)));

        Assert.Same(run, completed);
        engine.Release();
        await run;
        Assert.False(engine.Disposed, "il motore appartiene al pool e non va liberato durante lo stop");
    }

    [Fact]
    public async Task LoStopTornaSubitoConUnMotoreVeloce()
    {
        var engine = new InstantEngine();
        using var transcriber = Build(engine, TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();

        var run = Task.Run(() => transcriber.RunAsync(cancellation.Token));
        await Task.Delay(200);
        cancellation.Cancel();

        var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(run, completed);
        await run;
    }

    [Fact]
    public void LaCodaProvvisoriaEscludeLaParteGiaStabile()
    {
        var cue = new Cue(1, "ciao come", string.Empty, false, TimeSpan.Zero, TimeSpan.FromSeconds(2), DateTime.Now, "ciao come stai");
        Assert.Equal("stai", cue.ProvisionalTail);
        Assert.True(cue.HasProvisional);

        var final = cue with { IsFinal = true };
        Assert.False(final.HasProvisional);
    }

    [Fact]
    public async Task LaRifinituraNonBloccaIParziali()
    {
        var finalEngine = new BlockingEngine();
        var partialEngine = new CountingEngine();
        using var transcriber = new RealtimeTranscriber(
            new NoiseSource(),
            new SpeechSegmenter(new AlwaysSpeechVad()),
            partialEngine,
            new RealtimeOptions
            {
                StopTimeout = TimeSpan.FromSeconds(1),
                PartialInterval = TimeSpan.FromMilliseconds(20),
                MaxPartialInterval = TimeSpan.FromMilliseconds(20),
                MinPartialSeconds = 0.05,
            },
            finalEngine);
        using var cancellation = new CancellationTokenSource();

        var run = Task.Run(() => transcriber.RunAsync(cancellation.Token));
        await finalEngine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var callsAtRefinementStart = partialEngine.Calls;

        await Task.Delay(500);
        Assert.True(
            partialEngine.Calls > callsAtRefinementStart,
            "i parziali devono continuare mentre il modello grande rifinisce la frase precedente");

        cancellation.Cancel();
        finalEngine.Release();
        await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(6)));
    }

    private sealed class CountingEngine : IAsrEngine
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string Name => "contatore";

        public Task<AsrResult> TranscribeAsync(
            float[] samples,
            int sampleRate = 16000,
            string? language = null,
            bool useContext = true,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new AsrResult("ciao", language, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), []));
        }

        public void Dispose()
        {
        }
    }

    private static RealtimeTranscriber Build(IAsrEngine engine, TimeSpan stopTimeout) =>
        new(
            new NoiseSource(),
            new SpeechSegmenter(new AlwaysSpeechVad()),
            engine,
            new RealtimeOptions
            {
                StopTimeout = stopTimeout,
                PartialInterval = TimeSpan.FromMilliseconds(50),
                MaxPartialInterval = TimeSpan.FromMilliseconds(50),
                MinPartialSeconds = 0.1,
            });

    private sealed class NoiseSource : IPcmSource
    {
        private float _phase;

        public int Read(Span<float> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                _phase += 0.05f;
                destination[i] = MathF.Sin(_phase) * 0.3f;
            }

            return destination.Length;
        }
    }

    private sealed class AlwaysSpeechVad : IVadModel
    {
        public int FrameSamples => 512;

        public double SpeechProbability(ReadOnlySpan<float> frame) => 1.0;

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class BlockingEngine : IAsrEngine
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public string Name => "bloccante";

        public async Task<AsrResult> TranscribeAsync(
            float[] samples,
            int sampleRate = 16000,
            string? language = null,
            bool useContext = true,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _release.Task;
            return new AsrResult("test", language, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), []);
        }

        public void Release() => _release.TrySetResult();

        public void Dispose() => Disposed = true;
    }

    private sealed class InstantEngine : IAsrEngine
    {
        public string Name => "istantaneo";

        public Task<AsrResult> TranscribeAsync(
            float[] samples,
            int sampleRate = 16000,
            string? language = null,
            bool useContext = true,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AsrResult("ciao", language, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), []));

        public void Dispose()
        {
        }
    }
}
