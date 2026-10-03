using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Motore Marian (opus-mt) in ONNX: encoder una volta, decoder autoregressivo con cache.
/// È il secondo stadio della traduzione: gira in background, quindi può permettersi i millisecondi
/// che il motore rapido non ha.
/// </summary>
internal sealed class MarianOnnxModel : IDisposable
{
    private const string DefaultEncoderOutput = "last_hidden_state";
    private const string LogitsName = "logits";

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly MarianTokenizer _tokenizer;
    private readonly string _encoderIdsName;
    private readonly string _encoderMaskName;
    private readonly string _encoderOutputName;
    private readonly string _inputIdsName;
    private readonly string? _encoderHiddenName;
    private readonly string? _encoderMaskDecoderName;
    private readonly string? _useCacheName;
    private readonly (string Past, string Present)[] _cache;
    private readonly int _layers;
    private readonly int _heads;
    private readonly int _headSize;
    private readonly int _maxTokens;
    private readonly string[] _decoderOutputNames;
    private readonly string[] _cachedOutputNames;
    private bool _disposed;

    private MarianOnnxModel(
        InferenceSession encoder,
        InferenceSession decoder,
        MarianTokenizer tokenizer,
        string encoderIdsName,
        string encoderMaskName,
        string encoderOutputName,
        string inputIdsName,
        string? encoderHiddenName,
        string? encoderMaskDecoderName,
        string? useCacheName,
        (string Past, string Present)[] cache,
        int layers,
        int heads,
        int headSize,
        int maxTokens,
        string[] decoderOutputNames)
    {
        _encoder = encoder;
        _decoder = decoder;
        _tokenizer = tokenizer;
        _encoderIdsName = encoderIdsName;
        _encoderMaskName = encoderMaskName;
        _encoderOutputName = encoderOutputName;
        _inputIdsName = inputIdsName;
        _encoderHiddenName = encoderHiddenName;
        _encoderMaskDecoderName = encoderMaskDecoderName;
        _useCacheName = useCacheName;
        _cache = cache;
        _layers = layers;
        _heads = heads;
        _headSize = headSize;
        _maxTokens = maxTokens;
        _decoderOutputNames = decoderOutputNames;
        _cachedOutputNames =
        [
            LogitsName,
            .. cache.Where(pair => pair.Past.Contains(".decoder.", StringComparison.Ordinal))
                .Select(pair => pair.Present),
        ];
    }

    public static MarianOnnxModel Load(string directory, int threads, int maxTokens = 256)
    {
        var encoderPath = Path.Combine(directory, "onnx", "encoder_model_int8.onnx");
        var decoderPath = Path.Combine(directory, "onnx", "decoder_model_merged_int8.onnx");
        if (!File.Exists(decoderPath))
        {
            decoderPath = Path.Combine(directory, "onnx", "decoder_model_int8.onnx");
        }

        if (!File.Exists(encoderPath) || !File.Exists(decoderPath))
        {
            throw new FileNotFoundException("Modello Marian ONNX incompleto.", directory);
        }

        var options = new SessionOptions();

        var encoder = new InferenceSession(encoderPath, options);
        InferenceSession decoder;
        try
        {
            decoder = new InferenceSession(decoderPath, options);
        }
        catch
        {
            encoder.Dispose();
            throw;
        }

        try
        {
            var tokenizer = MarianTokenizer.Load(directory);
            var encoderIds = FirstName(encoder, "input_ids") ?? encoder.InputMetadata.Keys.First();
            var encoderMask = FirstName(encoder, "attention_mask") ?? encoder.InputMetadata.Keys.Last();
            var encoderOutput = FirstName(encoder, DefaultEncoderOutput) ?? encoder.OutputMetadata.Keys.First();
            var inputIds = FirstName(decoder, "input_ids") ?? decoder.InputMetadata.Keys.First();
            var encoderHidden = FirstName(decoder, "encoder_hidden_states");
            var encoderMaskIn = FirstName(decoder, "encoder_attention_mask");
            var useCache = FirstName(decoder, "use_cache_branch");
            var pastNames = decoder.InputMetadata.Keys
                .Where(name => name.StartsWith("past_key_values", StringComparison.Ordinal))
                .ToArray();
            var cache = pastNames
                .Select(name => (Past: name, Present: name.Replace("past_key_values", "present", StringComparison.Ordinal)))
                .Where(pair => decoder.OutputMetadata.ContainsKey(pair.Present))
                .ToArray();

            var (layers, heads, headSize) = ReadShapes(directory, decoder, cache);
            return new MarianOnnxModel(encoder, decoder, tokenizer, encoderIds, encoderMask, encoderOutput,
                inputIds, encoderHidden, encoderMaskIn, cache.Length > 0 ? useCache : null, cache,
                layers, heads, headSize, maxTokens, [.. decoder.OutputMetadata.Keys]);
        }
        catch
        {
            decoder.Dispose();
            encoder.Dispose();
            throw;
        }
    }

