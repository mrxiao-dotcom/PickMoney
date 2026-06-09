using System.Windows;
using PickMoney.App.ViewModels;

namespace PickMoney.App;

public partial class StrategySettingsWindow : Window
{
    public StrategySettingsWindow(StrategySettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Owner = Application.Current.MainWindow;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StrategySettingsViewModel.DialogResult) && viewModel.DialogResult.HasValue)
            {
                DialogResult = viewModel.DialogResult.Value;
                Close();
            }
        };
    }
}
