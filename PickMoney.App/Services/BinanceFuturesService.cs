using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PickMoney.App.Models;

namespace PickMoney.App.Services;

public sealed class BinanceFuturesService : IBinanceFuturesService, IDisposable
{
    private const string BaseUrl = "https://fapi.binance.com";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, decimal> _symbolStepSizeCache = new(StringComparer.OrdinalIgnoreCase);

    // 持仓上限本地缓存，避免每次下单都调用一次 leverageBracket 签名接口
    private readonly PositionLimitCacheService? _positionLimitCache;

    // 已完成保证金类型/杠杆设置的 symbol。稳态下每次下单只需 1 个请求，
    // 避免重复调用 marginType / leverage 触发币安的 -4192 冷静期。
    private readonly ConcurrentDictionary<string, bool> _symbolSetupCache = new(StringComparer.OrdinalIgnoreCase);

    // 每个 symbol 上一次交易类请求的时间，用于同 symbol 请求间隔节流。
    private readonly ConcurrentDictionary<string, long> _lastTradeRequestTicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _throttleGate = new();

    private const int CoolingOffErrorCode = -4192;
    private const int MaxCoolingOffRetries = 3;
    private static readonly TimeSpan CoolingOffWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SameSymbolMinInterval = TimeSpan.FromSeconds(2);
    private bool _disposed;

    public BinanceFuturesService(PositionLimitCacheService? positionLimitCache = null)
    {
        _positionLimitCache = positionLimitCache;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PickMoney/1.0");
    }

