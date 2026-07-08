using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public interface IMarketDataService
{
    MarketDataResult GetQuote(string symbol, AppSettings settings);
}

public interface IUsMarketDataService
{
    MarketDataResult GetUsQuote(string symbol, AppSettings settings);
}

public interface IJapanMarketDataService
{
    MarketDataResult GetJapanQuote(string code, AppSettings settings);
}

public interface IBrokerDataService
{
    string ProviderName { get; }
    bool IsReadOnly { get; }
}

public interface IBrokerCsvImportService : IBrokerDataService
{
    BrokerCsvPreview Preview(string filePath, string csvType);
    BrokerCsvImportResult Import(string filePath, string csvType, IReadOnlyDictionary<string, string> columnMappings);
}

public interface IBrokerReadOnlyConnector : IBrokerDataService
{
    IReadOnlyList<BrokerHoldingRecord> GetHoldings();
}
