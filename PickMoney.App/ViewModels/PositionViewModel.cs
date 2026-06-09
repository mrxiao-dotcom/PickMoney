using PickMoney.App.Models;

namespace PickMoney.App.ViewModels;

public class PositionViewModel
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal MarketValue { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal PnlPercent { get; set; }

    public static PositionViewModel FromModel(PositionInfo model)
    {
        return new PositionViewModel
        {
            Symbol = model.Symbol,
            Quantity = model.Quantity,
            EntryPrice = model.EntryPrice,
            MarkPrice = model.MarkPrice,
            MarketValue = model.MarketValue,
            UnrealizedPnl = model.UnrealizedPnl,
            PnlPercent = model.PnlPercent
        };
    }
}
