using System.Runtime.CompilerServices;

// I test verificano anche i dettagli interni (escape DOT, parsing dei template).
[assembly: InternalsVisibleTo("WisperTranslator.Tests")]

// Il CLI riusa le stesse funzioni interne per collaudare la modalità conversazione.
[assembly: InternalsVisibleTo("WisperTranslator.Cli")]
