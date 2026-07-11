using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class MutualFundCalculationResult
{
    public decimal AcquisitionAmount { get; init; }
    public decimal MarketValue { get; init; }
    public decimal UnrealizedGainLoss { get; init; }
    public decimal UnrealizedGainLossRate { get; init; }
    public decimal UnitBase { get; init; }
    public decimal AverageCostNav { get; init; }
    public decimal CurrentNav { get; init; }
}

public sealed class MutualFundCalculator
{
    public MutualFundCalculationResult Calculate(MutualFundHolding fund)
    {
        ArgumentNullException.ThrowIfNull(fund);

        var unitBase = NormalizeUnitBase(fund.UnitBase);
        var formulaAcquisitionAmount = fund.UnitsHeld / unitBase * fund.AverageCostNav;
        var formulaMarketValue = fund.UnitsHeld / unitBase * fund.CurrentNav;
        var acquisitionAmount = fund.AcquisitionAmount > 0m ? fund.AcquisitionAmount : formulaAcquisitionAmount;
        var marketValue = fund.MarketValue > 0m ? fund.MarketValue : formulaMarketValue;
        var unrealizedGainLoss = fund.UnrealizedGainLoss != 0m
            ? fund.UnrealizedGainLoss
            : marketValue - acquisitionAmount;

        return new MutualFundCalculationResult
        {
            AcquisitionAmount = acquisitionAmount,
            MarketValue = marketValue,
            UnrealizedGainLoss = unrealizedGainLoss,
            UnrealizedGainLossRate = acquisitionAmount == 0m ? 0m : unrealizedGainLoss / acquisitionAmount * 100m,
            UnitBase = unitBase,
            AverageCostNav = fund.AverageCostNav,
            CurrentNav = fund.CurrentNav
        };
    }

    public static decimal NormalizeUnitBase(decimal unitBase) => unitBase <= 0m ? 10000m : unitBase;
}
