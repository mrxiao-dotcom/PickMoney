namespace PickMoney.App.Models;

public class AccountConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int AllocationParts { get; set; } = 5;
    public decimal BuyAmount { get; set; } = 100m;
    public bool EnableOpenPosition { get; set; } = true;
    public bool EnableTakeProfit { get; set; } = true;
}
