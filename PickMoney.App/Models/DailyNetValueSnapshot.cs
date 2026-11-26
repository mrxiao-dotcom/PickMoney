namespace PickMoney.App.Models;

/// <summary>
/// 某一天的净值/权益快照（用于展示）。
/// </summary>
public class DailyNetValueSnapshot
{
    public DateOnly Date { get; set; }
    public decimal Equity { get; set; }
    public decimal NetValue { get; set; }
}
