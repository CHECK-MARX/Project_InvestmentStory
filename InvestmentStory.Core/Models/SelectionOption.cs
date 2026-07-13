namespace InvestmentStory.Core.Models;

public sealed class SelectionOption<T>
{
    public required T Value { get; init; }
    public required string DisplayName { get; init; }

    public override string ToString() => DisplayName;
}
