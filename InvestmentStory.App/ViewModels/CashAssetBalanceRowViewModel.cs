using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class CashAssetBalanceRowViewModel(CashAssetBalance balance)
{
    public string Account => balance.Account;
    public string Currency => balance.Currency;
    public string BalanceDate => balance.BalanceDate?.ToString("yyyy/MM/dd") ?? "未確認";
    public string StatementBalance => balance.StatementBalance?.ToString("#,0.############################") ?? "未確認";
    public string IncludedJpy => balance.IncludedJpy is decimal amount ? Formatters.Jpy(amount) : "集計対象外";
    public string Status => balance.Status;
    public string Evidence => balance.Evidence;
}
