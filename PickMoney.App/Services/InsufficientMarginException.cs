namespace PickMoney.App.Services;

/// <summary>
/// 币安返回 -2019 "Margin is insufficient."（保证金不足）时抛出。
/// 1 倍杠杆 + 逐仓模式下，初始保证金约等于名义市值本身，
/// 因此该错误通常意味着账户可用余额已被既有持仓占用，属于结构性不足，重试无意义。
/// </summary>
public sealed class InsufficientMarginException : Exception
{
    public InsufficientMarginException(string message) : base(message)
    {
    }
}
