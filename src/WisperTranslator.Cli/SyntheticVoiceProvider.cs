using NAudio.Wave;

namespace WisperTranslator.Cli;

/// <summary>
/// Riproduce il segnale di <see cref="AudioSignals"/> in modo continuo: alterna 2 s di
/// "voce" e 2,5 s di silenzio, così il loopback ha qualcosa da catturare.
/// </summary>
internal sealed class SyntheticVoiceProvider : ISampleProvider
{
    private const double CycleSeconds = 8.0;
    private const double FirstStart = 1.0;
    private const double FirstEnd = 3.0;
    private const double SecondStart = 4.5;
    private const double SecondEnd = 6.5;

    private readonly float[] _frame = new float[512];
    private long _position;
    private int _frameIndex;
    private int _frameFill;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);

    public int Read(float[] buffer, int offset, int count)
    {
        var written = 0;
        while (written < count)
        {
            if (_frameFill == 0)
            {
                FillFrame();
            }

            var available = Math.Min(count - written, _frame.Length - _frameFill);
            Array.Copy(_frame, _frameFill, buffer, offset + written, available);
            _frameFill += available;
            written += available;
            _position += available;

            if (_frameFill == _frame.Length)
            {
                _frameFill = 0;
                _frameIndex++;
            }
        }

        return written;
    }

    private void FillFrame()
    {
        var start = _frameIndex * (double)_frame.Length / AudioSignals.SampleRate;
        var cycle = start % CycleSeconds;
        var speaking = (cycle >= FirstStart && cycle < FirstEnd) || (cycle >= SecondStart && cycle < SecondEnd);
        if (speaking)
        {
            AudioSignals.FillVoice(_frame, _frameIndex);
        }
        else
        {
            AudioSignals.FillSilence(_frame, _frameIndex);
        }
    }
}
