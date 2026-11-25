namespace PickMoney.App.Services;

/// <summary>
/// 币安返回 -2027 "Exceeded the maximum allowable position at current leverage."（超过最大允许持仓）时抛出。
/// 这是币安按杠杆档位设置的单品种持仓上限，重试无意义，需减少仓位或先平仓。
/// </summary>
public sealed class PositionLimitExceededException : Exception
{
    public PositionLimitExceededException(string message) : base(message)
    {
    }
}
