namespace InvestmentStory.Core.Models;

public enum DividendScheduleStatus
{
    Paid,
    Expected,
    Estimated,
    MissedEligibility,
    NotAvailable,
    OverdueUnmatched
}

public static class DividendScheduleStatusResolver
{
    public static DividendScheduleStatus FromPlan(string eligibilityStatus, string dataQuality)
    {
        if (string.Equals(dataQuality, DividendPlanDataQuality.Missing, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eligibilityStatus, DividendPlanEligibility.Missing, StringComparison.OrdinalIgnoreCase))
        {
            return DividendScheduleStatus.NotAvailable;
        }

        if (string.Equals(eligibilityStatus, DividendPlanEligibility.Ineligible, StringComparison.OrdinalIgnoreCase))
        {
            return DividendScheduleStatus.MissedEligibility;
        }

        if (string.Equals(dataQuality, DividendPlanDataQuality.Estimated, StringComparison.OrdinalIgnoreCase)
            || string.Equals(eligibilityStatus, DividendPlanEligibility.Estimated, StringComparison.OrdinalIgnoreCase))
        {
            return DividendScheduleStatus.Estimated;
        }

        return DividendScheduleStatus.Expected;
    }
}
