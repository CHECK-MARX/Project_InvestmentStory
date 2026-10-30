using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class ApiDiagnosticRedactorTests
{
    [Fact]
    public void Redact_RemovesConfiguredSecretsAndCredentialParameters()
    {
        var text = "request failed https://host/path?apikey=known-secret&symbol=ABC token: other-secret";

        var cleaned = ApiDiagnosticRedactor.Redact(text, ["known-secret"]);

        Assert.DoesNotContain("known-secret", cleaned);
        Assert.DoesNotContain("other-secret", cleaned);
        Assert.Contains("symbol=ABC", cleaned);
    }
}
