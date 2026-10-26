using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using InvestmentStory.App.ViewModels;
using InvestmentStory.App.Services;
using InvestmentStory.Core.Services;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace InvestmentStory.App.Views;

public partial class DividendsView : UserControl
{
    public DividendsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DividendsViewModel vm || vm.SelectedPayment is null ||
            !vm.PlannedPayments.Contains(vm.SelectedPayment)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            PaymentTabs.SelectedIndex = 0;
            PaymentTabs.BringIntoView();
            PlannedGrid.ScrollIntoView(vm.SelectedPayment);
        });
    }

    private void OnExportCsv(object sender, RoutedEventArgs e) => Export(false);
    private void OnExportPdf(object sender, RoutedEventArgs e) => Export(true);

    private void Export(bool pdf)
    {
        if (DataContext is not DividendsViewModel vm) return;
        var dialog = new SaveFileDialog
        {
            Title = pdf ? "月次PDFの保存先" : "配当明細CSVの保存先",
            Filter = pdf ? "PDF (*.pdf)|*.pdf" : "CSV (*.csv)|*.csv",
            FileName = $"InvestmentStory_配当_{vm.SelectedYear}_{vm.ReportMonth:00}" + (pdf ? ".pdf" : ".csv")
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var report = vm.BuildMonthlyReport();
            if (pdf) MonthlyDividendPdfWriter.Write(dialog.FileName, report);
            else File.WriteAllText(dialog.FileName, new MonthlyDividendReportService().ToCsv(report),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            vm.ShowExportMessage($"{(pdf ? "PDF" : "CSV")}を保存しました: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            vm.ShowExportMessage($"出力に失敗しました: {ex.Message}");
        }
    }
}
