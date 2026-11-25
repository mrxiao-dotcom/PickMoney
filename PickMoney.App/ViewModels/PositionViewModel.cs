using PickMoney.App.Models;
using PickMoney.App.Services;

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

    /// <summary>开仓价（按价格规则格式化）</summary>
    public string EntryPriceText => PriceFormatter.Format(EntryPrice);

    /// <summary>标记价（按价格规则格式化）</summary>
    public string MarkPriceText => PriceFormatter.Format(MarkPrice);

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
