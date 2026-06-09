using System.Collections.ObjectModel;
using System.Windows;
using PickMoney.App.Models;

namespace PickMoney.App.Services;

public class TradingOrchestrator
{
    private readonly IBinanceFuturesService _binanceService;
    private readonly TradeLogService _tradeLogService;
    private readonly NotificationService _notificationService;
    private readonly Action<LogMessage> _logCallback;

    public TradingOrchestrator(
        IBinanceFuturesService binanceService,
        TradeLogService tradeLogService,
        NotificationService notificationService,
        Action<LogMessage> logCallback)
    {
        _binanceService = binanceService;
        _tradeLogService = tradeLogService;
        _notificationService = notificationService;
        _logCallback = logCallback;
    }

    public async Task ScanAsync(
        AppConfig config,
        ObservableCollection<AccountRuntimeState> runtimeStates,
        Func<Guid, Task> positionRefresh,
        CancellationToken cancellationToken = default)
    {
        var tickers = await _binanceService.GetTickersAsync(cancellationToken);
        var candidates = tickers
            .Where(item => item.DropPercent >= config.Strategy.TriggerDropPercent)
            .OrderByDescending(item => item.DropPercent)
            .ToList();

        Log("INFO", "SYSTEM", $"扫描到 {candidates.Count} 个触发品种，阈值 {config.Strategy.TriggerDropPercent}% 。");

        var buyResults = new List<TradeExecutionResult>();
        var tpResults = new List<TradeExecutionResult>();

        foreach (var account in config.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = runtimeStates.FirstOrDefault(item => item.AccountId == account.Id);
            if (runtime is null)
            {
                runtime = new AccountRuntimeState { AccountId = account.Id };
                Application.Current.Dispatcher.Invoke(() => runtimeStates.Add(runtime));
            }

            var positions = await _binanceService.GetPositionsAsync(account, cancellationToken);
            runtime.Positions = positions.ToList();
            runtime.LastScanTime = DateTime.Now;
            runtime.LastPositionCheckTime = DateTime.Now;

            if (account.EnableOpenPosition)
            {
                await HandleOpenPositionsAsync(account, config, candidates, positions, buyResults, cancellationToken);
            }

            if (account.EnableTakeProfit)
            {
                await HandleTakeProfitsAsync(account, config, positions, tpResults, cancellationToken);
            }

            await positionRefresh(account.Id);
        }

        await FlushNotificationsAsync(config, buyResults, tpResults, cancellationToken);
    }

    private async Task HandleOpenPositionsAsync(
        AccountConfig account,
        AppConfig config,
        IReadOnlyList<TickerSnapshot> candidates,
        IReadOnlyList<PositionInfo> positions,
        List<TradeExecutionResult> results,
        CancellationToken cancellationToken)
    {
        var unitAmount = account.AllocationParts <= 0
            ? account.BuyAmount
            : decimal.Round(account.BuyAmount / account.AllocationParts, 2, MidpointRounding.AwayFromZero);

        foreach (var candidate in candidates)
        {
            if (positions.Any(position => position.Symbol.Equals(candidate.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            await _binanceService.OpenLongPositionAsync(account, candidate.Symbol, unitAmount, cancellationToken);
            var message = $"账户 {account.AccountName} 买入 {candidate.Symbol}，金额 {unitAmount} USDT，触发跌幅 {candidate.DropPercent}% 。";
            await _tradeLogService.WriteAsync(account.AccountName, message);
            Log("TRADE", account.AccountName, message);

            lock (results)
            {
                results.Add(new TradeExecutionResult
                {
                    Symbol = candidate.Symbol,
                    AccountName = account.AccountName,
                    Amount = unitAmount,
                    DropPercent = candidate.DropPercent
                });
            }
            break;
        }
    }

    private async Task HandleTakeProfitsAsync(
        AccountConfig account,
        AppConfig config,
        IReadOnlyList<PositionInfo> positions,
        List<TradeExecutionResult> results,
        CancellationToken cancellationToken)
    {
        var unitAmount = account.AllocationParts <= 0
            ? account.BuyAmount
            : decimal.Round(account.BuyAmount / account.AllocationParts, 2, MidpointRounding.AwayFromZero);
        var targetValue = decimal.Round(unitAmount * config.Strategy.TakeProfitMultiplier, 2, MidpointRounding.AwayFromZero);

        foreach (var position in positions)
        {
            if (position.MarketValue < targetValue)
            {
                continue;
            }

            await _binanceService.ClosePositionAsync(account, position.Symbol, cancellationToken);
            var message = $"账户 {account.AccountName} 止盈平仓 {position.Symbol}，当前市值 {position.MarketValue}，目标 {targetValue}。";
            await _tradeLogService.WriteAsync(account.AccountName, message);
            Log("TAKE_PROFIT", account.AccountName, message);

            lock (results)
            {
                results.Add(new TradeExecutionResult
                {
                    Symbol = position.Symbol,
                    AccountName = account.AccountName,
                    MarketValue = position.MarketValue,
                    TargetValue = targetValue
                });
            }
        }
    }

    private async Task FlushNotificationsAsync(
        AppConfig config,
        List<TradeExecutionResult> buyResults,
        List<TradeExecutionResult> tpResults,
        CancellationToken cancellationToken)
    {
        var now = DateTime.Now;

        if (buyResults.Count > 0)
        {
            var symbolGroups = buyResults.GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).ToList();
            var lines = symbolGroups
                .Select(g =>
                {
                    var first = g.First();
                    var accountNames = g.Select(r => r.AccountName).Distinct().ToList();
                    return $"- {g.Key} | 账户数 {accountNames.Count} | 账户 {string.Join("、", accountNames)} | 单账户金额 {first.Amount:F2} USDT | 触发跌幅 {first.DropPercent:F2}%";
                })
                .ToList();
            var message = string.Join("\n", new[]
            {
                "【买入通知】",
                $"时间：{now:yyyy-MM-dd HH:mm:ss}",
                $"本次触发品种数：{symbolGroups.Count}",
                "明细：",
                string.Join("\n", lines)
            });
            await _notificationService.PushAsync(config.Notification, message, cancellationToken);
        }

        if (tpResults.Count > 0)
        {
            var symbolGroups = tpResults.GroupBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).ToList();
            var lines = symbolGroups
                .Select(g =>
                {
                    var first = g.First();
                    var accountNames = g.Select(r => r.AccountName).Distinct().ToList();
                    return $"- {g.Key} | 账户数 {accountNames.Count} | 账户 {string.Join("、", accountNames)} | 当前市值 {first.MarketValue:F2} USDT | 止盈目标 {first.TargetValue:F2} USDT";
                })
                .ToList();
            var message = string.Join("\n", new[]
            {
                "【止盈通知】",
                $"时间：{now:yyyy-MM-dd HH:mm:ss}",
                $"本次止盈品种数：{symbolGroups.Count}",
                "明细：",
                string.Join("\n", lines)
            });
            await _notificationService.PushAsync(config.Notification, message, cancellationToken);
        }
    }

    private void Log(string level, string accountName, string message)
    {
        _logCallback(new LogMessage
        {
            Timestamp = DateTime.Now,
            Level = level,
            AccountName = accountName,
            Message = message
        });
    }
}
