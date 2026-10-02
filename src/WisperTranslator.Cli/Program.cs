using System.Diagnostics;
using System.Globalization;
using NAudio.Wave;
using WisperTranslator.Core;
using WisperTranslator.Core.Asr;
using WisperTranslator.Core.Ai;
using WisperTranslator.Core.Audio;
using WisperTranslator.Core.Hardware;
using WisperTranslator.Core.History;
using WisperTranslator.Core.Models;
using WisperTranslator.Core.Rendering;
using WisperTranslator.Core.Settings;
using WisperTranslator.Core.Translation;
using WisperTranslator.Core.Vad;

namespace WisperTranslator.Cli;

internal static class Program
{
    private const string ItalianSample =
        "Buongiorno, questa è una prova di trascrizione in tempo reale. "
        + "Il sistema deve riconoscere le frasi italiane e tradurle in inglese il più rapidamente possibile.";

    private const string EnglishSample =
        "Good morning, this is a real time transcription test. "
        + "The system must recognize English sentences and translate them into Italian as fast as possible.";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "devices" => ListDevices(),
                "capture" => await CaptureAsync(args[1..]),
                "vad-selftest" => await VadSelfTestAsync(),
                "tts" => Tts(args[1..]),
                "transcribe" => await TranscribeAsync(args[1..]),
                "bench" => await BenchAsync(args[1..]),
                "live" => await LiveAsync(args[1..]),
                "translate" => await TranslateAsync(args[1..]),
                "mt-server" => await MtServerAsync(args[1..]),
                "play" => Play(args[1..]),
                "models" => await ModelsAsync(args[1..]),
                "hardware" => Hardware(),
                "history" => History(args[1..]),
                "ai" => await AiAsync(args[1..]),
                "help" or "--help" or "-h" => Help(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Errore: {exception.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        PrintHelp();
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Comando sconosciuto: {command}");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp() =>
        Console.WriteLine(
            """
            WisperTranslator — comandi di diagnostica

              devices                      elenca i dispositivi di sistema e microfono
              capture [opzioni]            cattura, applica il VAD e mostra gli enunciati
                  --seconds N              durata della cattura (default 10)
                  --source system|mic|both sorgenti da attivare (default both)
                  --device <id|nome>       dispositivo di uscita per il loopback
                  --mic <id|nome>          dispositivo di ingresso
                  --play                   riproduce un segnale sintetico per provare il loopback
                  --dump                   salva il WAV miscelato in audio-dump/
              vad-selftest                 verifica il modello VAD su silenzio e segnale sintetico
              tts [opzioni]                genera una clip vocale di prova con le voci di Windows
                  --lang it|en             lingua della voce (default it)
                  --voice <nome>           voce SAPI da usare
                  --text "..."             testo da pronunciare (default: frase di prova)
                  --lines N                usa N frasi di prova in fila con una pausa fra loro
                  --pause MS               pausa fra le frasi in millisecondi (default 800)
                  --out <file.wav>         file di uscita (default: test-clips/clip-<lang>.wav)
              transcribe <file.wav>        trascrive un file audio
                  --model base|small|large-v3-turbo   modello (default: small)
                  --lang it|en|auto        lingua (default: auto)
              bench <file.wav>             misura RTF, RAM e accuratezza
                  --model, --lang, --runs N, --expected "testo", --write
              live [opzioni]               pipeline real-time completa (VAD + parziali + finali)
                  --seconds N              durata dell'ascolto (default 20)
                  --model, --lang          come sopra
                  --final-model <id>       modello per le frasi finali (default: uguale a --model)
                  --source system|mic|both sorgenti (default system)
                  --play-clip <file.wav>   riproduce una clip per provare il loopback
                  --play                   riproduce il segnale sintetico
              translate ["testo"] [opzioni]  traduce con il motore locale
                  --from it|en             lingua di partenza (default it)
                  --to en|it               lingua di arrivo (default en)
                  --demo                   usa le frasi di prova integrate
                  --keep-alive             lascia il server acceso alla fine
              mt-server <start|stop|status|download> [pairs...]  controlla il server di traduzione
              play <file.wav>              riproduce una clip sul dispositivo predefinito (per i test)
              models <list|download|verify|delete|import|pairs> [id] [file]
                                           gestione dei modelli (catalogo, hash, spazio)
              hardware                     rileva CPU, GPU, RAM, AVX2 e tier consigliato
              history <list|export|purge> [id] [--format srt|txt|json] [--out file]
                                           storico delle sessioni, retention e export
              ai <providers|models|test|summary|points|map|render|report> [opzioni]
                                           riassunti, punti chiave, mappe e PDF con l'IA configurata
            """);

    private static async Task<int> AiAsync(string[] args)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "providers";
        var settings = AppSettings.Load();
        var ai = settings.Ai;

        switch (action)
        {
            case "providers":
            {
                foreach (var kind in Enum.GetValues<AiProviderKind>())
                {
                    var (endpoint, model) = AiSettings.DefaultsFor(kind);
                    var current = kind == ai.Provider ? " (attivo)" : string.Empty;
                    Console.WriteLine($"  {kind,-18} {endpoint}  [{model}]{current}");
                }

                return 0;
            }

            case "models":
            {
                var models = await OllamaDiscovery.ListModelsAsync(ai.Endpoint);
                foreach (var model in models)
                {
                    Console.WriteLine($"  {model}");
                }

                if (models.Count == 0)
                {
                    Console.WriteLine("  (nessun modello: Ollama è avviato?)");
                }

                return 0;
            }

            case "test":
            {
                using var assistant = new AiAssistant(ai);
                Console.Write($"Provo {assistant.ProviderName} ({ai.Model}) ... ");
                var answer = await assistant.TestAsync();
                Console.WriteLine($"risposta: {answer}");
                return 0;
            }

            case "summary":
            case "points":
            case "map":
            case "render":
            case "report":
            {
                if (args.Length < 2 || !long.TryParse(args[1], out var sessionId))
                {
                    Console.Error.WriteLine($"Uso: ai {action} <idSessione> [--out file]");
                    return 2;
                }

                using var store = new SessionStore();
                var session = store.Sessions(200).FirstOrDefault(item => item.Id == sessionId);
                if (session is null)
                {
                    Console.Error.WriteLine($"Sessione {sessionId} non trovata.");
                    return 1;
                }

                var cues = store.Cues(sessionId);
                var transcript = string.Join(
                    Environment.NewLine,
                    cues.Select(cue => cue.Translation.Trim().Length > 0 ? $"{cue.Original}\n{cue.Translation}" : cue.Original));

                using var client = new AiAssistant(ai);
                var note = store.GetNote(sessionId);

                if (action == "render")
                {
                    // Ridisegna la mappa già salvata, senza richiamare il modello.
                    if (note is null || note.ConceptMapJson.Length == 0)
                    {
                        Console.Error.WriteLine("Nessuna mappa salvata per questa sessione: usa `ai map`.");
                        return 1;
                    }

                    var stored = ConceptMap.Parse(note.ConceptMapJson);
                    var target = GetString(args, "--out", null)
                                 ?? Path.Combine(AppPaths.EnsureSubdirectory("exports"), $"mappa-{sessionId}.png");
                    File.WriteAllBytes(target, ConceptMapRenderer.RenderPng(stored));
                    Console.WriteLine($"{stored.Nodes.Count} nodi, {stored.Edges.Count} relazioni → {target}");
                    return 0;
                }

                if (action == "map")
                {
                    Console.Write($"Genero la mappa con {client.ProviderName} ({ai.Model}) ... ");
                    var map = await client.BuildConceptMapAsync(transcript);
                    if (map.IsEmpty)
                    {
                        Console.WriteLine("il modello non ha restituito una mappa valida.");
                        return 1;
                    }

                    store.SaveNote(sessionId, $"{ai.DisplayName} · {ai.Model}", conceptMapJson: SerializeMap(map));
                    var output = GetString(args, "--out", null)
                                 ?? Path.Combine(AppPaths.EnsureSubdirectory("exports"), $"mappa-{sessionId}.png");
                    File.WriteAllBytes(output, ConceptMapRenderer.RenderPng(map));
                    Console.WriteLine($"{map.Nodes.Count} nodi, {map.Edges.Count} relazioni → {output}");
                    return 0;
                }

                if (action == "report")
                {
                    var output = GetString(args, "--out", null)
                                 ?? Path.Combine(AppPaths.EnsureSubdirectory("exports"), $"sessione-{sessionId}.pdf");
                    var map = note is { ConceptMapJson.Length: > 0 } ? ConceptMap.Parse(note.ConceptMapJson) : null;
                    PdfReportBuilder.Build(output, session, cues, note, map);
                    Console.WriteLine($"PDF creato: {output}");
                    return 0;
                }

                var task = action == "points" ? AiTask.KeyPoints : AiTask.ShortSummary;
                Console.Write($"{action} con {client.ProviderName} ({ai.Model}) ... ");
                var text = await client.RunAsync(task, transcript);
                Console.WriteLine();
                Console.WriteLine(text);
                store.SaveNote(
                    sessionId,
                    $"{ai.DisplayName} · {ai.Model}",
                    summary: action == "summary" ? text : null,
                    keyPoints: action == "points" ? text : null);
                return 0;
            }

            default:
                Console.Error.WriteLine("Uso: ai <providers|models|test|summary|points|map|render|report>");
                return 2;
        }
    }

    private static int History(string[] args)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        using var store = new SessionStore();

        switch (action)
        {
            case "list":
            {
                var sessions = store.Sessions(30);
                Console.WriteLine($"Database: {store.DatabasePath} ({store.TotalSizeBytes() / (1024.0 * 1024):F1} MB, "
                                  + $"retention {store.RetentionDays} giorni)");
                foreach (var session in sessions)
                {
                    var state = session.EndedAt is null ? "in corso" : "chiusa";
                    Console.WriteLine(
                        $"  #{session.Id,-4} {session.StartedAt:dd/MM/yyyy HH:mm}  {session.SourceLanguage}->{session.TargetLanguage}  "
                        + $"{session.CueCount,4} battute  {state}");
                }

                if (sessions.Count == 0)
                {
                    Console.WriteLine("  (nessuna sessione)");
                }

                return 0;
            }

            case "export":
            {
                if (args.Length < 2 || !long.TryParse(args[1], out var id))
                {
                    Console.Error.WriteLine("Uso: history export <id> [--format srt|txt|json] [--out file]");
                    return 2;
                }

                var format = (GetString(args, "--format", "srt") ?? "srt").ToLowerInvariant();
                var session = store.Sessions(200).FirstOrDefault(item => item.Id == id);
                if (session is null)
                {
                    Console.Error.WriteLine($"Sessione {id} non trovata.");
                    return 1;
                }

                var cues = store.Cues(id);
                var extension = format switch { "json" => "json", "txt" => "txt", _ => "srt" };
                var output = GetString(args, "--out", null)
                             ?? Path.Combine(AppPaths.EnsureSubdirectory("exports"), $"session-{id}.{extension}");

                var content = format switch
                {
                    "json" => HistoryExporter.ToJson(session, cues),
                    "txt" => HistoryExporter.ToText(cues),
                    _ => HistoryExporter.ToSrt(cues),
                };

                File.WriteAllText(output, content, new System.Text.UTF8Encoding(false));
                Console.WriteLine($"Esportate {cues.Count} battute in {output}");
                return 0;
            }

            case "purge":
            {
                var removed = store.PurgeExpired();
                Console.WriteLine(removed > 0
                    ? $"Rimosse {removed} sessioni più vecchie di {store.RetentionDays} giorni."
                    : "Nessuna sessione scaduta.");
                return 0;
            }

            default:
                Console.Error.WriteLine("Uso: history <list|export|purge>");
                return 2;
        }
    }

