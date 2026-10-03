using WisperTranslator.Core.Hardware;
using WisperTranslator.Core.Models;

namespace WisperTranslator.Core.Asr;

/// <summary>
/// Costruisce il motore di una corsia. Se il motore richiesto dal profilo non è
/// disponibile si ripiega su Whisper, dicendolo: la trascrizione non resta mai muta.
/// </summary>
public static class AsrBackendFactory
{
    public static async Task<(IAsrEngine Engine, bool Owned)> CreateAsync(
        AsrBackend backend,
        AsrModelSpec whisperFallback,
        string language,
        Action<string> report,
        string? diarizerPath = null,
        bool diarize = false,
        CancellationToken cancellationToken = default)
    {
        if (backend == AsrBackend.Vosk)
        {
            try
            {
                report($"Preparo Vosk ({VoskModels.ModelId(language)})...");
                var directory = await VoskModels
                    .EnsureAsync(language, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return (new VoskAsrEngine(directory), true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                report($"Vosk non disponibile ({exception.Message}): uso Whisper.");
            }
        }

        if (backend == AsrBackend.NeMoSpeech)
        {
            try
            {
                var server = await NeMoSpeechHost
                    .EnsureAsync(report, diarizerPath, cancellationToken)
                    .ConfigureAwait(false);
                if (server is not null)
                {
                    return (new NeMoSpeechEngine(server, ownsServer: false, diarize: diarize && diarizerPath is not null), true);
                }

                report("NeMo-Speech non disponibile: uso Whisper.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                report($"NeMo-Speech non disponibile ({exception.Message}): uso Whisper.");
            }
        }

        var path = await ModelStore
            .EnsureAsrModelAsync(whisperFallback, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return (AsrEnginePool.Rent(path, whisperFallback.Id), false);
    }
}
