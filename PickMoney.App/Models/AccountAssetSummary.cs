namespace PickMoney.App.Models;

public class AccountAssetSummary
{
    public decimal WalletBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public decimal PositionMarketValue { get; set; }
    public int PositionSymbolCount { get; set; }
}
