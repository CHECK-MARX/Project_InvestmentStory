using InvestmentStory.Core.Models;
using InvestmentStory.Core.Services;
using Microsoft.Data.Sqlite;

namespace InvestmentStory.Data;

public sealed class InvestmentStoryRepository
{
    private readonly string _databasePath;

    public InvestmentStoryRepository(string? databasePath = null)
    {
        _databasePath = databasePath ?? DatabasePaths.GetDefaultDatabasePath();
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        new DatabaseInitializer().Initialize(_databasePath);
    }

    public IReadOnlyList<StockPosition> GetPositions()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.Id, s.Name, s.Ticker, s.Country, s.Currency, s.Broker, s.AccountType, s.CustodyType,
                s.Sector, s.Industry, s.Market, s.DataSource, s.Memo,
                p.Id, p.PurchaseDate, p.Shares, p.UnitPrice, p.ExchangeRate,
                p.ExchangeRateAcquiredAt, p.ExchangeRateSource, p.ExchangeRateInputType, p.Fee, p.Memo,
                sp.Id, sp.SplitDate, sp.SplitRatio, sp.Memo,
                ch.Id, ch.CurrentShares, ch.CurrentPrice, ch.CurrentExchangeRate,
                ch.ExchangeRateAcquiredAt, ch.ExchangeRateSource, ch.ExchangeRateInputType, ch.AnnualDividendPerShare,
                ch.DividendStatus, ch.DividendFrequency, ch.DividendMonths, ch.CurrentPriceAcquiredAt, ch.CurrentPriceSource,
                ch.DividendInfoAcquiredAt, ch.DividendInfoSource, ch.UpdatedAt,
                s.AssetType,
                mf.Id, mf.FundName, mf.FundCode, mf.AssociationCode, mf.UnitsHeld, mf.UnitBase,
                mf.AverageCostNav, mf.CurrentNav, mf.AcquisitionAmount, mf.MarketValue, mf.UnrealizedGainLoss,
                mf.NavDate, mf.NavSource, mf.DistributionMethod, mf.AccountType,
                mf.TotalPurchaseAmount, mf.TotalSaleAmount, mf.ReinvestedDistributionAmount
            FROM Stocks s
            LEFT JOIN Purchases p ON p.Id = (SELECT Id FROM Purchases WHERE StockId = s.Id ORDER BY PurchaseDate, Id LIMIT 1)
            LEFT JOIN StockSplits sp ON sp.Id = (SELECT Id FROM StockSplits WHERE StockId = s.Id ORDER BY SplitDate DESC, Id DESC LIMIT 1)
            LEFT JOIN CurrentHoldings ch ON ch.StockId = s.Id
            LEFT JOIN MutualFundHoldings mf ON mf.StockId = s.Id
            ORDER BY s.Ticker;
            """;

        using var reader = command.ExecuteReader();
        var positions = new List<StockPosition>();
        while (reader.Read())
        {
            var stockId = reader.GetInt32(0);
            positions.Add(new StockPosition
            {
                Stock = new Stock
                {
                    Id = stockId,
                    Name = GetString(reader, 1),
                    Ticker = GetString(reader, 2),
                    Country = GetString(reader, 3),
                    Currency = GetString(reader, 4),
                    Broker = GetString(reader, 5),
                    AccountType = GetStringOrDefault(reader, 6, AccountTypes.Unknown),
                    CustodyType = GetString(reader, 7),
                    Sector = GetString(reader, 8),
                    Industry = GetString(reader, 9),
                    Market = GetString(reader, 10),
                    DataSource = GetStringOrDefault(reader, 11, "手入力"),
                    Memo = GetString(reader, 12),
                    AssetType = GetStringOrDefault(reader, 43, AssetTypes.Stock)
                },
                Purchase = new Purchase
                {
                    Id = GetInt32OrZero(reader, 13),
                    StockId = stockId,
                    PurchaseDate = GetDateOrToday(reader, 14),
                    Shares = GetDecimalOrZero(reader, 15),
                    UnitPrice = GetDecimalOrZero(reader, 16),
                    ExchangeRate = GetDecimalOrDefault(reader, 17, 1m),
                    ExchangeRateAcquiredAt = GetDateTimeOrDefault(reader, 18, GetDateOrToday(reader, 14)),
                    ExchangeRateSource = GetStringOrDefault(reader, 19, "手入力"),
                    ExchangeRateInputType = GetStringOrDefault(reader, 20, "手入力"),
                    Fee = GetDecimalOrZero(reader, 21),
                    Memo = GetString(reader, 22)
                },
                Split = new StockSplit
                {
                    Id = GetInt32OrZero(reader, 23),
                    StockId = stockId,
                    SplitDate = GetDateOrToday(reader, 24),
                    SplitRatio = GetDecimalOrDefault(reader, 25, 1m),
                    Memo = GetString(reader, 26)
                },
                CurrentHolding = new CurrentHolding
                {
                    Id = GetInt32OrZero(reader, 27),
                    StockId = stockId,
                    CurrentShares = GetDecimalOrZero(reader, 28),
                    CurrentPrice = GetDecimalOrZero(reader, 29),
                    CurrentExchangeRate = GetDecimalOrDefault(reader, 30, 1m),
                    ExchangeRateAcquiredAt = GetDateTimeOrDefault(reader, 31, DateTime.Today),
                    ExchangeRateSource = GetStringOrDefault(reader, 32, "手入力"),
                    ExchangeRateInputType = GetStringOrDefault(reader, 33, "手入力"),
                    AnnualDividendPerShare = GetDecimalOrZero(reader, 34),
                    DividendStatus = GetDividendStatus(reader, 35, GetDecimalOrZero(reader, 34)),
                    DividendFrequency = GetString(reader, 36),
                    DividendMonths = GetString(reader, 37),
                    CurrentPriceAcquiredAt = GetDateTimeOrDefault(reader, 38, DateTime.MinValue),
                    CurrentPriceSource = GetString(reader, 39),
                    DividendInfoAcquiredAt = GetDateTimeOrDefault(reader, 40, DateTime.MinValue),
                    DividendInfoSource = GetString(reader, 41),
                    UpdatedAt = GetDateOrToday(reader, 42)
                },
                MutualFund = new MutualFundHolding
                {
                    Id = GetInt32OrZero(reader, 44),
                    StockId = stockId,
                    FundName = GetString(reader, 45),
                    FundCode = GetString(reader, 46),
                    AssociationCode = GetString(reader, 47),
                    UnitsHeld = GetDecimalOrZero(reader, 48),
                    UnitBase = GetDecimalOrDefault(reader, 49, 10000m),
                    AverageCostNav = GetDecimalOrZero(reader, 50),
                    CurrentNav = GetDecimalOrZero(reader, 51),
                    AcquisitionAmount = GetDecimalOrZero(reader, 52),
                    MarketValue = GetDecimalOrZero(reader, 53),
                    UnrealizedGainLoss = GetDecimalOrZero(reader, 54),
                    NavDate = GetDateTimeOrDefault(reader, 55, DateTime.MinValue),
                    NavSource = GetString(reader, 56),
                    DistributionMethod = GetString(reader, 57),
                    AccountType = GetString(reader, 58),
                    TotalPurchaseAmount = GetDecimalOrZero(reader, 59),
                    TotalSaleAmount = GetDecimalOrZero(reader, 60),
                    ReinvestedDistributionAmount = GetDecimalOrZero(reader, 61)
                }
            });
        }

        return positions;
    }

    public StockPosition? GetPosition(int stockId)
    {
        return GetPositions().FirstOrDefault(x => x.Stock.Id == stockId);
    }

    public int SavePosition(StockPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var stockId = position.Stock.Id == 0
            ? InsertStock(connection, transaction, position.Stock)
            : UpdateStock(connection, transaction, position.Stock);

        position.Stock.Id = stockId;
        position.Purchase.StockId = stockId;
        position.Split.StockId = stockId;
        position.CurrentHolding.StockId = stockId;
        position.MutualFund.StockId = stockId;

        SavePurchase(connection, transaction, position.Purchase);
        SaveSplit(connection, transaction, position.Split);
        SaveCurrentHolding(connection, transaction, position.CurrentHolding);
        if (position.IsMutualFund)
        {
            SaveMutualFundHolding(connection, transaction, position.MutualFund);
        }

        transaction.Commit();
        return stockId;
    }

    public void DeleteStock(int stockId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Stocks WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", stockId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<DividendPayment> GetDividendPayments()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                d.Id, d.StockId, d.AccountType, d.TaxAccountType, d.PaymentDate, d.RecordDate,
                d.ExDividendDate, d.DeclaredDate, d.FiscalYear, d.FiscalQuarter,
                s.Name, s.Ticker, d.Broker, d.DividendStatus, d.Source, d.SourceFile,
                d.SourceRowNumber, d.SourcePriority, d.Quantity, d.DividendPerShare,
                d.GrossAmount, d.ForeignTaxAmount, d.DomesticTaxAmount, d.TotalTaxAmount,
                d.TaxAmount, d.NetAmount, d.Currency, d.ExchangeRate, d.ExchangeRateAcquiredAt,
                d.ExchangeRateSource, d.ExchangeRateInputType, d.GrossAmountJpy,
                d.ForeignTaxAmountJpy, d.DomesticTaxAmountJpy, d.TotalTaxAmountJpy,
                d.NetAmountJpy, d.JpyAmount, d.IsTaxEstimated, d.IsNisa, d.IsForeignStock,
                d.TaxProfileId, d.MatchedActualDividendId, d.ReplacedByDividendId,
                d.CreatedAt, d.UpdatedAt, d.Memo
            FROM DividendPayments d
            INNER JOIN Stocks s ON s.Id = d.StockId
            ORDER BY d.PaymentDate DESC, d.Id DESC;
            """;

