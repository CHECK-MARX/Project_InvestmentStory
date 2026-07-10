using System.Globalization;

namespace InvestmentStory.App.Infrastructure;

public static class Formatters
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ja-JP");

    public static string Money(decimal value, string currency)
    {
        if (currency.Equals("JPY", StringComparison.OrdinalIgnoreCase))
        {
            return Jpy(value);
        }

        if (currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
        {
            return $"${value.ToString("N2", Culture)}";
        }

        return $"{value.ToString("N2", Culture)} {currency}";
    }

    public static string SignedMoney(decimal value, string currency) =>
        currency.Equals("JPY", StringComparison.OrdinalIgnoreCase)
            ? SignedJpy(value)
            : currency.Equals("USD", StringComparison.OrdinalIgnoreCase)
                ? value.ToString("+$#,0.00;-$#,0.00;$0.00", Culture)
                : $"{value.ToString("+#,0.00;-#,0.00;0.00", Culture)} {currency}";

    public static string Jpy(decimal value) => $"{value.ToString("N0", Culture)}円";

    public static string SignedJpy(decimal value) => $"{value.ToString("+#,0;-#,0;0", Culture)}円";

    public static string Percent(decimal value) => $"{value.ToString("N2", Culture)}%";

    public static string SignedPercent(decimal value) => $"{value.ToString("+#,0.00;-#,0.00;0.00", Culture)}%";

    public static string Number(decimal value) => value % 1m == 0m
        ? value.ToString("N0", Culture)
        : value.ToString("N2", Culture);
}
