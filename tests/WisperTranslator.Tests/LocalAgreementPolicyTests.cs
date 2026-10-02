using WisperTranslator.Core.Asr;

namespace WisperTranslator.Tests;

public class LocalAgreementPolicyTests
{
    [Fact]
    public void NonCommittaNullaConUnaSolaIpotesi()
    {
        var policy = new LocalAgreementPolicy();

        var update = policy.Commit("buongiorno come stai");

        Assert.Empty(update.NewlyCommitted);
        Assert.Equal("buongiorno come stai", update.PendingText);
    }

    [Fact]
    public void CommittaIlPrefissoComuneTraDueIpotesi()
    {
        var policy = new LocalAgreementPolicy();
        policy.Commit("buongiorno come stai");

        var update = policy.Commit("buongiorno come stai oggi");

        Assert.Equal("buongiorno come stai", update.NewlyCommitted);
        Assert.Equal("oggi", update.PendingText);
    }

    [Fact]
    public void NonCommittaOltreLaDivergenza()
    {
        var policy = new LocalAgreementPolicy();
        policy.Commit("il gatto dorme sul divano");

        var update = policy.Commit("il gatto mangia sul tavolo");

        Assert.Equal("il gatto", update.NewlyCommitted);
        Assert.Equal("mangia sul tavolo", update.PendingText);
    }

    [Fact]
    public void IgnoraDifferenzeDiPunteggiaturaEMaiuscole()
    {
        var policy = new LocalAgreementPolicy();
        policy.Commit("Buongiorno, come stai?");

        var update = policy.Commit("buongiorno come stai bene");

        Assert.Equal("buongiorno come stai", update.NewlyCommitted);
    }

    [Fact]
    public void ResetAzzeraLoStatoPerIlProssimoEnunciato()
    {
        var policy = new LocalAgreementPolicy();
        policy.Commit("buongiorno come stai");

        policy.Reset();
        var update = policy.Commit("nuova frase");

        Assert.Empty(update.NewlyCommitted);
        Assert.Equal("nuova frase", update.PendingText);
        Assert.Equal(0, policy.CommittedWords);
    }
}
