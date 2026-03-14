using System.Windows;
using System.Windows.Threading;
using HexToPcap.Core.Services;
using HexToPcap.Services;
using HexToPcap.ViewModels;

namespace HexToPcap
{
    public partial class App : Application
    {
        private void OnStartup(object sender, StartupEventArgs e)
        {
            var settingsService = new SettingsService();
            var wiresharkLocator = new WiresharkLocator();
            var viewModel = new MainWindowViewModel(
                new HexInputParser(),
                new PcapWriter(),
                settingsService,
                wiresharkLocator);

            var window = new MainWindow(viewModel, settingsService);
            MainWindow = window;
            window.Show();
            viewModel.InitializeAfterStartup();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                e.Exception.Message,
                "HexToPcap 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(-1);
        }
    }
}
