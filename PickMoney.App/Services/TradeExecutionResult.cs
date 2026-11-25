namespace PickMoney.App.Services;

internal sealed class TradeExecutionResult
{
    public string Symbol { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal DropPercent { get; init; }
    public decimal MarketValue { get; init; }
    public decimal TargetValue { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal PnlPercent { get; init; }
    public decimal PriceMultiplier { get; init; }
}
