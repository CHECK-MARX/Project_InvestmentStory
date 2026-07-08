using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class DividendTaxCalculator
{
    public DividendTaxCalculation Calculate(DividendTaxInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var exchangeRate = IsJpy(input.Currency) ? 1m : input.ExchangeRate <= 0m ? 1m : input.ExchangeRate;
        var grossAmount = input.Quantity * input.DividendPerShare;
        var foreignRate = input.TaxProfile.IsForeignTaxExempt
            ? 0m
            : input.TaxProfile.ForeignWithholdingTaxRate / 100m;
        var domesticRate = input.TaxProfile.IsDomesticTaxExempt
            ? 0m
            : input.TaxProfile.TotalDomesticTaxRate / 100m;

        if (IsJpy(input.Currency))
        {
            foreignRate = 0m;
        }

        var foreignTaxAmount = grossAmount * foreignRate;
        var amountAfterForeignTax = grossAmount - foreignTaxAmount;
        var domesticTaxAmount = amountAfterForeignTax * domesticRate;
        var totalTaxAmount = foreignTaxAmount + domesticTaxAmount;
        var netAmount = grossAmount - totalTaxAmount;

        return new DividendTaxCalculation
        {
            GrossAmount = grossAmount,
            ForeignTaxAmount = foreignTaxAmount,
            DomesticTaxAmount = domesticTaxAmount,
            TotalTaxAmount = totalTaxAmount,
            NetAmount = netAmount,
            GrossAmountJpy = grossAmount * exchangeRate,
            ForeignTaxAmountJpy = foreignTaxAmount * exchangeRate,
            DomesticTaxAmountJpy = domesticTaxAmount * exchangeRate,
            TotalTaxAmountJpy = totalTaxAmount * exchangeRate,
            NetAmountJpy = netAmount * exchangeRate
        };
    }

    private static bool IsJpy(string currency) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase) ||
        currency.Equals("YEN", StringComparison.OrdinalIgnoreCase);
}
