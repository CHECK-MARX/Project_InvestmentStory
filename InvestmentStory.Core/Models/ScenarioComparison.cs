namespace InvestmentStory.Core.Models;

public sealed record ScenarioInputs(
    string Name, decimal MonthlyContributionJpy, decimal AnnualReturnPercent,
    decimal DividendReductionPercent, decimal UsdJpyChange, int Years);

public sealed record ScenarioBaseline(
    DateTime AsOf, decimal KnownPortfolioValueJpy, int UnknownValuePositionCount,
    decimal UsdPortfolioValueJpy, decimal GrossAnnualDividendForecastJpy,
    decimal UsdAnnualDividendForecast, int ActivePositionCount);

public sealed record ScenarioOutcome(
    decimal FxAdjustedStartingValueJpy, decimal ProjectedPortfolioValueJpy,
    decimal GrossAnnualDividendForecastJpy, decimal ReferenceProjectedValueJpy,
    decimal DifferenceFromReferenceJpy);

public sealed record SavedScenario(
    string Id, DateTime RecordedAtUtc, ScenarioInputs Inputs,
    ScenarioBaseline Baseline, ScenarioOutcome Outcome);
