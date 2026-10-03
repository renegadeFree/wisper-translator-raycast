using WisperTranslator.Core.Vad;

namespace WisperTranslator.Tests;

public class SpeechSegmenterTests
{
    private const int Frame = 512;

    [Fact]
    public void EmetteUnEnunciatoPerUnaBurstDiParlato()
    {
        var segments = Run(voiceFrames: 60, silenceBefore: 40, silenceAfter: 60);

        var segment = Assert.Single(segments);
        Assert.InRange(segment.Seconds, 1.6, 2.3);
    }

    [Fact]
    public void EmetteDueEnunciatiPerDueBurstSeparate()
    {
        var segments = Run(voiceFrames: 60, silenceBefore: 30, silenceAfter: 60, repeats: 2);

        Assert.Equal(2, segments.Count);
        Assert.True(segments[0].Start < segments[1].Start);
    }

    [Fact]
    public void IncludeIlPreRollPrimaDellInizioRilevato()
    {
        var segments = Run(voiceFrames: 80, silenceBefore: 40, silenceAfter: 80);

        var segment = Assert.Single(segments);
        // Il timestamp include il margine di pre-roll effettivamente presente nei campioni.
        var expectedStart = 40 * Frame / 16000.0;
        Assert.InRange(segment.Start.TotalSeconds, expectedStart - 0.16, expectedStart);
    }

    [Fact]
    public void ScartaIDispositiviTroppoCorti()
    {
        var segments = Run(voiceFrames: 4, silenceBefore: 20, silenceAfter: 40);

        Assert.Empty(segments);
    }

    private static List<SpeechSegment> Run(int voiceFrames, int silenceBefore, int silenceAfter, int repeats = 1)
    {
        var totalFrames = silenceBefore + repeats * (voiceFrames + silenceAfter);
        var script = new List<double>(totalFrames);
        script.AddRange(Enumerable.Repeat(0.05, silenceBefore));
        for (var repeat = 0; repeat < repeats; repeat++)
        {
            script.AddRange(Enumerable.Repeat(0.9, voiceFrames));
            script.AddRange(Enumerable.Repeat(0.05, silenceAfter));
        }

        using var vad = new ScriptedVad(script);
        using var segmenter = new SpeechSegmenter(vad);
        var segments = new List<SpeechSegment>();
        var audio = new float[Frame];

        while (vad.Consumed < script.Count)
        {
            Array.Fill(audio, 0.3f);
            segments.AddRange(segmenter.Feed(audio));
        }

        return segments;
    }

    /// <summary>VAD finto: rende il test deterministico e indipendente dal modello ONNX.</summary>
    private sealed class ScriptedVad(List<double> probabilities) : IVadModel
    {
        public int FrameSamples => 512;

        public int Consumed { get; private set; }

        public double SpeechProbability(ReadOnlySpan<float> frame) =>
            Consumed < probabilities.Count ? probabilities[Consumed++] : 0.0;

        public void Reset() => Consumed = 0;

        public void Dispose()
        {
        }
    }
}
