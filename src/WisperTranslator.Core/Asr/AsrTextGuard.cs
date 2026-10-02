namespace WisperTranslator.Core.Asr;

/// <summary>
/// Difese contro un difetto noto di Whisper sul parlato continuo: su audio lungo, ridecodificato
/// più volte, il modello entra in ciclo e ripete la stessa frase. Qui il testo viene tagliato alla
/// prima ripetizione, senza toccare il resto.
/// </summary>
public static class AsrTextGuard
{
    public const int MaxGramTokens = 8;
    public const int MinRepeats = 3;

    public static string TrimRepetitions(string? text, int maxGramTokens = MaxGramTokens, int minRepeats = MinRepeats)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < minRepeats * 2)
        {
            return text.Trim();
        }

        for (var gram = 1; gram <= Math.Min(maxGramTokens, tokens.Length / minRepeats); gram++)
        {
            for (var start = 0; start + gram * minRepeats <= tokens.Length; start++)
            {
                var repeats = 1;
                while (start + gram * (repeats + 1) <= tokens.Length
                       && IsSameBlock(tokens, start, start + gram * repeats, gram))
                {
                    repeats++;
                }

                if (repeats < minRepeats)
                {
                    continue;
                }

                // Ripetizioni di una parola corta sono spesso legittime ("no no no").
                if (gram == 1 && tokens[start].Length <= 4)
                {
                    continue;
                }

                return string.Join(' ', tokens[..(start + gram)]).Trim();
            }
        }

        return text.Trim();
    }

    /// <summary>True se il testo contiene un ciclo di ripetizione.</summary>
    public static bool HasRepetition(string? text, int maxGramTokens = MaxGramTokens, int minRepeats = MinRepeats)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return !string.Equals(TrimRepetitions(text, maxGramTokens, minRepeats), text.Trim(), StringComparison.Ordinal);
    }

    private static bool IsSameBlock(string[] tokens, int first, int second, int length)
    {
        for (var index = 0; index < length; index++)
        {
            if (!string.Equals(tokens[first + index], tokens[second + index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
