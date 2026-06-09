namespace PickMoney.App.Models;

public class PositionInfo
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal MarketValue { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal PnlPercent { get; set; }
}
