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
    private bool _disposed;

    public BinanceFuturesService()
    {
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
            WalletBalance = decimal.Round(accountInfo?.TotalWalletBalance ?? 0m, 2, MidpointRounding.AwayFromZero),
            AvailableBalance = decimal.Round(accountInfo?.AvailableBalance ?? 0m, 2, MidpointRounding.AwayFromZero),
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

        await EnsureIsolatedMarginAsync(account, symbol, cancellationToken);
        await EnsureLeverageAsync(account, symbol, cancellationToken);

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
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Binance 实盘请求失败：{response.StatusCode} {content}");
        }

        return content;
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

    private sealed class FuturesAccountDto
    {
        public decimal TotalWalletBalance { get; set; }
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
