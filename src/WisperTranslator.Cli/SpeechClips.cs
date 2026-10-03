using System.Speech.Synthesis;
using System.Speech.AudioFormat;

namespace WisperTranslator.Cli;

/// <summary>
/// Genera clip vocali reali usando le voci SAPI installate in Windows: servono a misurare
/// l'ASR con parlato vero, senza dipendere da registrazioni esterne.
/// </summary>
internal static class SpeechClips
{
    public static IReadOnlyList<string> InstalledVoices()
    {
        using var synthesizer = new SpeechSynthesizer();
        return [.. synthesizer.GetInstalledVoices()
            .Where(voice => voice.Enabled)
            .Select(voice => voice.VoiceInfo.Name)];
    }

    public static string DefaultVoice(string language) =>
        language.ToLowerInvariant() switch
        {
            "it" or "it-it" => "Microsoft Elsa Desktop",
            "en" or "en-us" => "Microsoft Zira Desktop",
            _ => "Microsoft Zira Desktop",
        };

    public static void Write(string voiceName, string text, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var synthesizer = new SpeechSynthesizer();
        synthesizer.SelectVoice(ResolveVoice(voiceName));
        synthesizer.Rate = 0;
        synthesizer.SetOutputToWaveFile(
            outputPath,
            new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        synthesizer.Speak(text);
        synthesizer.SetOutputToNull();
    }

    /// <summary>
    /// Scrive più frasi di seguito con una pausa netta fra loro: serve a verificare che il VAD
    /// separi enunciati consecutivi e che le traduzioni non si mescolino.
    /// </summary>
    public static void WriteSentences(
        string voiceName,
        IReadOnlyList<string> sentences,
        string outputPath,
        int pauseMs,
        string language)
        => WriteSentences([voiceName], sentences, outputPath, pauseMs, language);

    /// <summary>
    /// Come sopra, ma alternando le voci frase per frase: serve a provare la diarizzazione
    /// (due o più parlanti) senza registrare una call vera.
    /// </summary>
    public static void WriteSentences(
        IReadOnlyList<string> voiceNames,
        IReadOnlyList<string> sentences,
        string outputPath,
        int pauseMs,
        string language)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var ssml = new System.Text.StringBuilder();
        ssml.Append($"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{language}\">");
        for (var index = 0; index < sentences.Count; index++)
        {
            var voice = ResolveVoice(voiceNames[index % voiceNames.Count]);
            ssml.Append($"<voice name=\"{voice}\">");
            ssml.Append(Escape(sentences[index]));
            ssml.Append("</voice>");
            if (index < sentences.Count - 1)
            {
                ssml.Append($"<break time=\"{pauseMs}ms\"/>");
            }
        }

        ssml.Append("</speak>");

        using var synthesizer = new SpeechSynthesizer();
        // La voce è selezionata nell'SSML, ma il sintetizzatore ne vuole comunque una valida.
        synthesizer.SelectVoice(ResolveVoice(voiceNames[0]));
        synthesizer.Rate = 0;
        synthesizer.SetOutputToWaveFile(
            outputPath,
            new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        synthesizer.SpeakSsml(ssml.ToString());
        synthesizer.SetOutputToNull();
    }

    private static string ResolveVoice(string voiceName)
    {
        var available = InstalledVoices();
        return available.FirstOrDefault(name => name.Contains(voiceName, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Voce \"{voiceName}\" non installata. Disponibili: {string.Join(", ", available)}");
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