    public async Task<IReadOnlyList<TickerSnapshot>> GetTickersAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("/fapi/v1/ticker/24hr", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var items = JsonSerializer.Deserialize<List<Ticker24HrDto>>(content, JsonOptions) ?? new List<Ticker24HrDto>();
        return items
            .Where(item => item.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.LastPrice > 0 && item.OpenPrice > 0)
            .Select(item => new TickerSnapshot
            {
                Symbol = item.Symbol,
                LastPrice = item.LastPrice,
                OpenPrice = item.OpenPrice,
                DropPercent = item.OpenPrice <= 0
                    ? 0
                    : decimal.Round(((item.OpenPrice - item.LastPrice) / item.OpenPrice) * 100m, 2, MidpointRounding.AwayFromZero)
            })
            .Where(item => item.DropPercent > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        var payload = await SendSignedAsync(
            account,
            HttpMethod.Get,
            "/fapi/v2/positionRisk",
            new Dictionary<string, string>(),
            cancellationToken);

        var positions = JsonSerializer.Deserialize<List<PositionRiskDto>>(payload, JsonOptions) ?? new List<PositionRiskDto>();
        return positions
            .Where(item => item.PositionAmt != 0)
            .Select(item =>
            {
                var quantity = Math.Abs(item.PositionAmt);
                var marketValue = decimal.Round(Math.Abs(item.MarkPrice * quantity), 2, MidpointRounding.AwayFromZero);
                var pnlPercent = item.EntryPrice == 0 || quantity == 0
                    ? 0
                    : decimal.Round((item.UnRealizedProfit / (item.EntryPrice * quantity)) * 100m, 2, MidpointRounding.AwayFromZero);

                return new PositionInfo
                {
                    Symbol = item.Symbol,
                    Quantity = quantity,
                    EntryPrice = item.EntryPrice,
                    MarkPrice = item.MarkPrice,
                    MarketValue = marketValue,
                    UnrealizedPnl = item.UnRealizedProfit,
                    PnlPercent = pnlPercent
                };
            })
            .ToList();
    }

    /// <summary>
    /// 查询某 symbol 的最大允许持仓（/fapi/v1/leverageBracket）。
    /// 优先使用本地缓存；未命中缓存时才调用接口，并把结果写入本地缓存。
    /// </summary>
    public async Task<PositionLimitInfo> GetPositionLimitAsync(AccountConfig account, string symbol, CancellationToken cancellationToken = default)
    {
        if (_positionLimitCache is not null)
        {
            var cached = _positionLimitCache.TryGet(symbol);
            if (cached is not null)
            {
                return cached;
            }
        }

        var payload = await SendSignedAsync(
            account,
            HttpMethod.Get,
            "/fapi/v1/leverageBracket",
            new Dictionary<string, string>(),
            cancellationToken);

        var brackets = JsonSerializer.Deserialize<List<LeverageBracketDto>>(payload, JsonOptions) ?? new List<LeverageBracketDto>();
        var bracket = brackets.FirstOrDefault(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (bracket is null)
        {
            return new PositionLimitInfo { Symbol = symbol };
        }

        // 取第一个有效档位（币安按 notional 从小到大排列，档位 1 对应最低保证金要求）
        var firstTier = bracket.Brackets?.FirstOrDefault(tier => tier.MaxQty > 0);
        if (firstTier is null)
        {
            return new PositionLimitInfo { Symbol = symbol };
        }

        var limit = new PositionLimitInfo
        {
            Symbol = symbol,
            MaxQuantity = firstTier.MaxQty,
            Leverage = firstTier.InitialLeverage,
            InitialMarginPercent = firstTier.InitialMargin
        };

        if (_positionLimitCache is not null)
        {
            await _positionLimitCache.SetAsync(symbol, limit.MaxQuantity, limit.Leverage, limit.InitialMarginPercent);
        }

        return limit;
    }

    public async Task<decimal> GetAccountEquityAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        var payload = await SendSignedAsync(
            account,
            HttpMethod.Get,
            "/fapi/v2/account",
            new Dictionary<string, string>(),
            cancellationToken);

        var accountInfo = JsonSerializer.Deserialize<FuturesAccountDto>(payload, JsonOptions);
        return accountInfo?.TotalMarginBalance ?? 0m;
    }

    public async Task<AccountAssetSummary> GetAccountAssetSummaryAsync(AccountConfig account, CancellationToken cancellationToken = default)
    {
        var payload = await SendSignedAsync(
            account,
            HttpMethod.Get,
            "/fapi/v2/account",
            new Dictionary<string, string>(),
            cancellationToken);

        var accountInfo = JsonSerializer.Deserialize<FuturesAccountDto>(payload, JsonOptions);
        var positions = await GetPositionsAsync(account, cancellationToken);

        return new AccountAssetSummary
        {
            AccountEquity = decimal.Round(accountInfo?.TotalMarginBalance ?? 0m, 2, MidpointRounding.AwayFromZero),
            WalletBalance = decimal.Round(accountInfo?.TotalWalletBalance ?? 0m, 2, MidpointRounding.AwayFromZero),
            AvailableBalance = decimal.Round(accountInfo?.AvailableBalance ?? 0m, 2, MidpointRounding.AwayFromZero),
            UnrealizedProfit = decimal.Round(accountInfo?.TotalUnrealizedProfit ?? 0m, 2, MidpointRounding.AwayFromZero),
            PositionMarketValue = decimal.Round(positions.Sum(item => item.MarketValue), 2, MidpointRounding.AwayFromZero),
            PositionSymbolCount = positions.Count
        };
    }

    public async Task OpenLongPositionAsync(AccountConfig account, string symbol, decimal notionalAmount, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.SecretKey))
        {
            throw new InvalidOperationException($"账户 {account.AccountName} 缺少实盘 API Key 或 Secret Key。");
        }

        await EnsureSymbolSetupAsync(account, symbol, cancellationToken);

        var ticker = await GetPriceAsync(symbol, cancellationToken);
        if (ticker <= 0)
        {
            throw new InvalidOperationException($"无法获取 {symbol} 最新价格，取消开仓。");
        }

