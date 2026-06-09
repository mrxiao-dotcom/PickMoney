namespace PickMoney.App.Services;

public class LogMessage
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "INFO";
    public string AccountName { get; set; } = "SYSTEM";
    public string Message { get; set; } = string.Empty;
}
