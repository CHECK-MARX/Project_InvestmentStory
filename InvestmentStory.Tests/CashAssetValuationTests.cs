using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class CashAssetValuationTests
{
    private static readonly DateTime Day = new(2026, 10, 18);
    private readonly CashAssetValuationService _service = new();

    private static CashReconciliation Review(decimal closing, string account = "SBI", string currency = "JPY",
        DateTime? end = null, DateTime? start = null, decimal? opening = null,
        IReadOnlyList<CashOperation>? operations = null, bool confirmed = true, DateTime? reviewedAt = null)
    {
        var scope = new CashReconciliation { Account = account, Currency = currency, StartDate = start ?? end ?? Day, EndDate = end ?? Day };
        return new CashReconciliation
        {
            Account = account, Currency = currency, StartDate = scope.StartDate, EndDate = scope.EndDate,
            OpeningBalance = opening ?? closing, ClosingBalance = closing, AllEntriesConfirmed = confirmed,
            ExternalFlowsConfirmed = confirmed, Evidence = "残高明細", ReviewedAtUtc = reviewedAt ?? Day,
            LedgerFingerprint = CashLedgerService.Fingerprint(operations ?? Array.Empty<CashOperation>(), scope)
        };
    }

    private static ExchangeRateQuote Quote(decimal rate = 150m, DateTime? at = null, string input = "API", string source = "Test quote") =>
        new() { Rate = rate, AcquiredAt = at ?? Day, InputType = input, Source = source };

    [Fact]
    public void NoCashRegistrationIsUnknownRatherThanZero()
    {
        var result = _service.Build(new([], []), Day);
        Assert.Null(result.TotalRegisteredCashJpy); Assert.Null(result.KnownSubtotalJpy);
        var vm = new DashboardViewModel();
        vm.Update(new DashboardSummary { TotalCurrentMarketValueJpy = 1000m }, [], [], cashAssets: result);
        Assert.Equal("算出不可", vm.RegisteredCashTotal);
        Assert.Equal("算出不可", vm.SecuritiesAndRegisteredCashTotal);
        Assert.Contains("未登録", vm.CashCoverageStatus);
    }

    [Theory]
    [InlineData("JPY")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void ConfirmedZeroBalanceDoesNotRequireAnExchangeRate(string currency)
    {
        var result = _service.Build(new([], [Review(0m, currency: currency)]), Day);
        Assert.Equal(0m, result.TotalRegisteredCashJpy);
        Assert.Equal(1, result.IncludedCount);
        Assert.Contains("換算不要", Assert.Single(result.Balances).Status);
    }

    [Fact]
    public void PastBalanceIsVisibleWithItsDateButCannotBecomeCurrentCash()
    {
        var result = _service.Build(new([], [Review(450m, end: Day.AddDays(-1))]), Day);
        Assert.Null(result.TotalRegisteredCashJpy);
        var row = Assert.Single(result.Balances);
        Assert.Equal(Day.AddDays(-1), row.BalanceDate);
        Assert.Equal(450m, row.StatementBalance);
        Assert.Contains("残高は未確認", row.Status);
    }

    [Fact]
    public void OverlappingPeriodsAreNotAddedTwice()
    {
        var result = _service.Build(new([], [Review(100m), Review(100m, start: Day.AddDays(-30))]), Day);
        Assert.Single(result.Balances);
        Assert.Equal(100m, result.TotalRegisteredCashJpy);
    }

    [Fact]
    public void ConflictingBalancesForTheSameDateAreNotSilentlySelected()
    {
        var result = _service.Build(new([], [Review(100m), Review(200m, start: Day.AddDays(-30))]), Day);
        Assert.Null(result.TotalRegisteredCashJpy);
        Assert.Contains("不一致", Assert.Single(result.Balances).Status);
    }

    [Fact]
    public void NewerUnconfirmedReviewDoesNotFallBackToOlderConfirmedReview()
    {
        var result = _service.Build(new([], [Review(100m, start: Day.AddDays(-5), reviewedAt: Day.AddHours(1)),
            Review(100m, confirmed: false, reviewedAt: Day.AddHours(2))]), Day);
        Assert.Null(result.TotalRegisteredCashJpy);
        Assert.Contains("未確認", Assert.Single(result.Balances).Status);
    }

    [Fact]
    public void MissingCurrencyConversionMakesTotalUnknownButKeepsKnownSubtotal()
    {
        var result = _service.Build(new([], [Review(1000m), Review(50m, currency: "EUR")]), Day, Quote());
        Assert.Null(result.TotalRegisteredCashJpy);
        Assert.Equal(1000m, result.KnownSubtotalJpy);
        Assert.Equal(1, result.IncludedCount);
        Assert.Contains("為替", result.Balances.Single(x => x.Currency == "EUR").Status);
    }

    [Fact]
    public void DollarCashUsesDocumentedQuoteAndKeepsNegativeBalances()
    {
        var result = _service.Build(new([], [Review(1000m), Review(-2m, currency: "USD")]), Day, Quote());
        Assert.Equal(700m, result.TotalRegisteredCashJpy);
        Assert.Contains("参考換算", result.Balances.Single(x => x.Currency == "USD").Status);
        Assert.Contains("Test quote", result.Balances.Single(x => x.Currency == "USD").Status);
    }

    [Fact]
    public void OldDocumentedQuoteIsMarkedDelayedInsteadOfLosingItsProvenance()
    {
        var result = _service.Build(new([], [Review(2m, currency: "USD")]), Day, Quote(at: Day.AddDays(-10)));
        Assert.Equal(300m, result.TotalRegisteredCashJpy);
        Assert.Contains("更新遅れ", Assert.Single(result.Balances).Status);
        Assert.Contains("2026/10/08", Assert.Single(result.Balances).Status);
    }

    [Fact]
    public void UnknownFutureOrMockQuotesCannotProduceCashValuation()
    {
        foreach (var quote in new[] { Quote(1m), Quote(0m), Quote(at: DateTime.MinValue), Quote(at: Day.AddDays(1)),
            Quote(input: "Mock"), Quote(source: "Mock fallback"), Quote(source: ""), Quote(input: "Sample"),
            new ExchangeRateQuote { Rate = 150m, BaseCurrency = "EUR", AcquiredAt = Day } })
        {
            var result = _service.Build(new([], [Review(20m, currency: "USD")]), Day, quote);
            Assert.Null(result.TotalRegisteredCashJpy);
        }
        Assert.Equal(3000m, _service.Build(new([], [Review(20m, currency: "USD")]), Day,
            Quote(input: "Sample"), allowSampleQuotes: true).TotalRegisteredCashJpy);
    }

    [Fact]
    public void AccountWithoutReviewCannotDisappearFromTheCoverage()
    {
        var operation = new CashOperation { Date = Day, Kind = CashOperationKind.Deposit, Evidence = "明細",
            Legs = [new("未照合口座", "JPY", 500m)] };
        var result = _service.Build(new([operation], [Review(100m)]), Day);
        Assert.Equal(2, result.Balances.Count);
        Assert.Equal(100m, result.KnownSubtotalJpy);
        Assert.Null(result.TotalRegisteredCashJpy);
        Assert.Contains("残高照合なし", result.Balances.Single(x => x.Account == "未照合口座").Status);
    }

    [Fact]
    public void DividendCashIsIncludedOnceAndDoesNotChangeSecuritiesGain()
    {
        var operation = new CashOperation { Date = Day, Kind = CashOperationKind.Dividend, Evidence = "配当明細",
            Legs = [new("SBI", "JPY", 100m)] };
        var result = _service.Build(new([operation], [Review(1100m, opening: 1000m, operations: [operation])]), Day);
        var vm = new DashboardViewModel();
        vm.Update(new DashboardSummary { TotalCurrentMarketValueJpy = 5000m, CumulativeDividendJpy = 100m,
            TotalUnrealizedGainJpy = 500m }, [], [], cashAssets: result);
        Assert.Equal("1,100円", vm.RegisteredCashTotal);
        Assert.Equal("6,100円", vm.SecuritiesAndRegisteredCashTotal);
        Assert.Equal("+500円", vm.TotalUnrealizedGain);
        Assert.Equal("100円", vm.CumulativeDividend);
    }

    [Fact]
    public void UnknownSecuritiesValuationSuppressesCombinedAmountButNotKnownCash()
    {
        var result = _service.Build(new([], [Review(100m)]), Day);
        var vm = new DashboardViewModel();
        vm.Update(new DashboardSummary { UnknownCurrentMarketValuePositionCount = 1 }, [], [], cashAssets: result);
        Assert.Equal("100円", vm.RegisteredCashTotal);
        Assert.Equal("算出不可", vm.SecuritiesAndRegisteredCashTotal);
        vm.UpdateCashAssets(new CashAssetSummary { Error = "読込失敗" });
        Assert.Equal("算出不可", vm.RegisteredCashTotal);
        Assert.Empty(vm.CashBalances);
    }

    [Fact]
    public void ChangedLedgerInvalidatesPreviouslyUsableCash()
    {
        var review = Review(100m);
        var cancelled = new CashOperation { Date = Day, Kind = CashOperationKind.Deposit, Evidence = "取消履歴",
            Legs = [new("SBI", "JPY", 50m)], CancelledAtUtc = Day.AddHours(1), CancellationReason = "訂正" };
        var result = _service.Build(new([cancelled], [review]), Day);
        Assert.Null(result.TotalRegisteredCashJpy);
        Assert.Contains("再読込", Assert.Single(result.Balances).Status);
    }
}
