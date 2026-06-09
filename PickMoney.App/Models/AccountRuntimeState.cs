namespace PickMoney.App.Models;

public class AccountRuntimeState
{
    public Guid AccountId { get; set; }
    public DateTime LastScanTime { get; set; }
    public DateTime LastPositionCheckTime { get; set; }
    public List<PositionInfo> Positions { get; set; } = new();
}
