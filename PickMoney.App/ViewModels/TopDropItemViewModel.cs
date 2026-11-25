using PickMoney.App.Services;

namespace PickMoney.App.ViewModels;

public class TopDropItemViewModel
{
    public string Symbol { get; set; } = string.Empty;
    public decimal DropPercent { get; set; }
    public decimal ThresholdGap { get; set; }
    public decimal LastPrice { get; set; }
    public decimal OpenPrice { get; set; }

    /// <summary>现价（按价格规则格式化）</summary>
    public string LastPriceText => PriceFormatter.Format(LastPrice);

    /// <summary>开盘价（按价格规则格式化）</summary>
    public string OpenPriceText => PriceFormatter.Format(OpenPrice);
}
