namespace PickMoney.App.ViewModels;

/// <summary>
/// 净值曲线上的一个数据点。
/// </summary>
public class NetValuePoint
{
    public DateOnly Date { get; set; }
    public decimal NetValue { get; set; }
    public decimal Equity { get; set; }
}
