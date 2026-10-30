using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MonthEndPortfolioService
{
    public const string FormulaVersion = "MonthEndPortfolio/1";

    public static bool IsMonthEnd(DateTime date) => date.Day == DateTime.DaysInMonth(date.Year, date.Month);

    public MonthEndPortfolioRecord Capture(DateTime date, IReadOnlyList<StockSnapshot> snapshots,
        CashAssetSummary cash, IReadOnlyList<DividendPayment> dividends,
        IReadOnlyList<BrokerTrade> trades, ExchangeRateQuote usdJpyQuote)
    {
        if (!IsMonthEnd(date)) throw new ArgumentException("月末日だけを記録できます。", nameof(date));
        var active = snapshots.Where(PositionEvidenceService.IsActiveHoldingSnapshot).ToList();
        var coverage = PositionEvidenceCoverage.Build(active, date);
        var positions = active.Select(snapshot =>
        {
            var position = snapshot.Position;
            var holding = position.CurrentHolding;
            var priceDate = position.IsMutualFund ? position.MutualFund.NavDate : holding.CurrentPriceAcquiredAt;
            var foreign = !position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase);
            return new MonthEndPositionEvidence
            {
                StockId = position.Stock.Id,
                Ticker = position.Stock.Ticker,
                Broker = position.Stock.Broker,
                AccountType = position.Stock.AccountType,
                Currency = position.Stock.Currency,
                Quantity = position.IsMutualFund ? position.MutualFund.UnitsHeld : holding.CurrentShares,
                MarketValueJpy = snapshot.HasKnownCurrentMarketValueJpy ? snapshot.CurrentMarketValueJpy : null,
                CostJpy = snapshot.HasKnownPurchaseCostJpy ? snapshot.PurchaseTotalJpy : null,
                CostQuality = PositionEvidenceService.CostQuality(snapshot).ToString(),
                CostSource = position.IsMutualFund ? "投信の取得額/基準価額" :
                    snapshot.IsReferencePurchaseCostJpy ? position.Purchase.ReferenceAcquisitionSource :
                    position.Purchase.AcquisitionAmountJpy > 0m ? position.Purchase.AcquisitionAmountSource :
                    position.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ? "円建て買付単価×数量" : "未確認",
                CostConfirmedAt = position.Purchase.AcquisitionAmountConfirmedAt == DateTime.MinValue
                    ? null : position.Purchase.AcquisitionAmountConfirmedAt,
                PriceSource = position.IsMutualFund ? position.MutualFund.NavSource : holding.CurrentPriceSource,
                PriceAcquiredAt = priceDate == DateTime.MinValue ? null : priceDate,
                PriceFreshness = PositionEvidenceService.PriceFreshness(position, date).ToString(),
                FxRate = foreign && PositionEvidenceService.HasUsableForeignExchangeRate(holding.CurrentExchangeRate)
                    ? holding.CurrentExchangeRate : foreign ? null : 1m,
                FxSource = foreign ? holding.ExchangeRateSource : "円建て",
                FxAcquiredAt = foreign && holding.ExchangeRateAcquiredAt != DateTime.MinValue
                    ? holding.ExchangeRateAcquiredAt : null,
                FxFreshness = foreign
                    ? PositionEvidenceService.HasUsableForeignExchangeRate(holding.CurrentExchangeRate)
                        ? PositionEvidenceService.Freshness(holding.ExchangeRateAcquiredAt, date).ToString()
                        : "Missing"
                    : "NotRequired"
            };
        }).ToList();
        var record = new MonthEndPortfolioRecord
        {
            SnapshotDate = date.Date,
            FormulaVersion = FormulaVersion,
            Positions = positions,
            CashBalances = cash.Balances.Select(x => new MonthEndCashEvidence
            {
                Account = x.Account, Currency = x.Currency, BalanceDate = x.BalanceDate,
                IncludedJpy = x.IncludedJpy, Status = x.Status, Evidence = x.Evidence
            }).ToList(),
            CumulativeDividendJpy = dividends.Where(x => x.PaymentDate.Date <= date.Date &&
                DividendConstants.IsVisibleActual(x.DividendStatus))
                .Sum(x => x.NetAmountJpy > 0m ? x.NetAmountJpy : x.JpyAmount),
            RealizedGainLossJpy = trades.Any(x => x.TradeDate.Date <= date.Date &&
                (x.TradeType is "TransferIn" or "StockSplitCandidate"))
                ? null : trades.Where(x => x.TradeDate.Date <= date.Date).Sum(x => x.RealizedGainLossJpy),
            UsdJpyRate = usdJpyQuote.Rate > 0m ? usdJpyQuote.Rate : null,
            UsdJpySource = usdJpyQuote.Source,
            UsdJpyAcquiredAt = usdJpyQuote.Rate > 0m ? usdJpyQuote.AcquiredAt : null,
            QualitySummary = $"取得額 確定{coverage.ConfirmedCostCount}・概算{coverage.EstimatedCostCount}・未確認{coverage.UnconfirmedCostCount}件 / " +
                $"評価額未取得{coverage.UnknownMarketValueCount}件 / 価格更新遅れ{coverage.DelayedPriceCount}件 / " +
                $"為替未取得{coverage.MissingFxCount}件 / 現金確認{cash.IncludedCount}/{cash.Balances.Count}口座"
        };
        var computed = Recalculate(record);
        record.StoredSecuritiesJpy = computed.SecuritiesJpy;
        record.StoredCostJpy = computed.CostJpy;
        record.StoredCashJpy = computed.CashJpy;
        record.StoredTotalJpy = computed.TotalJpy;
        record.StoredUnrealizedJpy = computed.UnrealizedJpy;
        record.StoredTotalReturnJpy = computed.TotalReturnJpy;
        return record;
    }

    public MonthEndRecalculation Recalculate(MonthEndPortfolioRecord record)
    {
        var securities = record.Positions.All(x => x.MarketValueJpy.HasValue)
            ? record.Positions.Sum(x => x.MarketValueJpy!.Value) : (decimal?)null;
        var cost = record.Positions.All(x => x.CostJpy.HasValue)
            ? record.Positions.Sum(x => x.CostJpy!.Value) : (decimal?)null;
        // An empty cash ledger means unknown, never a confirmed zero balance.
        var cash = record.CashBalances.Count > 0 && record.CashBalances.All(x => x.IncludedJpy.HasValue)
            ? record.CashBalances.Sum(x => x.IncludedJpy!.Value) : (decimal?)null;
        decimal? total = securities is decimal s && cash is decimal c ? s + c : null;
        decimal? unrealized = securities is decimal market && cost is decimal basis ? market - basis : null;
        decimal? totalReturn = unrealized is decimal gain && record.RealizedGainLossJpy is decimal realized
            ? gain + realized + record.CumulativeDividendJpy : null;
        decimal? difference = total is decimal current && record.StoredTotalJpy is decimal stored ? current - stored : null;
        var causes = new List<string>();
        if (record.FormulaVersion != FormulaVersion) causes.Add("算式版の相違");
        if (securities != record.StoredSecuritiesJpy) causes.Add("銘柄別評価額の再集計差");
        if (cost != record.StoredCostJpy) causes.Add("取得額の再集計差");
        if (cash != record.StoredCashJpy) causes.Add("現金残高の再集計差");
        if (total != record.StoredTotalJpy) causes.Add("総額の再集計差");
        if (unrealized != record.StoredUnrealizedJpy) causes.Add("含み損益の再集計差");
        if (totalReturn != record.StoredTotalReturnJpy) causes.Add("記録済み損益の再集計差");
        var reason = difference is null ? "評価額または現金残高の根拠が欠けるため差額を計算できません。" :
            difference == 0m && causes.Count == 0 ? "保存時と現行算式の再集計値が一致します。" :
            causes.Count > 0 ? $"差の確認事項: {string.Join("、", causes)}。保存明細と算式版を照合してください。" :
            "保存合計と明細の再集計に差があります。";
        return new MonthEndRecalculation(securities, cost, cash, total, unrealized, totalReturn, difference, reason);
    }
}
