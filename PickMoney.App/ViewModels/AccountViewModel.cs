using PickMoney.App.Models;

namespace PickMoney.App.ViewModels;

public class AccountViewModel : ObservableObject
{
    private string _accountName = string.Empty;
    private string _apiKey = string.Empty;
    private string _secretKey = string.Empty;
    private int _allocationParts = 5;
    private decimal _buyAmount = 100m;
    private bool _enableOpenPosition = true;
    private bool _enableTakeProfit = true;
    private bool _isSelected;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string AccountName
    {
        get => _accountName;
        set => SetProperty(ref _accountName, value);
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
        set => SetProperty(ref _allocationParts, value);
    }

    public decimal BuyAmount
    {
        get => _buyAmount;
        set => SetProperty(ref _buyAmount, value);
    }

    public bool EnableOpenPosition
    {
        get => _enableOpenPosition;
        set => SetProperty(ref _enableOpenPosition, value);
    }

    public bool EnableTakeProfit
    {
        get => _enableTakeProfit;
        set => SetProperty(ref _enableTakeProfit, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

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
            EnableOpenPosition = EnableOpenPosition,
            EnableTakeProfit = EnableTakeProfit
        };
    }
}
