namespace PickMoney.App.Models;

public class AppConfig
{
    public List<AccountConfig> Accounts { get; set; } = new();
    public StrategyConfig Strategy { get; set; } = new();
    public NotificationConfig Notification { get; set; } = new();
}
