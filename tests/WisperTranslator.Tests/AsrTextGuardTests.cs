using WisperTranslator.Core.Asr;

namespace WisperTranslator.Tests;

public class AsrTextGuardTests
{
    [Fact]
    public void TagliaIlCicloDiRipetizioneOsservatoNeiTest()
    {
        const string looped = "La lezione comincia alle 9 e finisce la lezione comincia alle 9 e finisce "
                              + "la lezione comincia alle 9 e finisce la lezione comincia alle 9 e finisce "
                              + "la lezione comincia alle 9 e finisce a mezzogiorno.";

        Assert.Equal("La lezione comincia alle 9 e finisce", AsrTextGuard.TrimRepetitions(looped));
        Assert.True(AsrTextGuard.HasRepetition(looped));
    }

    [Fact]
    public void NonToccaUnTestoNormale()
    {
        const string text = "Buongiorno, questa è una prova di trascrizione in tempo reale. "
                            + "Il sistema deve riconoscere le frasi italiane e tradurle in inglese.";

        Assert.Equal(text, AsrTextGuard.TrimRepetitions(text));
        Assert.False(AsrTextGuard.HasRepetition(text));
    }

    [Fact]
    public void ConservaLeRipetizioniLegittime()
    {
        Assert.Equal("no no no", AsrTextGuard.TrimRepetitions("no no no"));
        Assert.Equal("molto molto bene", AsrTextGuard.TrimRepetitions("molto molto bene"));
    }

    [Fact]
    public void RiconosceUnCicloDiPiuParole()
    {
        const string looped = "grazie mille per il tuo aiuto grazie mille per il tuo aiuto grazie mille per il tuo aiuto";

        Assert.Equal("grazie mille per il tuo aiuto", AsrTextGuard.TrimRepetitions(looped));
    }

    [Fact]
    public void TestoVuotoNonRompeNulla()
    {
        Assert.Equal(string.Empty, AsrTextGuard.TrimRepetitions(null));
        Assert.Equal(string.Empty, AsrTextGuard.TrimRepetitions("   "));
    }
}
