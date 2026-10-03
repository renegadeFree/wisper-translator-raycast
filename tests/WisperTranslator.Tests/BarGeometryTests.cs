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
        // Oltre cinque righe non si va: la barra coprirebbe lo schermo.
        Assert.Equal(400, BarGeometry.ViewportHeight(9));
        Assert.Equal(246, BarGeometry.WindowHeight(2));
        Assert.Equal(130, BarGeometry.IdleWindowHeight);
    }

    [Fact]
    public void LAltezzaLiberaRestaNeiLimitiEProduceRigheIntere()
    {
        Assert.Equal(BarGeometry.MinHeight, BarGeometry.ClampHeight(10));
        Assert.Equal(BarGeometry.MaxSliderHeight, BarGeometry.ClampHeight(5000));
        Assert.Equal(2, BarGeometry.RowsForHeight(BarGeometry.HeightForRows(2)));
        Assert.Equal(1, BarGeometry.RowsForHeight(BarGeometry.MinHeight));
        Assert.Equal(3, BarGeometry.RowsForHeight(BarGeometry.HeightForRows(3)));
        Assert.Equal(5, BarGeometry.RowsForHeight(2000));
        Assert.Equal(700, BarGeometry.MaxHeightFor(1000));
    }

    [Fact]
    public void IlVetroHaUnLimiteDiLeggibilita()
    {
        Assert.Equal(0.55, BarGeometry.ClampOpacity(0.1));
        Assert.Equal(0.92, BarGeometry.ClampOpacity(1.0));
        Assert.Equal(0.72, BarGeometry.ClampOpacity(double.NaN));
        Assert.Equal(0.72, new AppSettings().BarOpacity);
        Assert.Equal(BarGeometry.WindowHeight(2), new AppSettings().BarHeight);
    }

    [Fact]
    public void IlRidimensionamentoAncoraIlLatoOpposto()
    {
        // Bordo destro: la sinistra non si muove.
        var right = BarGeometry.Resize(600, 100, 889, 246, BarGeometry.Edges.Right, 100, 0, 0, 0, 3440, 1440);
        Assert.Equal(600, right.Left);
        Assert.Equal(100, right.Top);
        Assert.Equal(989, right.Width);
        Assert.Equal(246, right.Height);

        // Bordo sinistro: resta fermo il bordo destro (600 + 889 = 1489).
        var left = BarGeometry.Resize(600, 100, 889, 246, BarGeometry.Edges.Left, 100, 0, 0, 0, 3440, 1440);
        Assert.Equal(700, left.Left);
        Assert.Equal(789, left.Width);
        Assert.Equal(1489, left.Left + left.Width);

        // Bordo superiore: resta fermo il bordo inferiore (100 + 246 = 346).
        var top = BarGeometry.Resize(600, 100, 889, 246, BarGeometry.Edges.Top, 0, 60, 0, 0, 3440, 1440);
        Assert.Equal(160, top.Top);
        Assert.Equal(186, top.Height);
        Assert.Equal(346, top.Top + top.Height);

        // Angolo in basso a destra: crescono entrambe le misure.
        var corner = BarGeometry.Resize(600, 100, 889, 246, BarGeometry.Edges.Right | BarGeometry.Edges.Bottom,
            211, 154, 0, 0, 3440, 1440);
        Assert.Equal(1100, corner.Width);
        Assert.Equal(400, corner.Height);
    }

    [Fact]
    public void IlRidimensionamentoRispettaLimitiEAreaDiLavoro()
    {
        var tiny = BarGeometry.Resize(100, 100, 889, 246, BarGeometry.Edges.Left | BarGeometry.Edges.Top,
            5000, 5000, 0, 0, 3440, 1440);
        Assert.Equal(BarGeometry.MinWidth, tiny.Width);
        Assert.Equal(BarGeometry.MinHeight, tiny.Height);

        var huge = BarGeometry.Resize(100, 100, 889, 246, BarGeometry.Edges.Right | BarGeometry.Edges.Bottom,
            9000, 9000, 0, 0, 1000, 500);
        Assert.Equal(1000, huge.Width);
        Assert.Equal(350, huge.Height);
        Assert.True(huge.Left + huge.Width <= 1000);
        Assert.True(huge.Top + huge.Height <= 500);
    }

    [Fact]
    public void IBordiSiRiconosconoAncheNegliAngoli()
    {
        Assert.Equal(BarGeometry.Edges.None, BarGeometry.EdgeAt(400, 120, 889, 246));
        Assert.Equal(BarGeometry.Edges.Right, BarGeometry.EdgeAt(880, 120, 889, 246));
        Assert.Equal(BarGeometry.Edges.Left, BarGeometry.EdgeAt(2, 120, 889, 246));
        Assert.Equal(BarGeometry.Edges.Bottom, BarGeometry.EdgeAt(400, 243, 889, 246));
        Assert.Equal(BarGeometry.Edges.Right | BarGeometry.Edges.Bottom,
            BarGeometry.EdgeAt(880, 240, 889, 246));
        Assert.Equal(BarGeometry.Edges.Left | BarGeometry.Edges.Top,
            BarGeometry.EdgeAt(8, 8, 889, 246));
    }

    [Fact]
    public void LaPillolaMinimalHaMisureStabili()
    {
        Assert.Equal(320, BarGeometry.MiniWidth);
        Assert.Equal(48, BarGeometry.MiniHeight);
        Assert.Equal(BarGeometry.CornerRadiusValue, BarGeometry.MiniRadius);
    }

    [Fact]
    public void IlVetroSegueLoSliderSenzaPerdereLeggibilita()
    {
        // Slider basso = vetro più trasparente, slider alto = pannello più coprente.
        Assert.True(BarGeometry.AcrylicTint(0.55) < BarGeometry.AcrylicTint(0.92));
        Assert.True(BarGeometry.PanelVeil(0.55) < BarGeometry.PanelVeil(0.92));

        // Entrambi restano dentro limiti che tengono il testo leggibile su sfondi chiari.
        Assert.InRange(BarGeometry.AcrylicTint(double.NaN), 0.1, 0.5);
        Assert.InRange(BarGeometry.PanelVeil(double.NaN), 0.1, 0.45);
        Assert.Equal(BarGeometry.AcrylicTint(0.72), BarGeometry.AcrylicTint(0.72));
    }

    [Fact]
    public void LaLarghezzaPredefinitaE760()
    {
        Assert.Equal(760, new AppSettings().BarWidth);
        Assert.Equal(760, BarGeometry.ClampWidth(new AppSettings().BarWidth));
        Assert.True(new AppSettings().BarMiniIdle);
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
