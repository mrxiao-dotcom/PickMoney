namespace PickMoney.App.Models;

/// <summary>
/// 单个账户的每日净值/权益历史记录。
/// </summary>
public class AccountNetValueHistory
{
    /// <summary>账户 Id</summary>
    public Guid AccountId { get; set; }

    /// <summary>账户名（便于人工识别）</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>该账户记录的初始资金（用于计算净值）</summary>
    public decimal InitialCapital { get; set; }

    /// <summary>按日期升序排列的每日快照</summary>
    public List<DailyNetValueRecord> Records { get; set; } = new();
}

/// <summary>
/// 某一天的净值/权益快照。
/// </summary>
public class DailyNetValueRecord
{
    /// <summary>日期</summary>
    public DateOnly Date { get; set; }

    /// <summary>当日收盘时的账户权益</summary>
    public decimal Equity { get; set; }

    /// <summary>当日净值 = 权益 / 初始资金</summary>
    public decimal NetValue { get; set; }

    /// <summary>记录时间</summary>
    public DateTime RecordedAt { get; set; }
}
