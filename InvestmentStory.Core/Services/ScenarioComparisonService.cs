using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class ScenarioComparisonService
{
    public ScenarioBaseline CaptureBaseline(IReadOnlyList<StockSnapshot> snapshots, DateTime asOf)
    {
        var active = snapshots.Where(x => PositionEvidenceService.IsActiveHolding(x.Position)).ToArray();
        return new ScenarioBaseline(asOf.Date,
            active.Where(x => x.HasKnownCurrentMarketValueJpy).Sum(x => x.CurrentMarketValueJpy),
            active.Count(x => !x.HasKnownCurrentMarketValueJpy),
            active.Where(x => x.HasKnownCurrentMarketValueJpy &&
                x.Position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.CurrentMarketValueJpy),
            active.Sum(x => x.AnnualDividendForecastJpy),
            active.Where(x => x.Position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.AnnualDividendForecast), active.Length);
    }

    public ScenarioOutcome Calculate(ScenarioBaseline baseline, ScenarioInputs inputs,
        IReadOnlyList<StockSnapshot> snapshots)
    {
        Validate(inputs);
        if (baseline.ActivePositionCount == 0 || baseline.UnknownValuePositionCount > 0)
            throw new InvalidOperationException("評価額未確認の保有があるため、全保有を基準にしたシナリオは算出できません。");

        var usd = snapshots.Where(x => PositionEvidenceService.IsActiveHolding(x.Position) &&
            x.Position.Stock.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (inputs.UsdJpyChange != 0m && usd.Any(x => x.Position.CurrentHolding.CurrentExchangeRate <= 0m))
            throw new InvalidOperationException("USD保有の現在為替レートが未確認のため、為替変化を算出できません。");
        if (usd.Any(x => x.Position.CurrentHolding.CurrentExchangeRate + inputs.UsdJpyChange <= 0m))
            throw new ArgumentException("為替変化後のUSD/JPYが0円以下になります。");

        var fxImpact = inputs.UsdJpyChange == 0m ? 0m :
            usd.Sum(x => x.CurrentMarketValueJpy * inputs.UsdJpyChange /
                x.Position.CurrentHolding.CurrentExchangeRate);
        var fxStart = baseline.KnownPortfolioValueJpy + fxImpact;
        var dividend = (baseline.GrossAnnualDividendForecastJpy +
            baseline.UsdAnnualDividendForecast * inputs.UsdJpyChange) *
            (100m - inputs.DividendReductionPercent) / 100m;
        var projected = Project(fxStart, inputs.MonthlyContributionJpy,
            inputs.AnnualReturnPercent, inputs.Years);
        var reference = Project(baseline.KnownPortfolioValueJpy, 0m,
            inputs.AnnualReturnPercent, inputs.Years);
        return new ScenarioOutcome(fxStart, projected, dividend, reference, projected - reference);
    }

    public static void Validate(ScenarioInputs inputs)
    {
        if (string.IsNullOrWhiteSpace(inputs.Name) || inputs.Name.Length > 80 ||
            inputs.MonthlyContributionJpy < 0m || inputs.MonthlyContributionJpy > 100_000_000m ||
            inputs.AnnualReturnPercent < -50m || inputs.AnnualReturnPercent > 50m ||
            inputs.DividendReductionPercent < 0m || inputs.DividendReductionPercent > 100m ||
            inputs.UsdJpyChange < -1000m || inputs.UsdJpyChange > 1000m ||
            inputs.Years < 1 || inputs.Years > 50)
            throw new ArgumentException("名称、毎月拠出額、年率、配当減少率、USD/JPY変化、年数を有効な範囲で入力してください。");
    }

    private static decimal Project(decimal start, decimal contribution, decimal annualReturnPercent, int years)
    {
        var value = start;
        var monthlyRate = annualReturnPercent / 1200m;
        for (var month = 0; month < years * 12; month++)
            value = value * (1m + monthlyRate) + contribution;
        return decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    }
}
