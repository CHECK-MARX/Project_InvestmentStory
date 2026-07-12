using InvestmentStory.App.Infrastructure;
using InvestmentStory.Core.Models;

namespace InvestmentStory.App.ViewModels;

public sealed class BrokerTradeRowViewModel
{
    public BrokerTradeRowViewModel(BrokerTrade trade)
    {
        TradeDate = trade.TradeDate.ToString("yyyy/MM/dd");
        SettlementDate = trade.SettlementDate.ToString("yyyy/MM/dd");
        Broker = trade.Broker;
        AccountType = AccountTypes.DisplayName(trade.AccountType);
        TradeType = FormatTradeType(trade.TradeType);
        Quantity = Formatters.Number(trade.Quantity);
        UnitPrice = Formatters.Money(trade.UnitPrice, trade.Currency);
        Currency = trade.Currency;
        ExchangeRate = trade.ExchangeRate <= 0m ? "-" : trade.ExchangeRate.ToString("N2");
        SettlementAmountJpy = Formatters.Jpy(trade.SettlementAmountJpy);
        FeeJpy = Formatters.Jpy(trade.FeeJpy);
        TaxJpy = Formatters.Jpy(trade.TaxJpy);
        RealizedGainLossJpy = Formatters.SignedJpy(trade.RealizedGainLossJpy);
        AfterTradeQuantity = Formatters.Number(trade.AfterTradeQuantity);
        AfterTradeAverageCost = Formatters.Money(trade.AfterTradeAverageCost, trade.Currency);
        Source = string.IsNullOrWhiteSpace(trade.Source) ? "未取得" : FormatSource(trade.Source);
    }

    public string TradeDate { get; }
    public string SettlementDate { get; }
    public string Broker { get; }
    public string AccountType { get; }
    public string TradeType { get; }
    public string Quantity { get; }
    public string UnitPrice { get; }
    public string Currency { get; }
    public string ExchangeRate { get; }
    public string SettlementAmountJpy { get; }
    public string FeeJpy { get; }
    public string TaxJpy { get; }
    public string RealizedGainLossJpy { get; }
    public string AfterTradeQuantity { get; }
    public string AfterTradeAverageCost { get; }
    public string Source { get; }

    private static string FormatTradeType(string tradeType)
    {
        if (tradeType.Equals("InitialPosition", StringComparison.OrdinalIgnoreCase) ||
            tradeType.Equals("OpeningBalance", StringComparison.OrdinalIgnoreCase))
        {
            return "初期保有";
        }

        if (tradeType.Equals("StockSplit", StringComparison.OrdinalIgnoreCase))
        {
            return "株式分割";
        }

        if (tradeType.Equals("ReverseSplit", StringComparison.OrdinalIgnoreCase) ||
            tradeType.Equals("ReverseStockSplit", StringComparison.OrdinalIgnoreCase) ||
            tradeType.Equals("StockConsolidation", StringComparison.OrdinalIgnoreCase))
        {
            return "株式併合";
        }

        if (tradeType.Equals("TransferIn", StringComparison.OrdinalIgnoreCase))
        {
            return "入庫";
        }

        if (tradeType.Equals("TransferOut", StringComparison.OrdinalIgnoreCase))
        {
            return "出庫";
        }

        return tradeType;
    }

    private static string FormatSource(string source) =>
        source.Equals("HoldingsSnapshot", StringComparison.OrdinalIgnoreCase)
            ? "保有残高CSV/手入力"
            : source;
}
