namespace InvestmentStory.Core.Models;

public sealed record FundConstituent(string Ticker, string Name, decimal WeightPercent);

public sealed record FundDisclosure(
    string Id, string FundTicker, DateTime PublishedDate, string Source,
    bool UsageRightsConfirmed, DateTime RecordedAtUtc,
    IReadOnlyList<FundConstituent> Constituents)
{
    public string UsageRightsNote { get; init; } = string.Empty;
    public bool IsFundConfirmed { get; init; }
    public decimal CoveragePercent => Constituents.Sum(x => x.WeightPercent);
}

public sealed record FundExposureRow(string Ticker, string Name,
    decimal DirectJpy, decimal IndirectJpy, bool IsUndisclosed)
{
    public decimal TotalJpy => DirectJpy + IndirectJpy;
}

public sealed record FundLookThroughResult(
    decimal? PortfolioValueJpy, decimal KnownValueJpy,
    IReadOnlyList<FundExposureRow> Rows, IReadOnlyList<string> Notes);
