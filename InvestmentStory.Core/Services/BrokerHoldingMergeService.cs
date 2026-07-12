using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class BrokerHoldingMergeService
{
    public BrokerMergeResult MergeHoldings(
        IReadOnlyList<StockPosition> existingPositions,
        IReadOnlyList<BrokerHoldingRecord> brokerHoldings,
        DateTime acquiredAt)
    {
        ArgumentNullException.ThrowIfNull(existingPositions);
        ArgumentNullException.ThrowIfNull(brokerHoldings);

        var decisions = brokerHoldings
            .Where(x => !string.IsNullOrWhiteSpace(x.Ticker))
            .Select(x => BuildDecision(existingPositions, x, acquiredAt))
            .ToList();

        return new BrokerMergeResult { Decisions = decisions };
    }

    private static BrokerMergeDecision BuildDecision(
        IReadOnlyList<StockPosition> existingPositions,
        BrokerHoldingRecord source,
        DateTime acquiredAt)
    {
        var sourceKey = PositionIdentityService.BuildKey(source);
        var exactMatches = existingPositions
            .Where(x => string.Equals(PositionIdentityService.BuildKey(x), sourceKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactMatches.Count == 0)
        {
            var sourceAccountType = NormalizeSourceAccountType(source);
            exactMatches = existingPositions
                .Where(x => IsSameBroker(x.Stock.Broker, source.Broker) &&
                            IsSameTicker(x.Stock.Ticker, source.Ticker) &&
                            IsSameAccountType(x.Stock.AccountType, sourceAccountType) &&
                            IsSameCustodyType(x.Stock.CustodyType, source.Account))
                .ToList();
        }

        if (exactMatches.Count > 1)
        {
            exactMatches = exactMatches
                .OrderByDescending(x => x.CurrentHolding.UpdatedAt)
                .ThenByDescending(x => x.IsMutualFund ? x.MutualFund.MarketValue : x.CurrentHolding.CurrentShares * x.CurrentHolding.CurrentPrice)
                .ThenByDescending(x => x.Stock.Id)
                .Take(1)
                .ToList();
        }

        if (exactMatches.Count == 1)
        {
            var existing = exactMatches[0];
            var changedFields = new List<string>();
            var merged = CopyPosition(existing);
            ApplyBrokerHolding(merged, source, acquiredAt, changedFields);
            return new BrokerMergeDecision
            {
                Action = BrokerMergeAction.Overwrite,
                Source = source,
                Existing = existing,
                Merged = merged,
                Reason = "同一証券会社・同一ティッカーのため、証券会社データを正として上書きします。",
                ChangedFields = changedFields
            };
        }

        if (exactMatches.Count > 1)
        {
            return NeedsReview(source, $"同一証券会社・同一ティッカーの既存候補が{exactMatches.Count}件あります。口座区分や取引履歴で確認してください。");
        }

        var tickerMatches = existingPositions
            .Where(x => IsSameTicker(x.Stock.Ticker, source.Ticker))
            .ToList();

        if (tickerMatches.Count == 1 && string.IsNullOrWhiteSpace(source.Broker))
        {
            return NeedsReview(source, "ティッカーは一致しましたが、証券会社が取得できていないため確認が必要です。");
        }

        if (tickerMatches.Count > 0 && string.IsNullOrWhiteSpace(source.Broker))
        {
            return NeedsReview(source, "ティッカー一致の既存銘柄がありますが、証券会社が取得できていないため確認が必要です。");
        }

        var created = CreatePosition(source, acquiredAt);
        return new BrokerMergeDecision
        {
            Action = BrokerMergeAction.Create,
            Source = source,
            Merged = created,
            Reason = tickerMatches.Count > 0
                ? "同一ティッカーの既存銘柄がありますが、証券会社が異なるため別保有として追加します。"
                : "既存銘柄に一致しないため、新規追加候補です。",
            ChangedFields = new[] { "銘柄", "購入情報", "現在保有情報" }
        };
    }

    private static BrokerMergeDecision NeedsReview(BrokerHoldingRecord source, string reason) =>
        new()
        {
            Action = BrokerMergeAction.NeedsReview,
            Source = source,
            Reason = reason
        };

    private static void ApplyBrokerHolding(
        StockPosition target,
        BrokerHoldingRecord source,
        DateTime acquiredAt,
        List<string> changedFields)
    {
        SetIfChanged(target.Stock.Ticker, NormalizeTickerForDisplay(source.Ticker), value => target.Stock.Ticker = value, "ティッカー", changedFields);
        SetIfChanged(target.Stock.Broker, source.Broker.Trim(), value => target.Stock.Broker = value, "証券会社", changedFields);
        SetIfChanged(target.Stock.AccountType, NormalizeSourceAccountType(source), value => target.Stock.AccountType = value, "口座区分", changedFields);
        SetIfChanged(target.Stock.CustodyType, source.Account.Trim(), value => target.Stock.CustodyType = value, "預り区分", changedFields);
        if (!string.IsNullOrWhiteSpace(source.Name))
        {
            SetIfChanged(target.Stock.Name, source.Name.Trim(), value => target.Stock.Name = value, "銘柄名", changedFields);
        }

        if (!string.IsNullOrWhiteSpace(source.Product))
        {
            SetIfChanged(target.Stock.Sector, source.Product.Trim(), value => target.Stock.Sector = value, "商品種別", changedFields);
        }

        if (!string.IsNullOrWhiteSpace(source.Market))
        {
            SetIfChanged(target.Stock.Market, source.Market.Trim(), value => target.Stock.Market = value, "市場", changedFields);
        }

        var currency = NormalizeCurrency(source.Currency);
        if (!string.IsNullOrWhiteSpace(currency))
        {
            SetIfChanged(target.Stock.Currency, currency, value => target.Stock.Currency = value, "通貨", changedFields);
            SetIfChanged(target.Stock.Country, currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ? "日本" : "米国", value => target.Stock.Country = value, "国", changedFields);
        }

        var sourceLabel = string.IsNullOrWhiteSpace(source.Source) ? "証券会社データ" : source.Source.Trim();
        SetIfChanged(target.Stock.DataSource, sourceLabel, value => target.Stock.DataSource = value, "登録元", changedFields);
        SetIfChanged(
            target.Stock.AssetType,
            source.IsMutualFund ? AssetTypes.MutualFund : AssetTypes.Stock,
            value => target.Stock.AssetType = value,
            "資産種別",
            changedFields);

        if (source.IsMutualFund)
        {
            ApplyMutualFundHolding(target, source, acquiredAt, sourceLabel, changedFields);
            return;
        }

        if (source.Shares > 0m)
        {
            SetIfChanged(target.Purchase.Shares, source.Shares, value => target.Purchase.Shares = value, "購入株数", changedFields);
            SetIfChanged(target.CurrentHolding.CurrentShares, source.Shares, value => target.CurrentHolding.CurrentShares = value, "現在保有株数", changedFields);
        }

        if (source.AverageAcquisitionPrice > 0m)
        {
            SetIfChanged(target.Purchase.UnitPrice, source.AverageAcquisitionPrice, value => target.Purchase.UnitPrice = value, "購入単価", changedFields);
        }

        if (source.CurrentPrice > 0m)
        {
            SetIfChanged(target.CurrentHolding.CurrentPrice, source.CurrentPrice, value => target.CurrentHolding.CurrentPrice = value, "現在株価", changedFields);
        }
        else if (source.MarketValue > 0m && source.Shares > 0m)
        {
            SetIfChanged(target.CurrentHolding.CurrentPrice, decimal.Round(source.MarketValue / source.Shares, 6), value => target.CurrentHolding.CurrentPrice = value, "現在株価", changedFields);
        }

        if (target.Stock.Currency.Equals("JPY", StringComparison.OrdinalIgnoreCase))
        {
            SetIfChanged(target.Purchase.ExchangeRate, 1m, value => target.Purchase.ExchangeRate = value, "購入時為替", changedFields);
            SetIfChanged(target.CurrentHolding.CurrentExchangeRate, 1m, value => target.CurrentHolding.CurrentExchangeRate = value, "現在為替", changedFields);
        }
        else
        {
            if (source.PurchaseExchangeRate > 0m)
            {
                SetIfChanged(target.Purchase.ExchangeRate, source.PurchaseExchangeRate, value => target.Purchase.ExchangeRate = value, "購入時為替", changedFields);
            }

            if (source.CurrentExchangeRate > 0m)
            {
                SetIfChanged(target.CurrentHolding.CurrentExchangeRate, source.CurrentExchangeRate, value => target.CurrentHolding.CurrentExchangeRate = value, "現在為替", changedFields);
            }
        }

        var snapshotAt = source.SnapshotDate == DateTime.MinValue ? acquiredAt : source.SnapshotDate;
        target.Purchase.ExchangeRateAcquiredAt = snapshotAt;
        target.Purchase.ExchangeRateSource = sourceLabel;
        target.Purchase.ExchangeRateInputType = "CSV";
        target.CurrentHolding.ExchangeRateAcquiredAt = snapshotAt;
        target.CurrentHolding.ExchangeRateSource = sourceLabel;
        target.CurrentHolding.ExchangeRateInputType = "CSV";
        target.CurrentHolding.UpdatedAt = snapshotAt.Date;
        target.CurrentHolding.CurrentPriceAcquiredAt = snapshotAt;
        target.CurrentHolding.CurrentPriceSource = sourceLabel;
    }

    private static void ApplyMutualFundHolding(
        StockPosition target,
        BrokerHoldingRecord source,
        DateTime acquiredAt,
        string sourceLabel,
        List<string> changedFields)
    {
        var snapshotAt = source.SnapshotDate == DateTime.MinValue ? acquiredAt : source.SnapshotDate;
        var unitsHeld = source.UnitsHeld > 0m ? source.UnitsHeld : source.Shares;
        var unitBase = MutualFundCalculator.NormalizeUnitBase(source.UnitBase);
        var averageCostNav = source.AverageCostNav > 0m ? source.AverageCostNav : source.AverageAcquisitionPrice;
        var currentNav = source.CurrentNav > 0m ? source.CurrentNav : source.CurrentPrice;
        var marketValue = source.MarketValue > 0m ? source.MarketValue : source.MarketValueJpy;
        var unrealizedGainLoss = source.UnrealizedGainLossJpy != 0m
            ? source.UnrealizedGainLossJpy
            : marketValue - source.AcquisitionAmount;
        var navDate = source.NavDate == DateTime.MinValue ? snapshotAt : source.NavDate;

        target.Stock.AssetType = AssetTypes.MutualFund;
        target.Stock.Currency = "JPY";
        target.Stock.Country = "日本";
        target.Stock.AccountType = NormalizeSourceAccountType(source);
        target.Stock.CustodyType = source.Account.Trim();
        target.Stock.Sector = string.IsNullOrWhiteSpace(source.Product) ? "投資信託" : source.Product;

        target.MutualFund.FundName = string.IsNullOrWhiteSpace(source.FundName) ? source.Name.Trim() : source.FundName.Trim();
        target.MutualFund.FundCode = source.FundCode;
        target.MutualFund.AssociationCode = source.AssociationCode;
        target.MutualFund.UnitsHeld = unitsHeld;
        target.MutualFund.UnitBase = unitBase;
        target.MutualFund.AverageCostNav = averageCostNav;
        target.MutualFund.CurrentNav = currentNav;
        target.MutualFund.AcquisitionAmount = source.AcquisitionAmount;
        target.MutualFund.MarketValue = marketValue;
        target.MutualFund.UnrealizedGainLoss = unrealizedGainLoss;
        target.MutualFund.NavDate = navDate;
        target.MutualFund.NavSource = string.IsNullOrWhiteSpace(source.NavSource) ? sourceLabel : source.NavSource;
        target.MutualFund.DistributionMethod = source.DistributionMethod;
        target.MutualFund.AccountType = source.Account;
        target.MutualFund.TotalPurchaseAmount = source.AcquisitionAmount > 0m
            ? source.AcquisitionAmount
            : target.MutualFund.TotalPurchaseAmount;

        SetIfChanged(target.Purchase.Shares, unitsHeld, value => target.Purchase.Shares = value, "保有口数", changedFields);
        SetIfChanged(target.Purchase.UnitPrice, averageCostNav, value => target.Purchase.UnitPrice = value, "取得単価NAV", changedFields);
        target.Purchase.ExchangeRate = 1m;
        target.Purchase.ExchangeRateAcquiredAt = snapshotAt;
        target.Purchase.ExchangeRateSource = sourceLabel;
        target.Purchase.ExchangeRateInputType = "CSV";

        SetIfChanged(target.CurrentHolding.CurrentShares, unitsHeld, value => target.CurrentHolding.CurrentShares = value, "保有口数", changedFields);
        SetIfChanged(target.CurrentHolding.CurrentPrice, currentNav, value => target.CurrentHolding.CurrentPrice = value, "基準価額", changedFields);
        target.CurrentHolding.CurrentExchangeRate = 1m;
        target.CurrentHolding.ExchangeRateAcquiredAt = snapshotAt;
        target.CurrentHolding.ExchangeRateSource = sourceLabel;
        target.CurrentHolding.ExchangeRateInputType = "CSV";
        target.CurrentHolding.AnnualDividendPerShare = 0m;
        target.CurrentHolding.DividendStatus = string.Equals(source.DistributionMethod, "再投資", StringComparison.Ordinal)
            ? "再投資"
            : "配当なし";
        target.CurrentHolding.CurrentPriceAcquiredAt = navDate;
        target.CurrentHolding.CurrentPriceSource = target.MutualFund.NavSource;
        target.CurrentHolding.UpdatedAt = snapshotAt.Date;

        changedFields.Add("投資信託情報");
    }

    private static StockPosition CreatePosition(BrokerHoldingRecord source, DateTime acquiredAt)
    {
        if (source.IsMutualFund)
        {
            return CreateMutualFundPosition(source, acquiredAt);
        }

        var currency = NormalizeCurrency(source.Currency);
        var currentPrice = source.CurrentPrice > 0m
            ? source.CurrentPrice
            : source.MarketValue > 0m && source.Shares > 0m
            ? decimal.Round(source.MarketValue / source.Shares, 6)
            : 0m;
        var sourceLabel = string.IsNullOrWhiteSpace(source.Source) ? "証券会社データ" : source.Source.Trim();
        var snapshotAt = source.SnapshotDate == DateTime.MinValue ? acquiredAt : source.SnapshotDate;
        var purchaseExchangeRate = currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? 1m
            : source.PurchaseExchangeRate > 0m ? source.PurchaseExchangeRate : source.CurrentExchangeRate;
        var currentExchangeRate = currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? 1m
            : source.CurrentExchangeRate > 0m ? source.CurrentExchangeRate : purchaseExchangeRate;

        return new StockPosition
        {
            Stock = new Stock
            {
                Ticker = NormalizeTickerForDisplay(source.Ticker),
                Name = string.IsNullOrWhiteSpace(source.Name) ? NormalizeTickerForDisplay(source.Ticker) : source.Name.Trim(),
                Broker = source.Broker.Trim(),
                AccountType = NormalizeSourceAccountType(source),
                CustodyType = source.Account.Trim(),
                Currency = currency,
                Country = currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ? "日本" : "米国",
                Sector = source.Product,
                Market = source.Market,
                DataSource = sourceLabel
            },
            Purchase = new Purchase
            {
                PurchaseDate = snapshotAt.Date,
                Shares = source.Shares,
                UnitPrice = source.AverageAcquisitionPrice,
                ExchangeRate = purchaseExchangeRate > 0m ? purchaseExchangeRate : 1m,
                ExchangeRateAcquiredAt = snapshotAt,
                ExchangeRateSource = sourceLabel,
                ExchangeRateInputType = "CSV"
            },
            Split = new StockSplit
            {
                SplitDate = snapshotAt.Date,
                SplitRatio = 1m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = source.Shares,
                CurrentPrice = currentPrice,
                CurrentExchangeRate = currentExchangeRate > 0m ? currentExchangeRate : 1m,
                ExchangeRateAcquiredAt = snapshotAt,
                ExchangeRateSource = sourceLabel,
                ExchangeRateInputType = "CSV",
                CurrentPriceAcquiredAt = snapshotAt,
                CurrentPriceSource = sourceLabel,
                UpdatedAt = snapshotAt.Date
            }
        };
    }

    private static StockPosition CreateMutualFundPosition(BrokerHoldingRecord source, DateTime acquiredAt)
    {
        var sourceLabel = string.IsNullOrWhiteSpace(source.Source) ? "証券会社データ" : source.Source.Trim();
        var snapshotAt = source.SnapshotDate == DateTime.MinValue ? acquiredAt : source.SnapshotDate;
        var unitsHeld = source.UnitsHeld > 0m ? source.UnitsHeld : source.Shares;
        var unitBase = MutualFundCalculator.NormalizeUnitBase(source.UnitBase);
        var averageCostNav = source.AverageCostNav > 0m ? source.AverageCostNav : source.AverageAcquisitionPrice;
        var currentNav = source.CurrentNav > 0m ? source.CurrentNav : source.CurrentPrice;
        var marketValue = source.MarketValue > 0m ? source.MarketValue : source.MarketValueJpy;
        var unrealizedGainLoss = source.UnrealizedGainLossJpy != 0m
            ? source.UnrealizedGainLossJpy
            : marketValue - source.AcquisitionAmount;
        var navDate = source.NavDate == DateTime.MinValue ? snapshotAt : source.NavDate;
        var name = string.IsNullOrWhiteSpace(source.Name) ? source.FundName : source.Name.Trim();

        return new StockPosition
        {
            Stock = new Stock
            {
                AssetType = AssetTypes.MutualFund,
                Ticker = NormalizeTickerForDisplay(source.Ticker),
                Name = string.IsNullOrWhiteSpace(name) ? NormalizeTickerForDisplay(source.Ticker) : name,
                Broker = source.Broker.Trim(),
                AccountType = NormalizeSourceAccountType(source),
                CustodyType = source.Account.Trim(),
                Currency = "JPY",
                Country = "日本",
                Sector = string.IsNullOrWhiteSpace(source.Product) ? "投資信託" : source.Product,
                Market = source.Market,
                DataSource = sourceLabel
            },
            Purchase = new Purchase
            {
                PurchaseDate = snapshotAt.Date,
                Shares = unitsHeld,
                UnitPrice = averageCostNav,
                ExchangeRate = 1m,
                ExchangeRateAcquiredAt = snapshotAt,
                ExchangeRateSource = sourceLabel,
                ExchangeRateInputType = "CSV"
            },
            Split = new StockSplit
            {
                SplitDate = snapshotAt.Date,
                SplitRatio = 1m
            },
            CurrentHolding = new CurrentHolding
            {
                CurrentShares = unitsHeld,
                CurrentPrice = currentNav,
                CurrentExchangeRate = 1m,
                ExchangeRateAcquiredAt = snapshotAt,
                ExchangeRateSource = sourceLabel,
                ExchangeRateInputType = "CSV",
                DividendStatus = string.Equals(source.DistributionMethod, "再投資", StringComparison.Ordinal)
                    ? "再投資"
                    : "配当なし",
                CurrentPriceAcquiredAt = navDate,
                CurrentPriceSource = string.IsNullOrWhiteSpace(source.NavSource) ? sourceLabel : source.NavSource,
                UpdatedAt = snapshotAt.Date
            },
            MutualFund = new MutualFundHolding
            {
                FundName = string.IsNullOrWhiteSpace(source.FundName) ? name : source.FundName.Trim(),
                FundCode = source.FundCode,
                AssociationCode = source.AssociationCode,
                UnitsHeld = unitsHeld,
                UnitBase = unitBase,
                AverageCostNav = averageCostNav,
                CurrentNav = currentNav,
                AcquisitionAmount = source.AcquisitionAmount,
                MarketValue = marketValue,
                UnrealizedGainLoss = unrealizedGainLoss,
                NavDate = navDate,
                NavSource = string.IsNullOrWhiteSpace(source.NavSource) ? sourceLabel : source.NavSource,
                DistributionMethod = source.DistributionMethod,
                AccountType = source.Account,
                TotalPurchaseAmount = source.AcquisitionAmount
            }
        };
    }

    private static StockPosition CopyPosition(StockPosition source) =>
        new()
        {
            Stock = new Stock
            {
                Id = source.Stock.Id,
                AssetType = source.Stock.AssetType,
                CanonicalSecurityKey = source.Stock.CanonicalSecurityKey,
                Name = source.Stock.Name,
                Ticker = source.Stock.Ticker,
                Country = source.Stock.Country,
                Currency = source.Stock.Currency,
                Broker = source.Stock.Broker,
                AccountType = source.Stock.AccountType,
                CustodyType = source.Stock.CustodyType,
                Sector = source.Stock.Sector,
                Industry = source.Stock.Industry,
                Market = source.Stock.Market,
                DataSource = source.Stock.DataSource,
                Memo = source.Stock.Memo
            },
            Purchase = new Purchase
            {
                Id = source.Purchase.Id,
                StockId = source.Purchase.StockId,
                PurchaseDate = source.Purchase.PurchaseDate,
                Shares = source.Purchase.Shares,
                UnitPrice = source.Purchase.UnitPrice,
                ExchangeRate = source.Purchase.ExchangeRate,
                ExchangeRateAcquiredAt = source.Purchase.ExchangeRateAcquiredAt,
                ExchangeRateSource = source.Purchase.ExchangeRateSource,
                ExchangeRateInputType = source.Purchase.ExchangeRateInputType,
                Fee = source.Purchase.Fee,
                Memo = source.Purchase.Memo
            },
            Split = new StockSplit
            {
                Id = source.Split.Id,
                StockId = source.Split.StockId,
                SplitDate = source.Split.SplitDate,
                SplitRatio = source.Split.SplitRatio,
                Memo = source.Split.Memo
            },
            CurrentHolding = new CurrentHolding
            {
                Id = source.CurrentHolding.Id,
                StockId = source.CurrentHolding.StockId,
                CurrentShares = source.CurrentHolding.CurrentShares,
                CurrentPrice = source.CurrentHolding.CurrentPrice,
                CurrentExchangeRate = source.CurrentHolding.CurrentExchangeRate,
                ExchangeRateAcquiredAt = source.CurrentHolding.ExchangeRateAcquiredAt,
                ExchangeRateSource = source.CurrentHolding.ExchangeRateSource,
                ExchangeRateInputType = source.CurrentHolding.ExchangeRateInputType,
                AnnualDividendPerShare = source.CurrentHolding.AnnualDividendPerShare,
                DividendStatus = source.CurrentHolding.DividendStatus,
                DividendFrequency = source.CurrentHolding.DividendFrequency,
                DividendMonths = source.CurrentHolding.DividendMonths,
                CurrentPriceAcquiredAt = source.CurrentHolding.CurrentPriceAcquiredAt,
                CurrentPriceSource = source.CurrentHolding.CurrentPriceSource,
                DividendInfoAcquiredAt = source.CurrentHolding.DividendInfoAcquiredAt,
                DividendInfoSource = source.CurrentHolding.DividendInfoSource,
                UpdatedAt = source.CurrentHolding.UpdatedAt
            },
            MutualFund = new MutualFundHolding
            {
                Id = source.MutualFund.Id,
                StockId = source.MutualFund.StockId,
                FundName = source.MutualFund.FundName,
                FundCode = source.MutualFund.FundCode,
                AssociationCode = source.MutualFund.AssociationCode,
                UnitsHeld = source.MutualFund.UnitsHeld,
                UnitBase = source.MutualFund.UnitBase,
                AverageCostNav = source.MutualFund.AverageCostNav,
                CurrentNav = source.MutualFund.CurrentNav,
                AcquisitionAmount = source.MutualFund.AcquisitionAmount,
                MarketValue = source.MutualFund.MarketValue,
                UnrealizedGainLoss = source.MutualFund.UnrealizedGainLoss,
                NavDate = source.MutualFund.NavDate,
                NavSource = source.MutualFund.NavSource,
                DistributionMethod = source.MutualFund.DistributionMethod,
                AccountType = source.MutualFund.AccountType,
                TotalPurchaseAmount = source.MutualFund.TotalPurchaseAmount,
                TotalSaleAmount = source.MutualFund.TotalSaleAmount,
                ReinvestedDistributionAmount = source.MutualFund.ReinvestedDistributionAmount
            }
        };

    private static void SetIfChanged(string current, string value, Action<string> setValue, string fieldName, List<string> changedFields)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(current, value, StringComparison.Ordinal))
        {
            return;
        }

        setValue(value);
        changedFields.Add(fieldName);
    }

    private static void SetIfChanged(decimal current, decimal value, Action<decimal> setValue, string fieldName, List<string> changedFields)
    {
        if (current == value)
        {
            return;
        }

        setValue(value);
        changedFields.Add(fieldName);
    }

    private static bool IsSameBroker(string existingBroker, string sourceBroker)
    {
        return !string.IsNullOrWhiteSpace(existingBroker) &&
               !string.IsNullOrWhiteSpace(sourceBroker) &&
               string.Equals(
                   NormalizeText(SecuritySymbolNormalizer.NormalizeBroker(existingBroker)),
                   NormalizeText(SecuritySymbolNormalizer.NormalizeBroker(sourceBroker)),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameTicker(string existingTicker, string sourceTicker)
    {
        return !string.IsNullOrWhiteSpace(existingTicker) &&
               !string.IsNullOrWhiteSpace(sourceTicker) &&
               string.Equals(NormalizeTicker(existingTicker), NormalizeTicker(sourceTicker), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameAccountType(string existingAccountType, string sourceAccountType)
    {
        var existing = AccountTypeNormalizer.Normalize(existingAccountType);
        var source = AccountTypeNormalizer.Normalize(sourceAccountType);
        return source == AccountTypes.Unknown ||
               string.Equals(existing, source, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSourceAccountType(BrokerHoldingRecord source) =>
        source.IsMutualFund
            ? AccountTypeNormalizer.NormalizeForMutualFund(source.Account, source.Account)
            : AccountTypeNormalizer.Normalize(source.Account);

    private static bool IsSameCustodyType(string existingCustodyType, string sourceCustodyType)
    {
        if (string.IsNullOrWhiteSpace(sourceCustodyType))
        {
            return true;
        }

        var existing = PositionIdentityService.NormalizeCustodyType(existingCustodyType, sourceCustodyType);
        var source = PositionIdentityService.NormalizeCustodyType(sourceCustodyType, sourceCustodyType);
        return string.Equals(existing, source, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTicker(string ticker)
    {
        return SecuritySymbolNormalizer.NormalizeTicker(ticker);
    }

    private static string NormalizeTickerForDisplay(string ticker) => NormalizeTicker(ticker);

    private static string NormalizeCurrency(string currency)
    {
        var normalized = currency.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "JPY" : normalized;
    }

    private static string NormalizeText(string value) =>
        value.Trim().Replace(" ", string.Empty).Replace("　", string.Empty);
}
