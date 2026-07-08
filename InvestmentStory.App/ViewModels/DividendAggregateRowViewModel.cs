using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class DividendAggregateRowViewModel
{
    public DividendAggregateRowViewModel(DividendAggregate aggregate)
    {
        Label = aggregate.Label;
        AmountJpy = Formatters.Jpy(aggregate.AmountJpy);
    }

    public string Label { get; }
    public string AmountJpy { get; }
}
