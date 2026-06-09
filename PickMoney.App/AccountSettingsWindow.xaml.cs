using System.Windows;
using PickMoney.App.ViewModels;

namespace PickMoney.App;

public partial class AccountSettingsWindow : Window
{
    public AccountSettingsWindow(AccountSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Owner = Application.Current.MainWindow;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AccountSettingsViewModel.DialogResult) && viewModel.DialogResult.HasValue)
            {
                DialogResult = viewModel.DialogResult.Value;
                Close();
            }
        };
    }
}
