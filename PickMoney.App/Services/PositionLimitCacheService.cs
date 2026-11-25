using System.IO;
using System.Text.Json;
using PickMoney.App.Models;

namespace PickMoney.App.Services;

/// <summary>
/// 持仓上限本地缓存。
/// leverageBracket 是签名接口且返回全部 symbol 的档位（响应体较大），
/// 因此按 symbol 维度缓存到本地 JSON 文件，避免每次下单都调用。
/// </summary>
public sealed class PositionLimitCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _cachePath;
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly Dictionary<string, PositionLimitEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>缓存中的条目数量</summary>
    public int Count => _entries.Count;

    /// <summary>最后一次成功刷新时间</summary>
    public DateTime? LastRefreshedAt { get; private set; }

    public PositionLimitCacheService(string baseDirectory)
    {
        _cachePath = Path.Combine(baseDirectory, "config", "position-limits.json");
        Load();
    }

    /// <summary>
    /// 读取缓存的持仓上限。缓存不存在或已过期时返回 null。
    /// </summary>
    public PositionLimitInfo? TryGet(string symbol)
    {
        if (!_entries.TryGetValue(symbol, out var entry))
        {
            return null;
        }

        return new PositionLimitInfo
        {
            Symbol = entry.Symbol,
            MaxQuantity = entry.MaxQuantity,
            Leverage = entry.Leverage,
            InitialMarginPercent = entry.InitialMarginPercent
        };
    }

    /// <summary>
    /// 写入缓存条目并持久化到本地文件。
    /// </summary>
    public async Task SetAsync(string symbol, decimal maxQuantity, decimal leverage, decimal initialMarginPercent)
    {
        if (maxQuantity <= 0)
        {
            return;
        }

        var entry = new PositionLimitEntry
        {
            Symbol = symbol,
            MaxQuantity = maxQuantity,
            Leverage = leverage,
            InitialMarginPercent = initialMarginPercent,
            CachedAt = DateTime.Now
        };

        _entries[symbol] = entry;
        LastRefreshedAt = DateTime.Now;
        await PersistAsync();
    }

    /// <summary>
    /// 移除某个 symbol 的缓存条目。用于实际撞到 -2027 时让缓存失效，下次重新拉取。
    /// </summary>
    public async Task InvalidateAsync(string symbol)
    {
        if (!_entries.Remove(symbol))
        {
            return;
        }

        await PersistAsync();
    }

    /// <summary>清空本地缓存文件</summary>
    public async Task ClearAsync()
    {
        _entries.Clear();
        LastRefreshedAt = null;

        await _fileGate.WaitAsync();
        try
        {
            if (File.Exists(_cachePath))
            {
                File.Delete(_cachePath);
            }
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return;
            }

            var raw = File.ReadAllText(_cachePath);
            var cache = JsonSerializer.Deserialize<PositionLimitCache>(raw, JsonOptions);
            if (cache is null)
            {
                return;
            }

            foreach (var entry in cache.Entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Symbol) && entry.MaxQuantity > 0)
                {
                    _entries[entry.Symbol] = entry;
                }
            }

            LastRefreshedAt = cache.LastRefreshedAt;
        }
        catch
        {
            // 缓存文件损坏时忽略，按无缓存处理
        }
    }

    private async Task PersistAsync()
    {
        var cache = new PositionLimitCache
        {
            Entries = _entries.Values.ToList(),
            LastRefreshedAt = LastRefreshedAt
        };

        var directory = Path.GetDirectoryName(_cachePath)!;
        Directory.CreateDirectory(directory);

        await _fileGate.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(cache, JsonOptions);
            await File.WriteAllTextAsync(_cachePath, json);
        }
        finally
        {
            _fileGate.Release();
        }
    }
}
