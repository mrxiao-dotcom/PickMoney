namespace PickMoney.App.Models;

/// <summary>
/// 本地缓存的单品种持仓上限数据。
/// 来源：/fapi/v1/leverageBracket。币安对该接口的档位更新不频繁，
/// 因此按 symbol 缓存到本地，避免每次下单都调用一次签名接口。
/// </summary>
public class PositionLimitCache
{
    /// <summary>缓存条目</summary>
    public List<PositionLimitEntry> Entries { get; set; } = new();

    /// <summary>最后一次成功拉取的时间</summary>
    public DateTime? LastRefreshedAt { get; set; }

    /// <summary>拉取失败次数，连续失败时不再重复请求</summary>
    public int ConsecutiveFailures { get; set; }
}

/// <summary>
/// 单个 symbol 的持仓上限缓存条目。
/// </summary>
public class PositionLimitEntry
{
    public string Symbol { get; set; } = string.Empty;

    /// <summary>最大允许持仓数量（张）</summary>
    public decimal MaxQuantity { get; set; }

    /// <summary>该上限对应的杠杆倍数</summary>
    public decimal Leverage { get; set; }

    /// <summary>上限对应的初始保证金率</summary>
    public decimal InitialMarginPercent { get; set; }

    /// <summary>缓存时间</summary>
    public DateTime CachedAt { get; set; }
}
