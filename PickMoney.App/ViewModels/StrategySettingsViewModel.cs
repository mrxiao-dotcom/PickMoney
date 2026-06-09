namespace PickMoney.App.ViewModels;

public class StrategySettingsViewModel : ObservableObject
{
    private string _triggerDropPercent;
    private string _takeProfitMultiplier;
    private string _scanIntervalSeconds;
    private string _feishuWebhooks;
    private string _manualSymbolsInput;
    private bool? _dialogResult;

    public StrategySettingsViewModel(
        string triggerDropPercent,
        string takeProfitMultiplier,
        string scanIntervalSeconds,
        string feishuWebhooks,
        string manualSymbolsInput)
    {
        _triggerDropPercent = triggerDropPercent;
        _takeProfitMultiplier = takeProfitMultiplier;
        _scanIntervalSeconds = scanIntervalSeconds;
        _feishuWebhooks = feishuWebhooks;
        _manualSymbolsInput = manualSymbolsInput;

        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => Cancel());
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public string TriggerDropPercent
    {
        get => _triggerDropPercent;
        set => SetProperty(ref _triggerDropPercent, value);
    }

    public string TakeProfitMultiplier
    {
        get => _takeProfitMultiplier;
        set => SetProperty(ref _takeProfitMultiplier, value);
    }

    public string ScanIntervalSeconds
    {
        get => _scanIntervalSeconds;
        set => SetProperty(ref _scanIntervalSeconds, value);
    }

    public string FeishuWebhooks
    {
        get => _feishuWebhooks;
        set => SetProperty(ref _feishuWebhooks, value);
    }

    public string ManualSymbolsInput
    {
        get => _manualSymbolsInput;
        set => SetProperty(ref _manualSymbolsInput, value);
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    private void Save()
    {
        DialogResult = true;
    }

    private void Cancel()
    {
        DialogResult = false;
    }
}
