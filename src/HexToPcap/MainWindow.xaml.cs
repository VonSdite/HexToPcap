using System;
using System.Windows;
using HexToPcap.Services;
using HexToPcap.ViewModels;

namespace HexToPcap
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly ISettingsService _settingsService;

        public MainWindow(MainWindowViewModel viewModel, ISettingsService settingsService)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _settingsService = settingsService;
            DataContext = _viewModel;
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            const string helpText =
                "HexToPcap 用于把十六进制文本转换成标准 pcap 文件。\n\n" +
                "支持的输入格式：\n" +
                "1. 普通十六进制文本，支持空格分隔、跨多行粘贴，也支持 0x 前缀。\n" +
                "2. tcpdump -vv -nn -XX 输出，程序会自动忽略 0x0000: 这类偏移前缀并提取后面的十六进制内容。\n\n" +
                "拆包规则：\n" +
                "- 空行会作为报文分隔。\n" +
                "- 没有空行时，只在新的一行起始处识别到常见 Ethernet 头时开始一个新报文。\n" +
                "- 奇数个十六进制字符会自动在末尾补 0。\n\n" +
                "其他说明：\n" +
                "- 保存位置可在主界面顶部直接点击“设置”修改。\n" +
                "- 输出文件名格式：yyyyMMddHHmmss-报文个数.pcap。";

            MessageBox.Show(
                this,
                helpText,
                "使用说明",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var window = new SettingsWindow(_settingsService);
            window.Owner = this;

            var result = window.ShowDialog();
            if (result == true)
            {
                _viewModel.ReloadSettings();
            }
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnConvertClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var outcome = _viewModel.Convert();
                if (!string.IsNullOrWhiteSpace(outcome.OutputPath) &&
                    !string.IsNullOrWhiteSpace(outcome.WiresharkPath))
                {
                    var askOpen = MessageBox.Show(
                        this,
                        string.Format(
                            "已生成 {0} 个报文。\n是否使用 Wireshark 打开生成的 pcap 文件？",
                            outcome.SuccessfulPacketCount),
                        "HexToPcap",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (askOpen == MessageBoxResult.Yes)
                    {
                        _viewModel.OpenInWireshark(outcome.WiresharkPath, outcome.OutputPath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "转换失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
