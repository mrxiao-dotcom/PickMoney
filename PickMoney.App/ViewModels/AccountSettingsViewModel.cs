using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using PickMoney.App.Models;

namespace PickMoney.App.ViewModels;

public class AccountSettingsViewModel : ObservableObject
{
    private AccountViewModel? _selectedAccount;
    private bool? _dialogResult;

    public AccountSettingsViewModel(IEnumerable<AccountConfig> accounts)
    {
        Accounts = new ObservableCollection<AccountViewModel>(accounts.Select(AccountViewModel.FromModel));
        AddAccountCommand = new RelayCommand(_ => AddAccount());
        RemoveAccountCommand = new RelayCommand(_ => RemoveSelectedAccount(), _ => SelectedAccount is not null);
        SaveCommand = new RelayCommand(_ => Save());
        CancelCommand = new RelayCommand(_ => Cancel());
        SelectedAccount = Accounts.FirstOrDefault();
    }

    public ObservableCollection<AccountViewModel> Accounts { get; }

    public RelayCommand AddAccountCommand { get; }
    public RelayCommand RemoveAccountCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public AccountViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                RemoveAccountCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public List<AccountConfig> BuildAccounts()
    {
        return Accounts.Select(account => account.ToModel()).ToList();
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

        var index = Accounts.IndexOf(SelectedAccount);
        Accounts.Remove(SelectedAccount);
        SelectedAccount = Accounts.Count == 0
            ? null
            : Accounts[Math.Clamp(index, 0, Accounts.Count - 1)];
    }

    private void Save()
    {
        if (Accounts.Count == 0)
        {
            MessageBox.Show("至少保留一个账户。", "账户设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel()
    {
        DialogResult = false;
    }
}