        using var reader = command.ExecuteReader();
        var dividends = new List<DividendPayment>();
        while (reader.Read())
        {
            dividends.Add(new DividendPayment
            {
                Id = reader.GetInt32(0),
                StockId = reader.GetInt32(1),
                AccountType = GetStringOrDefault(reader, 2, DividendConstants.AccountUnknown),
                TaxAccountType = GetStringOrDefault(reader, 3, DividendConstants.AccountUnknown),
                PaymentDate = ParseDate(GetString(reader, 4)),
                RecordDate = GetDateTimeOrDefault(reader, 5, DateTime.MinValue),
                ExDividendDate = GetDateTimeOrDefault(reader, 6, DateTime.MinValue),
                DeclaredDate = GetDateTimeOrDefault(reader, 7, DateTime.MinValue),
                FiscalYear = GetInt32OrZero(reader, 8),
                FiscalQuarter = GetString(reader, 9),
                StockName = GetString(reader, 10),
                Ticker = GetString(reader, 11),
                Broker = GetString(reader, 12),
                DividendStatus = GetStringOrDefault(reader, 13, DividendConstants.Actual),
                Source = GetStringOrDefault(reader, 14, DividendConstants.SourceManual),
                SourceFile = GetString(reader, 15),
                SourceRowNumber = GetInt32OrZero(reader, 16),
                SourcePriority = GetInt32OrZero(reader, 17),
                Quantity = GetDecimalOrZero(reader, 18),
                DividendPerShare = GetDecimalOrZero(reader, 19),
                GrossAmount = GetDecimalOrZero(reader, 20),
                ForeignTaxAmount = GetDecimalOrZero(reader, 21),
                DomesticTaxAmount = GetDecimalOrZero(reader, 22),
                TotalTaxAmount = GetDecimalOrZero(reader, 23),
                TaxAmount = GetDecimalOrZero(reader, 24),
                NetAmount = GetDecimalOrZero(reader, 25),
                Currency = GetString(reader, 26),
                ExchangeRate = GetDecimalOrDefault(reader, 27, 1m),
                ExchangeRateAcquiredAt = GetDateTimeOrDefault(reader, 28, ParseDate(GetString(reader, 4))),
                ExchangeRateSource = GetStringOrDefault(reader, 29, "手入力"),
                ExchangeRateInputType = GetStringOrDefault(reader, 30, "手入力"),
                GrossAmountJpy = GetDecimalOrZero(reader, 31),
                ForeignTaxAmountJpy = GetDecimalOrZero(reader, 32),
                DomesticTaxAmountJpy = GetDecimalOrZero(reader, 33),
                TotalTaxAmountJpy = GetDecimalOrZero(reader, 34),
                NetAmountJpy = GetDecimalOrZero(reader, 35),
                JpyAmount = GetDecimalOrZero(reader, 36),
                IsTaxEstimated = GetInt32OrZero(reader, 37) == 1,
                IsNisa = GetInt32OrZero(reader, 38) == 1,
                IsForeignStock = GetInt32OrZero(reader, 39) == 1,
                TaxProfileId = GetNullableInt32(reader, 40),
                MatchedActualDividendId = GetNullableInt32(reader, 41),
                ReplacedByDividendId = GetNullableInt32(reader, 42),
                CreatedAt = GetDateTimeOrDefault(reader, 43, DateTime.MinValue),
                UpdatedAt = GetDateTimeOrDefault(reader, 44, DateTime.MinValue),
                Memo = GetString(reader, 45)
            });
        }

