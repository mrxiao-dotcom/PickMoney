namespace PickMoney.App.Models;

/// <summary>
/// 币安单品种最大允许持仓信息（来自 /fapi/v1/leverageBracket）。
/// 币安按杠杆档位给出每个 symbol 的 maxQty（张），超过即报 -2027。
/// </summary>
public class PositionLimitInfo
{
    public string Symbol { get; set; } = string.Empty;

    /// <summary>最大允许持仓数量（张）</summary>
    public decimal MaxQuantity { get; set; }

    /// <summary>该上限对应的杠杆倍数</summary>
    public decimal Leverage { get; set; }

    /// <summary>上限对应的初始保证金率（百分比）</summary>
    public decimal InitialMarginPercent { get; set; }

    /// <summary>该 symbol 是否有有效的持仓上限信息</summary>
    public bool HasLimit => MaxQuantity > 0;

    /// <summary>生成可读的限额提示</summary>
    public string Describe()
    {
        if (!HasLimit)
        {
            return "未获取到持仓上限";
        }

        return $"最大允许持仓 {MaxQuantity:0.####} 张（杠杆 {Leverage:0.##} 倍档位）";
    }
}
