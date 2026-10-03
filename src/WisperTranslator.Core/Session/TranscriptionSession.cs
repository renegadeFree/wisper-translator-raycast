using WisperTranslator.Core.Asr;
using WisperTranslator.Core.Audio;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Models;
using WisperTranslator.Core.Translation;
using WisperTranslator.Core.Vad;

namespace WisperTranslator.Core.Session;

/// <summary>
/// Compone l'intera catena (sorgenti audio → VAD → ASR → traduzione) e pubblica le battute
/// pronte per la UI. È il punto unico da cui l'applicazione avvia e ferma tutto.
/// </summary>
public sealed class TranscriptionSession : IAsyncDisposable
{
    private readonly Dictionary<int, Cue> _cues = [];
    private readonly Dictionary<int, int> _translationVersions = [];
    private readonly Dictionary<int, CancellationTokenSource> _pendingTranslations = [];
    private readonly object _cueGate = new();
    private readonly SemaphoreSlim _translationGate = new(2, 2);
    private SessionStore? _history;
    private readonly bool _ownsHistory;
    private long _historySessionId;
    private string? _diagnosticPath;
    private readonly object _diagnosticGate = new();

    private AudioMixer? _mixer;
    private VoiceCapture? _systemCapture;
    private VoiceCapture? _microphoneCapture;
    private IAsrEngine? _partialEngine;
    private IAsrEngine? _finalEngine;
    private NeMoSpeechServer? _realtimeServer;
    private SpeakerTracker? _speakerTracker;
    private bool _ownsPartialEngine;
    private bool _ownsFinalEngine;
    private readonly List<VoiceCapture> _captures = [];
    private readonly List<Lane> _lanes = [];

    /// <summary>
    /// Corsia → battuta. Con più sorgenti gli enunciati di ognuna partono da 1: senza questa
    /// mappa le due corsie si sovrascriverebbero a vicenda.
    /// </summary>
    private readonly Dictionary<(int Lane, int Utterance), int> _cueIds = [];

    private int _nextCueId;
    private TranslationServer? _translationServer;
    private TranslationService? _translationService;
    private CancellationTokenSource? _cancellation;
    private Task? _pipeline;
    private bool _disposed;

    /// <summary>Una corsia: VAD e trascrittore indipendenti su una sola sorgente audio.</summary>
    private sealed record Lane(int Index, int Speaker, SpeechSegmenter Segmenter, RealtimeTranscriber Transcriber)
    {
        /// <summary>Il parlante è noto a priori (il microfono è sempre "Tu").</summary>
        public bool SpeakerIsFixed => Speaker != Speakers.Unknown;
    }

    /// <summary>Attesa prima di tradurre un parziale: evita di tradurre ogni singola parola.</summary>
    private static readonly TimeSpan ProgressiveTranslationDelay = TimeSpan.FromMilliseconds(300);

    public TranscriptionSession(SessionOptions? options = null, SessionStore? history = null)
    {
        Options = options ?? new SessionOptions();
        _history = history;
        _ownsHistory = history is null;
    }

    public SessionOptions Options { get; }

    /// <summary>Battuta creata o aggiornata (parziale o finale).</summary>
    public event Action<Cue>? CueUpdated;

    /// <summary>Messaggi di stato leggibili, per la barra di stato della UI.</summary>
    public event Action<string>? StatusChanged;

    public event Action<Cue>? CueCompleted;

    public bool IsRunning => _pipeline is { IsCompleted: false };

    public string? LastError { get; private set; }

    public long Transcribed { get; private set; }

    /// <summary>Storico locale: viene creato automaticamente al primo avvio della sessione.</summary>
    public SessionStore? History => _history;

    public long HistorySessionId => _historySessionId;

