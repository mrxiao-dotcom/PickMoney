namespace PickMoney.App.Models;

public class AccountAssetSummary
{
    public decimal AccountEquity { get; set; }
    public decimal WalletBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal UnrealizedProfit { get; set; }
    public decimal PositionMarketValue { get; set; }
    public int PositionSymbolCount { get; set; }
}
