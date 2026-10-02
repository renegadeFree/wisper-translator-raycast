using System.Text.Json;
using System.Text.Json.Serialization;
using WisperTranslator.Core.Session;

namespace WisperTranslator.Core.Settings;

/// <summary>Preferenze dell'utente, salvate in JSON dentro la cartella dell'applicazione.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 570;

    public double WindowHeight { get; set; } = 320;

    public bool Topmost { get; set; } = true;

    /// <summary>Trasparenza della finestra widget (1 = opaca).</summary>
    public double Opacity { get; set; } = 0.95;

    public double FontSize { get; set; } = 16;

    public int MaxCues { get; set; } = 20;

    public bool ShowOriginal { get; set; } = true;

    public bool OverlayEnabled { get; set; }

    public int OverlayMaxCues { get; set; } = 3;

    public double OverlayFontSize { get; set; } = 30;

    /// <summary>
    /// True (default) = l'overlay non compare nelle registrazioni e nelle condivisioni schermo.
    /// Disattivabile per chi vuole i sottotitoli dentro lo streaming.
    /// </summary>
    public bool OverlayHiddenFromCapture { get; set; } = true;

    /// <summary>Chiudendo la finestra il programma resta attivo nell'area di notifica.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Avvio automatico con Windows (voce in HKCU\...\Run).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Giorni di conservazione dello storico: 0 = per sempre.</summary>
    public int RetentionDays { get; set; } = 0;

    /// <summary>Provider IA predefinito e relative impostazioni (persistite come JSON).</summary>
    public AiSettings Ai { get; set; } = new();

    public bool SystemAudio { get; set; } = true;

    public bool Microphone { get; set; }

    public string SourceLanguage { get; set; } = "it";

    public string? SystemDeviceId { get; set; }

    public string? MicrophoneDeviceId { get; set; }

    public string PartialModelId { get; set; } = "whisper-base-q5_1";

    public string FinalModelId { get; set; } = "whisper-small-q5_1";

    public bool Translate { get; set; } = true;

    public static string FilePath => Path.Combine(AppPaths.Root, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // impostazioni illeggibili: si riparte dai default senza perdere l'avvio
        }

        // Primo avvio: i modelli si scelgono in base alla macchina rilevata.
        var settings = new AppSettings();
        (settings.PartialModelId, settings.FinalModelId) = Hardware.HardwareDetector.Detect().RecommendedModels;
        return settings;
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception)
        {
            // il salvataggio non deve mai far cadere l'applicazione
        }
    }

    public SessionOptions ToSessionOptions() => new()
    {
        PartialModel = Models.AsrModels.FromId(PartialModelId),
        FinalModel = Models.AsrModels.FromId(FinalModelId),
        SourceLanguage = SourceLanguage,
        TargetLanguage = SourceLanguage == "it" ? "en" : "it",
        Translate = Translate,
        SystemAudio = SystemAudio,
        Microphone = Microphone,
        SystemDeviceId = SystemDeviceId,
        MicrophoneDeviceId = MicrophoneDeviceId,
    };
}
