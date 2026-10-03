using WisperTranslator.Core.Session;
using WisperTranslator.Core.Settings;

namespace WisperTranslator.Tests;

/// <summary>Misure della barra e contenuto d'esempio per gli screenshot.</summary>
public class BarGeometryTests
{
    [Fact]
    public void LaLarghezzaRestaNeiLimiti()
    {
        Assert.Equal(BarGeometry.MinWidth, BarGeometry.ClampWidth(100));
        Assert.Equal(BarGeometry.MaxWidth, BarGeometry.ClampWidth(9999));
        Assert.Equal(BarGeometry.DefaultWidth, BarGeometry.ClampWidth(double.NaN));
        Assert.Equal(900, BarGeometry.ClampWidth(900));
    }

    [Fact]
    public void IlRaggioRestaUnRettangoloArrotondato()
    {
        Assert.Equal(BarGeometry.CornerRadiusValue, BarGeometry.CornerRadius(BarGeometry.WindowHeight(2)));
        // Sotto una certa altezza non si scende: resterebbe un rettangolo spigoloso.
        Assert.Equal(8, BarGeometry.CornerRadius(4));
    }

    [Fact]
    public void AltezzaELarghezzaSeguonoLeImpostazioni()
    {
        Assert.Equal(80, BarGeometry.ViewportHeight(1));
        Assert.Equal(160, BarGeometry.ViewportHeight(2));
        // Oltre due righe non si va: la barra coprirebbe lo schermo.
        Assert.Equal(160, BarGeometry.ViewportHeight(9));
        Assert.Equal(248, BarGeometry.WindowHeight(2));
    }

    [Fact]
    public void IlDiametroDellaRegioneSegueIlDpi()
    {
        Assert.Equal(36, BarGeometry.RegionDiameter(BarGeometry.CornerRadiusValue, 1.0));
        Assert.Equal(45, BarGeometry.RegionDiameter(BarGeometry.CornerRadiusValue, 1.25));
    }

    [Fact]
    public void LaLarghezzaPredefinitaE760()
    {
        Assert.Equal(760, new AppSettings().BarWidth);
        Assert.Equal(760, BarGeometry.ClampWidth(new AppSettings().BarWidth));
    }

    [Fact]
    public void IContenutiDesempioHannoTreParlantiEUnaFraseProvvisoria()
    {
        var cues = BarSamples.Cues();

        Assert.Equal(3, cues.Count);
        Assert.Equal(3, cues.Select(cue => cue.Speaker).Distinct().Count());
        Assert.All(cues, cue => Assert.True(cue.HasSpeaker));
        Assert.Contains(cues, cue => !cue.IsFinal && cue.ProvisionalTail.Length > 0);
        Assert.Contains(cues, cue => cue.Speaker == Speakers.You);
        Assert.Equal(cues.Count, cues.Select(cue => cue.Id).Distinct().Count());
    }
}
