using WisperTranslator.Core.Audio;

namespace WisperTranslator.Tests;

public class FloatRingBufferTests
{
    [Fact]
    public void MantieneICampioniPiuRecentiQuandoTrabocca()
    {
        var buffer = new FloatRingBuffer(5);

        buffer.Write([1, 2, 3, 4, 5, 6, 7]);

        var result = new float[5];
        Assert.Equal(5, buffer.Read(result));
        Assert.Equal([3f, 4f, 5f, 6f, 7f], result);
    }

    [Fact]
    public void LeggeNellOrdineCorrettoDopoUnGiro()
    {
        var buffer = new FloatRingBuffer(4);
        buffer.Write([1, 2, 3]);

        var first = new float[2];
        Assert.Equal(2, buffer.Read(first));

        buffer.Write([4, 5, 6]);

        var rest = new float[4];
        Assert.Equal(4, buffer.Read(rest));
        Assert.Equal([3f, 4f, 5f, 6f], rest);
    }

    [Fact]
    public void RestituisceZeroQuandoVuoto()
    {
        var buffer = new FloatRingBuffer(8);
        Assert.Equal(0, buffer.Read(new float[4]));
        Assert.Equal(0, buffer.Count);
    }
}