        return dividends;
    }

    public int SaveDividendPayment(DividendPayment dividend)
    {
        ArgumentNullException.ThrowIfNull(dividend);

        using var connection = OpenConnection();
        return dividend.Id == 0
            ? InsertDividend(connection, dividend)
            : UpdateDividend(connection, dividend);
    }

    public void DeleteDividendPayment(int dividendPaymentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DividendPayments WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", dividendPaymentId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TaxProfile> GetTaxProfiles()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Country, Currency, AccountType, AssetType, ForeignWithholdingTaxRate,
                   DomesticIncomeTaxRate, DomesticLocalTaxRate, DomesticSpecialTaxRate, TotalDomesticTaxRate,
                   IsDomesticTaxExempt, IsForeignTaxExempt, Memo
            FROM TaxProfiles
            ORDER BY Currency, AccountType, Id;
            """;

        using var reader = command.ExecuteReader();
        var profiles = new List<TaxProfile>();
        while (reader.Read())
        {
            profiles.Add(new TaxProfile
            {
                Id = reader.GetInt32(0),
                Name = GetString(reader, 1),
                Country = GetString(reader, 2),
                Currency = GetString(reader, 3),
                AccountType = GetString(reader, 4),
                AssetType = GetString(reader, 5),
                ForeignWithholdingTaxRate = GetDecimalOrZero(reader, 6),
                DomesticIncomeTaxRate = GetDecimalOrZero(reader, 7),
                DomesticLocalTaxRate = GetDecimalOrZero(reader, 8),
                DomesticSpecialTaxRate = GetDecimalOrZero(reader, 9),
                TotalDomesticTaxRate = GetDecimalOrZero(reader, 10),
                IsDomesticTaxExempt = GetInt32OrZero(reader, 11) == 1,
                IsForeignTaxExempt = GetInt32OrZero(reader, 12) == 1,
                Memo = GetString(reader, 13)
            });
        }

        return profiles;
    }

    public int SaveTaxProfile(TaxProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = profile.Id == 0
            ? """
              INSERT INTO TaxProfiles
                  (Name, Country, Currency, AccountType, AssetType, ForeignWithholdingTaxRate, DomesticIncomeTaxRate,
                   DomesticLocalTaxRate, DomesticSpecialTaxRate, TotalDomesticTaxRate, IsDomesticTaxExempt, IsForeignTaxExempt, Memo)
              VALUES
                  ($name, $country, $currency, $accountType, $assetType, $foreignRate, $domesticIncomeRate,
                   $domesticLocalRate, $domesticSpecialRate, $totalDomesticRate, $isDomesticTaxExempt, $isForeignTaxExempt, $memo);
              SELECT last_insert_rowid();
              """
            : """
              UPDATE TaxProfiles
              SET Name = $name,
                  Country = $country,
                  Currency = $currency,
                  AccountType = $accountType,
                  AssetType = $assetType,
                  ForeignWithholdingTaxRate = $foreignRate,
                  DomesticIncomeTaxRate = $domesticIncomeRate,
                  DomesticLocalTaxRate = $domesticLocalRate,
                  DomesticSpecialTaxRate = $domesticSpecialRate,
                  TotalDomesticTaxRate = $totalDomesticRate,
                  IsDomesticTaxExempt = $isDomesticTaxExempt,
                  IsForeignTaxExempt = $isForeignTaxExempt,
                  Memo = $memo
              WHERE Id = $id;
              """;
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$country", profile.Country);
        command.Parameters.AddWithValue("$currency", NormalizeCurrency(profile.Currency));
        command.Parameters.AddWithValue("$accountType", DividendConstants.NormalizeAccountType(profile.AccountType));
        command.Parameters.AddWithValue("$assetType", string.IsNullOrWhiteSpace(profile.AssetType) ? "Stock" : profile.AssetType);
        command.Parameters.AddWithValue("$foreignRate", profile.ForeignWithholdingTaxRate);
        command.Parameters.AddWithValue("$domesticIncomeRate", profile.DomesticIncomeTaxRate);
        command.Parameters.AddWithValue("$domesticLocalRate", profile.DomesticLocalTaxRate);
        command.Parameters.AddWithValue("$domesticSpecialRate", profile.DomesticSpecialTaxRate);
        command.Parameters.AddWithValue("$totalDomesticRate", profile.TotalDomesticTaxRate);
        command.Parameters.AddWithValue("$isDomesticTaxExempt", profile.IsDomesticTaxExempt ? 1 : 0);
        command.Parameters.AddWithValue("$isForeignTaxExempt", profile.IsForeignTaxExempt ? 1 : 0);
        command.Parameters.AddWithValue("$memo", profile.Memo);
        if (profile.Id != 0)
        {
            command.Parameters.AddWithValue("$id", profile.Id);
            command.ExecuteNonQuery();
            return profile.Id;
        }

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IncomeGoal? GetGoal(int year)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TargetYear, AnnualPassiveIncomeGoal, MonthlyPassiveIncomeGoal, TotalAssetGoal
            FROM IncomeGoals
            WHERE TargetYear = $year;
            """;
        command.Parameters.AddWithValue("$year", year);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new IncomeGoal
        {
            Id = reader.GetInt32(0),
            TargetYear = reader.GetInt32(1),
            AnnualPassiveIncomeGoal = GetDecimalOrZero(reader, 2),
            MonthlyPassiveIncomeGoal = GetDecimalOrZero(reader, 3),
            TotalAssetGoal = GetDecimalOrZero(reader, 4)
        };
    }

    public void SaveGoal(IncomeGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO IncomeGoals (TargetYear, AnnualPassiveIncomeGoal, MonthlyPassiveIncomeGoal, TotalAssetGoal)
            VALUES ($targetYear, $annualGoal, $monthlyGoal, $totalAssetGoal)
            ON CONFLICT(TargetYear) DO UPDATE SET
                AnnualPassiveIncomeGoal = excluded.AnnualPassiveIncomeGoal,
                MonthlyPassiveIncomeGoal = excluded.MonthlyPassiveIncomeGoal,
                TotalAssetGoal = excluded.TotalAssetGoal;
            """;
        command.Parameters.AddWithValue("$targetYear", goal.TargetYear);
        command.Parameters.AddWithValue("$annualGoal", goal.AnnualPassiveIncomeGoal);
        command.Parameters.AddWithValue("$monthlyGoal", goal.MonthlyPassiveIncomeGoal);
        command.Parameters.AddWithValue("$totalAssetGoal", goal.TotalAssetGoal);
        command.ExecuteNonQuery();
    }

    public AppSettings GetSettings()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM AppSettings;";
        using var reader = command.ExecuteReader();

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return new AppSettings
        {
            MarketDataMode = GetSetting(values, nameof(AppSettings.MarketDataMode), "Web/API"),
            UsMarketDataProvider = GetSetting(values, nameof(AppSettings.UsMarketDataProvider), "Alpha Vantage"),
            JapanMarketDataProvider = GetSetting(values, nameof(AppSettings.JapanMarketDataProvider), "Yahoo Finance"),
            ExchangeRateProvider = GetSetting(values, nameof(AppSettings.ExchangeRateProvider), "Yahoo Finance"),
            BrokerDataMode = GetSetting(values, nameof(AppSettings.BrokerDataMode), "手入力"),
            AlphaVantageApiKey = GetSetting(values, nameof(AppSettings.AlphaVantageApiKey), string.Empty),
            JQuantsApiKey = GetSetting(values, nameof(AppSettings.JQuantsApiKey), string.Empty),
            ApiTimeoutSeconds = GetIntSetting(values, nameof(AppSettings.ApiTimeoutSeconds), 10),
            UseLastValueOnApiFailure = GetBoolSetting(values, nameof(AppSettings.UseLastValueOnApiFailure), true),
            SaveLoginCredentials = GetBoolSetting(values, nameof(AppSettings.SaveLoginCredentials), false),
            EnableApiResponseLog = GetBoolSetting(values, nameof(AppSettings.EnableApiResponseLog), false),
            ThemeMode = GetSetting(values, nameof(AppSettings.ThemeMode), "Light"),
            IsSidebarCollapsed = GetBoolSetting(values, nameof(AppSettings.IsSidebarCollapsed), false),
            StockListDisplayMode = GetSetting(values, nameof(AppSettings.StockListDisplayMode), "基本"),
            LastDashboardCompositionMode = GetSetting(values, nameof(AppSettings.LastDashboardCompositionMode), "Country"),
            LastOpenedPage = GetSetting(values, nameof(AppSettings.LastOpenedPage), "Dashboard")
        };
    }

    public void SaveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertSetting(connection, transaction, nameof(AppSettings.MarketDataMode), settings.MarketDataMode);
        UpsertSetting(connection, transaction, nameof(AppSettings.UsMarketDataProvider), settings.UsMarketDataProvider);
        UpsertSetting(connection, transaction, nameof(AppSettings.JapanMarketDataProvider), settings.JapanMarketDataProvider);
        UpsertSetting(connection, transaction, nameof(AppSettings.ExchangeRateProvider), settings.ExchangeRateProvider);
        UpsertSetting(connection, transaction, nameof(AppSettings.BrokerDataMode), settings.BrokerDataMode);
        UpsertSetting(connection, transaction, nameof(AppSettings.AlphaVantageApiKey), settings.AlphaVantageApiKey);
        UpsertSetting(connection, transaction, nameof(AppSettings.JQuantsApiKey), settings.JQuantsApiKey);
        UpsertSetting(connection, transaction, nameof(AppSettings.ApiTimeoutSeconds), settings.ApiTimeoutSeconds.ToString());
        UpsertSetting(connection, transaction, nameof(AppSettings.UseLastValueOnApiFailure), settings.UseLastValueOnApiFailure.ToString());
        UpsertSetting(connection, transaction, nameof(AppSettings.SaveLoginCredentials), settings.SaveLoginCredentials.ToString());
        UpsertSetting(connection, transaction, nameof(AppSettings.EnableApiResponseLog), settings.EnableApiResponseLog.ToString());
        UpsertSetting(connection, transaction, nameof(AppSettings.ThemeMode), settings.ThemeMode);
        UpsertSetting(connection, transaction, nameof(AppSettings.IsSidebarCollapsed), settings.IsSidebarCollapsed.ToString());
        UpsertSetting(connection, transaction, nameof(AppSettings.StockListDisplayMode), settings.StockListDisplayMode);
        UpsertSetting(connection, transaction, nameof(AppSettings.LastDashboardCompositionMode), settings.LastDashboardCompositionMode);
        UpsertSetting(connection, transaction, nameof(AppSettings.LastOpenedPage), settings.LastOpenedPage);
        transaction.Commit();
    }

    public void SaveApiFetchLogs(IEnumerable<ApiFetchLogEntry> logs)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var log in logs)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ApiFetchLogs
                    (ApiType, Provider, Symbol, HttpStatusCode, IsSuccess, ErrorMessage, FetchedAt, Summary)
                VALUES
                    ($apiType, $provider, $symbol, $httpStatusCode, $isSuccess, $errorMessage, $fetchedAt, $summary);
                """;
            command.Parameters.AddWithValue("$apiType", log.ApiType);
            command.Parameters.AddWithValue("$provider", log.Provider);
            command.Parameters.AddWithValue("$symbol", log.Symbol);
            command.Parameters.AddWithValue("$httpStatusCode", log.HttpStatusCode is null ? DBNull.Value : log.HttpStatusCode);
            command.Parameters.AddWithValue("$isSuccess", log.IsSuccess ? 1 : 0);
            command.Parameters.AddWithValue("$errorMessage", RedactSecret(log.ErrorMessage));
            command.Parameters.AddWithValue("$fetchedAt", ToDateTimeText(log.FetchedAt));
            command.Parameters.AddWithValue("$summary", RedactSecret(log.Summary));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<ApiFetchLogEntry> GetRecentApiFetchLogs(int limit = 100)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ApiType, Provider, Symbol, HttpStatusCode, IsSuccess, ErrorMessage, FetchedAt, Summary
            FROM ApiFetchLogs
            ORDER BY Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var logs = new List<ApiFetchLogEntry>();
        while (reader.Read())
        {
            logs.Add(new ApiFetchLogEntry
            {
                Id = reader.GetInt32(0),
                ApiType = GetString(reader, 1),
                Provider = GetString(reader, 2),
                Symbol = GetString(reader, 3),
                HttpStatusCode = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                IsSuccess = reader.GetInt32(5) == 1,
                ErrorMessage = GetString(reader, 6),
                FetchedAt = GetDateTimeOrDefault(reader, 7, DateTime.MinValue),
                Summary = GetString(reader, 8)
            });
        }

        return logs;
    }

    public void SavePortfolioSnapshot(PortfolioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PortfolioSnapshots
                (SnapshotDate, TotalMarketValueJpy, TotalCostBasisJpy, UnrealizedGainLossJpy,
                 CumulativeDividendJpy, RealizedGainLossJpy, TotalReturnJpy, UsdJpyRate,
                 StockValueJpy, MutualFundValueJpy, CashValueJpy, CreatedAt, UpdatedAt)
            VALUES
                ($snapshotDate, $totalMarketValueJpy, $totalCostBasisJpy, $unrealizedGainLossJpy,
                 $cumulativeDividendJpy, $realizedGainLossJpy, $totalReturnJpy, $usdJpyRate,
                 $stockValueJpy, $mutualFundValueJpy, $cashValueJpy, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT(SnapshotDate) DO UPDATE SET
                TotalMarketValueJpy = excluded.TotalMarketValueJpy,
                TotalCostBasisJpy = excluded.TotalCostBasisJpy,
                UnrealizedGainLossJpy = excluded.UnrealizedGainLossJpy,
                CumulativeDividendJpy = excluded.CumulativeDividendJpy,
                RealizedGainLossJpy = excluded.RealizedGainLossJpy,
                TotalReturnJpy = excluded.TotalReturnJpy,
                UsdJpyRate = excluded.UsdJpyRate,
                StockValueJpy = excluded.StockValueJpy,
                MutualFundValueJpy = excluded.MutualFundValueJpy,
                CashValueJpy = excluded.CashValueJpy,
                UpdatedAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$snapshotDate", ToDateText(snapshot.SnapshotDate));
        command.Parameters.AddWithValue("$totalMarketValueJpy", snapshot.TotalMarketValueJpy);
        command.Parameters.AddWithValue("$totalCostBasisJpy", snapshot.TotalCostBasisJpy);
        command.Parameters.AddWithValue("$unrealizedGainLossJpy", snapshot.UnrealizedGainLossJpy);
        command.Parameters.AddWithValue("$cumulativeDividendJpy", snapshot.CumulativeDividendJpy);
        command.Parameters.AddWithValue("$realizedGainLossJpy", snapshot.RealizedGainLossJpy);
        command.Parameters.AddWithValue("$totalReturnJpy", snapshot.TotalReturnJpy);
        command.Parameters.AddWithValue("$usdJpyRate", snapshot.UsdJpyRate);
        command.Parameters.AddWithValue("$stockValueJpy", snapshot.StockValueJpy);
        command.Parameters.AddWithValue("$mutualFundValueJpy", snapshot.MutualFundValueJpy);
        command.Parameters.AddWithValue("$cashValueJpy", snapshot.CashValueJpy);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<PortfolioSnapshot> GetPortfolioSnapshots()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SnapshotDate, TotalMarketValueJpy, TotalCostBasisJpy, UnrealizedGainLossJpy,
                   CumulativeDividendJpy, RealizedGainLossJpy, TotalReturnJpy, UsdJpyRate,
                   StockValueJpy, MutualFundValueJpy, CashValueJpy, CreatedAt, UpdatedAt
            FROM PortfolioSnapshots
            ORDER BY SnapshotDate;
            """;

        using var reader = command.ExecuteReader();
        var snapshots = new List<PortfolioSnapshot>();
        while (reader.Read())
        {
            snapshots.Add(new PortfolioSnapshot
            {
                Id = reader.GetInt32(0),
                SnapshotDate = ParseDate(GetString(reader, 1)),
                TotalMarketValueJpy = GetDecimalOrZero(reader, 2),
                TotalCostBasisJpy = GetDecimalOrZero(reader, 3),
                UnrealizedGainLossJpy = GetDecimalOrZero(reader, 4),
                CumulativeDividendJpy = GetDecimalOrZero(reader, 5),
                RealizedGainLossJpy = GetDecimalOrZero(reader, 6),
                TotalReturnJpy = GetDecimalOrZero(reader, 7),
                UsdJpyRate = GetDecimalOrZero(reader, 8),
                StockValueJpy = GetDecimalOrZero(reader, 9),
                MutualFundValueJpy = GetDecimalOrZero(reader, 10),
                CashValueJpy = GetDecimalOrZero(reader, 11),
                CreatedAt = GetDateTimeOrDefault(reader, 12, DateTime.MinValue),
                UpdatedAt = GetDateTimeOrDefault(reader, 13, DateTime.MinValue)
            });
        }

        return snapshots;
    }

    public void SaveBrokerTrades(IEnumerable<BrokerTradeRecord> tradeRecords)
    {
        ArgumentNullException.ThrowIfNull(tradeRecords);

        var records = tradeRecords.ToList();
        if (records.Count == 0)
        {
            return;
        }

        var positions = GetPositions()
            .GroupBy(x => BuildPositionKey(x.Stock.Broker, x.Stock.Ticker, x.Stock.AccountType), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var ledgerBuilder = new TradeLedgerService();
        var ledgers = new List<BrokerTrade>();

        foreach (var group in records.GroupBy(x => BuildPositionKey(x.Broker, x.Ticker, x.Account), StringComparer.OrdinalIgnoreCase))
        {
            if (!positions.TryGetValue(group.Key, out var position))
            {
                continue;
            }

            ledgers.AddRange(ledgerBuilder.BuildLedger(position.Stock.Id, group.ToList()));
        }

        if (ledgers.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var trade in ledgers)
        {
            InsertBrokerTrade(connection, transaction, trade);
        }

        transaction.Commit();
    }

    public IReadOnlyList<BrokerTrade> GetBrokerTrades(int stockId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StockId, TradeDate, SettlementDate, Broker, AccountType, CustodyType, TradeType,
                   Quantity, SignedQuantity, UnitPrice, Currency, ExchangeRate, SettlementAmountJpy,
                   FeeJpy, TaxJpy, RealizedGainLoss, RealizedGainLossJpy, AfterTradeQuantity,
                   AfterTradeAverageCost, Source, SourceFile, CreatedAt
            FROM BrokerTrades
            WHERE StockId = $stockId
            ORDER BY TradeDate DESC, SettlementDate DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$stockId", stockId);
        return ReadBrokerTrades(command);
    }

    public IReadOnlyList<BrokerTrade> GetAllBrokerTrades()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StockId, TradeDate, SettlementDate, Broker, AccountType, CustodyType, TradeType,
                   Quantity, SignedQuantity, UnitPrice, Currency, ExchangeRate, SettlementAmountJpy,
                   FeeJpy, TaxJpy, RealizedGainLoss, RealizedGainLossJpy, AfterTradeQuantity,
                   AfterTradeAverageCost, Source, SourceFile, CreatedAt
            FROM BrokerTrades
            ORDER BY TradeDate DESC, SettlementDate DESC, Id DESC;
            """;
        return ReadBrokerTrades(command);
    }

    public IReadOnlyList<DataQualityInfo> GetDataQualityInfos(int stockId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, StockId, FieldName, Value, SourceType, SourceName, RetrievedAt, ConfidenceLevel,
                   IsEstimated, IsStale, HasConflict, ConflictDescription, ManualOverride, Memo
            FROM DataQualityInfos
            WHERE StockId = $stockId
            ORDER BY FieldName;
            """;
        command.Parameters.AddWithValue("$stockId", stockId);

        using var reader = command.ExecuteReader();
        var values = new List<DataQualityInfo>();
        while (reader.Read())
        {
            values.Add(new DataQualityInfo
            {
                Id = reader.GetInt32(0),
                StockId = reader.GetInt32(1),
                FieldName = GetString(reader, 2),
                Value = GetString(reader, 3),
                SourceType = GetStringOrDefault(reader, 4, DataSourceTypes.Unknown),
                SourceName = GetString(reader, 5),
                RetrievedAt = GetDateTimeOrDefault(reader, 6, DateTime.MinValue),
                ConfidenceLevel = GetStringOrDefault(reader, 7, DataQualityStates.Missing),
                IsEstimated = GetInt32OrZero(reader, 8) == 1,
                IsStale = GetInt32OrZero(reader, 9) == 1,
                HasConflict = GetInt32OrZero(reader, 10) == 1,
                ConflictDescription = GetString(reader, 11),
                ManualOverride = GetInt32OrZero(reader, 12) == 1,
                Memo = GetString(reader, 13)
            });
        }

        return values;
    }

    public void SaveDataQualityInfos(IEnumerable<DataQualityInfo> items)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var item in items)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO DataQualityInfos
                    (StockId, FieldName, Value, SourceType, SourceName, RetrievedAt, ConfidenceLevel,
                     IsEstimated, IsStale, HasConflict, ConflictDescription, ManualOverride, Memo, UpdatedAt)
                VALUES
                    ($stockId, $fieldName, $value, $sourceType, $sourceName, $retrievedAt, $confidenceLevel,
                     $isEstimated, $isStale, $hasConflict, $conflictDescription, $manualOverride, $memo, CURRENT_TIMESTAMP)
                ON CONFLICT(StockId, FieldName) DO UPDATE SET
                    Value = excluded.Value,
                    SourceType = excluded.SourceType,
                    SourceName = excluded.SourceName,
                    RetrievedAt = excluded.RetrievedAt,
                    ConfidenceLevel = excluded.ConfidenceLevel,
                    IsEstimated = excluded.IsEstimated,
                    IsStale = excluded.IsStale,
                    HasConflict = excluded.HasConflict,
                    ConflictDescription = excluded.ConflictDescription,
                    ManualOverride = excluded.ManualOverride,
                    Memo = excluded.Memo,
                    UpdatedAt = CURRENT_TIMESTAMP;
                """;
            command.Parameters.AddWithValue("$stockId", item.StockId);
            command.Parameters.AddWithValue("$fieldName", item.FieldName);
            command.Parameters.AddWithValue("$value", item.Value);
            command.Parameters.AddWithValue("$sourceType", item.SourceType);
            command.Parameters.AddWithValue("$sourceName", item.SourceName);
            command.Parameters.AddWithValue("$retrievedAt", ToOptionalDateText(item.RetrievedAt));
            command.Parameters.AddWithValue("$confidenceLevel", item.ConfidenceLevel);
            command.Parameters.AddWithValue("$isEstimated", item.IsEstimated ? 1 : 0);
            command.Parameters.AddWithValue("$isStale", item.IsStale ? 1 : 0);
            command.Parameters.AddWithValue("$hasConflict", item.HasConflict ? 1 : 0);
            command.Parameters.AddWithValue("$conflictDescription", item.ConflictDescription);
            command.Parameters.AddWithValue("$manualOverride", item.ManualOverride ? 1 : 0);
            command.Parameters.AddWithValue("$memo", item.Memo);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(DatabaseInitializer.CreateConnectionString(_databasePath));
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();

        return connection;
    }

    private static int InsertStock(SqliteConnection connection, SqliteTransaction transaction, Stock stock)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Stocks (AssetType, Name, Ticker, Country, Currency, Broker, AccountType, CustodyType, Sector, Industry, Market, DataSource, Memo)
            VALUES ($assetType, $name, $ticker, $country, $currency, $broker, $accountType, $custodyType, $sector, $industry, $market, $dataSource, $memo);
            SELECT last_insert_rowid();
            """;
        AddStockParameters(command, stock);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int UpdateStock(SqliteConnection connection, SqliteTransaction transaction, Stock stock)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Stocks
            SET AssetType = $assetType,
                Name = $name,
                Ticker = $ticker,
                Country = $country,
                Currency = $currency,
                Broker = $broker,
                AccountType = $accountType,
                CustodyType = $custodyType,
                Sector = $sector,
                Industry = $industry,
                Market = $market,
                DataSource = $dataSource,
                Memo = $memo,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = $id;
            """;
        AddStockParameters(command, stock);
        command.Parameters.AddWithValue("$id", stock.Id);
        command.ExecuteNonQuery();
        return stock.Id;
    }

    private static void SavePurchase(SqliteConnection connection, SqliteTransaction transaction, Purchase purchase)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = purchase.Id == 0
            ? """
              INSERT INTO Purchases
                  (StockId, PurchaseDate, Shares, UnitPrice, ExchangeRate, ExchangeRateAcquiredAt, ExchangeRateSource, ExchangeRateInputType, Fee, Memo)
              VALUES
                  ($stockId, $purchaseDate, $shares, $unitPrice, $exchangeRate, $exchangeRateAcquiredAt, $exchangeRateSource, $exchangeRateInputType, $fee, $memo);
              """
            : """
              UPDATE Purchases
              SET PurchaseDate = $purchaseDate,
                  Shares = $shares,
                  UnitPrice = $unitPrice,
                  ExchangeRate = $exchangeRate,
                  ExchangeRateAcquiredAt = $exchangeRateAcquiredAt,
                  ExchangeRateSource = $exchangeRateSource,
                  ExchangeRateInputType = $exchangeRateInputType,
                  Fee = $fee,
                  Memo = $memo
              WHERE Id = $id;
              """;
        command.Parameters.AddWithValue("$stockId", purchase.StockId);
        command.Parameters.AddWithValue("$purchaseDate", ToDateText(purchase.PurchaseDate));
        command.Parameters.AddWithValue("$shares", purchase.Shares);
        command.Parameters.AddWithValue("$unitPrice", purchase.UnitPrice);
        command.Parameters.AddWithValue("$exchangeRate", purchase.ExchangeRate);
        command.Parameters.AddWithValue("$exchangeRateAcquiredAt", ToDateTimeText(purchase.ExchangeRateAcquiredAt));
        command.Parameters.AddWithValue("$exchangeRateSource", purchase.ExchangeRateSource);
        command.Parameters.AddWithValue("$exchangeRateInputType", purchase.ExchangeRateInputType);
        command.Parameters.AddWithValue("$fee", purchase.Fee);
        command.Parameters.AddWithValue("$memo", purchase.Memo);
        if (purchase.Id != 0)
        {
            command.Parameters.AddWithValue("$id", purchase.Id);
        }

        command.ExecuteNonQuery();
    }

    private static void SaveSplit(SqliteConnection connection, SqliteTransaction transaction, StockSplit split)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = split.Id == 0
            ? """
              INSERT INTO StockSplits (StockId, SplitDate, SplitRatio, Memo)
              VALUES ($stockId, $splitDate, $splitRatio, $memo);
              """
            : """
              UPDATE StockSplits
              SET SplitDate = $splitDate,
                  SplitRatio = $splitRatio,
                  Memo = $memo
              WHERE Id = $id;
              """;
        command.Parameters.AddWithValue("$stockId", split.StockId);
        command.Parameters.AddWithValue("$splitDate", ToDateText(split.SplitDate));
        command.Parameters.AddWithValue("$splitRatio", split.SplitRatio);
        command.Parameters.AddWithValue("$memo", split.Memo);
        if (split.Id != 0)
        {
            command.Parameters.AddWithValue("$id", split.Id);
        }

        command.ExecuteNonQuery();
    }

    private static void SaveCurrentHolding(SqliteConnection connection, SqliteTransaction transaction, CurrentHolding currentHolding)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = currentHolding.Id == 0
            ? """
              INSERT INTO CurrentHoldings
                  (StockId, CurrentShares, CurrentPrice, CurrentExchangeRate, ExchangeRateAcquiredAt, ExchangeRateSource, ExchangeRateInputType, AnnualDividendPerShare, DividendStatus, DividendFrequency, DividendMonths, CurrentPriceAcquiredAt, CurrentPriceSource, DividendInfoAcquiredAt, DividendInfoSource, UpdatedAt)
              VALUES
                  ($stockId, $currentShares, $currentPrice, $currentExchangeRate, $exchangeRateAcquiredAt, $exchangeRateSource, $exchangeRateInputType, $annualDividendPerShare, $dividendStatus, $dividendFrequency, $dividendMonths, $currentPriceAcquiredAt, $currentPriceSource, $dividendInfoAcquiredAt, $dividendInfoSource, $updatedAt);
              """
            : """
              UPDATE CurrentHoldings
              SET CurrentShares = $currentShares,
                  CurrentPrice = $currentPrice,
                  CurrentExchangeRate = $currentExchangeRate,
                  ExchangeRateAcquiredAt = $exchangeRateAcquiredAt,
                  ExchangeRateSource = $exchangeRateSource,
                  ExchangeRateInputType = $exchangeRateInputType,
                  AnnualDividendPerShare = $annualDividendPerShare,
                  DividendStatus = $dividendStatus,
                  DividendFrequency = $dividendFrequency,
                  DividendMonths = $dividendMonths,
                  CurrentPriceAcquiredAt = $currentPriceAcquiredAt,
                  CurrentPriceSource = $currentPriceSource,
                  DividendInfoAcquiredAt = $dividendInfoAcquiredAt,
                  DividendInfoSource = $dividendInfoSource,
                  UpdatedAt = $updatedAt
              WHERE Id = $id;
              """;
        command.Parameters.AddWithValue("$stockId", currentHolding.StockId);
        command.Parameters.AddWithValue("$currentShares", currentHolding.CurrentShares);
        command.Parameters.AddWithValue("$currentPrice", currentHolding.CurrentPrice);
        command.Parameters.AddWithValue("$currentExchangeRate", currentHolding.CurrentExchangeRate);
        command.Parameters.AddWithValue("$exchangeRateAcquiredAt", ToDateTimeText(currentHolding.ExchangeRateAcquiredAt));
        command.Parameters.AddWithValue("$exchangeRateSource", currentHolding.ExchangeRateSource);
        command.Parameters.AddWithValue("$exchangeRateInputType", currentHolding.ExchangeRateInputType);
        command.Parameters.AddWithValue("$annualDividendPerShare", currentHolding.AnnualDividendPerShare);
        command.Parameters.AddWithValue("$dividendStatus", string.IsNullOrWhiteSpace(currentHolding.DividendStatus) ? InferDividendStatus(currentHolding.AnnualDividendPerShare) : currentHolding.DividendStatus);
        command.Parameters.AddWithValue("$dividendFrequency", currentHolding.DividendFrequency);
        command.Parameters.AddWithValue("$dividendMonths", currentHolding.DividendMonths);
        command.Parameters.AddWithValue("$currentPriceAcquiredAt", ToDateTimeText(currentHolding.CurrentPriceAcquiredAt));
        command.Parameters.AddWithValue("$currentPriceSource", currentHolding.CurrentPriceSource);
        command.Parameters.AddWithValue("$dividendInfoAcquiredAt", ToDateTimeText(currentHolding.DividendInfoAcquiredAt));
        command.Parameters.AddWithValue("$dividendInfoSource", currentHolding.DividendInfoSource);
        command.Parameters.AddWithValue("$updatedAt", ToDateText(currentHolding.UpdatedAt));
        if (currentHolding.Id != 0)
        {
            command.Parameters.AddWithValue("$id", currentHolding.Id);
        }

        command.ExecuteNonQuery();
    }

    private static void SaveMutualFundHolding(SqliteConnection connection, SqliteTransaction transaction, MutualFundHolding fund)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO MutualFundHoldings
                (StockId, FundName, FundCode, AssociationCode, UnitsHeld, UnitBase, AverageCostNav, CurrentNav,
                 AcquisitionAmount, MarketValue, UnrealizedGainLoss, NavDate, NavSource, DistributionMethod,
                 AccountType, TotalPurchaseAmount, TotalSaleAmount, ReinvestedDistributionAmount, UpdatedAt)
            VALUES
                ($stockId, $fundName, $fundCode, $associationCode, $unitsHeld, $unitBase, $averageCostNav, $currentNav,
                 $acquisitionAmount, $marketValue, $unrealizedGainLoss, $navDate, $navSource, $distributionMethod,
                 $accountType, $totalPurchaseAmount, $totalSaleAmount, $reinvestedDistributionAmount, CURRENT_TIMESTAMP)
            ON CONFLICT(StockId) DO UPDATE SET
                FundName = excluded.FundName,
                FundCode = excluded.FundCode,
                AssociationCode = excluded.AssociationCode,
                UnitsHeld = excluded.UnitsHeld,
                UnitBase = excluded.UnitBase,
                AverageCostNav = excluded.AverageCostNav,
                CurrentNav = excluded.CurrentNav,
                AcquisitionAmount = excluded.AcquisitionAmount,
                MarketValue = excluded.MarketValue,
                UnrealizedGainLoss = excluded.UnrealizedGainLoss,
                NavDate = excluded.NavDate,
                NavSource = excluded.NavSource,
                DistributionMethod = excluded.DistributionMethod,
                AccountType = excluded.AccountType,
                TotalPurchaseAmount = excluded.TotalPurchaseAmount,
                TotalSaleAmount = excluded.TotalSaleAmount,
                ReinvestedDistributionAmount = excluded.ReinvestedDistributionAmount,
                UpdatedAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$stockId", fund.StockId);
        command.Parameters.AddWithValue("$fundName", fund.FundName);
        command.Parameters.AddWithValue("$fundCode", fund.FundCode);
        command.Parameters.AddWithValue("$associationCode", fund.AssociationCode);
        command.Parameters.AddWithValue("$unitsHeld", fund.UnitsHeld);
        command.Parameters.AddWithValue("$unitBase", MutualFundCalculator.NormalizeUnitBase(fund.UnitBase));
        command.Parameters.AddWithValue("$averageCostNav", fund.AverageCostNav);
        command.Parameters.AddWithValue("$currentNav", fund.CurrentNav);
        command.Parameters.AddWithValue("$acquisitionAmount", fund.AcquisitionAmount);
        command.Parameters.AddWithValue("$marketValue", fund.MarketValue);
        command.Parameters.AddWithValue("$unrealizedGainLoss", fund.UnrealizedGainLoss);
        command.Parameters.AddWithValue("$navDate", ToOptionalDateText(fund.NavDate));
        command.Parameters.AddWithValue("$navSource", fund.NavSource);
        command.Parameters.AddWithValue("$distributionMethod", fund.DistributionMethod);
        command.Parameters.AddWithValue("$accountType", fund.AccountType);
        command.Parameters.AddWithValue("$totalPurchaseAmount", fund.TotalPurchaseAmount);
        command.Parameters.AddWithValue("$totalSaleAmount", fund.TotalSaleAmount);
        command.Parameters.AddWithValue("$reinvestedDistributionAmount", fund.ReinvestedDistributionAmount);
        command.ExecuteNonQuery();
    }

    private static void InsertBrokerTrade(SqliteConnection connection, SqliteTransaction transaction, BrokerTrade trade)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO BrokerTrades
                (StockId, TradeDate, SettlementDate, Broker, AccountType, CustodyType, TradeType,
                 Quantity, SignedQuantity, UnitPrice, Currency, ExchangeRate, SettlementAmountJpy,
                 FeeJpy, TaxJpy, RealizedGainLoss, RealizedGainLossJpy, AfterTradeQuantity,
                 AfterTradeAverageCost, Source, SourceFile, CreatedAt)
            VALUES
                ($stockId, $tradeDate, $settlementDate, $broker, $accountType, $custodyType, $tradeType,
                 $quantity, $signedQuantity, $unitPrice, $currency, $exchangeRate, $settlementAmountJpy,
                 $feeJpy, $taxJpy, $realizedGainLoss, $realizedGainLossJpy, $afterTradeQuantity,
                 $afterTradeAverageCost, $source, $sourceFile, CURRENT_TIMESTAMP);
            """;
        command.Parameters.AddWithValue("$stockId", trade.StockId);
        command.Parameters.AddWithValue("$tradeDate", ToDateText(trade.TradeDate));
        command.Parameters.AddWithValue("$settlementDate", ToDateText(trade.SettlementDate));
        command.Parameters.AddWithValue("$broker", trade.Broker);
        command.Parameters.AddWithValue("$accountType", AccountTypeNormalizer.Normalize(trade.AccountType));
        command.Parameters.AddWithValue("$custodyType", trade.CustodyType);
        command.Parameters.AddWithValue("$tradeType", trade.TradeType);
        command.Parameters.AddWithValue("$quantity", trade.Quantity);
        command.Parameters.AddWithValue("$signedQuantity", trade.SignedQuantity);
        command.Parameters.AddWithValue("$unitPrice", trade.UnitPrice);
        command.Parameters.AddWithValue("$currency", NormalizeCurrency(trade.Currency));
        command.Parameters.AddWithValue("$exchangeRate", trade.ExchangeRate <= 0m ? 1m : trade.ExchangeRate);
        command.Parameters.AddWithValue("$settlementAmountJpy", trade.SettlementAmountJpy);
        command.Parameters.AddWithValue("$feeJpy", trade.FeeJpy);
        command.Parameters.AddWithValue("$taxJpy", trade.TaxJpy);
        command.Parameters.AddWithValue("$realizedGainLoss", trade.RealizedGainLoss);
        command.Parameters.AddWithValue("$realizedGainLossJpy", trade.RealizedGainLossJpy);
        command.Parameters.AddWithValue("$afterTradeQuantity", trade.AfterTradeQuantity);
        command.Parameters.AddWithValue("$afterTradeAverageCost", trade.AfterTradeAverageCost);
        command.Parameters.AddWithValue("$source", trade.Source);
        command.Parameters.AddWithValue("$sourceFile", trade.SourceFile);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<BrokerTrade> ReadBrokerTrades(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var trades = new List<BrokerTrade>();
        while (reader.Read())
        {
            trades.Add(new BrokerTrade
            {
                Id = reader.GetInt32(0),
                StockId = reader.GetInt32(1),
                TradeDate = ParseDate(GetString(reader, 2)),
                SettlementDate = ParseDate(GetString(reader, 3)),
                Broker = GetString(reader, 4),
                AccountType = GetStringOrDefault(reader, 5, AccountTypes.Unknown),
                CustodyType = GetString(reader, 6),
                TradeType = GetString(reader, 7),
                Quantity = GetDecimalOrZero(reader, 8),
                SignedQuantity = GetDecimalOrZero(reader, 9),
                UnitPrice = GetDecimalOrZero(reader, 10),
                Currency = GetStringOrDefault(reader, 11, "JPY"),
                ExchangeRate = GetDecimalOrDefault(reader, 12, 1m),
                SettlementAmountJpy = GetDecimalOrZero(reader, 13),
                FeeJpy = GetDecimalOrZero(reader, 14),
                TaxJpy = GetDecimalOrZero(reader, 15),
                RealizedGainLoss = GetDecimalOrZero(reader, 16),
                RealizedGainLossJpy = GetDecimalOrZero(reader, 17),
                AfterTradeQuantity = GetDecimalOrZero(reader, 18),
                AfterTradeAverageCost = GetDecimalOrZero(reader, 19),
                Source = GetString(reader, 20),
                SourceFile = GetString(reader, 21),
                CreatedAt = GetDateTimeOrDefault(reader, 22, DateTime.MinValue)
            });
        }

        return trades;
    }

    private static int InsertDividend(SqliteConnection connection, DividendPayment dividend)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DividendPayments
                (StockId, AccountType, TaxAccountType, PaymentDate, RecordDate, ExDividendDate, DeclaredDate,
                 FiscalYear, FiscalQuarter, Broker, DividendStatus, Source, SourceFile, SourceRowNumber,
                 SourcePriority, Quantity, DividendPerShare, GrossAmount, ForeignTaxAmount, DomesticTaxAmount,
                 TotalTaxAmount, TaxAmount, NetAmount, Currency, ExchangeRate, ExchangeRateAcquiredAt,
                 ExchangeRateSource, ExchangeRateInputType, GrossAmountJpy, ForeignTaxAmountJpy,
                 DomesticTaxAmountJpy, TotalTaxAmountJpy, NetAmountJpy, JpyAmount, IsTaxEstimated,
                 IsNisa, IsForeignStock, TaxProfileId, MatchedActualDividendId, ReplacedByDividendId,
                 CreatedAt, UpdatedAt, Memo)
            VALUES
                ($stockId, $accountType, $taxAccountType, $paymentDate, $recordDate, $exDividendDate, $declaredDate,
                 $fiscalYear, $fiscalQuarter, $broker, $dividendStatus, $source, $sourceFile, $sourceRowNumber,
                 $sourcePriority, $quantity, $dividendPerShare, $grossAmount, $foreignTaxAmount, $domesticTaxAmount,
                 $totalTaxAmount, $taxAmount, $netAmount, $currency, $exchangeRate, $exchangeRateAcquiredAt,
                 $exchangeRateSource, $exchangeRateInputType, $grossAmountJpy, $foreignTaxAmountJpy,
                 $domesticTaxAmountJpy, $totalTaxAmountJpy, $netAmountJpy, $jpyAmount, $isTaxEstimated,
                 $isNisa, $isForeignStock, $taxProfileId, $matchedActualDividendId, $replacedByDividendId,
                 $createdAt, $updatedAt, $memo);
            SELECT last_insert_rowid();
            """;
        AddDividendParameters(command, dividend);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int UpdateDividend(SqliteConnection connection, DividendPayment dividend)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DividendPayments
            SET StockId = $stockId,
                AccountType = $accountType,
                TaxAccountType = $taxAccountType,
                PaymentDate = $paymentDate,
                RecordDate = $recordDate,
                ExDividendDate = $exDividendDate,
                DeclaredDate = $declaredDate,
                FiscalYear = $fiscalYear,
                FiscalQuarter = $fiscalQuarter,
                Broker = $broker,
                DividendStatus = $dividendStatus,
                Source = $source,
                SourceFile = $sourceFile,
                SourceRowNumber = $sourceRowNumber,
                SourcePriority = $sourcePriority,
                Quantity = $quantity,
                DividendPerShare = $dividendPerShare,
                GrossAmount = $grossAmount,
                ForeignTaxAmount = $foreignTaxAmount,
                DomesticTaxAmount = $domesticTaxAmount,
                TotalTaxAmount = $totalTaxAmount,
                TaxAmount = $taxAmount,
                NetAmount = $netAmount,
                Currency = $currency,
                ExchangeRate = $exchangeRate,
                ExchangeRateAcquiredAt = $exchangeRateAcquiredAt,
                ExchangeRateSource = $exchangeRateSource,
                ExchangeRateInputType = $exchangeRateInputType,
                GrossAmountJpy = $grossAmountJpy,
                ForeignTaxAmountJpy = $foreignTaxAmountJpy,
                DomesticTaxAmountJpy = $domesticTaxAmountJpy,
                TotalTaxAmountJpy = $totalTaxAmountJpy,
                NetAmountJpy = $netAmountJpy,
                JpyAmount = $jpyAmount,
                IsTaxEstimated = $isTaxEstimated,
                IsNisa = $isNisa,
                IsForeignStock = $isForeignStock,
                TaxProfileId = $taxProfileId,
                MatchedActualDividendId = $matchedActualDividendId,
                ReplacedByDividendId = $replacedByDividendId,
                CreatedAt = $createdAt,
                UpdatedAt = $updatedAt,
                Memo = $memo
            WHERE Id = $id;
            """;
        AddDividendParameters(command, dividend);
        command.Parameters.AddWithValue("$id", dividend.Id);
        command.ExecuteNonQuery();
        return dividend.Id;
    }

    private static void AddStockParameters(SqliteCommand command, Stock stock)
    {
        command.Parameters.AddWithValue("$assetType", string.IsNullOrWhiteSpace(stock.AssetType) ? AssetTypes.Stock : stock.AssetType);
        command.Parameters.AddWithValue("$name", stock.Name);
        command.Parameters.AddWithValue("$ticker", stock.Ticker);
        command.Parameters.AddWithValue("$country", stock.Country);
        command.Parameters.AddWithValue("$currency", stock.Currency);
        command.Parameters.AddWithValue("$broker", stock.Broker);
        command.Parameters.AddWithValue("$accountType", AccountTypeNormalizer.Normalize(stock.AccountType));
        command.Parameters.AddWithValue("$custodyType", stock.CustodyType);
        command.Parameters.AddWithValue("$sector", stock.Sector);
        command.Parameters.AddWithValue("$industry", stock.Industry);
        command.Parameters.AddWithValue("$market", stock.Market);
        command.Parameters.AddWithValue("$dataSource", string.IsNullOrWhiteSpace(stock.DataSource) ? "手入力" : stock.DataSource);
        command.Parameters.AddWithValue("$memo", stock.Memo);
    }

    private static void AddDividendParameters(SqliteCommand command, DividendPayment dividend)
    {
        var currency = NormalizeCurrency(dividend.Currency);
        var exchangeRate = currency == "JPY" ? 1m : dividend.ExchangeRate <= 0m ? 1m : dividend.ExchangeRate;
        var accountType = DividendConstants.NormalizeAccountType(dividend.AccountType);
        var taxAccountType = DividendConstants.NormalizeAccountType(dividend.TaxAccountType);
        if (taxAccountType == DividendConstants.AccountUnknown)
        {
            taxAccountType = accountType;
        }

        var totalTaxAmount = dividend.TotalTaxAmount > 0m
            ? dividend.TotalTaxAmount
            : dividend.TaxAmount > 0m ? dividend.TaxAmount : dividend.ForeignTaxAmount + dividend.DomesticTaxAmount;
        var taxAmount = dividend.TaxAmount > 0m ? dividend.TaxAmount : totalTaxAmount;
        var grossAmount = dividend.GrossAmount > 0m ? dividend.GrossAmount : dividend.NetAmount + taxAmount;
        var netAmount = dividend.NetAmount > 0m ? dividend.NetAmount : Math.Max(0m, grossAmount - taxAmount);
        var dividendPerShare = dividend.DividendPerShare > 0m
            ? dividend.DividendPerShare
            : dividend.Quantity > 0m && grossAmount > 0m ? grossAmount / dividend.Quantity : 0m;
        var grossAmountJpy = dividend.GrossAmountJpy > 0m ? dividend.GrossAmountJpy : grossAmount * exchangeRate;
        var foreignTaxAmountJpy = dividend.ForeignTaxAmountJpy > 0m ? dividend.ForeignTaxAmountJpy : dividend.ForeignTaxAmount * exchangeRate;
        var domesticTaxAmountJpy = dividend.DomesticTaxAmountJpy > 0m ? dividend.DomesticTaxAmountJpy : dividend.DomesticTaxAmount * exchangeRate;
        var totalTaxAmountJpy = dividend.TotalTaxAmountJpy > 0m ? dividend.TotalTaxAmountJpy : taxAmount * exchangeRate;
        var netAmountJpy = dividend.NetAmountJpy > 0m
            ? dividend.NetAmountJpy
            : dividend.JpyAmount > 0m ? dividend.JpyAmount : netAmount * exchangeRate;
        var jpyAmount = dividend.JpyAmount > 0m ? dividend.JpyAmount : netAmountJpy;
        var source = string.IsNullOrWhiteSpace(dividend.Source) ? DividendConstants.SourceManual : dividend.Source.Trim();
        var sourcePriority = dividend.SourcePriority > 0 ? dividend.SourcePriority : ResolveDividendSourcePriority(source);
        var createdAt = dividend.CreatedAt == default || dividend.CreatedAt == DateTime.MinValue ? DateTime.Now : dividend.CreatedAt;
        var updatedAt = dividend.UpdatedAt == default || dividend.UpdatedAt == DateTime.MinValue ? DateTime.Now : dividend.UpdatedAt;

        command.Parameters.AddWithValue("$stockId", dividend.StockId);
        command.Parameters.AddWithValue("$accountType", accountType);
        command.Parameters.AddWithValue("$taxAccountType", taxAccountType);
        command.Parameters.AddWithValue("$paymentDate", ToDateText(dividend.PaymentDate));
        command.Parameters.AddWithValue("$recordDate", ToOptionalDateText(dividend.RecordDate));
        command.Parameters.AddWithValue("$exDividendDate", ToOptionalDateText(dividend.ExDividendDate));
        command.Parameters.AddWithValue("$declaredDate", ToOptionalDateText(dividend.DeclaredDate));
        command.Parameters.AddWithValue("$fiscalYear", dividend.FiscalYear == 0 ? dividend.PaymentDate.Year : dividend.FiscalYear);
        command.Parameters.AddWithValue("$fiscalQuarter", string.IsNullOrWhiteSpace(dividend.FiscalQuarter)
            ? $"Q{((dividend.PaymentDate.Month - 1) / 3) + 1}"
            : dividend.FiscalQuarter);
        command.Parameters.AddWithValue("$broker", dividend.Broker);
        command.Parameters.AddWithValue("$dividendStatus", string.IsNullOrWhiteSpace(dividend.DividendStatus)
            ? DividendConstants.Actual
            : dividend.DividendStatus.Trim());
        command.Parameters.AddWithValue("$source", source);
        command.Parameters.AddWithValue("$sourceFile", dividend.SourceFile);
        command.Parameters.AddWithValue("$sourceRowNumber", dividend.SourceRowNumber);
        command.Parameters.AddWithValue("$sourcePriority", sourcePriority);
        command.Parameters.AddWithValue("$quantity", dividend.Quantity);
        command.Parameters.AddWithValue("$dividendPerShare", dividendPerShare);
        command.Parameters.AddWithValue("$grossAmount", grossAmount);
        command.Parameters.AddWithValue("$foreignTaxAmount", dividend.ForeignTaxAmount);
        command.Parameters.AddWithValue("$domesticTaxAmount", dividend.DomesticTaxAmount);
        command.Parameters.AddWithValue("$totalTaxAmount", totalTaxAmount);
        command.Parameters.AddWithValue("$taxAmount", taxAmount);
        command.Parameters.AddWithValue("$netAmount", netAmount);
        command.Parameters.AddWithValue("$currency", currency);
        command.Parameters.AddWithValue("$exchangeRate", exchangeRate);
        command.Parameters.AddWithValue("$exchangeRateAcquiredAt", ToDateTimeText(dividend.ExchangeRateAcquiredAt));
        command.Parameters.AddWithValue("$exchangeRateSource", string.IsNullOrWhiteSpace(dividend.ExchangeRateSource) ? "手入力" : dividend.ExchangeRateSource);
        command.Parameters.AddWithValue("$exchangeRateInputType", string.IsNullOrWhiteSpace(dividend.ExchangeRateInputType) ? "手入力" : dividend.ExchangeRateInputType);
        command.Parameters.AddWithValue("$grossAmountJpy", grossAmountJpy);
        command.Parameters.AddWithValue("$foreignTaxAmountJpy", foreignTaxAmountJpy);
        command.Parameters.AddWithValue("$domesticTaxAmountJpy", domesticTaxAmountJpy);
        command.Parameters.AddWithValue("$totalTaxAmountJpy", totalTaxAmountJpy);
        command.Parameters.AddWithValue("$netAmountJpy", netAmountJpy);
        command.Parameters.AddWithValue("$jpyAmount", jpyAmount);
        command.Parameters.AddWithValue("$isTaxEstimated", dividend.IsTaxEstimated ? 1 : 0);
        command.Parameters.AddWithValue("$isNisa", dividend.IsNisa || DividendConstants.IsNisaAccount(accountType) || DividendConstants.IsNisaAccount(taxAccountType) ? 1 : 0);
        command.Parameters.AddWithValue("$isForeignStock", dividend.IsForeignStock || currency != "JPY" ? 1 : 0);
        command.Parameters.AddWithValue("$taxProfileId", dividend.TaxProfileId.HasValue ? dividend.TaxProfileId.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$matchedActualDividendId", dividend.MatchedActualDividendId.HasValue ? dividend.MatchedActualDividendId.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$replacedByDividendId", dividend.ReplacedByDividendId.HasValue ? dividend.ReplacedByDividendId.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", ToDateTimeText(createdAt));
        command.Parameters.AddWithValue("$updatedAt", ToDateTimeText(updatedAt));
        command.Parameters.AddWithValue("$memo", dividend.Memo);
    }

    private static string GetString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static string GetStringOrDefault(SqliteDataReader reader, int ordinal, string defaultValue) =>
        reader.IsDBNull(ordinal) || string.IsNullOrWhiteSpace(reader.GetString(ordinal))
            ? defaultValue
            : reader.GetString(ordinal);

    private static int GetInt32OrZero(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));

    private static decimal GetDecimalOrZero(SqliteDataReader reader, int ordinal) =>
        GetDecimalOrDefault(reader, ordinal, 0m);

    private static decimal GetDecimalOrDefault(SqliteDataReader reader, int ordinal, decimal defaultValue) =>
        reader.IsDBNull(ordinal) ? defaultValue : Convert.ToDecimal(reader.GetValue(ordinal));

    private static DateTime GetDateOrToday(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? DateTime.Today : ParseDate(reader.GetString(ordinal));

    private static DateTime GetDateTimeOrDefault(SqliteDataReader reader, int ordinal, DateTime defaultValue) =>
        reader.IsDBNull(ordinal) ? defaultValue : ParseDateTime(reader.GetString(ordinal), defaultValue);

    private static string GetDividendStatus(SqliteDataReader reader, int ordinal, decimal annualDividendPerShare)
    {
        var value = GetString(reader, ordinal);
        return string.IsNullOrWhiteSpace(value) ? InferDividendStatus(annualDividendPerShare) : value;
    }

    private static string InferDividendStatus(decimal annualDividendPerShare) =>
        annualDividendPerShare > 0m ? "配当あり" : "配当なし";

    private static string GetSetting(IReadOnlyDictionary<string, string> values, string key, string defaultValue) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : defaultValue;

    private static int GetIntSetting(IReadOnlyDictionary<string, string> values, string key, int defaultValue) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static bool GetBoolSetting(IReadOnlyDictionary<string, string> values, string key, bool defaultValue) =>
        values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static void UpsertSetting(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static string RedactSecret(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = value;
        foreach (var marker in new[] { "apikey=", "token=", "password=", "refreshToken=", "idToken=" })
        {
            var index = redacted.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var start = index + marker.Length;
                var end = redacted.IndexOfAny(new[] { '&', ' ', '"' }, start);
                if (end < 0)
                {
                    end = redacted.Length;
                }

                redacted = redacted.Remove(start, end - start).Insert(start, "***");
                index = redacted.IndexOf(marker, start + 3, StringComparison.OrdinalIgnoreCase);
            }
        }

        return redacted;
    }

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, out var date) ? date.Date : DateTime.Today;

    private static DateTime ParseDateTime(string value, DateTime defaultValue) =>
        DateTime.TryParse(value, out var date) ? date : defaultValue;

    private static string ToDateText(DateTime date) => date.ToString("yyyy-MM-dd");

    private static string ToOptionalDateText(DateTime date) =>
        date == default || date == DateTime.MinValue ? string.Empty : ToDateText(date);

    private static string ToDateTimeText(DateTime date) => date.ToString("yyyy-MM-dd HH:mm:ss");

    private static string NormalizeCurrency(string currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency) ? "JPY" : currency.Trim().ToUpperInvariant();
        return normalized is "YEN" or "円" ? "JPY" : normalized;
    }

    private static string BuildPositionKey(string broker, string ticker, string accountType)
    {
        var normalizedBroker = SecuritySymbolNormalizer.NormalizeBroker(broker);
        var normalizedTicker = SecuritySymbolNormalizer.NormalizeTicker(ticker);
        var normalizedAccount = AccountTypeNormalizer.Normalize(accountType);
        return $"{normalizedBroker}|{normalizedTicker}|{normalizedAccount}";
    }

    private static int ResolveDividendSourcePriority(string source)
    {
        if (source.Equals(DividendConstants.SourceCsv, StringComparison.OrdinalIgnoreCase) ||
            source.Equals(DividendConstants.SourceImportedFromBroker, StringComparison.OrdinalIgnoreCase) ||
            source.Contains("CSV", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (source.Equals(DividendConstants.SourceApi, StringComparison.OrdinalIgnoreCase))
        {
            return 70;
        }

        if (source.Equals(DividendConstants.SourceEstimatedFromAnnualDividend, StringComparison.OrdinalIgnoreCase) ||
            source.Equals(DividendConstants.SourceEstimatedFromHistory, StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        return 50;
    }
}
