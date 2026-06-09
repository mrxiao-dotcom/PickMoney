namespace PickMoney.App.Models;

public class TickerSnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public decimal LastPrice { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal DropPercent { get; set; }
}
