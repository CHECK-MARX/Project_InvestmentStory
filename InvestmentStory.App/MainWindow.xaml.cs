using System.Windows;
using InvestmentStory.App.ViewModels;

namespace InvestmentStory.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
