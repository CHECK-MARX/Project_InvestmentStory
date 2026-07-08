using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class SecuritySymbolNormalizerTests
{
    [Fact]
    public void NormalizeTicker_MapsEurnToCurrentCmbtTicker()
    {
        Assert.Equal("CMBT", SecuritySymbolNormalizer.NormalizeTicker("EURN"));
        Assert.Equal("CMBT", SecuritySymbolNormalizer.NormalizeTicker("eurn"));
        Assert.Equal("CMB テック", SecuritySymbolNormalizer.NormalizeName("EURN", "ユーロナブ"));
    }

    [Fact]
    public void NormalizeBroker_UnifiesOldNomuraKanji()
    {
        Assert.Equal("野村証券", SecuritySymbolNormalizer.NormalizeBroker("野村證券"));
    }
}
