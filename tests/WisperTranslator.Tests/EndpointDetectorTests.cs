using WisperTranslator.Core.Vad;

namespace WisperTranslator.Tests;

public class EndpointDetectorTests
{
    private static readonly EndpointOptions Options = new();

    [Fact]
    public void ApreLEnunciatoDopoIlMinimoDiParlato()
    {
        var detector = new EndpointDetector(Options);
        var signals = Feed(detector, 0.9, 10);

        var expected = (int)Math.Ceiling(Options.MinSpeechSeconds / Options.FrameSeconds);
        Assert.Equal(expected, signals.FindIndex(signal => signal == EndpointSignal.SpeechStart) + 1);
    }

    [Fact]
    public void NonApreLEnunciatoPerUnClickIsolato()
    {
        var detector = new EndpointDetector(Options);
        var signals = Feed(detector, 0.9, 3);
        signals.AddRange(Feed(detector, 0.05, 10));

        Assert.DoesNotContain(EndpointSignal.SpeechStart, signals);
    }

    [Fact]
    public void ChiudeLEnunciatoDopoIlSilenzioMinimo()
    {
        var detector = new EndpointDetector(Options);
        Feed(detector, 0.9, 20);

        var signals = Feed(detector, 0.05, 30);
        var index = signals.IndexOf(EndpointSignal.SpeechEnd);

        var expected = (int)Math.Ceiling(Options.MinSilenceSeconds / Options.FrameSeconds);
        Assert.Equal(expected, index + 1);
    }

    [Fact]
    public void ChiudeLEnunciatoAlRaggiungimentoDelMassimo()
    {
        var detector = new EndpointDetector(Options);
        var signals = Feed(detector, 0.9, 500);

        var index = signals.IndexOf(EndpointSignal.SpeechEnd);
        Assert.True(index >= 0, "l'enunciato deve chiudersi da solo dopo MaxUtteranceSeconds");
        Assert.True(
            detector.UtteranceDuration.TotalSeconds <= Options.MaxUtteranceSeconds,
            $"durata {detector.UtteranceDuration.TotalSeconds:F2} s oltre il massimo");
    }

    private static List<EndpointSignal> Feed(EndpointDetector detector, double probability, int frames)
    {
        var signals = new List<EndpointSignal>();
        for (var i = 0; i < frames; i++)
        {
            signals.Add(detector.Process(probability));
        }

        return signals;
    }
}
