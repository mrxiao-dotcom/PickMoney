namespace PickMoney.App.Models;

public class StrategyConfig
{
    public decimal TriggerDropPercent { get; set; } = 3m;
    public decimal TakeProfitMultiplier { get; set; } = 1.03m;
    public int ScanIntervalSeconds { get; set; } = 60;
}
