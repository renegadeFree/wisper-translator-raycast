using System.Text.Json;
using System.Text.Json.Serialization;
using WisperTranslator.Core.Hardware;
using WisperTranslator.Core.Session;

namespace WisperTranslator.Core.Settings;

/// <summary>Preferenze dell'utente, salvate in JSON dentro la cartella dell'applicazione.</summary>
public sealed class AppSettings
{
    /// <summary>Versione del file: serve solo alle migrazioni una-tantum.</summary>
    public int SettingsVersion { get; set; }

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

    /// <summary>Scrive un log diagnostico per ogni battuta (misure di latenza e carico).</summary>
    public bool DiagnosticLog { get; set; }

    /// <summary>Giorni di conservazione dello storico: 0 = per sempre.</summary>
    public int RetentionDays { get; set; } = 0;

    /// <summary>Template di mappa scelto nel dettaglio sessione.</summary>
    public string MapTemplateId { get; set; } = "gerarchica-lr";

    /// <summary>Template di report PDF scelto nel dettaglio sessione.</summary>
    public string PdfTemplateId { get; set; } = "verbale-riunione";

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

    /// <summary>Modalità conversazione: microfono "Tu" e audio di sistema con i nomi dei parlanti.</summary>
    public bool ConversationMode { get; set; }

    /// <summary>Diarizzatore scelto per la modalità conversazione (id di catalogo).</summary>
    public string DiarizerModelId { get; set; } = Asr.NeMoModels.DiarizerDefaultId;

    // --- Barra fluttuante ---

    /// <summary>Cosa si apre all'avvio: barra fluttuante o pannello esteso.</summary>
    public bool StartWithBar { get; set; }

    public double? BarLeft { get; set; }

    public double? BarTop { get; set; }

    /// <summary>Frasi visibili nella barra (1–2): le altre restano sotto, raggiungibili con la rotellina.</summary>
    public int BarRows { get; set; } = 2;

    /// <summary>Larghezza della capsula in pixel: una frase lunga deve starci senza essere tagliata.</summary>
    public double BarWidth { get; set; } = BarGeometry.DefaultWidth;

    /// <summary>Quante frasi tenere in memoria nella barra (3–8).</summary>
    public int BarBuffer { get; set; } = 5;

    /// <summary>Testo mostrato nella barra: entrambi, solo originale o solo traduzione.</summary>
    public BarTextMode BarText { get; set; } = BarTextMode.Entrambi;

    /// <summary>Modalità discreta: la barra lascia passare i clic tranne sulla maniglia.</summary>
    public bool BarDiscreet { get; set; }

    /// <summary>Riduce o disattiva le animazioni della barra.</summary>
    public bool BarAnimations { get; set; } = true;

    /// <summary>Profilo prestazioni: Auto segue l'hardware rilevato, gli altri lo forzano.</summary>
    public PerformancePreset Preset { get; set; } = PerformancePreset.Auto;

    /// <summary>Motore della corsia istantanea; null = quello del profilo.</summary>
    public AsrBackend? LiveBackend { get; set; }

    /// <summary>Motore della corsia definitiva; null = quello del profilo.</summary>
    public AsrBackend? FinalBackend { get; set; }

    /// <summary>Runtime accelerato scaricato a parte (null = solo CPU).</summary>
    public GpuRuntime Gpu { get; set; } = GpuRuntime.Nessuno;

    /// <summary>Profilo risolto per questa macchina, con gli eventuali override dell'utente.</summary>
    public PerformanceProfile ResolveProfile()
    {
        var profile = PerformanceProfile.Resolve(Preset, HardwareDetector.Detect());
        return profile with
        {
            Live = LiveBackend ?? profile.Live,
            Final = FinalBackend ?? profile.Final,
        };
    }

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
                    loaded.Migrate();
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

    /// <summary>
    /// Le versioni precedenti aprivano la barra all'avvio. Dalla 2.2 la barra è un pannello
    /// su richiesta: la migrazione spegne quella tendenza una sola volta, senza impedire di
    /// riattivarla nelle impostazioni.
    /// </summary>
    private void Migrate()
    {
        if (SettingsVersion >= 4)
        {
            return;
        }

        if (SettingsVersion < 3)
        {
            StartWithBar = false;
        }

        // Il vecchio predefinito (Nemotron) resta selezionabile, ma per le call normali
        // Sortformer separa meglio le voci: si migra solo il valore rimasto al default.
        if (string.Equals(DiarizerModelId, "nemotron-3-diarization", StringComparison.OrdinalIgnoreCase))
        {
            DiarizerModelId = Asr.NeMoModels.DiarizerDefaultId;
        }

        BarRows = Math.Clamp(BarRows, 1, BarGeometry.MaxRows);
        BarBuffer = Math.Clamp(BarBuffer, 3, 8);
        SettingsVersion = 4;
        Save();
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

    public SessionOptions ToSessionOptions()
    {
        var profile = ResolveProfile();
        return new SessionOptions
        {
            PartialModel = Models.AsrModels.FromId(PartialModelId),
            FinalModel = Models.AsrModels.FromId(FinalModelId),
            LiveBackend = profile.Live,
            FinalBackend = profile.Final,
            SourceLanguage = SourceLanguage,
            TargetLanguage = SourceLanguage == "it" ? "en" : "it",
            Translate = Translate,
            SystemAudio = SystemAudio,
            Microphone = Microphone,
            ConversationMode = ConversationMode,
            DiarizerModelId = DiarizerModelId,
            PerformancePreset = Preset,
            SystemDeviceId = SystemDeviceId,
            MicrophoneDeviceId = MicrophoneDeviceId,
            DiagnosticLog = DiagnosticLog,
        };
    }
}

/// <summary>Cosa mostra ogni riga della barra fluttuante.</summary>
public enum BarTextMode
{
    Entrambi,
    Originale,
    Traduzione,
}
