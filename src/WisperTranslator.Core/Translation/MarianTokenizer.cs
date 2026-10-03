using System.Text;
using System.Text.Json;

namespace WisperTranslator.Core.Translation;

/// <summary>
/// Tokenizer Unigram di SentencePiece (Marian / opus-mt) letto da <c>tokenizer.json</c>.
/// La segmentazione è la Viterbi standard; la normalizzazione "precompiled" di questi modelli è
/// vuota (il file esporta <c>precompiled_charsmap: null</c>), quindi non serve il motore nativo.
/// </summary>
internal sealed class MarianTokenizer
{
    private const char Space = '▁';

    private readonly Dictionary<string, int> _pieces;
    private readonly string[] _byId;
    private readonly float[] _scores;
    private readonly int _unkId;
    private readonly int _eosId;
    private readonly int _padId;
    private readonly int _startId;
    private readonly float _unkScore;
    private readonly int _maxPieceLength;

    private MarianTokenizer(
        Dictionary<string, int> pieces,
        string[] byId,
        float[] scores,
        int unkId,
        int eosId,
        int padId,
        int startId,
        float unkScore,
        int maxPieceLength)
    {
        _pieces = pieces;
        _byId = byId;
        _scores = scores;
        _unkId = unkId;
        _eosId = eosId;
        _padId = padId;
        _startId = startId;
        _unkScore = unkScore;
        _maxPieceLength = maxPieceLength;
    }

    public int StartTokenId => _startId;

    public int EndTokenId => _eosId;

    public static MarianTokenizer Load(string directory)
    {
        var path = Path.Combine(directory, "tokenizer.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var model = root.GetProperty("model");
        if (model.GetProperty("type").GetString() != "Unigram")
        {
            throw new InvalidOperationException("Tokenizer non Unigram: atteso un modello Marian.");
        }

        var vocab = model.GetProperty("vocab");
        var count = vocab.GetArrayLength();
        var pieces = new Dictionary<string, int>(count, StringComparer.Ordinal);
        var byId = new string[count];
        var scores = new float[count];
        var maxPieceLength = 1;
        var index = 0;
        foreach (var entry in vocab.EnumerateArray())
        {
            var piece = entry[0].GetString() ?? string.Empty;
            scores[index] = entry[1].GetSingle();
            byId[index] = piece;
            if (piece.Length > 0 && !pieces.ContainsKey(piece))
            {
                pieces[piece] = index;
                if (piece.Length > maxPieceLength)
                {
                    maxPieceLength = piece.Length;
                }
            }

            index++;
        }

        var specials = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("added_tokens", out var added))
        {
            foreach (var token in added.EnumerateArray())
            {
                specials[token.GetProperty("content").GetString() ?? string.Empty] = token.GetProperty("id").GetInt32();
            }
        }

        var generation = LoadIds(directory, "generation_config.json");
        var config = LoadIds(directory, "config.json");
        var unkId = Resolve(specials, "<unk>", _ => pieces.TryGetValue("<unk>", out var id) ? id : 1);
        var eosId = Resolve(specials, "</s>", _ => generation.Eos ?? config.Eos ?? 0);
        var padId = Resolve(specials, "<pad>", _ => generation.Pad ?? config.Pad ?? eosId);
        var startId = generation.Start ?? config.Start ?? padId;
        var minimum = scores.Length > 0 ? scores.Min() : 0f;

        return new MarianTokenizer(pieces, byId, scores, unkId, eosId, padId, startId,
            minimum - 10f, Math.Min(maxPieceLength, 64));
    }

    /// <summary>Testo → id, con il token di fine frase in coda come fa il post-processore.</summary>
    public List<int> Encode(string text)
    {
        var pieces = ToPieces(text);
        var ids = Viterbi(pieces);
        ids.Add(_eosId);
        return ids;
    }

    /// <summary>Id → testo, con la ricomposizione degli spazi di SentencePiece.</summary>
    public string Decode(IReadOnlyList<int> ids)
    {
        var builder = new StringBuilder();
        foreach (var id in ids)
        {
            if (id == _eosId || id == _padId || id < 0 || id >= _byId.Length)
            {
                continue;
            }

            var piece = _byId[id];
            if (piece is "<unk>" or "<pad>" or "</s>")
            {
                continue;
            }

            builder.Append(piece);
        }

        var text = builder.ToString().Replace(Space, ' ');
        while (text.Contains("  ", StringComparison.Ordinal))
        {
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        }

        foreach (var punctuation in ".,;:!?)]}%")
        {
            text = text.Replace($" {punctuation}", punctuation.ToString(), StringComparison.Ordinal);
        }

        text = text.Replace("( ", "(", StringComparison.Ordinal)
            .Replace("[ ", "[", StringComparison.Ordinal);
        return text.Trim();
    }

    /// <summary>Normalizzazione minima: spazi compattati e apostrofi tipografici raddrizzati.</summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var character in text.Normalize(NormalizationForm.FormC))
        {
            var current = character switch
            {
                '’' or '‘' => '\'',
                '“' or '”' => '"',
                '–' or '—' => '-',
                _ => character,
            };

            if (char.IsControl(current))
            {
                space = true;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(current);
        }

        return builder.ToString();
    }

    /// <summary>Spazi → "▁" con prefisso, come fa Metaspace con add_prefix_space.</summary>
    private static string ToPieces(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length + 2);
        builder.Append(Space);
        foreach (var character in normalized)
        {
            builder.Append(character == ' ' ? Space : character);
        }

        return builder.ToString();
    }

    /// <summary>Viterbi: la segmentazione con il punteggio totale più alto.</summary>
    private List<int> Viterbi(string text)
    {
        var result = new List<int>();
        var length = text.Length;
        if (length == 0)
        {
            return result;
        }

        var best = new float[length + 1];
        var bestLength = new int[length + 1];
        var bestId = new int[length + 1];
        for (var index = 1; index <= length; index++)
        {
            best[index] = float.NegativeInfinity;
        }

        for (var index = 0; index < length; index++)
        {
            if (float.IsNegativeInfinity(best[index]))
            {
                continue;
            }

            var limit = Math.Min(_maxPieceLength, length - index);
            for (var size = limit; size >= 1; size--)
            {
                var piece = text.Substring(index, size);
                if (_pieces.TryGetValue(piece, out var id))
                {
                    var score = best[index] + _scores[id];
                    if (score > best[index + size])
                    {
                        best[index + size] = score;
                        bestLength[index + size] = size;
                        bestId[index + size] = id;
                    }
                }
                else if (size == 1)
                {
                    var score = best[index] + _unkScore;
                    if (score > best[index + 1])
                    {
                        best[index + 1] = score;
                        bestLength[index + 1] = 1;
                        bestId[index + 1] = _unkId;
                    }
                }
            }
        }

        var position = length;
        while (position > 0)
        {
            var size = bestLength[position];
            if (size <= 0)
            {
                size = 1;
                bestId[position] = _unkId;
            }

            result.Add(bestId[position]);
            position -= size;
        }

        result.Reverse();
        return result;
    }

    private static int Resolve(Dictionary<string, int> specials, string token, Func<int, int> fallback) =>
        specials.TryGetValue(token, out var id) ? id : fallback(0);

    private static (int? Start, int? Eos, int? Pad) LoadIds(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return (null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return (Read(document.RootElement, "decoder_start_token_id"),
                Read(document.RootElement, "eos_token_id"),
                Read(document.RootElement, "pad_token_id"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static int? Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var id) ? id : null;
}
