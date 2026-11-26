using System.IO;
using System.Text.Json;
using PickMoney.App.Models;

namespace PickMoney.App.Services;

/// <summary>
/// 账户每日净值/权益历史的本地存储。
/// 每个账户一个 JSON 文件，按天覆盖写入（同一天只保留一条收盘快照）。
/// </summary>
public sealed class NetValueHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _historyDirectory;
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public NetValueHistoryService(string baseDirectory)
    {
        _historyDirectory = Path.Combine(baseDirectory, "history");
    }

    /// <summary>
    /// 记录当天的净值与权益。同一天重复记录会覆盖当天已有的快照。
    /// </summary>
    public async Task RecordAsync(Guid accountId, string accountName, decimal initialCapital, decimal equity, CancellationToken cancellationToken = default)
    {
        if (initialCapital <= 0)
        {
            // 未配置初始资金时无法计算净值，不记录
            return;
        }

        var history = await LoadAsync(accountId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Now);

        var existing = history.Records.FirstOrDefault(item => item.Date == today);
        if (existing is null)
        {
            history.Records.Add(new DailyNetValueRecord
            {
                Date = today,
                Equity = equity,
                NetValue = equity / initialCapital,
                RecordedAt = DateTime.Now
            });
        }
        else
        {
            existing.Equity = equity;
            existing.NetValue = equity / initialCapital;
            existing.RecordedAt = DateTime.Now;
        }

        history.AccountName = accountName;
        history.InitialCapital = initialCapital;

        // 保持按日期升序
        history.Records = history.Records.OrderBy(item => item.Date).ToList();

        await SaveAsync(accountId, history);
    }

    /// <summary>读取指定账户的历史记录</summary>
    public async Task<AccountNetValueHistory> LoadAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(accountId);

        await _fileGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return new AccountNetValueHistory { AccountId = accountId };
            }

            var raw = await File.ReadAllTextAsync(path, cancellationToken);
            var history = JsonSerializer.Deserialize<AccountNetValueHistory>(raw, JsonOptions);
            if (history is null)
            {
                return new AccountNetValueHistory { AccountId = accountId };
            }

            history.AccountId = accountId;
            history.Records = history.Records.OrderBy(item => item.Date).ToList();
            return history;
        }
        catch
        {
            // 文件损坏时按空历史处理，不影响主流程
            return new AccountNetValueHistory { AccountId = accountId };
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private async Task SaveAsync(Guid accountId, AccountNetValueHistory history)
    {
        var path = GetPath(accountId);
        Directory.CreateDirectory(_historyDirectory);

        await _fileGate.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(history, JsonOptions);
            await File.WriteAllTextAsync(path, json);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private string GetPath(Guid accountId)
    {
        return Path.Combine(_historyDirectory, $"{accountId:N}.json");
    }
}