    private static int Hardware()
    {
        var profile = HardwareDetector.Detect();
        Console.WriteLine(profile.Summary);
        var (partial, final) = profile.RecommendedModels;
        Console.WriteLine($"Modelli consigliati: parziali {partial}, finali {final}");
        return 0;
    }

    private static async Task<int> ModelsAsync(string[] args)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case "list":
            {
                Console.WriteLine($"Cartella: {AppPaths.ModelsDirectory}");
                foreach (var entry in ModelCatalog.All)
                {
                    var installed = ModelStore.IsInstalled(entry);
                    var size = ModelStore.InstalledSize(entry);
                    Console.WriteLine(
                        $"  [{(installed ? "x" : " ")}] {entry.Id,-28} tier {entry.Tier,-5} "
                        + $"{InstalledModel.FormatSize(size),10} {entry.License,-12} {entry.About}");
                }

                return 0;
            }

            case "download":
            {
                foreach (var entry in ResolveEntries(args[1..]))
                {
                    if (entry.ManagedByServer)
                    {
                        Console.WriteLine($"{entry.Id}: gestito dal server di traduzione, usa `models pairs`");
                        continue;
                    }

                    Console.Write($"Scarico {entry.Id} ... ");
                    var progress = new Progress<long>(bytes =>
                        Console.Write(entry.ExpectedSizeBytes > 0
                            ? $"\r  {entry.Id} {bytes / (1024.0 * 1024):F1} / {entry.ExpectedSizeBytes / (1024.0 * 1024):F0} MB"
                            : $"\r  {entry.Id} {bytes / (1024.0 * 1024):F1} MB"));
                    await ModelStore.EnsureAsync(entry, progress);
                    Console.WriteLine($"\r  {entry.Id}: pronto ({ModelStore.InstalledSize(entry) / (1024.0 * 1024):F1} MB)");
                }

                return 0;
            }

