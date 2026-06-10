using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PickMoney.App.Models;
using PickMoney.App.Services;

namespace PickMoney.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly TradingOrchestrator _tradingOrchestrator;
    private readonly IBinanceFuturesService _binanceService;
    private readonly NotificationService _notificationService;
    private readonly DispatcherTimer _scanTimer;
    private readonly StringBuilder _logTextBuilder = new();
    private AppConfig _currentConfig = new();
    private string _triggerDropPercent = "3";
    private string _takeProfitMultiplier = "1.03";
    private string _scanIntervalSeconds = "60";
    private string _feishuWebhooks = string.Empty;
    private string _manualSymbolsInput = string.Empty;
    private string _logText = string.Empty;
    private decimal _selectedAccountEquity;
    private decimal _selectedAccountWalletBalance;
    private decimal _selectedAccountAvailableBalance;
    private decimal _selectedAccountUnrealizedProfit;
    private decimal _selectedAccountPositionMarketValue;
    private int _selectedAccountPositionSymbolCount;
    private bool _isRunning;
    private bool _isImportingManualSymbols;
    private AccountViewModel? _selectedAccount;
    private CancellationTokenSource? _scanCancellation;

    public MainViewModel(ConfigService configService, TradingOrchestrator tradingOrchestrator, IBinanceFuturesService binanceService, NotificationService notificationService)
    {
        _configService = configService;
        _tradingOrchestrator = tradingOrchestrator;
        _binanceService = binanceService;
        _notificationService = notificationService;
        Accounts = new ObservableCollection<AccountViewModel>();
        Positions = new ObservableCollection<PositionViewModel>();
        Logs = new ObservableCollection<LogEntryViewModel>();
        TopDropTickers = new ObservableCollection<TopDropItemViewModel>();
        RuntimeStates = new ObservableCollection<AccountRuntimeState>();

        AddAccountCommand = new RelayCommand(_ => AddAccount());
        RemoveAccountCommand = new RelayCommand(_ => RemoveSelectedAccount(), _ => SelectedAccount is not null);
        OpenAccountSettingsCommand = new RelayCommand(_ => OpenAccountSettings());
        OpenStrategySettingsCommand = new RelayCommand(_ => OpenStrategySettings());
        SaveConfigCommand = new RelayCommand(async _ => await SaveConfigAsync());
        StartCommand = new RelayCommand(async _ => await StartAsync(), _ => !IsRunning);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsRunning);
        RefreshPositionsCommand = new RelayCommand(async _ => await RefreshSelectedAccountPositionsAsync(), _ => SelectedAccount is not null);
        ImportManualSymbolsCommand = new RelayCommand(async _ => await ImportManualSymbolsAsync(), _ => CanImportManualSymbols());

        _scanTimer = new DispatcherTimer();
        _scanTimer.Tick += async (_, _) => await ExecuteScanAsync();
    }

    public ObservableCollection<AccountViewModel> Accounts { get; }
    public ObservableCollection<PositionViewModel> Positions { get; }
    public ObservableCollection<LogEntryViewModel> Logs { get; }
    public ObservableCollection<TopDropItemViewModel> TopDropTickers { get; }
    public ObservableCollection<AccountRuntimeState> RuntimeStates { get; }

    public RelayCommand AddAccountCommand { get; }
    public RelayCommand RemoveAccountCommand { get; }
    public RelayCommand OpenAccountSettingsCommand { get; }
    public RelayCommand OpenStrategySettingsCommand { get; }
    public RelayCommand SaveConfigCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand RefreshPositionsCommand { get; }
    public RelayCommand ImportManualSymbolsCommand { get; }

    public string TriggerDropPercent
    {
        get => _triggerDropPercent;
        set
        {
            if (SetProperty(ref _triggerDropPercent, value))
            {
                OnPropertyChanged(nameof(StrategySummary));
            }
        }
    }

    public string TakeProfitMultiplier
    {
        get => _takeProfitMultiplier;
        set
        {
            if (SetProperty(ref _takeProfitMultiplier, value))
            {
                OnPropertyChanged(nameof(StrategySummary));
            }
        }
    }

    public string ScanIntervalSeconds
    {
        get => _scanIntervalSeconds;
        set
        {
            if (SetProperty(ref _scanIntervalSeconds, value))
            {
                OnPropertyChanged(nameof(StrategySummary));
            }
        }
    }

    public string FeishuWebhooks
    {
        get => _feishuWebhooks;
        set
        {
            if (SetProperty(ref _feishuWebhooks, value))
            {
                OnPropertyChanged(nameof(WebhookSummary));
            }
        }
    }

    public string ManualSymbolsInput
    {
        get => _manualSymbolsInput;
        set
        {
            if (SetProperty(ref _manualSymbolsInput, value))
            {
                ImportManualSymbolsCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ManualSymbolsSummary));
            }
        }
    }

    public string StrategySummary => $"触发跌幅 {TriggerDropPercent}% · 止盈倍数 {TakeProfitMultiplier} · 扫描间隔 {ScanIntervalSeconds} 秒";

    public string WebhookSummary
    {
        get
        {
            var count = FeishuWebhooks
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Count(item => !string.IsNullOrWhiteSpace(item));
            return count == 0 ? "未配置飞书 Webhook" : $"已配置 {count} 个飞书 Webhook";
        }
    }

    public string ManualSymbolsSummary
    {
        get
        {
            var count = ParseManualSymbolsInput().Count;
            return count == 0 ? "未填写手工导入品种" : $"当前待导入 {count} 个品种";
        }
    }

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public decimal SelectedAccountEquity
    {
        get => _selectedAccountEquity;
        set => SetProperty(ref _selectedAccountEquity, value);
    }

    public decimal SelectedAccountWalletBalance
    {
        get => _selectedAccountWalletBalance;
        set => SetProperty(ref _selectedAccountWalletBalance, value);
    }

    public decimal SelectedAccountAvailableBalance
    {
        get => _selectedAccountAvailableBalance;
        set => SetProperty(ref _selectedAccountAvailableBalance, value);
    }

    public decimal SelectedAccountUnrealizedProfit
    {
        get => _selectedAccountUnrealizedProfit;
        set => SetProperty(ref _selectedAccountUnrealizedProfit, value);
    }

    public decimal SelectedAccountPositionMarketValue
    {
        get => _selectedAccountPositionMarketValue;
        set => SetProperty(ref _selectedAccountPositionMarketValue, value);
    }

    public int SelectedAccountPositionSymbolCount
    {
        get => _selectedAccountPositionSymbolCount;
        set => SetProperty(ref _selectedAccountPositionSymbolCount, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsImportingManualSymbols
    {
        get => _isImportingManualSymbols;
        set
        {
            if (SetProperty(ref _isImportingManualSymbols, value))
            {
                ImportManualSymbolsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AccountViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                RemoveAccountCommand.RaiseCanExecuteChanged();
                RefreshPositionsCommand.RaiseCanExecuteChanged();
                _ = RefreshSelectedAccountPositionsAsync();
            }
        }
    }

    public async Task InitializeAsync()
    {
        var config = await _configService.LoadAsync();
        _currentConfig = config;
        LoadConfig(config);
        AppendExternalLog("INFO", "SYSTEM", "配置加载完成。当前使用币安实盘服务。", DateTime.Now);
        await RefreshTopDropTickersAsync();
    }

    public void AppendExternalLog(string level, string accountName, string message, DateTime? timestamp = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var logItem = new LogEntryViewModel
            {
                Timestamp = timestamp ?? DateTime.Now,
                Level = level,
                AccountName = accountName,
                Message = message
            };

            Logs.Insert(0, logItem);
            _logTextBuilder.Insert(0, FormatLogLine(logItem) + Environment.NewLine);
            LogText = _logTextBuilder.ToString();

            while (Logs.Count > 300)
            {
                Logs.RemoveAt(Logs.Count - 1);
            }

            TrimLogText();
        });
    }

    public async Task RefreshSelectedAccountPositionsAsync()
    {
        if (SelectedAccount is null)
        {
            Positions.Clear();
            SelectedAccountEquity = 0m;
            SelectedAccountWalletBalance = 0m;
            SelectedAccountAvailableBalance = 0m;
            SelectedAccountUnrealizedProfit = 0m;
            SelectedAccountPositionMarketValue = 0m;
            SelectedAccountPositionSymbolCount = 0;
            return;
        }

        var accountModel = SelectedAccount.ToModel();
        var positions = await _binanceService.GetPositionsAsync(accountModel);
        var summary = await _binanceService.GetAccountAssetSummaryAsync(accountModel);

        Positions.Clear();
        foreach (var position in positions.Select(PositionViewModel.FromModel))
        {
            Positions.Add(position);
        }

        SelectedAccountEquity = summary.AccountEquity;
        SelectedAccountWalletBalance = summary.WalletBalance;
        SelectedAccountAvailableBalance = summary.AvailableBalance;
        SelectedAccountUnrealizedProfit = summary.UnrealizedProfit;
        SelectedAccountPositionMarketValue = summary.PositionMarketValue;
        SelectedAccountPositionSymbolCount = summary.PositionSymbolCount;
    }

    private async Task StartAsync()
    {
        await SaveConfigAsync();
        IsRunning = true;
        _scanCancellation = new CancellationTokenSource();
        ImportManualSymbolsCommand.RaiseCanExecuteChanged();
        _scanTimer.Interval = TimeSpan.FromSeconds(GetScanInterval());
        _scanTimer.Start();
        AppendExternalLog("INFO", "SYSTEM", $"扫描已启动，间隔 {_scanTimer.Interval.TotalSeconds} 秒。", DateTime.Now);
        await ExecuteScanAsync();
    }

    private void Stop()
    {
        _scanTimer.Stop();
        _scanCancellation?.Cancel();
        _scanCancellation = null;
        IsRunning = false;
        ImportManualSymbolsCommand.RaiseCanExecuteChanged();
        AppendExternalLog("INFO", "SYSTEM", "扫描已停止。", DateTime.Now);
    }

    private async Task ExecuteScanAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        try
        {
            _currentConfig = BuildConfig();
            _scanTimer.Interval = TimeSpan.FromSeconds(GetScanInterval());
            await RefreshTopDropTickersAsync();
            await _tradingOrchestrator.ScanAsync(_currentConfig, RuntimeStates, RefreshPositionByAccountIdAsync, _scanCancellation?.Token ?? CancellationToken.None);
            await PushDailyAccountSummaryIfNeededAsync(_scanCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendExternalLog("ERROR", "SYSTEM", $"扫描异常：{ex.Message}", DateTime.Now);
        }
    }

    private async Task RefreshTopDropTickersAsync()
    {
        var tickers = await _binanceService.GetTickersAsync(_scanCancellation?.Token ?? CancellationToken.None);
        var triggerThreshold = ParseDecimal(TriggerDropPercent, 3m);
        var topDrops = tickers
            .OrderByDescending(item => item.DropPercent)
            .Take(10)
            .Select(item => new TopDropItemViewModel
            {
                Symbol = item.Symbol,
                DropPercent = item.DropPercent,
                ThresholdGap = decimal.Round(item.DropPercent - triggerThreshold, 2, MidpointRounding.AwayFromZero),
                LastPrice = item.LastPrice,
                OpenPrice = item.OpenPrice
            })
            .ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            TopDropTickers.Clear();
            foreach (var item in topDrops)
            {
                TopDropTickers.Add(item);
            }
        });

        if (topDrops.Count > 0)
        {
            var summary = string.Join("，", topDrops.Select(item => $"{item.Symbol} {item.DropPercent:F2}%"));
            AppendExternalLog("MARKET", "SYSTEM", $"本轮跌幅前{topDrops.Count}：{summary}", DateTime.Now);
        }
    }

    private async Task RefreshPositionByAccountIdAsync(Guid accountId)
    {
        if (SelectedAccount is null || SelectedAccount.Id != accountId)
        {
            return;
        }

        await RefreshSelectedAccountPositionsAsync();
    }

    private async void OpenAccountSettings()
    {
        var dialogViewModel = new AccountSettingsViewModel(Accounts.Select(account => account.ToModel()));
        var window = new AccountSettingsWindow(dialogViewModel);
        var result = window.ShowDialog();
        if (result != true)
        {
            return;
        }

        var selectedAccountId = SelectedAccount?.Id;
        ApplyAccounts(dialogViewModel.BuildAccounts(), selectedAccountId);
        await SaveConfigAsync();
        AppendExternalLog("INFO", "SYSTEM", "账户配置已更新。", DateTime.Now);

        if (IsRunning)
        {
            AppendExternalLog("INFO", "SYSTEM", "后续扫描将自动使用最新账户配置。", DateTime.Now);
        }
    }

    private async void OpenStrategySettings()
    {
        var dialogViewModel = new StrategySettingsViewModel(
            TriggerDropPercent,
            TakeProfitMultiplier,
            ScanIntervalSeconds,
            FeishuWebhooks,
            ManualSymbolsInput);
        var window = new StrategySettingsWindow(dialogViewModel);
        var result = window.ShowDialog();
        if (result != true)
        {
            return;
        }

        TriggerDropPercent = dialogViewModel.TriggerDropPercent;
        TakeProfitMultiplier = dialogViewModel.TakeProfitMultiplier;
        ScanIntervalSeconds = dialogViewModel.ScanIntervalSeconds;
        FeishuWebhooks = dialogViewModel.FeishuWebhooks;
        ManualSymbolsInput = dialogViewModel.ManualSymbolsInput;

        await SaveConfigAsync();
        AppendExternalLog("INFO", "SYSTEM", "策略配置已更新。", DateTime.Now);

        if (IsRunning)
        {
            _scanTimer.Interval = TimeSpan.FromSeconds(GetScanInterval());
            AppendExternalLog("INFO", "SYSTEM", $"扫描间隔已更新为 {_scanTimer.Interval.TotalSeconds} 秒。", DateTime.Now);
        }
    }

    private void AddAccount()
    {
        var account = new AccountViewModel
        {
            AccountName = $"Account {Accounts.Count + 1}",
            ApiKey = string.Empty,
            SecretKey = string.Empty,
            AllocationParts = 5,
            BuyAmount = 100m,
            EnableOpenPosition = true,
            EnableTakeProfit = true
        };
        Accounts.Add(account);
        SelectedAccount = account;
    }

    private void RemoveSelectedAccount()
    {
        if (SelectedAccount is null)
        {
            return;
        }

        var current = SelectedAccount;
        Accounts.Remove(current);
        SelectedAccount = Accounts.FirstOrDefault();
    }

    private void LoadConfig(AppConfig config)
    {
        ApplyAccounts(config.Accounts);
        TriggerDropPercent = config.Strategy.TriggerDropPercent.ToString("0.##", CultureInfo.InvariantCulture);
        TakeProfitMultiplier = config.Strategy.TakeProfitMultiplier.ToString("0.####", CultureInfo.InvariantCulture);
        ScanIntervalSeconds = config.Strategy.ScanIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        FeishuWebhooks = string.Join(Environment.NewLine, config.Notification.FeishuWebhooks);
    }

    private async Task SaveConfigAsync()
    {
        var config = BuildConfig();
        _currentConfig = config;
        await _configService.SaveAsync(config);
        AppendExternalLog("INFO", "SYSTEM", "配置已保存。", DateTime.Now);
        OnPropertyChanged(nameof(StrategySummary));
        OnPropertyChanged(nameof(WebhookSummary));
        OnPropertyChanged(nameof(ManualSymbolsSummary));
    }

    private bool CanImportManualSymbols()
    {
        return !IsImportingManualSymbols
            && Accounts.Count > 0
            && !string.IsNullOrWhiteSpace(ManualSymbolsInput);
    }

    private async Task PushDailyAccountSummaryIfNeededAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        if (now.Hour < 16)
        {
            return;
        }

        var state = _currentConfig.Notification.State ??= new NotificationState();
        var today = DateOnly.FromDateTime(now);
        if (state.LastDailySummaryDate == today)
        {
            return;
        }

        var summaries = new List<string>();
        foreach (var account in _currentConfig.Accounts)
        {
            var summary = await _binanceService.GetAccountAssetSummaryAsync(account, cancellationToken);
            summaries.Add($"{account.AccountName} - 账户权益 {summary.AccountEquity:F2} USDT | 钱包余额 {summary.WalletBalance:F2} USDT | 未实现盈亏 {summary.UnrealizedProfit:F2} USDT | 可用余额 {summary.AvailableBalance:F2} USDT");
        }

        if (summaries.Count == 0)
        {
            return;
        }

        var message = string.Join("\n", new[]
        {
            "【16:00 账户权益】",
            $"时间：{now:yyyy-MM-dd HH:mm:ss}",
            $"账户总数：{summaries.Count}",
            "明细：",
            string.Join("\n", summaries.Select(item => $"- {item}"))
        });
        await _notificationService.PushAsync(_currentConfig.Notification, message, cancellationToken);
        state.LastDailySummaryDate = today;
        await _configService.SaveAsync(_currentConfig);
        AppendExternalLog("INFO", "SYSTEM", "已推送当日 16:00 账户权益列表。", now);
    }

    private async Task ImportManualSymbolsAsync()
    {
        var symbols = ParseManualSymbolsInput();
        if (symbols.Count == 0)
        {
            AppendExternalLog("WARN", "SYSTEM", "请先输入要导入的合约代码，多个合约请用逗号分隔。", DateTime.Now);
            return;
        }

        IsImportingManualSymbols = true;
        try
        {
            await SaveConfigAsync();
            var config = BuildConfig();
            AppendExternalLog("INFO", "SYSTEM", $"开始手工导入品种：{string.Join(", ", symbols)}。", DateTime.Now);

            foreach (var account in config.Accounts)
            {
                IReadOnlyList<PositionInfo> positions;
                try
                {
                    positions = await _binanceService.GetPositionsAsync(account, _scanCancellation?.Token ?? CancellationToken.None);
                }
                catch (Exception ex)
                {
                    AppendExternalLog("ERROR", account.AccountName, $"查询持仓失败：{ex.Message}", DateTime.Now);
                    continue;
                }

                var unitAmount = account.AllocationParts <= 0
                    ? account.BuyAmount
                    : decimal.Round(account.BuyAmount / account.AllocationParts, 2, MidpointRounding.AwayFromZero);

                foreach (var symbol in symbols)
                {
                    if (positions.Any(position => position.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
                    {
                        AppendExternalLog("INFO", account.AccountName, $"已持有 {symbol}，跳过手工导入。", DateTime.Now);
                        continue;
                    }

                    try
                    {
                        await _binanceService.OpenLongPositionAsync(account, symbol, unitAmount, _scanCancellation?.Token ?? CancellationToken.None);
                        AppendExternalLog("TRADE", account.AccountName, $"手工导入买入 {symbol}，分摊后的单币金额 {unitAmount:F2} USDT。", DateTime.Now);
                    }
                    catch (Exception ex)
                    {
                        AppendExternalLog("ERROR", account.AccountName, $"手工导入 {symbol} 失败：{ex.Message}", DateTime.Now);
                    }
                }

                if (SelectedAccount?.Id == account.Id)
                {
                    await RefreshSelectedAccountPositionsAsync();
                }
            }

            AppendExternalLog("INFO", "SYSTEM", "手工导入执行完成。", DateTime.Now);
        }
        finally
        {
            IsImportingManualSymbols = false;
        }
    }

    private List<string> ParseManualSymbolsInput()
    {
        return ManualSymbolsInput
            .Split(new[] { ',', '，', '\r', '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().ToUpperInvariant())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private AppConfig BuildConfig()
    {
        return new AppConfig
        {
            Accounts = Accounts.Select(account => account.ToModel()).ToList(),
            Strategy = new StrategyConfig
            {
                TriggerDropPercent = ParseDecimal(TriggerDropPercent, 3m),
                TakeProfitMultiplier = ParseDecimal(TakeProfitMultiplier, 1.03m),
                ScanIntervalSeconds = (int)ParseDecimal(ScanIntervalSeconds, 60m)
            },
            Notification = new NotificationConfig
            {
                FeishuWebhooks = FeishuWebhooks
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList(),
                State = _currentConfig.Notification.State
            }
        };
    }

    private void ApplyAccounts(IEnumerable<AccountConfig> accounts, Guid? preferredSelectedAccountId = null)
    {
        var previousSelectedId = preferredSelectedAccountId ?? SelectedAccount?.Id;
        var accountViewModels = accounts.Select(AccountViewModel.FromModel).ToList();

        Accounts.Clear();
        foreach (var account in accountViewModels)
        {
            Accounts.Add(account);
        }

        RuntimeStates.Clear();
        foreach (var account in accountViewModels)
        {
            RuntimeStates.Add(new AccountRuntimeState { AccountId = account.Id });
        }

        SelectedAccount = accountViewModels.FirstOrDefault(account => account.Id == previousSelectedId)
            ?? accountViewModels.FirstOrDefault();

        ImportManualSymbolsCommand.RaiseCanExecuteChanged();
    }

    private decimal ParseDecimal(string? input, decimal fallback)
    {
        return decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            || decimal.TryParse(input, NumberStyles.Any, CultureInfo.CurrentCulture, out value)
            ? value
            : fallback;
    }

    private int GetScanInterval()
    {
        var interval = (int)ParseDecimal(ScanIntervalSeconds, 60m);
        return interval <= 0 ? 60 : interval;
    }

    private static string FormatLogLine(LogEntryViewModel logItem)
    {
        return $"[{logItem.Timestamp:yyyy-MM-dd HH:mm:ss}] [{logItem.Level}] [{logItem.AccountName}] {logItem.Message}";
    }

    private void TrimLogText()
    {
        var lines = _logTextBuilder
            .ToString()
            .Split(new[] { Environment.NewLine }, StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(300)
            .ToList();

        _logTextBuilder.Clear();
        if (lines.Count > 0)
        {
            _logTextBuilder.Append(string.Join(Environment.NewLine, lines));
            _logTextBuilder.Append(Environment.NewLine);
        }

        LogText = _logTextBuilder.ToString();
    }
}
