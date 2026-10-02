namespace WisperTranslator.Core.Asr;

public sealed record AgreementUpdate(string CommittedText, string NewlyCommitted, string PendingText);

/// <summary>
/// Politica LocalAgreement-2: confrontando due ipotesi consecutive si committano solo le
/// parole su cui entrambe concordano. È ciò che evita al testo di riscriversi a schermo.
/// </summary>
public sealed class LocalAgreementPolicy
{
    private readonly List<string> _committed = [];
    private string[] _previous = [];

    public string CommittedText => string.Join(' ', _committed);

    public int CommittedWords => _committed.Count;

    public AgreementUpdate Commit(string hypothesis)
    {
        var current = Tokenize(hypothesis);
        var common = LongestCommonPrefix(_previous, current);
        var newly = new List<string>();

        for (var index = _committed.Count; index < common; index++)
        {
            _committed.Add(current[index]);
            newly.Add(current[index]);
        }

        _previous = current;
        var pending = string.Join(' ', current[common..]);
        return new AgreementUpdate(CommittedText, string.Join(' ', newly), pending);
    }

    public void Reset()
    {
        _committed.Clear();
        _previous = [];
    }

    internal static string[] Tokenize(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static int LongestCommonPrefix(string[] left, string[] right)
    {
        var limit = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < limit && SameToken(left[index], right[index]))
        {
            index++;
        }

        return index;
    }

    private static bool SameToken(string left, string right) =>
        string.Equals(Trim(left), Trim(right), StringComparison.OrdinalIgnoreCase);

    private static string Trim(string token) =>
        token.Trim('.', ',', '?', '!', ';', ':', '"', '\'', '«', '»', '(', ')');
}
