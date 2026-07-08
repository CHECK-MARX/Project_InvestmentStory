using System.Windows;
using System.Windows.Controls;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not SettingsViewModel viewModel)
        {
            return;
        }

        AlphaVantagePasswordBox.Password = viewModel.AlphaVantageApiKey;
        JQuantsPasswordBox.Password = viewModel.JQuantsApiKey;
    }

    private void AlphaVantagePasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.AlphaVantageApiKey = AlphaVantagePasswordBox.Password;
        }
    }

    private void JQuantsPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.JQuantsApiKey = JQuantsPasswordBox.Password;
        }
    }
}
