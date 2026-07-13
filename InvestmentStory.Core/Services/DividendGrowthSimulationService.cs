using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DividendGrowthSimulationService
{
    private const decimal DomesticTaxRate = 20.315m;
    private const decimal UsForeignTaxRate = 10m;

    private readonly DividendTaxCalculator _taxCalculator = new();

    public IReadOnlyList<DividendGrowthPlanItem> CreateDefaultPlanItems(
        IEnumerable<StockPosition> positions,
        string displayMode)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var items = positions
            .Where(position => !position.IsMutualFund && position.CurrentHolding.CurrentShares > 0m)
            .Select(CreatePlanItem)
            .Where(item => !string.IsNullOrWhiteSpace(item.PlanKey))
            .Where(IsCurrentDividendItem)
            .ToList();

        if (!string.Equals(displayMode, DividendGrowthDisplayModes.AggregateBySecurity, StringComparison.OrdinalIgnoreCase))
        {
            return items
                .OrderBy(item => item.Ticker, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Broker, StringComparer.CurrentCulture)
                .ThenBy(item => NormalizeAccountType(item.AccountType), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return items
            .GroupBy(item => item.CanonicalKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => AggregatePlanItems(group.ToList()))
            .OrderBy(item => item.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public DividendGrowthSimulationResult Simulate(
        DividendGrowthSimulationInput input,
        IReadOnlyList<TaxProfile> taxProfiles)
    {
        ArgumentNullException.ThrowIfNull(input);
        taxProfiles ??= Array.Empty<TaxProfile>();

        var holdings = input.PlanItems
            .Where(item => !item.IsNewStock)
            .Select(item => BuildHolding(item, taxProfiles))
            .ToList();
        var newStocks = input.PlanItems
            .Where(item => item.IsNewStock)
            .Select(item => BuildHolding(item, taxProfiles))
            .ToList();
        var allRows = holdings.Concat(newStocks).ToList();
        var target = Math.Max(0m, input.TargetAnnualDividendJpy);
        var plannedInvestment = allRows.Sum(row => row.PlannedPurchaseAmountJpy);
        var postGross = allRows.Sum(row => row.PostAddAnnualDividendJpy);
        var postNet = allRows.Sum(row => row.PostAddNetAnnualDividendJpy);
        var currentGross = holdings.Sum(row => row.CurrentAnnualDividendJpy);
        var currentNet = holdings.Sum(row => row.CurrentNetAnnualDividendJpy);
        var increase = postGross - currentGross;
        var netIncrease = postNet - currentNet;
        var projections = BuildProjections(input, input.PlanItems, taxProfiles, target);
        var targetProjection = projections.FirstOrDefault(row => target > 0m && row.PlannedNetDividendJpy >= target);

        return new DividendGrowthSimulationResult
        {
            Summary = new DividendGrowthSimulationSummary
            {
                CurrentGrossAnnualDividendJpy = RoundYen(currentGross),
                CurrentNetAnnualDividendJpy = RoundYen(currentNet),
                ExistingPlannedInvestmentJpy = RoundYen(holdings.Sum(row => row.PlannedPurchaseAmountJpy)),
                NewPlannedInvestmentJpy = RoundYen(newStocks.Sum(row => row.PlannedPurchaseAmountJpy)),
                TotalPlannedInvestmentJpy = RoundYen(plannedInvestment),
                PostAddGrossAnnualDividendJpy = RoundYen(postGross),
                PostAddNetAnnualDividendJpy = RoundYen(postNet),
                AnnualDividendIncreaseJpy = RoundYen(increase),
                NetAnnualDividendIncreaseJpy = RoundYen(netIncrease),
                ForeignTaxJpy = RoundYen(allRows.Sum(row => row.ForeignTaxJpy)),
                DomesticTaxJpy = RoundYen(allRows.Sum(row => row.DomesticTaxJpy)),
                TotalTaxJpy = RoundYen(allRows.Sum(row => row.TotalTaxJpy)),
                MonthlyNetDividendJpy = RoundYen(postNet / 12m),
                InvestmentDividendYieldRate = plannedInvestment <= 0m ? 0m : increase / plannedInvestment * 100m,
                TargetAnnualDividendJpy = RoundYen(target),
                TargetAchievementRate = target <= 0m ? 0m : postNet / target * 100m,
                TargetGapJpy = RoundYen(Math.Max(0m, target - postNet)),
                TargetAchievementYear = targetProjection?.Year
            },
            Holdings = holdings,
            NewStocks = newStocks,
            Projections = projections
        };
    }

    private static DividendGrowthPlanItem CreatePlanItem(StockPosition position)
    {
        var stock = position.Stock;
        var holding = position.CurrentHolding;
        var canonicalKey = FirstNonBlank(
            stock.CanonicalSecurityKey,
            SecuritySymbolNormalizer.NormalizeTicker(stock.Ticker),
            stock.Ticker,
            stock.Name);
        var positionKey = PositionIdentityService.BuildKey(position);
        var accountType = FirstKnownAccountType(stock.AccountType, stock.CustodyType);
        var exchangeRate = NormalizeCurrency(stock.Currency) == "JPY"
            ? 1m
            : holding.CurrentExchangeRate <= 0m ? position.Purchase.ExchangeRate : holding.CurrentExchangeRate;

        return new DividendGrowthPlanItem
        {
            PlanKey = positionKey,
            CanonicalKey = canonicalKey,
            PositionKey = positionKey,
            Ticker = stock.Ticker,
            Name = stock.Name,
            Broker = stock.Broker,
            AccountType = accountType,
            Country = stock.Country,
            Currency = NormalizeCurrency(stock.Currency),
            CurrentShares = holding.CurrentShares,
            CurrentPrice = holding.CurrentPrice,
            ExchangeRate = exchangeRate <= 0m ? 1m : exchangeRate,
            AnnualDividendPerShare = Math.Max(0m, holding.AnnualDividendPerShare),
            AnnualDividendSource = ResolveDividendSource(holding),
            PlannedAdditionalShares = 0m,
            PlannedBroker = stock.Broker,
            PlannedAccountType = accountType,
            AnnualDividendGrowthRate = 0m,
            PurchaseMode = DividendGrowthPurchaseModes.OneTime
        };
    }

    private static DividendGrowthPlanItem AggregatePlanItems(IReadOnlyList<DividendGrowthPlanItem> items)
    {
        var first = items.First();
        var brokers = DistinctNonBlank(items.Select(item => item.Broker)).ToList();
        var accounts = items.Select(item => NormalizeAccountType(item.AccountType)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var plannedAccounts = items.Select(item => NormalizeAccountType(item.PlannedAccountType)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return new DividendGrowthPlanItem
        {
            PlanKey = first.CanonicalKey,
            CanonicalKey = first.CanonicalKey,
            PositionKey = first.CanonicalKey,
            Ticker = first.Ticker,
            Name = first.Name,
            Broker = brokers.Count <= 1 ? first.Broker : "複数",
            AccountType = accounts.Count <= 1 ? accounts[0] : "Multiple",
            Country = first.Country,
            Currency = first.Currency,
            CurrentShares = items.Sum(item => item.CurrentShares),
            CurrentPrice = FirstPositive(items.Select(item => item.CurrentPrice)),
            ExchangeRate = FirstPositive(items.Select(item => item.ExchangeRate), 1m),
            AnnualDividendPerShare = FirstPositive(items.Select(item => item.AnnualDividendPerShare)),
            AnnualDividendSource = items.Any(item => item.AnnualDividendPerShare > 0m)
                ? FirstNonBlank(items.Select(item => item.AnnualDividendSource).ToArray())
                : "配当情報未取得",
            PlannedAdditionalShares = items.Sum(item => item.PlannedAdditionalShares),
            PlannedBroker = brokers.Count <= 1 ? first.PlannedBroker : "複数",
            PlannedAccountType = plannedAccounts.Count <= 1 ? plannedAccounts[0] : first.PlannedAccountType,
            AnnualDividendGrowthRate = items.Count == 0 ? 0m : items.Average(item => item.AnnualDividendGrowthRate),
            PurchaseMode = first.PurchaseMode,
            IsNewStock = false,
            Components = items
        };
    }

    private DividendGrowthSimulationHolding BuildHolding(
        DividendGrowthPlanItem item,
        IReadOnlyList<TaxProfile> taxProfiles)
    {
        var currency = NormalizeCurrency(item.Currency);
        var exchangeRate = IsJpy(currency) ? 1m : item.ExchangeRate <= 0m ? 1m : item.ExchangeRate;
        var currentShares = Math.Max(0m, item.CurrentShares);
        var plannedShares = Math.Max(0m, item.PlannedAdditionalShares);
        var postShares = currentShares + plannedShares;
        var price = Math.Max(0m, item.CurrentPrice);
        var dividendPerShare = Math.Max(0m, item.AnnualDividendPerShare);
        var plannedAmount = plannedShares * price;
        var plannedAmountJpy = plannedAmount * exchangeRate;
        var currentDividend = currentShares * dividendPerShare;
        var postDividend = postShares * dividendPerShare;
        var currentDividendJpy = currentDividend * exchangeRate;
        var postDividendJpy = postDividend * exchangeRate;
        var currentTax = CalculateCurrentTax(item, dividendPerShare, exchangeRate, taxProfiles);
        var plannedTax = CalculateTax(item, plannedShares, dividendPerShare, exchangeRate, taxProfiles);
        var postTax = AddTax(currentTax, plannedTax);

        if (item.Components.Count > 0)
        {
            currentDividendJpy = item.Components.Sum(component =>
            {
                var componentCurrency = NormalizeCurrency(component.Currency);
                var componentRate = IsJpy(componentCurrency) ? 1m : component.ExchangeRate <= 0m ? 1m : component.ExchangeRate;
                return Math.Max(0m, component.CurrentShares) * Math.Max(0m, component.AnnualDividendPerShare) * componentRate;
            });
            postDividendJpy = currentDividendJpy + plannedShares * dividendPerShare * exchangeRate;
        }

        return new DividendGrowthSimulationHolding
        {
            PlanKey = item.PlanKey,
            CanonicalKey = item.CanonicalKey,
            PositionKey = item.PositionKey,
            Ticker = item.Ticker,
            Name = item.Name,
            Broker = item.Broker,
            AccountType = NormalizeAccountType(item.AccountType),
            Country = item.Country,
            Currency = currency,
            CurrentShares = currentShares,
            CurrentPrice = price,
            ExchangeRate = exchangeRate,
            AnnualDividendPerShare = dividendPerShare,
            AnnualDividendSource = dividendPerShare > 0m ? FirstNonBlank(item.AnnualDividendSource, "手入力/API") : "配当情報未取得",
            CurrentAnnualDividend = RoundMoney(currentDividend),
            CurrentAnnualDividendJpy = RoundYen(currentDividendJpy),
            CurrentNetAnnualDividendJpy = RoundYen(currentTax.NetAmountJpy),
            PlannedAdditionalShares = plannedShares,
            PlannedPurchaseAmount = RoundMoney(plannedAmount),
            PlannedPurchaseAmountJpy = RoundYen(plannedAmountJpy),
            PlannedBroker = item.PlannedBroker,
            PlannedAccountType = NormalizeAccountType(item.PlannedAccountType),
            AnnualDividendGrowthRate = item.AnnualDividendGrowthRate,
            PurchaseMode = item.PurchaseMode,
            PostAddShares = postShares,
            PostAddAnnualDividend = RoundMoney(postDividend),
            PostAddAnnualDividendJpy = RoundYen(postDividendJpy),
            PostAddNetAnnualDividendJpy = RoundYen(postTax.NetAmountJpy),
            DividendIncreaseJpy = RoundYen(postDividendJpy - currentDividendJpy),
            NetDividendIncreaseJpy = RoundYen(postTax.NetAmountJpy - currentTax.NetAmountJpy),
            CurrentYieldRate = price <= 0m ? 0m : dividendPerShare / price * 100m,
            InvestmentDividendYieldRate = plannedAmountJpy <= 0m ? 0m : (postDividendJpy - currentDividendJpy) / plannedAmountJpy * 100m,
            ForeignTaxJpy = RoundYen(postTax.ForeignTaxAmountJpy),
            DomesticTaxJpy = RoundYen(postTax.DomesticTaxAmountJpy),
            TotalTaxJpy = RoundYen(postTax.TotalTaxAmountJpy),
            IsNewStock = item.IsNewStock,
            Warning = BuildWarning(item, plannedShares)
        };
    }

    private IReadOnlyList<DividendGrowthProjection> BuildProjections(
        DividendGrowthSimulationInput input,
        IReadOnlyList<DividendGrowthPlanItem> items,
        IReadOnlyList<TaxProfile> taxProfiles,
        decimal targetAnnualDividendJpy)
    {
        var years = Math.Clamp(input.ProjectionYears, 1, 80);
        var startYear = input.StartYear <= 0 ? DateTime.Today.Year : input.StartYear;
        var rows = new List<DividendGrowthProjection>(years);
        for (var index = 0; index < years; index++)
        {
            var currentGross = 0m;
            var currentNet = 0m;
            var plannedGross = 0m;
            var plannedNet = 0m;
            foreach (var item in items)
            {
                var currency = NormalizeCurrency(item.Currency);
                var exchangeRate = IsJpy(currency) ? 1m : item.ExchangeRate <= 0m ? 1m : item.ExchangeRate;
                var dividendPerShare = GrowDividend(Math.Max(0m, item.AnnualDividendPerShare), item.AnnualDividendGrowthRate, index);
                var currentShares = item.IsNewStock ? 0m : Math.Max(0m, item.CurrentShares);
                var postShares = currentShares + Math.Max(0m, item.PlannedAdditionalShares);
                var currentTax = CalculateCurrentTax(item, dividendPerShare, exchangeRate, taxProfiles, index);
                var plannedTax = CalculateTax(item, Math.Max(0m, item.PlannedAdditionalShares), dividendPerShare, exchangeRate, taxProfiles);
                var postTax = AddTax(currentTax, plannedTax);
                var currentGrossJpy = item.Components.Count > 0
                    ? item.Components.Sum(component =>
                    {
                        var componentCurrency = NormalizeCurrency(component.Currency);
                        var componentRate = IsJpy(componentCurrency) ? 1m : component.ExchangeRate <= 0m ? 1m : component.ExchangeRate;
                        var componentDividend = GrowDividend(Math.Max(0m, component.AnnualDividendPerShare), item.AnnualDividendGrowthRate, index);
                        return Math.Max(0m, component.CurrentShares) * componentDividend * componentRate;
                    })
                    : currentShares * dividendPerShare * exchangeRate;

                currentGross += currentGrossJpy;
                currentNet += currentTax.NetAmountJpy;
                plannedGross += postShares * dividendPerShare * exchangeRate;
                if (item.Components.Count > 0)
                {
                    plannedGross += currentGrossJpy - (currentShares * dividendPerShare * exchangeRate);
                }

                plannedNet += postTax.NetAmountJpy;
            }

            rows.Add(new DividendGrowthProjection
            {
                Year = startYear + index,
                YearsFromNow = index,
                CurrentOnlyGrossDividendJpy = RoundYen(currentGross),
                CurrentOnlyNetDividendJpy = RoundYen(currentNet),
                PlannedGrossDividendJpy = RoundYen(plannedGross),
                PlannedNetDividendJpy = RoundYen(plannedNet),
                MonthlyAverageNetDividendJpy = RoundYen(plannedNet / 12m),
                TargetAnnualDividendJpy = RoundYen(targetAnnualDividendJpy),
                TargetAchievementRate = targetAnnualDividendJpy <= 0m ? 0m : plannedNet / targetAnnualDividendJpy * 100m
            });
        }

        return rows;
    }

    private DividendTaxCalculation CalculateCurrentTax(
        DividendGrowthPlanItem item,
        decimal dividendPerShare,
        decimal exchangeRate,
        IReadOnlyList<TaxProfile> taxProfiles,
        int yearIndex = 0)
    {
        if (item.Components.Count == 0)
        {
            return CalculateTax(item, Math.Max(0m, item.CurrentShares), dividendPerShare, exchangeRate, taxProfiles);
        }

        var aggregate = new DividendTaxCalculation();
        foreach (var component in item.Components)
        {
            var componentCurrency = NormalizeCurrency(component.Currency);
            var componentRate = IsJpy(componentCurrency) ? 1m : component.ExchangeRate <= 0m ? 1m : component.ExchangeRate;
            var componentDividend = GrowDividend(Math.Max(0m, component.AnnualDividendPerShare), item.AnnualDividendGrowthRate, yearIndex);
            aggregate = AddTax(aggregate, CalculateTax(component, Math.Max(0m, component.CurrentShares), componentDividend, componentRate, taxProfiles));
        }

        return aggregate;
    }

    private static DividendTaxCalculation AddTax(DividendTaxCalculation left, DividendTaxCalculation right) =>
        new()
        {
            GrossAmount = left.GrossAmount + right.GrossAmount,
            ForeignTaxAmount = left.ForeignTaxAmount + right.ForeignTaxAmount,
            DomesticTaxAmount = left.DomesticTaxAmount + right.DomesticTaxAmount,
            TotalTaxAmount = left.TotalTaxAmount + right.TotalTaxAmount,
            NetAmount = left.NetAmount + right.NetAmount,
            GrossAmountJpy = left.GrossAmountJpy + right.GrossAmountJpy,
            ForeignTaxAmountJpy = left.ForeignTaxAmountJpy + right.ForeignTaxAmountJpy,
            DomesticTaxAmountJpy = left.DomesticTaxAmountJpy + right.DomesticTaxAmountJpy,
            TotalTaxAmountJpy = left.TotalTaxAmountJpy + right.TotalTaxAmountJpy,
            NetAmountJpy = left.NetAmountJpy + right.NetAmountJpy
        };

    private DividendTaxCalculation CalculateTax(
        DividendGrowthPlanItem item,
        decimal quantity,
        decimal dividendPerShare,
        decimal exchangeRate,
        IReadOnlyList<TaxProfile> taxProfiles)
    {
        return _taxCalculator.Calculate(new DividendTaxInput
        {
            Quantity = Math.Max(0m, quantity),
            DividendPerShare = Math.Max(0m, dividendPerShare),
            Currency = NormalizeCurrency(item.Currency),
            ExchangeRate = exchangeRate,
            TaxProfile = ResolveTaxProfile(item, taxProfiles)
        });
    }

    private static TaxProfile ResolveTaxProfile(DividendGrowthPlanItem item, IReadOnlyList<TaxProfile> taxProfiles)
    {
        var currency = NormalizeCurrency(item.Currency);
        var plannedAccount = NormalizeAccountType(item.PlannedAccountType);
        var currentAccount = NormalizeAccountType(item.AccountType);
        var account = plannedAccount != AccountTypes.Unknown ? plannedAccount : currentAccount;
        var profile = taxProfiles
            .FirstOrDefault(profile =>
                string.Equals(NormalizeAccountType(profile.AccountType), account, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeCurrency(profile.Currency), currency, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(profile.AssetType) ||
                 string.Equals(profile.AssetType, AssetTypes.Stock, StringComparison.OrdinalIgnoreCase)));
        if (profile is not null)
        {
            return profile;
        }

        var isNisa = AccountTypes.IsNisa(account);
        var isJpy = IsJpy(currency);
        return new TaxProfile
        {
            AccountType = account,
            Currency = currency,
            AssetType = AssetTypes.Stock,
            ForeignWithholdingTaxRate = isJpy ? 0m : UsForeignTaxRate,
            TotalDomesticTaxRate = isNisa ? 0m : DomesticTaxRate,
            IsDomesticTaxExempt = isNisa,
            IsForeignTaxExempt = isJpy
        };
    }

    private static string BuildWarning(DividendGrowthPlanItem item, decimal plannedShares)
    {
        if (plannedShares <= 0m)
        {
            return string.Empty;
        }

        var currency = NormalizeCurrency(item.Currency);
        var lot = IsJpy(currency) ? 100m : 1m;
        if (lot <= 1m)
        {
            return string.Empty;
        }

        return plannedShares % lot == 0m
            ? string.Empty
            : $"購入予定株数は{lot:N0}株単位ではありません";
    }

    private static decimal GrowDividend(decimal dividendPerShare, decimal annualGrowthRate, int yearIndex)
    {
        if (dividendPerShare <= 0m)
        {
            return 0m;
        }

        var rate = Math.Max(-99m, annualGrowthRate) / 100m;
        return dividendPerShare * (decimal)Math.Pow((double)(1m + rate), yearIndex);
    }

    private static string ResolveDividendSource(CurrentHolding holding)
    {
        if (holding.AnnualDividendPerShare <= 0m)
        {
            return "配当情報未取得";
        }

        return FirstNonBlank(
            holding.DividendInfoSource,
            holding.DividendStatus,
            "手入力/API");
    }

    private static bool IsCurrentDividendItem(DividendGrowthPlanItem item) =>
        item.CurrentShares > 0m &&
        item.AnnualDividendPerShare > 0m &&
        item.CurrentShares * item.AnnualDividendPerShare > 0m;

    private static string FirstKnownAccountType(params string[] values)
    {
        foreach (var value in values)
        {
            var normalized = NormalizeAccountType(value);
            if (normalized != AccountTypes.Unknown)
            {
                return normalized;
            }
        }

        return AccountTypes.Unknown;
    }

    private static string NormalizeAccountType(string accountType) =>
        string.Equals(accountType, "Multiple", StringComparison.OrdinalIgnoreCase)
            ? "Multiple"
            : AccountTypeNormalizer.Normalize(accountType);

    private static string NormalizeCurrency(string currency) =>
        string.IsNullOrWhiteSpace(currency) ? "JPY" : currency.Trim().ToUpperInvariant() is "YEN" ? "JPY" : currency.Trim().ToUpperInvariant();

    private static bool IsJpy(string currency) =>
        string.Equals(NormalizeCurrency(currency), "JPY", StringComparison.OrdinalIgnoreCase);

    private static decimal FirstPositive(IEnumerable<decimal> values, decimal fallback = 0m) =>
        values.FirstOrDefault(value => value > 0m) is var first && first > 0m ? first : fallback;

    private static IEnumerable<string> DistinctNonBlank(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCulture);

    private static string FirstNonBlank(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static decimal RoundYen(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static decimal RoundMoney(decimal value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
