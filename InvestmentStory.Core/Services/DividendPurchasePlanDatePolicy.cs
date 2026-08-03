namespace InvestmentStory.Core.Services;

public static class DividendPurchasePlanDatePolicy
{
    public const int PlanningHorizonYears = 20;

    public static bool IsSupportedYear(int year, DateTime today) =>
        year >= today.Year && year <= today.Year + PlanningHorizonYears;

    public static int NormalizeYear(int year, DateTime today) =>
        IsSupportedYear(year, today) ? year : today.Year;

    public static DateTime NormalizePurchaseDate(DateTime value, int targetYear, DateTime today)
    {
        var year = NormalizeYear(targetYear, today);
        var source = value == default ? today : value.Date;
        var day = Math.Min(source.Day, DateTime.DaysInMonth(year, source.Month));
        return new DateTime(year, source.Month, day);
    }
}
