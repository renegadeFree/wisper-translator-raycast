using System.Text;

namespace WisperTranslator.Core.Asr;

/// <summary>
/// Ricompone i delta del WebSocket in una frase leggibile. Il server può mandare sia
/// incrementi ("nel", "20") sia aggiornamenti cumulativi ("nel 20"): entrambi devono
/// produrre una riga che cresce, non una parola che sostituisce la precedente.
/// </summary>
public sealed class StreamingTextAccumulator
{
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public void Reset() => _text.Clear();

    /// <summary>Accoda un delta o sostituisce l'intero testo quando l'evento è completato.</summary>
    public string Append(string? delta, bool completed)
    {
        if (delta is null)
        {
            return Text;
        }

        var value = delta.Trim();
        if (value.Length == 0)
        {
            return Text;
        }

        // NeMo segnala l'inizio di una nuova parola con uno spazio iniziale; i
        // sottotoken successivi arrivano senza spazio e vanno concatenati.
        var newWord = delta.Length > value.Length && char.IsWhiteSpace(delta[0]);

        if (completed)
        {
            _text.Clear();
            _text.Append(value);
            return Text;
        }

        var current = Text;
        if (current.Length == 0)
        {
            _text.Append(value);
            return Text;
        }

        // Aggiornamento cumulativo: il server rimanda la frase intera.
        if (value.StartsWith(current, StringComparison.OrdinalIgnoreCase))
        {
            _text.Clear();
            _text.Append(value);
            return Text;
        }

        var lastWordStart = current.LastIndexOf(' ');
        var lastWord = lastWordStart >= 0 ? current[(lastWordStart + 1)..] : current;
        if (string.Equals(lastWord.TrimEnd('.', ',', '!', '?', ';', ':'), value, StringComparison.OrdinalIgnoreCase))
        {
            return Text;
        }

        if (newWord)
        {
            AppendWithSeparator(value);
        }
        else
        {
            _text.Append(value);
        }

        return Text;
    }

    private void AppendWithSeparator(string value)
    {
        if (_text.Length > 0 && !char.IsWhiteSpace(_text[^1]) && !char.IsPunctuation(value[0]))
        {
            _text.Append(' ');
        }

        _text.Append(value);
    }
}
