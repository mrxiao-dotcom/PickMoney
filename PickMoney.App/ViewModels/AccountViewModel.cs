using System.Globalization;
using PickMoney.App.Models;

using PickMoney.App.Services;

namespace PickMoney.App.ViewModels;

public class AccountViewModel : ObservableObject
{
    private string _accountName = string.Empty;
    private string _apiKey = string.Empty;
    private string _secretKey = string.Empty;
    private int _allocationParts = 5;
    private decimal _buyAmount = 100m;
    private decimal _initialCapital;
    private bool _enableOpenPosition = true;
    private bool _enableTakeProfit = true;
    private bool _isSelected;
    private decimal _currentEquity;
    private bool _hasEquity;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountName
    {
        get => _accountName;
        set
        {
            if (SetProperty(ref _accountName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    public string SecretKey
    {
        get => _secretKey;
        set => SetProperty(ref _secretKey, value);
    }

    public int AllocationParts
    {
        get => _allocationParts;
        set
        {
            if (SetProperty(ref _allocationParts, value))
            {
                OnPropertyChanged(nameof(UnitAmount));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public decimal BuyAmount
    {
        get => _buyAmount;
        set
        {
            if (SetProperty(ref _buyAmount, value))
            {
                OnPropertyChanged(nameof(UnitAmount));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool EnableOpenPosition
    {
        get => _enableOpenPosition;
        set
        {
            if (SetProperty(ref _enableOpenPosition, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public bool EnableTakeProfit
    {
        get => _enableTakeProfit;
        set
        {
            if (SetProperty(ref _enableTakeProfit, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>账户初始资金（用于计算净值）</summary>
    public decimal InitialCapital
    {
        get => _initialCapital;
        set
        {
            if (SetProperty(ref _initialCapital, value))
            {
                NotifyEquityChanged();
            }
        }
    }

    /// <summary>分摊后的单币买入金额</summary>
    public decimal UnitAmount => AllocationParts <= 0
        ? BuyAmount
        : decimal.Round(BuyAmount / AllocationParts, 2, MidpointRounding.AwayFromZero);

    /// <summary>当前账户权益，由主窗口刷新时写入</summary>
    public decimal CurrentEquity
    {
        get => _currentEquity;
        private set
        {
            if (SetProperty(ref _currentEquity, value))
            {
                NotifyEquityChanged();
            }
        }
    }

    /// <summary>是否已取到账户权益</summary>
    public bool HasEquity
    {
        get => _hasEquity;
        private set
        {
            if (SetProperty(ref _hasEquity, value))
            {
                NotifyEquityChanged();
            }
        }
    }

    /// <summary>写入最新的账户权益，驱动净值显示刷新</summary>
    public void UpdateEquity(decimal equity)
    {
        CurrentEquity = equity;
        HasEquity = true;
    }

    private void NotifyEquityChanged()
    {
        OnPropertyChanged(nameof(NetValue));
        OnPropertyChanged(nameof(NetValueText));
        OnPropertyChanged(nameof(IsNetValueLoss));
        OnPropertyChanged(nameof(IsNetValueProfit));
        OnPropertyChanged(nameof(EquityText));
    }

    /// <summary>净值 = 账户权益 / 初始资金。未配置初始资金或未取到权益时为 null</summary>
    public decimal? NetValue
    {
        get
        {
            if (!HasEquity || InitialCapital <= 0)
            {
                return null;
            }

            return CurrentEquity / InitialCapital;
        }
    }

    /// <summary>净值显示文本</summary>
    public string NetValueText => NetValue.HasValue
        ? NetValue.Value.ToString("F3", CultureInfo.InvariantCulture)
        : "--";

    /// <summary>净值是否亏损（小于 1.0 显示绿色）</summary>
    public bool IsNetValueLoss => NetValue.HasValue && NetValue.Value < 1.0m;

    /// <summary>净值是否盈利（大于等于 1.0 显示红色）</summary>
    public bool IsNetValueProfit => NetValue.HasValue && NetValue.Value >= 1.0m;

    /// <summary>账户权益显示文本</summary>
    public string EquityText => HasEquity
        ? CurrentEquity.ToString("F2", CultureInfo.InvariantCulture)
        : "--";

    /// <summary>单行显示用的账户摘要</summary>
    public string SummaryText =>
        $"分摊 {AllocationParts} | 单币 {UnitAmount:F2}";

    /// <summary>账户名（含启用状态标记）</summary>
    public string DisplayName =>
        $"{AccountName}{(EnableOpenPosition || EnableTakeProfit ? "" : "  [已停用]")}";

    /// <summary>打开净值弹窗的命令，由主窗口注入</summary>
    public RelayCommand? ShowNetValueCommand { get; set; }

    public static AccountViewModel FromModel(AccountConfig model)
    {
        return new AccountViewModel
        {
            Id = model.Id,
            AccountName = model.AccountName,
            ApiKey = model.ApiKey,
            SecretKey = model.SecretKey,
            AllocationParts = model.AllocationParts,
            BuyAmount = model.BuyAmount,
            InitialCapital = model.InitialCapital,
            EnableOpenPosition = model.EnableOpenPosition,
            EnableTakeProfit = model.EnableTakeProfit
        };
    }

    public AccountConfig ToModel()
    {
        return new AccountConfig
        {
            Id = Id,
            AccountName = AccountName,
            ApiKey = ApiKey,
            SecretKey = SecretKey,
            AllocationParts = AllocationParts,
            BuyAmount = BuyAmount,
            InitialCapital = InitialCapital,
            EnableOpenPosition = EnableOpenPosition,
            EnableTakeProfit = EnableTakeProfit
        };
    }
}
