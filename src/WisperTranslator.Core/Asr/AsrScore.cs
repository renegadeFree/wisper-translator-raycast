using System.Globalization;
using System.Text;

namespace WisperTranslator.Core.Asr;

/// <summary>Confronto fra trascrizione attesa e ottenuta: serve ai test di accettazione.</summary>
public static class AsrScore
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character))
            {
                builder.Append(' ');
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Accuratezza a livello di parola (1 = identico), basata sulla distanza di Levenshtein.</summary>
    public static double WordAccuracy(string? expected, string? actual)
    {
        var expectedWords = Normalize(expected).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var actualWords = Normalize(actual).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (expectedWords.Length == 0)
        {
            return actualWords.Length == 0 ? 1 : 0;
        }

        var distance = Levenshtein(expectedWords, actualWords);
        return Math.Max(0, 1.0 - (distance / (double)expectedWords.Length));
    }

    private static int Levenshtein(string[] left, string[] right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = string.Equals(left[i - 1], right[j - 1], StringComparison.Ordinal) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
