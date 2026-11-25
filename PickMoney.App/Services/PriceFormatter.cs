using System.Globalization;

namespace PickMoney.App.Services;

/// <summary>
/// 价格显示格式化。
/// 规则（按绝对值从小到大分段，越小的小数位越多）：
///   &lt; 0.0001  -> 5 位小数
///   &lt; 0.01    -> 4 位小数
///   &gt;= 0.01   -> 3 位小数
/// </summary>
public static class PriceFormatter
{
    public static string Format(decimal value)
    {
        var absolute = Math.Abs(value);

        var digits = absolute switch
        {
            < 0.0001m => 5,
            < 0.01m => 4,
            _ => 3
        };

        return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }
}
