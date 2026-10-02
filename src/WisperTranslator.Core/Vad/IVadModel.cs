namespace WisperTranslator.Core.Vad;

/// <summary>Modello di rilevamento voce: dato un frame restituisce una probabilità in [0,1].</summary>
public interface IVadModel : IDisposable
{
    int FrameSamples { get; }

    double SpeechProbability(ReadOnlySpan<float> frame);

    void Reset();
}
