using WisperTranslator.Core.Asr;

namespace WisperTranslator.Tests;

public class StreamingTextAccumulatorTests
{
    [Fact]
    public void ComponeUnaFraseDaDeltaIncrementali()
    {
        var accumulator = new StreamingTextAccumulator();

        Assert.Equal("Good", accumulator.Append("Good", false));
        Assert.Equal("Good mor", accumulator.Append(" \nmor", false));
        Assert.Equal("Good morning.", accumulator.Append("ning.", false));
        Assert.Equal("Good morning. This", accumulator.Append(" This", false));
        Assert.Equal("Good morning. This is", accumulator.Append(" is", false));
    }

    [Fact]
    public void SostituisceIlTestoQuandoIlServerMandaLaFraseCumulativa()
    {
        var accumulator = new StreamingTextAccumulator();

        accumulator.Append("nel", false);
        Assert.Equal("nel 20", accumulator.Append(" nel 20", false));
    }

    [Fact]
    public void NonDuplicaUnDeltaGiaPresenteInCoda()
    {
        var accumulator = new StreamingTextAccumulator();

        accumulator.Append("nel 20", false);
        Assert.Equal("nel 20", accumulator.Append(" 20", false));
    }

    [Fact]
    public void NonSpezzaLaPunteggiatura()
    {
        var accumulator = new StreamingTextAccumulator();

        Assert.Equal("Good", accumulator.Append("Good", false));
        Assert.Equal("Good.", accumulator.Append(".", false));
        Assert.Equal("Good. This", accumulator.Append(" This", false));
    }

    [Fact]
    public void UnEventoCompletatoSostituisceTuttoIlTesto()
    {
        var accumulator = new StreamingTextAccumulator();

        accumulator.Append("Good", false);
        Assert.Equal(
            "Good morning, this is the final sentence.",
            accumulator.Append("Good morning, this is the final sentence.", completed: true));
    }

    [Fact]
    public void ResetAzzeraLaFrasePerIlProssimoEnunciato()
    {
        var accumulator = new StreamingTextAccumulator();

        accumulator.Append("prima frase", false);
        accumulator.Reset();

        Assert.Equal(string.Empty, accumulator.Text);
        Assert.Equal("seconda frase", accumulator.Append("seconda frase", false));
    }
}
