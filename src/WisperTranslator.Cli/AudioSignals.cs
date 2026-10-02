namespace WisperTranslator.Cli;

/// <summary>
/// Segnali sintetici deterministici: servono a verificare il modello VAD e il loopback
/// senza dipendere da un microfono o da audio riprodotto a mano.
/// </summary>
internal static class AudioSignals
{
    public const int SampleRate = 16000;

    public static void FillSilence(float[] frame, int frameIndex) => Array.Clear(frame);

    /// <summary>Vocale sintetica: due formanti con modulazione a 4 Hz e un filo di rumore.</summary>
    public static void FillVoice(float[] frame, int frameIndex)
    {
        var start = (long)frameIndex * frame.Length;
        for (var i = 0; i < frame.Length; i++)
        {
            var t = (start + i) / (double)SampleRate;
            var carrier = Math.Sin(2 * Math.PI * 180 * t)
                          + 0.5 * Math.Sin(2 * Math.PI * 900 * t)
                          + 0.3 * Math.Sin(2 * Math.PI * 1800 * t);
            var envelope = 0.6 + 0.4 * Math.Sin(2 * Math.PI * 4 * t);
            var noise = (Noise(frameIndex, i) - 0.5) * 0.04;
            frame[i] = (float)(0.25 * carrier * envelope + noise);
        }
    }

    private static double Noise(int frameIndex, int sampleIndex)
    {
        var seed = (frameIndex * 73856093) ^ (sampleIndex * 19349663);
        seed = (seed * 1103515245 + 12345) & 0x7fffffff;
        return seed / (double)0x7fffffff;
    }
}