    public async Task StartAsync(IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        void Report(string message)
        {
            status?.Report(message);
            StatusChanged?.Invoke(message);
        }

        Report("Preparo il rilevatore di voce...");
        await ModelStore.EnsureVadModelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var diarizerPath = Options.ConversationMode
            ? await PrepareDiarizerAsync(Report, cancellationToken).ConfigureAwait(false)
            : null;

        Report("Preparo il riconoscitore istantaneo...");
        var partial = await AsrBackendFactory
            .CreateAsync(
                Options.LiveBackend,
                Options.PartialModel,
                Options.SourceLanguage,
                Report,
                diarizerPath,
                diarize: false,
                cancellationToken)
            .ConfigureAwait(false);
        _partialEngine = partial.Engine;
        _ownsPartialEngine = partial.Owned;

        Report("Preparo il riconoscitore accurato...");
        var final = await AsrBackendFactory
            .CreateAsync(
                Options.FinalBackend,
                Options.FinalModel,
                Options.SourceLanguage,
                Report,
                diarizerPath,
                diarize: diarizerPath is not null,
                cancellationToken)
            .ConfigureAwait(false);
        _finalEngine = final.Engine;
        _ownsFinalEngine = final.Owned;

        if (Options.LiveBackend == Hardware.AsrBackend.NeMoSpeech)
        {
            try
            {
                _realtimeServer = await NeMoSpeechHost
                    .EnsureAsync(Report, diarizerPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Report($"Streaming NeMo non disponibile ({exception.Message}): uso i parziali batch.");
                _realtimeServer = null;
            }

            if (_realtimeServer is not null && diarizerPath is not null)
            {
                _speakerTracker = new SpeakerTracker(new NeMoDiarizationClient(_realtimeServer.Port));
            }
        }

        if (Options.ConversationMode && diarizerPath is null)
        {
            Report("Conversazione senza diarizzazione: le battute restano senza nome del parlante.");
        }

        if (Options.Translate)
        {
            Report("Avvio il traduttore locale...");
            await StartTranslationAsync(Report, cancellationToken).ConfigureAwait(false);
        }

        Report("Apro le sorgenti audio...");
        _cueIds.Clear();
        _nextCueId = 0;
        BuildPipeline();

        StartHistory();
        _cancellation = new CancellationTokenSource();
        _pipeline = Task.WhenAll(_lanes.Select(lane => lane.Transcriber.RunAsync(_cancellation.Token)));
        Report("In ascolto");
    }

    /// <summary>
    /// Il diarizzatore serve solo in modalità conversazione. Si scarica da solo una volta, ma
    /// non sulle macchine minime: lì resta spento finché non lo si scarica a mano.
    /// </summary>
    private async Task<string?> PrepareDiarizerAsync(Action<string> report, CancellationToken cancellationToken)
    {
        if (Options.FinalBackend != Hardware.AsrBackend.NeMoSpeech)
        {
            report("La diarizzazione richiede il motore definitivo NeMo: la salto.");
            return null;
        }

        if (NeMoModels.DiarizerPathIfInstalled(Options.DiarizerModelId) is { } installed)
        {
            return installed;
        }

        if (Options.PerformancePreset == Hardware.PerformancePreset.Reattivo)
        {
            report("PC minimo: diarizzazione spenta. Si attiva scaricando il diarizzatore dalle impostazioni.");
            return null;
        }

        try
        {
            var entry = NeMoModels.DiarizerEntry(Options.DiarizerModelId);
            report($"Scarico il diarizzatore {entry.DisplayName} (una volta sola)...");
            return await NeMoModels.EnsureDiarizerAsync(Options.DiarizerModelId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            report($"Diarizzatore non disponibile ({exception.Message}): continuo senza nomi dei parlanti.");
            return null;
        }
    }

    private void StartHistory()
    {
        _history ??= new SessionStore();
        if (_history.RecoveredFromCorruption)
        {
            StatusChanged?.Invoke(
                "Lo storico era danneggiato ed è stato messo da parte: riparto con un archivio nuovo.");
        }

        _history.PurgeExpired();
        _historySessionId = _history.BeginSession(Options.SourceLanguage, Options.TargetLanguage);

        if (Options.DiagnosticLog)
        {
            _diagnosticPath = Options.DiagnosticLogPath
                              ?? Path.Combine(AppPaths.EnsureSubdirectory("logs"), $"sessione-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(_diagnosticPath)!);
        }
    }

    /// <summary>Una riga JSON per aggiornamento: timestamp, tipo, latenza, durata e testo.</summary>
    private void WriteDiagnostic(Cue cue, TimeSpan latency, int textLength)
    {
        if (_diagnosticPath is null)
        {
            return;
        }

        var line = System.Text.Json.JsonSerializer.Serialize(new
        {
            t = DateTime.Now.ToString("HH:mm:ss.fff"),
            utterance = cue.Id,
            final = cue.IsFinal,
            audioStart = Math.Round(cue.AudioStart.TotalSeconds, 2),
            audioDuration = Math.Round(cue.Duration.TotalSeconds, 2),
            latencyMs = (int)latency.TotalMilliseconds,
            chars = textLength,
            speaker = cue.Speaker,
            text = cue.Original,
        });

        lock (_diagnosticGate)
        {
            File.AppendAllText(_diagnosticPath, line + Environment.NewLine);
        }
    }

    private async Task StartTranslationAsync(Action<string> report, CancellationToken cancellationToken)
    {
        try
        {
            var executable = await TranslationServer
                .EnsureExecutableAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _translationServer = new TranslationServer(executable, port: Options.TranslationPort);
            if (!await _translationServer.EnsureStartedAsync(TimeSpan.FromSeconds(30), cancellationToken)
                    .ConfigureAwait(false))
            {
                report("Traduttore non disponibile: continuo solo con la trascrizione");
                _translationServer.Dispose();
                _translationServer = null;
                return;
            }

            _translationService = new TranslationService(
                new LocalHttpEngine(Options.TranslationPort));
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            report($"Traduttore non disponibile ({exception.Message}): continuo solo con la trascrizione");
        }
    }

    private void BuildPipeline()
    {
        var sources = new List<(IPcmSource Source, VoiceCapture? Capture, int Speaker)>();

        if (Options.SystemAudio)
        {
            _systemCapture = new VoiceCapture(SourceKind.System, Options.SystemDeviceId);
            _captures.Add(_systemCapture);
            sources.Add((_systemCapture, _systemCapture, Speakers.Unknown));
        }

        if (Options.Microphone)
        {
            _microphoneCapture = new VoiceCapture(SourceKind.Microphone, Options.MicrophoneDeviceId);
            _captures.Add(_microphoneCapture);
            sources.Add((
                _microphoneCapture,
                _microphoneCapture,
                Options.ConversationMode ? Speakers.You : Speakers.Unknown));
        }

        if (sources.Count == 0)
        {
            throw new InvalidOperationException("Nessuna sorgente audio selezionata.");
        }

        if (Options.ConversationMode)
        {
            // Una corsia per sorgente: il VAD di ognuna chiude le frasi per conto proprio.
            foreach (var (source, _, speaker) in sources)
            {
                AddLane(source, speaker);
            }
        }
        else
        {
            _mixer = new AudioMixer();
            foreach (var (_, capture, _) in sources)
            {
                _mixer.Add(capture!);
            }

            AddLane(_mixer, Speakers.Unknown);
        }

        foreach (var capture in _captures)
        {
            capture.Start();
        }
    }

    private void AddLane(IPcmSource source, int speaker)
    {
        var segmenter = new SpeechSegmenter(new SileroVad(AppPaths.VadModelPath));
        var streaming = _realtimeServer is null ? null : new NeMoRealtimeClient(_realtimeServer.Port);
        var lane = new Lane(
            _lanes.Count,
            speaker,
            segmenter,
            new RealtimeTranscriber(source, segmenter, _partialEngine!, null, _finalEngine!, streaming)
            {
                Language = Options.SourceLanguage,
            });

        lane.Transcriber.Update += update => OnTranscriptUpdate(lane, update);
        if (!lane.SpeakerIsFixed && _speakerTracker is not null)
        {
            lane.Transcriber.AudioBlock += (block, _) => _speakerTracker?.Append(block);
        }

        lane.Transcriber.FinalResolved += (utterance, start, samples, result) =>
            _ = ResolveSpeakersAsync(lane, utterance, start, samples, result);
        _lanes.Add(lane);
    }

    /// <summary>
    /// La frase è già a schermo; qui si risolve solo chi l'ha detta. Se la diarizzazione
    /// tarda o fallisce, il testo resta valido e senza etichetta.
    /// </summary>
    private async Task ResolveSpeakersAsync(
        Lane lane,
        int utterance,
        TimeSpan utteranceStart,
        float[] samples,
        AsrResult result)
    {
        if (!Options.ConversationMode
            || lane.SpeakerIsFixed
            || _speakerTracker is null
            || result.Segments.Count == 0)
        {
            return;
        }

        try
        {
            var cueId = ResolveCueId(lane.Index, utterance);

            var token = _cancellation?.Token ?? CancellationToken.None;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
            IReadOnlyList<AsrSegment>? tagged = null;
            while (DateTime.UtcNow < deadline)
            {
                if (_speakerTracker.TryApply(result.Segments, utteranceStart, out tagged))
                {
                    break;
                }

                await Task.Delay(250, token).ConfigureAwait(false);
            }

            if (tagged is null)
            {
                return;
            }

            var turns = RealtimeTranscriber.BuildTurns(new AsrResult(
                result.Text,
                result.Language,
                result.AudioDuration,
                result.Elapsed,
                tagged));
            if (turns.Count == 0)
            {
                return;
            }

            if (turns.Count == 1)
            {
                UpdateSpeaker(cueId, turns[0].Speaker);
                return;
            }

            for (var index = 0; index < turns.Count; index++)
            {
                var turn = turns[index];
                var turnCueId = index == 0
                    ? ResolveCueId(lane.Index, utterance)
                    : NextCueId();
                PublishCue(
                    turnCueId,
                    turn.Speaker,
                    turn.Text,
                    isFinal: true,
                    turn.Start,
                    turn.Duration,
                    provisional: string.Empty,
                    TimeSpan.Zero);
            }
        }
        catch (OperationCanceledException)
        {
            // sessione fermata
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            StatusChanged?.Invoke($"Diarizzazione non riuscita: {exception.Message}");
        }
    }

    /// <summary>Aggiorna solo l'etichetta del parlante: testo e posizione non si muovono.</summary>
    private void UpdateSpeaker(int cueId, int speaker)
    {
        Cue updated;
        lock (_cueGate)
        {
            if (!_cues.TryGetValue(cueId, out var cue))
            {
                return;
            }

            updated = cue with { Speaker = speaker };
            _cues[cueId] = updated;
        }

        CueUpdated?.Invoke(updated);
        if (_history is not null && _historySessionId > 0)
        {
            try
            {
                _history.SaveCue(_historySessionId, updated);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
            }
        }
    }

    private void OnTranscriptUpdate(Lane lane, TranscriptUpdate update)
    {
        var turns = !lane.SpeakerIsFixed && update.IsFinal ? update.Turns : null;
        if (turns is { Count: > 1 })
        {
            // Nella stessa frase si sono sentite più voci: la battuta in corso diventa il primo
            // turno e gli altri si accodano, senza far saltare l'elenco.
            for (var i = 0; i < turns.Count; i++)
            {
                var turn = turns[i];
                var cueId = i == 0
                    ? ResolveCueId(lane.Index, update.UtteranceId)
                    : NextCueId();
                PublishCue(
                    cueId,
                    turn.Speaker,
                    turn.Text,
                    isFinal: true,
                    turn.Start,
                    turn.Duration,
                    provisional: string.Empty,
                    update.Latency);
            }

            return;
        }

        var speaker = lane.SpeakerIsFixed
            ? lane.Speaker
            : turns is { Count: 1 } single ? single[0].Speaker : Speakers.Unknown;
        PublishCue(
            ResolveCueId(lane.Index, update.UtteranceId),
            speaker,
            update.Text,
            update.IsFinal,
            update.Start,
            update.Duration,
            update.Provisional,
            update.Latency);
    }

    /// <summary>Id stabile per la coppia corsia+enunciato: parziali e finale restano la stessa riga.</summary>
    private int ResolveCueId(int lane, int utterance)
    {
        lock (_cueGate)
        {
            if (_cueIds.TryGetValue((lane, utterance), out var existing))
            {
                return existing;
            }

            var created = NextCueIdLocked();
            _cueIds[(lane, utterance)] = created;
            return created;
        }
    }

    private int NextCueId()
    {
        lock (_cueGate)
        {
            return NextCueIdLocked();
        }
    }

    private int NextCueIdLocked() => ++_nextCueId;

    private void PublishCue(
        int cueId,
        int speaker,
        string text,
        bool isFinal,
        TimeSpan start,
        TimeSpan duration,
        string provisional,
        TimeSpan latency)
    {
        Cue cue;
        lock (_cueGate)
        {
            var translation = _cues.TryGetValue(cueId, out var previous) ? previous.Translation : string.Empty;
            cue = new Cue(
                cueId,
                text,
                translation,
                isFinal,
                start,
                duration,
                previous?.CreatedAt ?? DateTime.Now,
                provisional,
                speaker);
            _cues[cueId] = cue;
            if (isFinal)
            {
                Transcribed++;
            }
        }

        CueUpdated?.Invoke(cue);
        WriteDiagnostic(cue, latency, cue.Original.Length);
        if (isFinal)
        {
            CueCompleted?.Invoke(cue);
        }

        if (_history is not null && _historySessionId > 0)
        {
            try
            {
                _history.SaveCue(_historySessionId, cue);
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
            }
        }

        if (Options.Translate && _translationService is not null && cue.Original.Length > 1)
        {
            ScheduleTranslation(cue, isFinal);
        }
    }

    /// <summary>Accoda la traduzione annullando quella precedente della stessa battuta.</summary>
    private void ScheduleTranslation(Cue cue, bool isFinal)
    {
        CancellationTokenSource cancellation;
        lock (_cueGate)
        {
            if (_pendingTranslations.TryGetValue(cue.Id, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            cancellation = new CancellationTokenSource();
            _pendingTranslations[cue.Id] = cancellation;
        }

        _ = TranslateCueAsync(cue, isFinal, cancellation.Token);
    }

    private async Task TranslateCueAsync(Cue cue, bool isFinal, CancellationToken cancellationToken)
    {
        int version;
        lock (_cueGate)
        {
            version = _translationVersions.TryGetValue(cue.Id, out var current) ? current + 1 : 1;
            _translationVersions[cue.Id] = version;
        }

        try
        {
            if (!isFinal)
            {
                // Debounce: mentre l'utente parla il testo si assesta di continuo.
                await Task.Delay(ProgressiveTranslationDelay, cancellationToken).ConfigureAwait(false);
            }

            await _translationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            TranslationResult result;
            try
            {
                result = await _translationService!
                    .TranslateAsync(cue.Original, Options.SourceLanguage, Options.TargetLanguage, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _translationGate.Release();
            }

            Cue updated;
            lock (_cueGate)
            {
                if (_translationVersions.TryGetValue(cue.Id, out var latest) && latest != version)
                {
                    return;
                }

                if (!_cues.TryGetValue(cue.Id, out var existing))
                {
                    return;
                }

                updated = existing with { Translation = result.Text };
                _cues[cue.Id] = updated;
            }

            CueUpdated?.Invoke(updated);
        }
        catch (OperationCanceledException)
        {
            // superata da un aggiornamento più recente
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            StatusChanged?.Invoke($"Traduzione non riuscita: {exception.Message}");
        }
    }

    public void SetSystemAudio(bool enabled)
    {
        Options.SystemAudio = enabled;
        if (_systemCapture is not null)
        {
            _systemCapture.Enabled = enabled;
        }
    }

    /// <summary>Livelli audio correnti delle sorgenti: la UI li usa come spia di funzionamento.</summary>
    public IReadOnlyList<(string Name, float Level, bool Enabled)> Levels()
    {
        return
        [
            .. _captures
                .Select(capture => (capture.Name, capture.Level, capture.Enabled)),
        ];
    }

    public void SetMicrophone(bool enabled)
    {
        Options.Microphone = enabled;
        if (_microphoneCapture is not null)
        {
            _microphoneCapture.Enabled = enabled;
        }
    }

    public void SetDirection(string sourceLanguage, string targetLanguage)
    {
        Options.SourceLanguage = sourceLanguage;
        Options.TargetLanguage = targetLanguage;
        foreach (var lane in _lanes)
        {
            lane.Transcriber.Language = sourceLanguage;
        }

        if (_translationService is not null)
        {
            _translationService.ClearCache();
        }
    }

    public async Task StopAsync()
    {
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (_pipeline is not null)
        {
            try
            {
                // Tetto di sicurezza: la decodifica nativa non deve poter bloccare la UI.
                await _pipeline.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                LastError ??= "Arresto oltre il tempo massimo: la decodifica in corso è stata abbandonata.";
            }
            catch (Exception)
            {
                // arresto richiesto
            }
        }

        _pipeline = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);

        lock (_cueGate)
        {
            foreach (var pending in _pendingTranslations.Values)
            {
                pending.Cancel();
                pending.Dispose();
            }

            _pendingTranslations.Clear();
        }

        Safe(() =>
        {
            foreach (var lane in _lanes)
            {
                lane.Transcriber.Dispose();
                lane.Segmenter.Dispose();
            }

            _lanes.Clear();
        });

        // Whisper appartiene al pool e resta caldo; Vosk/NeMo sono nostri e si liberano qui.
        Safe(() =>
        {
            if (_ownsPartialEngine)
            {
                _partialEngine?.Dispose();
            }
        });
        Safe(() =>
        {
            if (_ownsFinalEngine)
            {
                _finalEngine?.Dispose();
            }
        });

        // Le sorgenti del mixer appartengono al mixer; in modalità conversazione no.
        Safe(() =>
        {
            foreach (var capture in _captures.Where(capture => _mixer is null || !_mixer.Sources.Contains(capture)))
            {
                capture.Dispose();
            }

            _captures.Clear();
        });
        Safe(() => _mixer?.Dispose());
        Safe(() => _translationService?.Dispose());
        Safe(() => _translationServer?.Dispose());
        Safe(() => _speakerTracker?.Dispose());
        Safe(() => _translationGate.Dispose());
        Safe(() => _cancellation?.Dispose());

        if (_history is not null && _historySessionId > 0)
        {
            Safe(() => _history.EndSession(_historySessionId));
        }

        if (_ownsHistory)
        {
            Safe(() => _history?.Dispose());
        }
    }

    /// <summary>La pulizia non deve mai trasformare uno stop in un errore fatale.</summary>
    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }
    }
}
