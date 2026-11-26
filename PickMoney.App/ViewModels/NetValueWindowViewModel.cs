using System.Collections.ObjectModel;
using System.Globalization;
using PickMoney.App.Models;
using PickMoney.App.Services;

namespace PickMoney.App.ViewModels;

/// <summary>
/// 账户净值弹窗的 ViewModel：日历 + 净值曲线。
/// </summary>
public class NetValueWindowViewModel : ObservableObject
{
    private readonly NetValueHistoryService _historyService;
    private readonly Guid _accountId;
    private readonly string _accountName;

    private string _displayMonth = string.Empty;
    private DateOnly _monthAnchor;
    private CalendarDayViewModel? _selectedDay;
    private AccountNetValueHistory _history = new();

    public NetValueWindowViewModel(
        NetValueHistoryService historyService,
        Guid accountId,
        string accountName,
        decimal initialCapital)
    {
        _historyService = historyService;
        _accountId = accountId;
        _accountName = accountName;
        InitialCapital = initialCapital;

        _monthAnchor = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);

        PreviousMonthCommand = new RelayCommand(_ => ShiftMonth(-1));
        NextMonthCommand = new RelayCommand(_ => ShiftMonth(1));
        TodayCommand = new RelayCommand(_ => GoToday());
    }

    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }

    /// <summary>日历格子集合（按周排列，7 列）</summary>
    public ObservableCollection<CalendarDayViewModel> Days { get; } = new();

    /// <summary>净值曲线数据点</summary>
    public ObservableCollection<NetValuePoint> CurvePoints { get; } = new();

    /// <summary>初始资金</summary>
    public decimal InitialCapital { get; }

    /// <summary>账户名</summary>
    public string AccountName => _accountName;

    /// <summary>当前显示的月份文本</summary>
    public string DisplayMonth
    {
        get => _displayMonth;
        private set => SetProperty(ref _displayMonth, value);
    }

    /// <summary>是否有历史数据</summary>
    public bool HasData => CurvePoints.Count > 0;

    /// <summary>最新净值</summary>
    public decimal? LatestNetValue => CurvePoints.Count > 0 ? CurvePoints[^1].NetValue : null;

    /// <summary>最新净值文本</summary>
    public string LatestNetValueText => LatestNetValue?.ToString("F3", CultureInfo.InvariantCulture) ?? "--";

    /// <summary>最新净值是否亏损（小于 1.0 显示绿色）</summary>
    public bool IsLatestLoss => LatestNetValue.HasValue && LatestNetValue.Value < 1.0m;

    /// <summary>最新净值是否盈利</summary>
    public bool IsLatestProfit => LatestNetValue.HasValue && LatestNetValue.Value >= 1.0m;

    /// <summary>最新净值颜色</summary>
    public string LatestNetValueBrush => IsLatestLoss ? "#16A34A" : IsLatestProfit ? "#DC2626" : "#64748B";

    /// <summary>初始净值基准（1.000）</summary>
    public decimal BaselineNetValue => 1.0m;

    /// <summary>净值区间最小值</summary>
    public decimal MinNetValue => CurvePoints.Count > 0 ? CurvePoints.Min(item => item.NetValue) : 0m;

    /// <summary>净值区间最大值</summary>
    public decimal MaxNetValue => CurvePoints.Count > 0 ? CurvePoints.Max(item => item.NetValue) : 1m;

    /// <summary>曲线区间的下界（留出边距并保证包含基准线 1.0）</summary>
    public decimal ChartMinValue
    {
        get
        {
            if (CurvePoints.Count == 0)
            {
                return 0.9m;
            }

            var min = Math.Min(MinNetValue, BaselineNetValue);
            var max = Math.Max(MaxNetValue, BaselineNetValue);
            var padding = Math.Max((max - min) * 0.15m, 0.01m);
            return min - padding;
        }
    }

    /// <summary>曲线区间的上界</summary>
    public decimal ChartMaxValue
    {
        get
        {
            if (CurvePoints.Count == 0)
            {
                return 1.1m;
            }

            var min = Math.Min(MinNetValue, BaselineNetValue);
            var max = Math.Max(MaxNetValue, BaselineNetValue);
            var padding = Math.Max((max - min) * 0.15m, 0.01m);
            return max + padding;
        }
    }

    /// <summary>选中日的详情</summary>
    public CalendarDayViewModel? SelectedDay
    {
        get => _selectedDay;
        set
        {
            if (!SetProperty(ref _selectedDay, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedDayTitle));
            OnPropertyChanged(nameof(SelectedDayNetValue));
            OnPropertyChanged(nameof(SelectedDayEquity));
            OnPropertyChanged(nameof(SelectedDayChange));
            OnPropertyChanged(nameof(SelectedDayHasData));
            OnPropertyChanged(nameof(SelectedDayBrush));
        }
    }

    /// <summary>选中日标题</summary>
    public string SelectedDayTitle => SelectedDay is null
        ? ""
        : SelectedDay.Date.ToString("yyyy年MM月dd日", CultureInfo.InvariantCulture);

    /// <summary>选中日净值</summary>
    public string SelectedDayNetValue => SelectedDay?.NetValueText ?? "--";

    /// <summary>选中日权益</summary>
    public string SelectedDayEquity => SelectedDay?.EquityText ?? "--";

    /// <summary>选中日涨跌</summary>
    public string SelectedDayChange => SelectedDay?.ChangeText ?? "";

    /// <summary>选中日是否有数据</summary>
    public bool SelectedDayHasData => SelectedDay?.HasData ?? false;

    /// <summary>选中日涨跌颜色</summary>
    public string SelectedDayBrush => SelectedDay?.ChangeBrush ?? "#64748B";

    /// <summary>加载历史数据并刷新日历与曲线</summary>
    public async Task LoadAsync()
    {
        _history = await _historyService.LoadAsync(_accountId);

        var snapshots = _history.Records
            .OrderBy(item => item.Date)
            .Select(item => new DailyNetValueSnapshot
            {
                Date = item.Date,
                Equity = item.Equity,
                NetValue = item.NetValue
            })
            .ToList();

        CurvePoints.Clear();
        foreach (var snapshot in snapshots)
        {
            CurvePoints.Add(new NetValuePoint
            {
                Date = snapshot.Date,
                NetValue = snapshot.NetValue,
                Equity = snapshot.Equity
            });
        }

        BuildCalendar(snapshots);

        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(LatestNetValue));
        OnPropertyChanged(nameof(LatestNetValueText));
        OnPropertyChanged(nameof(IsLatestLoss));
        OnPropertyChanged(nameof(IsLatestProfit));
        OnPropertyChanged(nameof(LatestNetValueBrush));
        OnPropertyChanged(nameof(MinNetValue));
        OnPropertyChanged(nameof(MaxNetValue));
        OnPropertyChanged(nameof(ChartMinValue));
        OnPropertyChanged(nameof(ChartMaxValue));
    }

    private void ShiftMonth(int offset)
    {
        _monthAnchor = _monthAnchor.AddMonths(offset);
        BuildCalendar(CurrentSnapshots);
    }

    private void GoToday()
    {
        var now = DateTime.Now;
        _monthAnchor = new DateOnly(now.Year, now.Month, 1);
        BuildCalendar(CurrentSnapshots);
    }

    private List<DailyNetValueSnapshot> CurrentSnapshots => _history.Records
        .OrderBy(item => item.Date)
        .Select(item => new DailyNetValueSnapshot
        {
            Date = item.Date,
            Equity = item.Equity,
            NetValue = item.NetValue
        })
        .ToList();

    /// <summary>
    /// 构建日历网格。按周对齐（周一起始），补齐前后月份的空白格。
    /// </summary>
    private void BuildCalendar(List<DailyNetValueSnapshot> snapshots)
    {
        DisplayMonth = _monthAnchor.ToString("yyyy 年 MM 月", CultureInfo.InvariantCulture);

        var byDate = snapshots.ToDictionary(item => item.Date);
        var sorted = snapshots.OrderBy(item => item.Date).ToList();

        // 周一为一周起点
        var firstDayOfWeek = _monthAnchor.AddDays(-(((int)_monthAnchor.DayOfWeek + 6) % 7));
        var lastDayOfMonth = _monthAnchor.AddMonths(1).AddDays(-1);

        // 补齐到完整周（周日结束）：先前进到本周日，再推到 6 行整
        var lastDayOfWeek = lastDayOfMonth.AddDays(6 - (int)lastDayOfMonth.DayOfWeek);
        var totalCells = (int)Math.Ceiling((lastDayOfWeek.DayNumber - firstDayOfWeek.DayNumber + 1) / 7.0) * 7;
        lastDayOfWeek = firstDayOfWeek.AddDays(totalCells - 1);

        Days.Clear();

        for (var date = firstDayOfWeek; date <= lastDayOfWeek; date = date.AddDays(1))
        {
            byDate.TryGetValue(date, out var snapshot);

            // 前一交易日：取该日期之前最近的一条记录
            DailyNetValueSnapshot? previous = null;
            foreach (var item in sorted)
            {
                if (item.Date < date)
                {
                    previous = item;
                }
                else
                {
                    break;
                }
            }

            Days.Add(new CalendarDayViewModel(date, date.Month == _monthAnchor.Month, snapshot, previous));
        }

        // 默认选中今天（有数据时优先选有数据的最近一天）
        var today = DateOnly.FromDateTime(DateTime.Now);
        var target = Days.FirstOrDefault(item => item.Date == today && item.HasData)
            ?? Days.LastOrDefault(item => item.HasData)
            ?? Days.FirstOrDefault(item => item.Date == today);

        foreach (var day in Days)
        {
            day.IsSelected = ReferenceEquals(day, target);
        }

        SelectedDay = target;
    }
}