            case "verify":
            {
                var failed = 0;
                foreach (var entry in ResolveEntries(args[1..]))
                {
                    var (ok, message) = await ModelStore.VerifyAsync(entry);
                    Console.WriteLine($"  [{(ok ? "OK" : "!!")}] {entry.Id}: {message}");
                    if (!ok)
                    {
                        failed++;
                    }
                }

                return failed == 0 ? 0 : 1;
            }

            case "delete":
            {
                foreach (var entry in ResolveEntries(args[1..], requireId: true))
                {
                    ModelStore.Delete(entry);
                    Console.WriteLine($"{entry.Id}: eliminato");
                }

                return 0;
            }

            case "import":
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Uso: models import <id> <file>");
                    return 2;
                }

                var entry = ModelCatalog.ById(args[1]);
                var destination = await ModelStore.ImportAsync(args[2], entry);
                Console.WriteLine($"Importato in {destination}");
                var (ok, message) = await ModelStore.VerifyAsync(entry);
                Console.WriteLine($"[{(ok ? "OK" : "!!")}] verifica: {message}");
                return ok ? 0 : 1;
            }

            case "pairs":
            {
                var executable = await TranslationServer.EnsureExecutableAsync();
                using var server = new TranslationServer(executable);
                foreach (var pair in ModelStore.TranslationPairs)
                {
                    Console.Write($"Scarico i modelli {pair} ... ");
                    var ok = await server.DownloadModelsAsync([pair]);
                    Console.WriteLine(ok ? "fatto" : "fallito");
                }

                Console.WriteLine($"Spazio usato: {ModelStore.InstalledSize(ModelCatalog.TranslationModels) / (1024.0 * 1024):F0} MB");
                return 0;
            }

            default:
                Console.Error.WriteLine("Uso: models <list|download|verify|delete|import|pairs>");
                return 2;
        }
    }

    private static IEnumerable<ModelCatalogEntry> ResolveEntries(string[] args, bool requireId = false)
    {
        if (args.Length == 0 || args[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (requireId)
            {
                throw new ArgumentException("Indica l'id del modello (usa `models list`).");
            }

            return ModelCatalog.All;
        }

        return [ModelCatalog.ById(args[0])];
    }

    private static string SerializeMap(ConceptMap map) =>
        System.Text.Json.JsonSerializer.Serialize(
            map,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

    private static int Play(string[] args)
    {
        var file = RequireFile(args);
        var extraSeconds = GetInt(args, "--seconds", 0);

        using var reader = new AudioFileReader(file);
        using var device = new WaveOutEvent();
        device.Init(reader);
        device.Play();
        Console.WriteLine($"Riproduco {file} ({reader.TotalTime.TotalSeconds:F1} s)...");

        while (device.PlaybackState == PlaybackState.Playing)
        {
            Thread.Sleep(100);
        }

        if (extraSeconds > 0)
        {
            Thread.Sleep(extraSeconds * 1000);
        }

        Console.WriteLine("Riproduzione conclusa.");
        return 0;
    }

    private static int ListDevices()
    {
        foreach (var kind in new[] { SourceKind.System, SourceKind.Microphone })
        {
            Console.WriteLine(kind == SourceKind.System
                ? "Dispositivi di uscita (sorgente audio di sistema):"
                : "Dispositivi di ingresso (microfono):");

            var devices = AudioDevices.List(kind);
            if (devices.Count == 0)
            {
                Console.WriteLine("  (nessuno)");
            }

            foreach (var device in devices)
            {
                Console.WriteLine($"  {(device.IsDefault ? "*" : " ")} {device.Name}");
                Console.WriteLine($"      {device.Id}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static int Tts(string[] args)
    {
        var language = (GetString(args, "--lang", "it") ?? "it").ToLowerInvariant();
        var voice = GetString(args, "--voice", null) ?? SpeechClips.DefaultVoice(language);
        var lines = GetInt(args, "--lines", 0);
        var pauseMs = GetInt(args, "--pause", 800);
        var text = GetString(args, "--text", null)
                   ?? (language.StartsWith("en", StringComparison.Ordinal) ? EnglishSample : ItalianSample);
        var output = GetString(args, "--out", null)
                     ?? Path.Combine(AppPaths.EnsureSubdirectory("test-clips"), $"clip-{language}.wav");

        if (lines > 0)
        {
            var pool = language.StartsWith("en", StringComparison.Ordinal)
                ? TranslationSamples.English
                : TranslationSamples.Italian;
            var sentences = pool.Take(Math.Min(lines, pool.Count)).ToList();
            text = string.Join(' ', sentences);
            SpeechClips.WriteSentences(voice, sentences, output, pauseMs, language);
        }
        else
        {
            SpeechClips.Write(voice, text, output);
        }

        File.WriteAllText(Path.ChangeExtension(output, ".txt"), text);

        var samples = WavFile.ReadMono16k(output);
        Console.WriteLine($"Clip creata: {output}");
        Console.WriteLine($"Voce       : {voice}");
        Console.WriteLine($"Durata     : {samples.Length / 16000.0:F1} s");
        Console.WriteLine($"Testo      : {text}");
        return 0;
    }

    private static async Task<int> TranscribeAsync(string[] args)
    {
        var file = RequireFile(args);
        var spec = AsrModels.FromId(GetString(args, "--model", "small")!);
        var language = NormalizeLanguage(GetString(args, "--lang", "auto"));

        var modelPath = await EnsureAsrModelAsync(spec);
        var samples = WavFile.ReadMono16k(file);
        Console.WriteLine($"File    : {file} ({samples.Length / 16000.0:F1} s)");
        Console.WriteLine($"Modello : {spec.Id}");

        using var engine = new WhisperAsrEngine(modelPath, spec.Id);
        var result = await engine.TranscribeAsync(samples, 16000, language);

        Console.WriteLine($"Tempo   : {result.Elapsed.TotalSeconds:F2} s (RTF {result.RealTimeFactor:F3})");
        Console.WriteLine($"Testo   : {result.Text}");
        foreach (var segment in result.Segments)
        {
            var end = segment.Start.TotalSeconds + segment.Duration.TotalSeconds;
            Console.WriteLine($"   [{segment.Start.TotalSeconds,6:F2} -> {end,6:F2}] {segment.Text}");
        }

        return 0;
    }

    private static async Task<int> BenchAsync(string[] args)
    {
        var file = RequireFile(args);
        var spec = AsrModels.FromId(GetString(args, "--model", "small")!);
        var language = NormalizeLanguage(GetString(args, "--lang", "auto"));
        var runs = Math.Max(1, GetInt(args, "--runs", 2));
        var expected = GetString(args, "--expected", null) ?? ReadSiblingText(file);
        var write = Has(args, "--write");

        var modelPath = await EnsureAsrModelAsync(spec);
        var samples = WavFile.ReadMono16k(file);
        var audioSeconds = samples.Length / 16000.0;

        Console.WriteLine($"Macchina : {Environment.MachineName} ({Environment.ProcessorCount} core logici)");
        Console.WriteLine($"File     : {file} ({audioSeconds:F1} s)");
        Console.WriteLine($"Modello  : {spec.Id} ({spec.Note})");

        using var engine = new WhisperAsrEngine(modelPath, spec.Id);
        var times = new List<double>();
        var text = string.Empty;
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var startBefore = DateTime.UtcNow;

        for (var run = 1; run <= runs; run++)
        {
            var result = await engine.TranscribeAsync(samples, 16000, language);
            times.Add(result.Elapsed.TotalSeconds);
            text = result.Text;
            Console.WriteLine($"  run {run}: {result.Elapsed.TotalSeconds,6:F2} s   RTF {result.RealTimeFactor:F3}");
        }

        var ordered = times.OrderBy(value => value).ToList();
        var median = ordered[ordered.Count / 2];
        var realTimeFactor = median / audioSeconds;
        var peakMb = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024);
        var cpuSeconds = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
        var wallSeconds = (DateTime.UtcNow - startBefore).TotalSeconds;
        double? accuracy = expected is null ? null : AsrScore.WordAccuracy(expected, text);

        Console.WriteLine($"Mediana  : {median:F2} s   RTF {realTimeFactor:F3}");
        Console.WriteLine($"CPU      : {cpuSeconds:F2} core-secondi in {wallSeconds:F2} s "
                          + $"→ {cpuSeconds / Math.Max(0.01, wallSeconds):F1} core occupati in media "
                          + $"({cpuSeconds / Math.Max(0.01, audioSeconds):F2} core-secondi per secondo di audio)");
        Console.WriteLine($"RAM picco: {peakMb} MB");
        Console.WriteLine($"Testo    : {text}");
        if (accuracy is not null)
        {
            Console.WriteLine($"Atteso   : {expected}");
            Console.WriteLine($"Accuratezza: {accuracy.Value * 100:F1}%");
        }

        if (write)
        {
            var row = new BenchmarkRow(
                $"{Environment.MachineName} ({Environment.ProcessorCount} core)",
                spec.Id,
                language ?? "auto",
                audioSeconds,
                median,
                realTimeFactor,
                peakMb,
                accuracy);

            BenchmarkWriter.Append(Environment.CurrentDirectory, row);
            Console.WriteLine("Riga aggiunta a docs/BENCHMARKS.md");
        }

        return 0;
    }

    private static string RequireFile(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Indica il file audio come primo argomento.");
        }

        if (!File.Exists(args[0]))
        {
            throw new FileNotFoundException($"File audio non trovato: {args[0]}", args[0]);
        }

        return args[0];
    }

    private static string? ReadSiblingText(string file)
    {
        var path = Path.ChangeExtension(file, ".txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static string? NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : language.ToLowerInvariant();

    private static async Task<string> EnsureAsrModelAsync(AsrModelSpec spec)
    {
        var path = ModelStore.AsrModelPath(spec);
        if (File.Exists(path) && new FileInfo(path).Length > 1024)
        {
            return path;
        }

        Console.WriteLine($"Download del modello {spec.Id} ...");
        var progress = new Progress<long>(bytes =>
            Console.Write($"\r  {bytes / (1024.0 * 1024.0):F1} MB"));
        var downloaded = await ModelStore.EnsureAsrModelAsync(spec, progress);
        Console.WriteLine();
        return downloaded;
    }

    private static async Task<int> LiveAsync(string[] args)
    {
        var seconds = GetInt(args, "--seconds", 20);
        var spec = AsrModels.FromId(GetString(args, "--model", "small")!);
        var language = NormalizeLanguage(GetString(args, "--lang", "auto"));
        var source = GetString(args, "--source", "system");
        var clip = GetString(args, "--play-clip", null);
        var synthetic = Has(args, "--play");

        var system = source is "both" or "system";
        var microphone = source is "both" or "mic" or "microphone";

        await EnsureVadModelAsync();
        var modelPath = await EnsureAsrModelAsync(spec);
        var finalSpec = GetString(args, "--final-model", null) is { } finalId
            ? AsrModels.FromId(finalId)
            : spec;
        var finalModelPath = ReferenceEquals(finalSpec, spec)
            ? modelPath
            : await EnsureAsrModelAsync(finalSpec);

        using var vad = new SileroVad(AppPaths.VadModelPath);
        using var segmenter = new SpeechSegmenter(vad);
        using var mixer = new AudioMixer();
        using var engine = new WhisperAsrEngine(modelPath, spec.Id);
        using var finalEngine = ReferenceEquals(finalSpec, spec)
            ? null
            : new WhisperAsrEngine(finalModelPath, finalSpec.Id);
        using var transcriber = new RealtimeTranscriber(mixer, segmenter, engine, null, finalEngine)
        {
            Language = language,
        };

        var captures = new List<VoiceCapture>();
        if (system)
        {
            captures.Add(new VoiceCapture(SourceKind.System, GetString(args, "--device", null)));
        }

        if (microphone)
        {
            captures.Add(new VoiceCapture(SourceKind.Microphone, GetString(args, "--mic", null)));
        }

        foreach (var capture in captures)
        {
            mixer.Add(capture);
            Console.WriteLine($"{(capture.Kind == SourceKind.System ? "Sistema " : "Microfono")}: {capture.Name}");
        }

        if (captures.Count == 0)
        {
            Console.Error.WriteLine("Nessuna sorgente selezionata.");
            return 1;
        }

        var partialLatencies = new List<double>();
        var finalLatencies = new List<double>();
        var committed = new List<string>();

        transcriber.Update += update =>
        {
            if (update.IsFinal)
            {
                finalLatencies.Add(update.Latency.TotalSeconds);
                committed.Add(update.Text);
                Console.WriteLine(
                    $"FINALE   [{update.Start.TotalSeconds,6:F2}s] latenza {update.Latency.TotalSeconds,5:F2}s  {update.Text}");
            }
            else
            {
                partialLatencies.Add(update.Latency.TotalSeconds);
                Console.WriteLine(
                    $"parziale [{update.Start.TotalSeconds,6:F2}s] latenza {update.Latency.TotalSeconds,5:F2}s  {update.Text}");
            }
        };

        transcriber.Hypothesis += hypothesis =>
            Console.WriteLine($"  (in corso) {hypothesis}");

        foreach (var capture in captures)
        {
            capture.Start();
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        Console.WriteLine($"Modello {spec.Id}, lingua {(language ?? "auto")}, ascolto per {seconds} s ...");

        Playback? playback = null;
        if (clip is not null || synthetic)
        {
            await Task.Delay(300);
            playback = Playback.Start(clip, synthetic);
            Console.WriteLine(clip is null
                ? "Riproduzione del segnale sintetico."
                : $"Riproduzione di {clip}.");
        }

        await transcriber.RunAsync(cancellation.Token);
        playback?.Dispose();

        Console.WriteLine();
        Console.WriteLine($"Enunciati finali : {transcriber.FinalSegments}");
        Console.WriteLine($"Parziali committati: {partialLatencies.Count}");
        if (partialLatencies.Count > 0)
        {
            Console.WriteLine($"Latenza parziale  : mediana {Median(partialLatencies):F2} s, peggiore {partialLatencies.Max():F2} s");
        }

        if (finalLatencies.Count > 0)
        {
            Console.WriteLine($"Latenza finale    : mediana {Median(finalLatencies):F2} s, peggiore {finalLatencies.Max():F2} s");
        }

        var text = string.Join(' ', committed);
        Console.WriteLine($"Testo committato  : {text}");

        if (clip is not null)
        {
            var expected = ReadSiblingText(clip);
            if (expected is not null)
            {
                Console.WriteLine($"Accuratezza       : {AsrScore.WordAccuracy(expected, text) * 100:F1}%");
            }
        }

        return 0;
    }

    private static async Task<int> TranslateAsync(string[] args)
    {
        var from = (GetString(args, "--from", "it") ?? "it").ToLowerInvariant();
        var to = (GetString(args, "--to", "en") ?? "en").ToLowerInvariant();
        var keepAlive = Has(args, "--keep-alive");
        var bench = Has(args, "--bench");
        var text = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : null;

        if (!await StartTranslationServerAsync())
        {
            Console.Error.WriteLine(
                "Il server di traduzione non risponde. Verifica i modelli con: mt-server download it-en en-it");
            return 1;
        }

        using var engine = new LocalHttpEngine();
        using var service = new TranslationService(engine);

        if (bench)
        {
            await TranslateBenchAsync(service, LanguagePair.ItToEn);
            await TranslateBenchAsync(service, LanguagePair.EnToIt);
            if (!keepAlive)
            {
                StopTranslationServer();
            }

            return 0;
        }

        var sentences = text is null ? TranslationSamples.Italian.Take(5) : [text];
        var latencies = new List<double>();

        foreach (var sentence in sentences)
        {
            var result = await service.TranslateAsync(sentence, from, to);
            latencies.Add(result.Elapsed.TotalMilliseconds);
            Console.WriteLine($"{from}->{to}  {result.Elapsed.TotalMilliseconds,6:F0} ms  {result.Text}");
        }

        if (latencies.Count > 1)
        {
            Console.WriteLine($"\nMediana: {Median(latencies):F0} ms su {latencies.Count} frasi");
        }

        if (!keepAlive)
        {
            StopTranslationServer();
        }

        return 0;
    }

    private enum LanguagePair
    {
        ItToEn,
        EnToIt,
    }

    private static async Task TranslateBenchAsync(TranslationService service, LanguagePair pair)
    {
        var (from, to, sentences) = pair == LanguagePair.ItToEn
            ? ("it", "en", TranslationSamples.Italian)
            : ("en", "it", TranslationSamples.English);

        var latencies = new List<double>();
        foreach (var sentence in sentences)
        {
            var result = await service.TranslateAsync(sentence, from, to);
            latencies.Add(result.Elapsed.TotalMilliseconds);
        }

        var ordered = latencies.OrderBy(value => value).ToList();
        var p95 = ordered[Math.Max(0, (int)Math.Floor(ordered.Count * 0.95) - 1)];
        Console.WriteLine(
            $"{from}->{to}: mediana {Median(ordered):F0} ms, p95 {p95:F0} ms, peggiore {ordered[^1]:F0} ms su {ordered.Count} frasi");
    }

    private static async Task<int> MtServerAsync(string[] args)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

        switch (action)
        {
            case "start":
            {
                var ok = await StartTranslationServerAsync();
                Console.WriteLine(ok ? "Server pronto su http://127.0.0.1:8989" : "Server non avviato.");
                return ok ? 0 : 1;
            }

            case "stop":
            {
                StopTranslationServer();
                Console.WriteLine("Server fermato.");
                return 0;
            }

            case "status":
            {
                using var server = new TranslationServer();
                var ready = await server.IsReadyAsync();
                Console.WriteLine(ready ? "Server attivo." : "Server non attivo.");
                foreach (var line in server.RecentLog.TakeLast(5))
                {
                    Console.WriteLine($"  {line}");
                }

                return ready ? 0 : 1;
            }

            case "download":
            {
                var pairs = args.Length > 1 ? args[1..] : ["it-en", "en-it"];
                var executable = await TranslationServer.EnsureExecutableAsync(
                    new Progress<double>(value => Console.Write($"\r  binario {value * 100,5:F1}%")));
                Console.WriteLine();
                using var server = new TranslationServer(executable);
                Console.Write($"Scarico i modelli: {string.Join(", ", pairs)} ... ");
                var ok = await server.DownloadModelsAsync(pairs);
                Console.WriteLine(ok ? "fatto" : "fallito");
                return ok ? 0 : 1;
            }

            default:
                Console.Error.WriteLine("Uso: mt-server <start|stop|status|download>");
                return 2;
        }
    }

    private static async Task<bool> StartTranslationServerAsync()
    {
        var executable = await TranslationServer.EnsureExecutableAsync(
            new Progress<double>(value => Console.Write($"\r  download server {value * 100,5:F1}%")));
        Console.WriteLine();

        var server = new TranslationServer(executable);
        var ready = await server.EnsureStartedAsync(TimeSpan.FromSeconds(30));
        if (ready)
        {
            ActiveServer = server;
        }
        else
        {
            server.Dispose();
        }

        return ready;
    }

    private static void StopTranslationServer()
    {
        ActiveServer?.Dispose();
        ActiveServer = null;
    }

    private static TranslationServer? ActiveServer { get; set; }

    private static double Median(List<double> values)
    {
        var ordered = values.OrderBy(value => value).ToList();
        return ordered[ordered.Count / 2];
    }

    private static async Task<int> CaptureAsync(string[] args)
    {
        var seconds = GetInt(args, "--seconds", 10);
        var source = GetString(args, "--source", "both");
        var system = source is "both" or "system";
        var microphone = source is "both" or "mic" or "microphone";
        var play = Has(args, "--play");
        var dump = Has(args, "--dump");

        await EnsureVadModelAsync();

        using var vad = new SileroVad(AppPaths.VadModelPath);
        using var segmenter = new SpeechSegmenter(vad);
        using var mixer = new AudioMixer();

        var captures = new List<VoiceCapture>();
        if (system)
        {
            captures.Add(new VoiceCapture(SourceKind.System, GetString(args, "--device", null)));
        }

        if (microphone)
        {
            captures.Add(new VoiceCapture(SourceKind.Microphone, GetString(args, "--mic", null)));
        }

        foreach (var capture in captures)
        {
            mixer.Add(capture);
            Console.WriteLine($"{(capture.Kind == SourceKind.System ? "Sistema " : "Microfono")}: {capture.Name} [{capture.CapturedFormat}]");
        }

        if (captures.Count == 0)
        {
            Console.Error.WriteLine("Nessuna sorgente selezionata.");
            return 1;
        }

        WaveFileWriter? writer = null;
        if (dump)
        {
            var path = Path.Combine(AppPaths.EnsureSubdirectory("audio-dump"), $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
            writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(VoiceCapture.SampleRate, 1));
            Console.WriteLine($"Dump audio: {path}");
        }

        WaveOutEvent? playback = null;
        if (play)
        {
            playback = new WaveOutEvent();
            playback.Init(new SyntheticVoiceProvider().ToWaveProvider());
            playback.Play();
            Console.WriteLine("Riproduzione del segnale sintetico sul dispositivo predefinito...");
        }

        foreach (var capture in captures)
        {
            capture.Start();
        }

        Console.WriteLine($"Cattura di {seconds} s in corso...");
        var block = new float[512];
        var watch = Stopwatch.StartNew();
        var samples = 0L;
        var peak = 0f;
        var segments = new List<SpeechSegment>();

        while (watch.Elapsed.TotalSeconds < seconds)
        {
            var read = mixer.Read(block);
            if (read <= 0)
            {
                await Task.Delay(10);
                continue;
            }

            for (var i = 0; i < read; i++)
            {
                peak = Math.Max(peak, Math.Abs(block[i]));
            }

            writer?.WriteSamples(block, 0, read);
            samples += read;
            segments.AddRange(segmenter.Feed(block.AsSpan(0, read)));
            await Task.Delay(5);
        }

        foreach (var capture in captures)
        {
            capture.Stop();
        }

        playback?.Stop();
        playback?.Dispose();
        writer?.Dispose();

        Console.WriteLine();
        Console.WriteLine($"Campioni catturati : {samples} ({samples / (double)VoiceCapture.SampleRate:F1} s)");
        Console.WriteLine($"Picco              : {peak:F4} {(peak < 0.001f ? "<- ATTENZIONE: nessun segnale, dispositivo muto o nessun audio in riproduzione" : string.Empty)}");
        Console.WriteLine($"Enunciati rilevati : {segments.Count}");
        foreach (var segment in segments)
        {
            Console.WriteLine($"  {segment.Start.TotalSeconds,6:F2} s  durata {segment.Seconds,5:F2} s  picco {segment.Peak:F3}");
        }

        return 0;
    }

    private static async Task<int> VadSelfTestAsync()
    {
        await EnsureVadModelAsync();

        using var vad = new SileroVad(AppPaths.VadModelPath);
        var frame = new float[vad.FrameSamples];

        var silenceProbabilities = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            AudioSignals.FillSilence(frame, i);
            silenceProbabilities.Add(vad.SpeechProbability(frame));
        }

        var voiceProbabilities = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            AudioSignals.FillVoice(frame, i);
            voiceProbabilities.Add(vad.SpeechProbability(frame));
        }

        vad.Reset();
        var afterReset = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            AudioSignals.FillSilence(frame, i);
            afterReset.Add(vad.SpeechProbability(frame));
        }

        var silenceMax = silenceProbabilities.Max();
        var voiceMax = voiceProbabilities.Max();
        var resetMax = afterReset.Max();

        Console.WriteLine($"Silenzio  : probabilità max {silenceMax:F3}");
        Console.WriteLine($"Segnale   : probabilità max {voiceMax:F3} (informativo)");
        Console.WriteLine($"Post-reset: probabilità max {resetMax:F3}");

        var silenceOk = silenceMax < 0.2;
        var resetOk = resetMax < 0.2;
        Console.WriteLine(silenceOk && resetOk
            ? "ESITO: PASS (il modello ONNX risponde correttamente a silenzio e reset)"
            : "ESITO: FAIL (il modello non riconosce il silenzio: input/state non allineati)");

        return silenceOk && resetOk ? 0 : 1;
    }

    private static async Task EnsureVadModelAsync()
    {
        if (File.Exists(AppPaths.VadModelPath))
        {
            return;
        }

        Console.WriteLine($"Download del modello VAD in {AppPaths.VadModelPath} ...");
        var progress = new Progress<long>(bytes => Console.Write($"\r  {bytes / (1024.0 * 1024):F1} MB"));
        await ModelStore.EnsureVadModelAsync(progress);
        Console.WriteLine();
    }

    private static bool Has(string[] args, string name) =>
        args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static string? GetString(string[] args, string name, string? fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return fallback;
    }

    private static int GetInt(string[] args, string name, int fallback) =>
        int.TryParse(GetString(args, name, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
