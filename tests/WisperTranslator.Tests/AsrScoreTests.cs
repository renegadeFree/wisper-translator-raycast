using WisperTranslator.Core.Asr;

namespace WisperTranslator.Tests;

public class AsrScoreTests
{
    [Fact]
    public void NormalizzaPunteggiaturaECase()
    {
        Assert.Equal("buongiorno come stai", AsrScore.Normalize("Buongiorno, come stai?"));
    }

    [Fact]
    public void RiconosceUnaTrascrizioneIdentica()
    {
        Assert.Equal(1.0, AsrScore.WordAccuracy("Il gatto dorme sul divano.", "il gatto dorme sul divano"));
    }

    [Fact]
    public void PenalizzaLeParoleMancanti()
    {
        var accuracy = AsrScore.WordAccuracy("il gatto dorme sul divano", "il gatto dorme");

        // 2 parole sbagliate su 5 attese.
        Assert.InRange(accuracy, 0.55, 0.65);
    }

    [Fact]
    public void TestoAttesoVuotoNonDanneggiaIlPunteggio()
    {
        Assert.Equal(1.0, AsrScore.WordAccuracy("", ""));
    }
}
