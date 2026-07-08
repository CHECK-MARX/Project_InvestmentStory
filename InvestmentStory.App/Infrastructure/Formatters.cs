using System.Globalization;

namespace InvestmentStory.App.Infrastructure;

public static class Formatters
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ja-JP");

    public static string Money(decimal value, string currency) => $"{value.ToString("N2", Culture)} {currency}";

    public static string SignedMoney(decimal value, string currency) =>
        $"{value.ToString("+#,0.00;-#,0.00;0.00", Culture)} {currency}";

    public static string Jpy(decimal value) => $"{value.ToString("N0", Culture)} JPY";

    public static string SignedJpy(decimal value) => $"{value.ToString("+#,0;-#,0;0", Culture)} JPY";

    public static string Percent(decimal value) => $"{value.ToString("N2", Culture)}%";

    public static string SignedPercent(decimal value) => $"{value.ToString("+#,0.00;-#,0.00;0.00", Culture)}%";

    public static string Number(decimal value) => value % 1m == 0m
        ? value.ToString("N0", Culture)
        : value.ToString("N2", Culture);
}
