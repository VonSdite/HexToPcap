using System.IO;
using System.Windows;
using HexToPcap.Core.Models;
using HexToPcap.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace HexToPcap
{
    public partial class SettingsWindow : Window
    {
        private readonly ISettingsService _settingsService;

        public SettingsWindow(ISettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;

            var settings = _settingsService.Load();
            OutputDirectoryTextBox.Text = settings.OutputDirectory;
            WiresharkPathTextBox.Text = settings.WiresharkPath;
        }

        private void OnBrowseOutputDirectoryClick(object sender, RoutedEventArgs e)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择 PCAP 输出目录";
                dialog.ShowNewFolderButton = true;
                dialog.SelectedPath = OutputDirectoryTextBox.Text;

                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    OutputDirectoryTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void OnBrowseWiresharkClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "Wireshark 可执行文件|wireshark.exe|可执行文件|*.exe";
            dialog.CheckFileExists = true;
            dialog.Title = "选择 Wireshark 程序";

            if (!string.IsNullOrWhiteSpace(WiresharkPathTextBox.Text))
            {
                dialog.FileName = WiresharkPathTextBox.Text;
            }

            if (dialog.ShowDialog(this) == true)
            {
                WiresharkPathTextBox.Text = dialog.FileName;
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var outputDirectory = (OutputDirectoryTextBox.Text ?? string.Empty).Trim();
            var wiresharkPath = (WiresharkPathTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                MessageBox.Show(this, "PCAP 输出目录不能为空。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(wiresharkPath) && !File.Exists(wiresharkPath))
            {
                MessageBox.Show(this, "指定的 Wireshark 路径不存在。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settingsService.Save(new AppSettings
            {
                OutputDirectory = outputDirectory,
                WiresharkPath = wiresharkPath
            });

            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
