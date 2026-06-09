namespace PickMoney.App.Models;

public class NotificationState
{
    public int NextWebhookIndex { get; set; }
    public DateOnly? LastDailySummaryDate { get; set; }
}
