using WisperTranslator.Core.Audio;

namespace WisperTranslator.Tests;

public class AudioMixerTests
{
    [Fact]
    public void SommaLeSorgentiConIlLoroGain()
    {
        using var mixer = new AudioMixer();
        mixer.Add(new FakeSource([1f, 1f, 1f, 1f]) { Gain = 0.5f });
        mixer.Add(new FakeSource([1f, 1f, 1f, 1f]) { Gain = 0.25f });

        var block = new float[4];
        var read = mixer.Read(block);

        Assert.Equal(4, read);
        Assert.Equal([0.75f, 0.75f, 0.75f, 0.75f], block);
    }

    [Fact]
    public void LimitaILivelloAMenoUno()
    {
        using var mixer = new AudioMixer();
        mixer.Add(new FakeSource([1f, 1f, 1f, 1f]));
        mixer.Add(new FakeSource([1f, 1f, 1f, 1f]));

        var block = new float[4];
        mixer.Read(block);

        Assert.Equal([1f, 1f, 1f, 1f], block);
    }

    [Fact]
    public void IgnoraLeSorgentiDisabilitate()
    {
        using var mixer = new AudioMixer();
        mixer.Add(new FakeSource([1f, 1f], 2) { Enabled = false });
        mixer.Add(new FakeSource([0.5f, 0.5f], 2));

        var block = new float[2];
        var read = mixer.Read(block);

        Assert.Equal(2, read);
        Assert.Equal([0.5f, 0.5f], block);
    }

    private sealed class FakeSource(float[] data, int? length = null) : IAudioSource
    {
        private readonly float[] _data = length is null ? data : data[..length.Value];

        public string Name => "fake";

        public bool Enabled { get; set; } = true;

        public float Gain { get; set; } = 1f;

        public int Read(Span<float> destination)
        {
            var count = Math.Min(destination.Length, _data.Length);
            _data.AsSpan(0, count).CopyTo(destination);
            return count;
        }
    }
}