    public string Translate(string text, CancellationToken cancellationToken = default)
    {
        var source = _tokenizer.Encode(text);
        if (source.Count == 0)
        {
            return string.Empty;
        }

        float[] hidden;
        int[] hiddenShape;
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_encoderIdsName,
                Longs([1, source.Count], source.Select(id => (long)id).ToList())),
            NamedOnnxValue.CreateFromTensor(_encoderMaskName,
                Longs([1, source.Count], Enumerable.Repeat(1L, source.Count).ToList())),
        };
        using (var encoded = _encoder.Run(inputs, [_encoderOutputName]))
        {
            var tensor = encoded[0].AsTensor<float>();
            hiddenShape = tensor.Dimensions.ToArray();
            hidden = tensor.ToArray();
        }

        var encoderHidden = new DenseTensor<float>(hidden, hiddenShape);
        var encoderMask = Longs([1, source.Count], Enumerable.Repeat(1L, source.Count).ToList());
        var cache = CreateEmptyCache();
        var current = new List<long> { _tokenizer.StartTokenId };
        var produced = new List<int>();
        var useCache = false;

        for (var step = 0; step < _maxTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var result = RunDecoder(current, encoderHidden, encoderMask, cache, useCache);
            var logits = result.First(value => value.Name == LogitsName).AsTensor<float>();
            var vocabulary = logits.Dimensions[2];
            var offset = (logits.Dimensions[1] - 1) * vocabulary;
            var next = ArgMax(logits, offset, vocabulary);
            if (_cache.Length > 0)
            {
                cache = ReadCache(result, cache);
                useCache = true;
            }

            if (next == _tokenizer.EndTokenId)
            {
                break;
            }

            produced.Add(next);
            current = [next];
        }

        return _tokenizer.Decode(produced);
    }

    private IDisposableReadOnlyCollection<DisposableNamedOnnxValue> RunDecoder(
        List<long> ids,
        DenseTensor<float> encoderHidden,
        DenseTensor<long> encoderMask,
        Dictionary<string, DenseTensor<float>> cache,
        bool useCache)
    {
        var inputs = new List<NamedOnnxValue>(_cache.Length + 5)
        {
            NamedOnnxValue.CreateFromTensor(_inputIdsName, Longs([1, ids.Count], ids)),
        };

        if (_encoderHiddenName is not null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_encoderHiddenName, encoderHidden));
        }

        if (_encoderMaskDecoderName is not null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_encoderMaskDecoderName, encoderMask));
        }

        if (_useCacheName is not null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_useCacheName,
                new DenseTensor<bool>(new[] { useCache }, [1])));
        }

        foreach (var (past, _) in _cache)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(past, cache[past]));
        }

        // Con la cache attiva bastano i logits e le cache del decoder: gli output "present"
        // delle cache incrociate di questo export sono inutilizzabili (forma [0,8,1,64]) e
        // chiedendoli il runtime prova a calcolarli, fallendo.
        return _decoder.Run(inputs, useCache ? _cachedOutputNames : _decoderOutputNames);
    }

    private Dictionary<string, DenseTensor<float>> CreateEmptyCache()
    {
        var cache = new Dictionary<string, DenseTensor<float>>(_cache.Length, StringComparer.Ordinal);
        foreach (var (past, _) in _cache)
        {
            cache[past] = new DenseTensor<float>(new[] { 1, _heads, 0, _headSize });
        }

        return cache;
    }

    private Dictionary<string, DenseTensor<float>> ReadCache(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> result,
        Dictionary<string, DenseTensor<float>> current)
    {
        var updated = new Dictionary<string, DenseTensor<float>>(_cache.Length, StringComparer.Ordinal);
        foreach (var (past, present) in _cache)
        {
            var value = result.FirstOrDefault(item => item.Name == present);
            if (value is null)
            {
                // Le cache incrociate (encoder) restano quelle calcolate al primo passo: sono
                // costanti per tutta la frase e l'export le ricalcola in modo sbagliato.
                updated[past] = current[past];
                continue;
            }

            var tensor = value.AsTensor<float>();
            updated[past] = new DenseTensor<float>(tensor.ToArray(), tensor.Dimensions.ToArray());
        }

        return updated;
    }

    private static int ArgMax(Tensor<float> logits, int offset, int count)
    {
        var best = 0;
        var bestScore = float.NegativeInfinity;
        for (var index = 0; index < count; index++)
        {
            var score = logits.GetValue(offset + index);
            if (score > bestScore)
            {
                bestScore = score;
                best = index;
            }
        }

        return best;
    }

    private static DenseTensor<long> Longs(int[] shape, IReadOnlyList<long> values)
    {
        var tensor = new DenseTensor<long>(shape);
        for (var index = 0; index < values.Count; index++)
        {
            tensor.SetValue(index, values[index]);
        }

        return tensor;
    }

    private static string? FirstName(InferenceSession session, string expected) =>
        session.InputMetadata.Keys.FirstOrDefault(name =>
            name.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Forma delle cache: si legge dal primo input <c>past_key_values</c> se il file la espone in
    /// chiaro, altrimenti si ricava da config.json (6 strati, 8 teste, 64 per testa per opus-mt).
    /// </summary>
    private static (int Layers, int Heads, int HeadSize) ReadShapes(
        string directory,
        InferenceSession decoder,
        (string Past, string Present)[] cache)
    {
        var layers = Math.Max(1, cache.Length / 2);
        var heads = 8;
        var headSize = 64;
        var config = Path.Combine(directory, "config.json");
        if (File.Exists(config))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(config));
                var root = document.RootElement;
                if (root.TryGetProperty("decoder_layers", out var layerValue)) layers = layerValue.GetInt32();
                if (root.TryGetProperty("decoder_attention_heads", out var headValue)) heads = headValue.GetInt32();
                if (root.TryGetProperty("d_model", out var dimension))
                {
                    headSize = headValue.ValueKind == JsonValueKind.Number
                        ? Math.Max(1, dimension.GetInt32() / Math.Max(1, heads))
                        : headSize;
                }
            }
            catch (JsonException)
            {
                // si tengono i valori standard di opus-mt
            }
        }

        return (layers, heads, headSize);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _decoder.Dispose();
        _encoder.Dispose();
    }
}
