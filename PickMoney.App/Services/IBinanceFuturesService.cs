using PickMoney.App.Models;

namespace PickMoney.App.Services;

public interface IBinanceFuturesService
{
    Task<IReadOnlyList<TickerSnapshot>> GetTickersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(AccountConfig account, CancellationToken cancellationToken = default);
    Task<AccountAssetSummary> GetAccountAssetSummaryAsync(AccountConfig account, CancellationToken cancellationToken = default);
    Task OpenLongPositionAsync(AccountConfig account, string symbol, decimal notionalAmount, CancellationToken cancellationToken = default);
    Task ClosePositionAsync(AccountConfig account, string symbol, CancellationToken cancellationToken = default);
}
