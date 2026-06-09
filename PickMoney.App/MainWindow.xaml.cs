using System.Windows;
using PickMoney.App.Services;
using PickMoney.App.ViewModels;

namespace PickMoney.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var baseDirectory = AppContext.BaseDirectory;
        var configService = new ConfigService(baseDirectory);
        var binanceService = new BinanceFuturesService();
        var tradeLogService = new TradeLogService(baseDirectory);
        var notificationService = new NotificationService();

        MainViewModel? viewModel = null;
        viewModel = new MainViewModel(
            configService,
            new TradingOrchestrator(
                binanceService,
                tradeLogService,
                notificationService,
                log => viewModel?.AppendExternalLog(log.Level, log.AccountName, log.Message, log.Timestamp)),
            binanceService,
            notificationService);

        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            try
            {
                await viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                viewModel.AppendExternalLog("ERROR", "SYSTEM", $"初始化失败：{ex.Message}", DateTime.Now);
                MessageBox.Show($"程序初始化失败：{ex.Message}", "PickMoney", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        Closed += (_, _) => binanceService.Dispose();
    }
}
