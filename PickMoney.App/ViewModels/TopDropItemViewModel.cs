namespace PickMoney.App.ViewModels;

public class TopDropItemViewModel
{
    public string Symbol { get; set; } = string.Empty;
    public decimal DropPercent { get; set; }
    public decimal ThresholdGap { get; set; }
    public decimal LastPrice { get; set; }
    public decimal OpenPrice { get; set; }
}
