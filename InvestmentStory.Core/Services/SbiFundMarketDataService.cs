using System.Globalization;
using System.Net;
using System.Text;
using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class SbiFundMarketDataService : IFundMarketDataService
{
    private const string ProviderName = "SBI証券 基準価額履歴";
    private const string StandardPriceCsvUrl = "https://site0.sbisec.co.jp/marble/fund/history/standardprice/standardPriceHistoryCsvAction.do";

    private static readonly IReadOnlyDictionary<string, string> KnownFundCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SBI-V-SP500"] = "89311199",
        ["SBIVSP500"] = "89311199",
        ["SBI・V・S&P500"] = "89311199",
        ["SBI・V・S＆P500"] = "89311199",
        ["ＳＢＩ・Ｖ・Ｓ＆Ｐ５００インデックス・ファンド"] = "89311199",
        ["SBI・V・S&P500インデックス・ファンド"] = "89311199"
    };

    public Task<FundInfoResult> GetFundInfoAsync(string fundCodeOrName, CancellationToken cancellationToken = default)
    {
        var code = ResolveFundSecurityCode(fundCodeOrName);
        return Task.FromResult(new FundInfoResult
        {
            IsSuccess = !string.IsNullOrWhiteSpace(code),
            FundName = fundCodeOrName,
            FundCode = code,
            Source = ProviderName,
            ErrorMessage = string.IsNullOrWhiteSpace(code) ? "SBI基準価額履歴に対応する投資信託コードを特定できませんでした。" : string.Empty
        });
    }

    public async Task<FundNavResult> GetLatestNavAsync(string fundCodeOrName, CancellationToken cancellationToken = default)
    {
        var to = DateTime.Today;
        var from = to.AddMonths(-1);
        var points = await GetNavHistoryAsync(fundCodeOrName, from, to, cancellationToken);
        var latest = points.OrderByDescending(x => x.Date).FirstOrDefault();
        return latest is null
            ? new FundNavResult
            {
                IsSuccess = false,
                Source = ProviderName,
                ErrorMessage = "SBI基準価額履歴から最新基準価額を取得できませんでした。"
            }
            : new FundNavResult
            {
                IsSuccess = true,
                Nav = latest.Nav,
                UnitBase = 10000m,
                NavDate = latest.Date,
                Source = ProviderName
            };
    }

    public async Task<IReadOnlyList<FundNavHistoryPoint>> GetNavHistoryAsync(
        string fundCodeOrName,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var fundSecCode = ResolveFundSecurityCode(fundCodeOrName);
        if (string.IsNullOrWhiteSpace(fundSecCode))
        {
            return Array.Empty<FundNavHistoryPoint>();
        }

        using var httpClient = CreateHttpClient();
        using var content = CreateRequestContent(fundSecCode, from, to);
        using var response = await httpClient.PostAsync(
            $"{StandardPriceCsvUrl}?fund_sec_code={Uri.EscapeDataString(fundSecCode)}",
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<FundNavHistoryPoint>();
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return ParseStandardPriceCsv(DecodeShiftJis(bytes));
    }

    public Task<IReadOnlyList<FundDistributionRecord>> GetDistributionHistoryAsync(
        string fundCodeOrName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FundDistributionRecord>>(Array.Empty<FundDistributionRecord>());

    internal static IReadOnlyList<FundNavHistoryPoint> ParseStandardPriceCsv(string csv)
    {
        var points = new List<FundNavHistoryPoint>();
        foreach (var line in csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = ParseCsvLine(line);
            if (columns.Count < 2 || !DateTime.TryParse(columns[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            if (!decimal.TryParse(RemoveNumberDecorations(columns[1]), NumberStyles.Number, CultureInfo.InvariantCulture, out var nav) || nav <= 0m)
            {
                continue;
            }

            points.Add(new FundNavHistoryPoint
            {
                Date = date.Date,
                Nav = nav
            });
        }

        return points
            .GroupBy(x => x.Date)
            .Select(x => x.OrderByDescending(y => y.Nav).First())
            .OrderBy(x => x.Date)
            .ToList();
    }

    public static string ResolveFundSecurityCode(string fundCodeOrName)
    {
        var normalized = NormalizeFundQuery(fundCodeOrName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        if (digits.Length == 8)
        {
            return digits;
        }

        if (KnownFundCodes.TryGetValue(normalized, out var directCode))
        {
            return directCode;
        }

        foreach (var pair in KnownFundCodes)
        {
            if (normalized.Contains(NormalizeFundQuery(pair.Key), StringComparison.OrdinalIgnoreCase) ||
                NormalizeFundQuery(pair.Key).Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return string.Empty;
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 InvestmentStory/1.0");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream,text/csv,text/plain,*/*");
        return httpClient;
    }

    private static FormUrlEncodedContent CreateRequestContent(string fundSecCode, DateTime from, DateTime to)
    {
        var values = new Dictionary<string, string>
        {
            ["in_term_from_yyyy"] = from.Year.ToString(CultureInfo.InvariantCulture),
            ["in_term_from_mm"] = from.Month.ToString("00", CultureInfo.InvariantCulture),
            ["in_term_from_dd"] = from.Day.ToString("00", CultureInfo.InvariantCulture),
            ["in_term_to_yyyy"] = to.Year.ToString(CultureInfo.InvariantCulture),
            ["in_term_to_mm"] = to.Month.ToString("00", CultureInfo.InvariantCulture),
            ["in_term_to_dd"] = to.Day.ToString("00", CultureInfo.InvariantCulture),
            ["dispRows"] = "100",
            ["page"] = "0",
            ["fund_sec_code"] = fundSecCode
        };
        return new FormUrlEncodedContent(values);
    }

    private static string DecodeShiftJis(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932).GetString(bytes);
    }

    private static string NormalizeFundQuery(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim()
            .Replace("FUND:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("インデックス･ファンド", "インデックス・ファンド", StringComparison.Ordinal)
            .Replace("＆", "&", StringComparison.Ordinal)
            .Replace("５００", "500", StringComparison.Ordinal)
            .Replace("ＳＢＩ", "SBI", StringComparison.Ordinal)
            .Replace("Ｖ", "V", StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized.ToUpperInvariant();
    }

    private static string RemoveNumberDecorations(string value) =>
        value.Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("円", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }
}
