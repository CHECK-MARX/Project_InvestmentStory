using InvestmentStory.App.ViewModels;
using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;

namespace InvestmentStory.Tests;

public sealed class DividendsViewModelTests
{
    [Fact]
    public void Update_SeparatesPaidAndUnconfirmedPaymentsIntoDifferentTabs()
    {
        var viewModel = new DividendsViewModel(_ => { }, _ => { });
        var paid = new DividendPayment
        {
            Id = 1, StockId = 1, PaymentDate = new DateTime(2026, 1, 5),
            DividendStatus = DividendConstants.Actual, Broker = "SBI証券",
            NetAmountJpy = 1_000m
        };
        var planned = new DividendPayment
        {
            Id = 2, StockId = 1, PaymentDate = new DateTime(2026, 12, 5),
            DividendStatus = DividendConstants.Estimated, Broker = "SBI証券",
            NetAmountJpy = 1_200m
        };

        viewModel.Update(Array.Empty<StockPosition>(), new[] { paid, planned },
            asOf: new DateTime(2026, 10, 13));

        Assert.Equal(paid.Id, Assert.Single(viewModel.ActualPayments).Id);
        Assert.Equal(planned.Id, Assert.Single(viewModel.PlannedPayments).Id);
    }
}
