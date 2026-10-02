namespace WisperTranslator.Core.Audio;

/// <summary>Sorgente audio a 16 kHz mono float32, campionabile dal mixer.</summary>
public interface IAudioSource : IPcmSource
{
    string Name { get; }

    bool Enabled { get; set; }

    float Gain { get; set; }
}
