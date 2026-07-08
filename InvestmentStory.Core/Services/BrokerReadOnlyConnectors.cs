using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

public sealed class SbiReadOnlyConnector : IBrokerReadOnlyConnector
{
    public string ProviderName => "SBI証券";
    public bool IsReadOnly => true;

    public IReadOnlyList<BrokerHoldingRecord> GetHoldings() =>
        throw new NotSupportedException("SBI証券の画面取得/API連携は未実装です。CSV取込または手入力を使用してください。");
}

public sealed class NomuraReadOnlyConnector : IBrokerReadOnlyConnector
{
    public string ProviderName => "野村證券";
    public bool IsReadOnly => true;

    public IReadOnlyList<BrokerHoldingRecord> GetHoldings() =>
        throw new NotSupportedException("野村證券の画面取得/API連携は未実装です。CSV取込または手入力を使用してください。");
}
