namespace WisperTranslator.Core.Audio;

/// <summary>Sorgente di campioni float32 a 16 kHz mono, campionabile a blocchi.</summary>
public interface IPcmSource
{
    /// <summary>Riempie <paramref name="destination"/> e restituisce i campioni disponibili (0 se nessuno).</summary>
    int Read(Span<float> destination);
}
