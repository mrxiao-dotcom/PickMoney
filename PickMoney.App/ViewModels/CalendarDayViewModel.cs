using System.Globalization;
using PickMoney.App.Models;

namespace PickMoney.App.ViewModels;

/// <summary>
/// 日历中的一个日期格子。
/// </summary>
public class CalendarDayViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isToday;

    public CalendarDayViewModel(DateOnly date, bool isCurrentMonth, DailyNetValueSnapshot? snapshot, DailyNetValueSnapshot? previous)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        Snapshot = snapshot;
        Previous = previous;
        _isToday = date == DateOnly.FromDateTime(DateTime.Now);
    }

    public DateOnly Date { get; }

    /// <summary>是否属于当前显示的月份</summary>
    public bool IsCurrentMonth { get; }

    /// <summary>当日快照，无数据时为 null</summary>
    public DailyNetValueSnapshot? Snapshot { get; }

    /// <summary>前一日快照，用于判断涨跌</summary>
    public DailyNetValueSnapshot? Previous { get; }

    /// <summary>是否选中</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>是否为今天</summary>
    public bool IsToday
    {
        get => _isToday;
        set => SetProperty(ref _isToday, value);
    }

    /// <summary>日期数字（居中大字）</summary>
    public string DayText => Date.Day.ToString(CultureInfo.InvariantCulture);

    /// <summary>该日是否有数据</summary>
    public bool HasData => Snapshot is not null;

    /// <summary>当日净值显示文本</summary>
    public string NetValueText => Snapshot is null
        ? ""
        : Snapshot.NetValue.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>当日权益显示文本</summary>
    public string EquityText => Snapshot is null
        ? ""
        : Snapshot.Equity.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>相对前一交易日的涨跌额</summary>
    public decimal? ChangeAmount =>
        Snapshot is null || Previous is null
            ? null
            : Snapshot.NetValue - Previous.NetValue;

    /// <summary>相对前一交易日的涨跌幅（百分比）</summary>
    public decimal? ChangePercent =>
        Snapshot is null || Previous is null || Previous.NetValue == 0
            ? null
            : (Snapshot.NetValue - Previous.NetValue) / Previous.NetValue * 100m;

    /// <summary>涨跌显示文本</summary>
    public string ChangeText
    {
        get
        {
            var change = ChangeAmount;
            if (!change.HasValue)
            {
                return "";
            }

            var percent = ChangePercent ?? 0m;
            var sign = change.Value > 0 ? "+" : "";
            return $"{sign}{change.Value:F3} ({sign}{percent:F2}%)";
        }
    }

    /// <summary>是否为上涨日（相对前一交易日净值）</summary>
    public bool IsUp => ChangeAmount.HasValue && ChangeAmount.Value > 0;

    /// <summary>是否为下跌日</summary>
    public bool IsDown => ChangeAmount.HasValue && ChangeAmount.Value < 0;

    /// <summary>是否持平</summary>
    public bool IsFlat => ChangeAmount.HasValue && ChangeAmount.Value == 0;

    /// <summary>涨日底色（红）</summary>
    public string BackgroundBrush => IsUp ? "#FDECEC" : IsDown ? "#E8F7EE" : "Transparent";

    /// <summary>涨日边框色（红）</summary>
    public string BorderBrush => IsUp ? "#F5C2C2" : IsDown ? "#B7E4C7" : "Transparent";

    /// <summary>涨跌文字颜色</summary>
    public string ChangeBrush => IsUp ? "#DC2626" : IsDown ? "#16A34A" : "#64748B";
}
