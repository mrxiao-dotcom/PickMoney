namespace PickMoney.App.Models;

public class NotificationConfig
{
    public List<string> FeishuWebhooks { get; set; } = new();
    public NotificationState State { get; set; } = new();
}
