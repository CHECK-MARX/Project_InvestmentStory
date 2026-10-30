using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class AccountBalanceRowViewModel(AccountBalance balance)
{
    public string Broker { get; } = balance.Broker;
    public string Account { get; } = AccountTypes.DisplayName(balance.Account);
    public string Quantity { get; } = balance.Quantity.ToString("N4");
    public string CostJpy { get; } = balance.CostJpy.HasValue
        ? $"{balance.CostJpy:N0}円" : "原価不明";
}
