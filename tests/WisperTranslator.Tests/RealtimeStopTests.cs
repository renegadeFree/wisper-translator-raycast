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
        Assert.False(transcriber.WorkersCompletion.IsCompleted,
            "il timeout di stop non equivale alla fine del decoder nativo");
        engine.Release();
        await run;
        await transcriber.WorkersCompletion.WaitAsync(TimeSpan.FromSeconds(3));
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
        var source = new BufferedSource();
        var partialAfterRefinement = new TaskCompletionSource<TranscriptUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transcriber = new RealtimeTranscriber(
            source,
            new SpeechSegmenter(new AlwaysSpeechVad()),
            new InstantEngine(),
            new RealtimeOptions
            {
                StopTimeout = TimeSpan.FromSeconds(1),
                PartialInterval = TimeSpan.Zero,
                MaxPartialInterval = TimeSpan.Zero,
                MinPartialSeconds = 0.05,
            },
            finalEngine);
        using var cancellation = new CancellationTokenSource();
        transcriber.Update += update =>
        {
            if (!update.IsFinal && update.UtteranceId == 2)
            {
                partialAfterRefinement.TrySetResult(update);
            }
        };
        // Chiude esattamente la prima frase; senza nuovi campioni la sorgente rende il thread.
        source.Buffer.Write(new float[8 * 16000]);
        var run = Task.Run(() => transcriber.RunAsync(cancellation.Token));
        try
        {
            await finalEngine.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Solo ora alimenta la seconda frase, mantenendo bloccata la rifinitura della prima.
            source.Buffer.Write(new float[16 * 512]);
            var update = await partialAfterRefinement.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(update.Start >= TimeSpan.FromSeconds(8));
            Assert.Equal("ciao", update.Provisional);
        }
        finally
        {
            cancellation.Cancel();
            finalEngine.Release();
            await run.WaitAsync(TimeSpan.FromSeconds(6));
            await transcriber.WorkersCompletion.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class BufferedSource : IPcmSource
    {
        public FloatRingBuffer Buffer { get; } = new(10 * 16000);
        public int Read(Span<float> destination) => Buffer.Read(destination);
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
