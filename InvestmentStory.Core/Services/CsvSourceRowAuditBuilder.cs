using InvestmentStory.Core.Models;

namespace InvestmentStory.Core.Services;

internal sealed class CsvSourceRowAuditBuilder
{
    private int _input;
    private int _parsed;
    private int _pending;
    private int _excluded;
    private readonly List<CsvRowIssue> _issues = new();
    private readonly HashSet<int> _seenRows = new();

    public void Seen(int rowNumber)
    {
        if (!_seenRows.Add(rowNumber))
            throw new InvalidOperationException($"CSV {rowNumber}行目が複数回解析されました。");
        _input++;
    }

    public void Parsed() => _parsed++;

    public void Pending(int rowNumber, string reason)
    {
        _pending++;
        _issues.Add(new CsvRowIssue(rowNumber, "要確認", reason));
    }

    public void Excluded(int rowNumber, string reason)
    {
        _excluded++;
        _issues.Add(new CsvRowIssue(rowNumber, "対象外", reason));
    }

    public CsvSourceRowAudit Build()
    {
        if (_input != _parsed + _pending + _excluded)
            throw new InvalidOperationException(
                $"CSV明細の行数が一致しません: 入力 {_input}行、解析 {_parsed}行、要確認 {_pending}行、対象外 {_excluded}行。");
        return new CsvSourceRowAudit
        {
            InputRows = _input,
            ParsedRows = _parsed,
            PendingRows = _pending,
            ExcludedRows = _excluded,
            Issues = _issues
        };
    }
}