        var rawQuantity = notionalAmount / ticker;
        var quantity = await NormalizeOrderQuantityAsync(symbol, rawQuantity, cancellationToken);
        if (quantity <= 0)
        {
            throw new InvalidOperationException($"账户 {account.AccountName} 的下单数量无效，请检查单币金额设置。当前品种最小下单步长可能大于可买数量。");
        }

        await EnsureWithinPositionLimitAsync(account, symbol, quantity, cancellationToken);

        try
        {
            await SendSignedAsync(
                account,
                HttpMethod.Post,
                "/fapi/v1/order",
                new Dictionary<string, string>
                {
                    ["symbol"] = symbol,
                    ["side"] = "BUY",
                    ["type"] = "MARKET",
                    ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
                    ["positionSide"] = "BOTH"
                },
                cancellationToken);
        }
        catch (PositionLimitExceededException)
        {
            // 实际撞到 -2027 说明缓存的持仓上限已失效，作废该 symbol 的缓存，下次重新拉取
            if (_positionLimitCache is not null)
            {
                await _positionLimitCache.InvalidateAsync(symbol);
            }

            throw;
        }
    }

    public async Task ClosePositionAsync(AccountConfig account, string symbol, CancellationToken cancellationToken = default)
    {
        var positions = await GetPositionsAsync(account, cancellationToken);
        var target = positions.FirstOrDefault(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (target is null || target.Quantity <= 0)
        {
            return;
        }

        var normalizedQuantity = await NormalizeOrderQuantityAsync(symbol, target.Quantity, cancellationToken);
        if (normalizedQuantity <= 0)
        {
            return;
        }

        await SendSignedAsync(
            account,
            HttpMethod.Post,
            "/fapi/v1/order",
            new Dictionary<string, string>
            {
                ["symbol"] = symbol,
                ["side"] = "SELL",
                ["type"] = "MARKET",
                ["quantity"] = normalizedQuantity.ToString(CultureInfo.InvariantCulture),
                ["reduceOnly"] = "true"
            },
            cancellationToken);
    }

    private async Task<decimal> GetPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/fapi/v1/ticker/price?symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = JsonSerializer.Deserialize<SymbolPriceDto>(content, JsonOptions);
        return dto?.Price ?? 0m;
    }

    private async Task<decimal> NormalizeOrderQuantityAsync(string symbol, decimal quantity, CancellationToken cancellationToken)
    {
        if (quantity <= 0)
        {
            return 0m;
        }

        var stepSize = await GetSymbolStepSizeAsync(symbol, cancellationToken);
        if (stepSize <= 0)
        {
            return quantity;
        }

        var steps = decimal.Floor(quantity / stepSize);
        if (steps <= 0)
        {
            return 0m;
        }

        var normalized = steps * stepSize;
        return decimal.Parse(normalized.ToString("0.###############################", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    private async Task<decimal> GetSymbolStepSizeAsync(string symbol, CancellationToken cancellationToken)
    {
        if (_symbolStepSizeCache.TryGetValue(symbol, out var cachedStepSize))
        {
            return cachedStepSize;
        }

        using var response = await _httpClient.GetAsync($"/fapi/v1/exchangeInfo?symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = JsonSerializer.Deserialize<ExchangeInfoDto>(content, JsonOptions);
        var stepSize = dto?.Symbols?
            .FirstOrDefault(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))?
            .Filters?
            .FirstOrDefault(item => item.FilterType.Equals("LOT_SIZE", StringComparison.OrdinalIgnoreCase))?
            .StepSize ?? 0m;

        _symbolStepSizeCache[symbol] = stepSize;
        return stepSize;
    }

    /// <summary>
    /// 下单前校验：本次下单后该 symbol 的累计持仓是否超过币安按杠杆档位给出的最大允许持仓。
    /// 超限时抛出可读错误，避免直接撞上 -2027。
    /// </summary>
    private async Task EnsureWithinPositionLimitAsync(AccountConfig account, string symbol, decimal orderQuantity, CancellationToken cancellationToken)
    {
        PositionLimitInfo limit;
        try
        {
            limit = await GetPositionLimitAsync(account, symbol, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 查询失败不阻断下单，交由币安最终判定
            return;
        }

        if (!limit.HasLimit)
        {
            return;
        }

        var currentQuantity = 0m;
        try
        {
            var positions = await GetPositionsAsync(account, cancellationToken);
            currentQuantity = positions
                .FirstOrDefault(item => item.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))?
                .Quantity ?? 0m;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 持仓查询失败时按"无持仓"估算，仅作保守提示
        }

        var totalQuantity = currentQuantity + orderQuantity;
        if (totalQuantity <= limit.MaxQuantity)
        {
            return;
        }

        throw new PositionLimitExceededException(
            $"账户 {account.AccountName} {symbol} 持仓将达 {totalQuantity:0.####} 张，超过币安最大允许持仓 {limit.MaxQuantity:0.####} 张" +
            $"（杠杆 {limit.Leverage:0.##} 倍档位）。请减少单币金额或先平掉部分持仓。");
    }

    /// <summary>
    /// 确保 symbol 已设置为逐仓 + 1 倍杠杆。
    /// 成功后写入缓存，后续同 symbol 的下单不再重复调用这两个接口。
    /// </summary>
    private async Task EnsureSymbolSetupAsync(AccountConfig account, string symbol, CancellationToken cancellationToken)
    {
        if (_symbolSetupCache.ContainsKey(symbol))
        {
            return;
        }

        await EnsureIsolatedMarginAsync(account, symbol, cancellationToken);
        await EnsureLeverageAsync(account, symbol, cancellationToken);
        _symbolSetupCache[symbol] = true;
    }

    private async Task EnsureIsolatedMarginAsync(AccountConfig account, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            await SendSignedAsync(
                account,
                HttpMethod.Post,
                "/fapi/v1/marginType",
                new Dictionary<string, string>
                {
                    ["symbol"] = symbol,
                    ["marginType"] = "ISOLATED"
                },
                cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No need to change margin type", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private Task EnsureLeverageAsync(AccountConfig account, string symbol, CancellationToken cancellationToken)
    {
        return SendSignedAsync(
            account,
            HttpMethod.Post,
            "/fapi/v1/leverage",
            new Dictionary<string, string>
            {
                ["symbol"] = symbol,
                ["leverage"] = "1"
            },
            cancellationToken);
    }

    private async Task<string> SendSignedAsync(
        AccountConfig account,
        HttpMethod method,
        string path,
        IDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.SecretKey))
        {
            throw new InvalidOperationException($"账户 {account.AccountName} 缺少实盘 API Key 或 Secret Key。");
        }

        var symbol = parameters.TryGetValue("symbol", out var symbolValue) ? symbolValue : null;

        // 交易类请求按 symbol 节流：币安对同一 symbol 的连续下单/撤单会触发 -4192 冷静期。
        if (!string.IsNullOrWhiteSpace(symbol) && IsTradePath(path))
        {
            await ThrottleAsync(symbol, cancellationToken);
        }

        for (var attempt = 0; ; attempt++)
        {
            var (isSuccess, content) = await SendSignedCoreAsync(account, method, path, parameters, cancellationToken);

            if (isSuccess || !IsCoolingOffResponse(content))
            {
                if (!isSuccess)
                {
                    throw BuildRequestFailureException(account, content);
                }

                return content;
            }

            if (attempt >= MaxCoolingOffRetries)
            {
                throw new InvalidOperationException(
                    $"Binance 实盘请求失败：账户 {account.AccountName} 在 {path} 上连续触发冷静期（-4192），已重试 {MaxCoolingOffRetries} 次仍未成功。{content}");
            }

            await Task.Delay(CoolingOffWait, cancellationToken);
        }
    }

    private async Task<(bool IsSuccess, string Content)> SendSignedCoreAsync(
        AccountConfig account,
        HttpMethod method,
        string path,
        IDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var allParameters = new Dictionary<string, string>(parameters)
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["recvWindow"] = "5000"
        };

        var queryString = string.Join("&", allParameters.Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value)}"));
        var signature = Sign(queryString, account.SecretKey);
        var requestUri = $"{path}?{queryString}&signature={signature}";

        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("X-MBX-APIKEY", account.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.IsSuccessStatusCode, content);
    }

    private static Exception BuildRequestFailureException(AccountConfig account, string content)
    {
        // -2019 Margin is insufficient. 需要单独识别，便于上层按"余额不足"降级处理
        if (content.Contains("\"-2019\"", StringComparison.Ordinal))
        {
            return new InsufficientMarginException(
                $"账户 {account.AccountName} 保证金不足（-2019）：{content}");
        }

        // -2027 Exceeded the maximum allowable position at current leverage.
        // 币安按杠杆档位设置的单品种持仓上限，重试无意义。
        if (content.Contains("\"-2027\"", StringComparison.Ordinal))
        {
            return new PositionLimitExceededException(
                $"账户 {account.AccountName} 持仓超过币安最大允许持仓（-2027）：{content}");
        }
        return new InvalidOperationException($"Binance 实盘请求失败：{content}");
    }

    /// <summary>
    /// 同一 symbol 的交易类请求之间保持最小间隔，避免命中币安 -4192 冷静期。
    /// </summary>
    private async Task ThrottleAsync(string symbol, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.Zero;

        lock (_throttleGate)
        {
            if (_lastTradeRequestTicks.TryGetValue(symbol, out var lastTicks))
            {
                var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastTicks);
                if (elapsed < SameSymbolMinInterval)
                {
                    delay = SameSymbolMinInterval - elapsed;
                }
            }

            // 预占时间戳，避免并发请求同时通过检查
            _lastTradeRequestTicks[symbol] = DateTime.UtcNow.Ticks + delay.Ticks;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    private static bool IsTradePath(string path)
    {
        return path.StartsWith("/fapi/v1/order", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/fapi/v1/marginType", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/fapi/v1/leverage", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoolingOffResponse(string content)
    {
        return content.Contains($"\"{CoolingOffErrorCode}\"", StringComparison.Ordinal)
            || content.Contains("Cooling-off Period", StringComparison.OrdinalIgnoreCase);
    }

    private static string Sign(string payload, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
    }

    private sealed class Ticker24HrDto
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal LastPrice { get; set; }
        public decimal OpenPrice { get; set; }
    }

    private sealed class PositionRiskDto
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal PositionAmt { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal MarkPrice { get; set; }
        public decimal UnRealizedProfit { get; set; }
    }

    private sealed class LeverageBracketDto
    {
        public string Symbol { get; set; } = string.Empty;
        public List<LeverageBracketTierDto>? Brackets { get; set; }
    }

    private sealed class LeverageBracketTierDto
    {
        public decimal Bracket { get; set; }
        public decimal InitialLeverage { get; set; }
        public decimal InitialMargin { get; set; }
        public decimal MaxQty { get; set; }
        public decimal NotionalCap { get; set; }
        public decimal NotionalFloor { get; set; }
    }

    private sealed class FuturesAccountDto
    {
        public decimal TotalWalletBalance { get; set; }
        public decimal TotalMarginBalance { get; set; }
        public decimal TotalUnrealizedProfit { get; set; }
        public decimal AvailableBalance { get; set; }
    }

    private sealed class SymbolPriceDto
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    private sealed class ExchangeInfoDto
    {
        public List<ExchangeInfoSymbolDto> Symbols { get; set; } = new();
    }

    private sealed class ExchangeInfoSymbolDto
    {
        public string Symbol { get; set; } = string.Empty;
        public List<SymbolFilterDto> Filters { get; set; } = new();
    }

    private sealed class SymbolFilterDto
    {
        public string FilterType { get; set; } = string.Empty;
        public decimal StepSize { get; set; }
    }
}
