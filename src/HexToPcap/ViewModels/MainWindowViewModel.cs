using System;
using System.IO;
using HexToPcap.Core.Interfaces;
using HexToPcap.Models;
using HexToPcap.Services;

namespace HexToPcap.ViewModels
{
    public sealed class MainWindowViewModel : ViewModelBase
    {
        private readonly IInputParser _inputParser;
        private readonly IPcapWriter _pcapWriter;
        private readonly ISettingsService _settingsService;
        private readonly IWiresharkLocator _wiresharkLocator;
        private string _inputText;
        private string _outputDirectory;
        private string _summaryText;
        private string _lastOutputPath;

        public MainWindowViewModel(
            IInputParser inputParser,
            IPcapWriter pcapWriter,
            ISettingsService settingsService,
            IWiresharkLocator wiresharkLocator)
        {
            _inputParser = inputParser;
            _pcapWriter = pcapWriter;
            _settingsService = settingsService;
            _wiresharkLocator = wiresharkLocator;
            ReloadSettings();
            SummaryText = "\u5C31\u7EEA";
        }

        public string InputText
        {
            get { return _inputText; }
            set
            {
                if (_inputText == value)
                {
                    return;
                }

                _inputText = value;
                RaisePropertyChanged();
            }
        }

        public string OutputDirectory
        {
            get { return _outputDirectory; }
            private set
            {
                if (_outputDirectory == value)
                {
                    return;
                }

                _outputDirectory = value;
                RaisePropertyChanged();
            }
        }

        public string SummaryText
        {
            get { return _summaryText; }
            private set
            {
                if (_summaryText == value)
                {
                    return;
                }

                _summaryText = value;
                RaisePropertyChanged();
            }
        }

        public string LastOutputPath
        {
            get { return _lastOutputPath; }
            private set
            {
                if (_lastOutputPath == value)
                {
                    return;
                }

                _lastOutputPath = value;
                RaisePropertyChanged();
            }
        }

        public void ReloadSettings()
        {
            var settings = _settingsService.Load();
            OutputDirectory = settings.OutputDirectory;
        }

        public ConversionOutcome Convert()
        {
            LastOutputPath = null;

            var settings = _settingsService.Load();
            OutputDirectory = settings.OutputDirectory;

            var parseResult = _inputParser.Parse(InputText ?? string.Empty);

            if (parseResult.SuccessfulPackets.Count == 0)
            {
                SummaryText = "\u672A\u8BC6\u522B\u5230\u62A5\u6587";

                return new ConversionOutcome
                {
                    SuccessfulPacketCount = 0
                };
            }

            var outputPath = _pcapWriter.Write(settings.OutputDirectory, parseResult.SuccessfulPackets);
            LastOutputPath = outputPath;

            SummaryText = BuildSummaryText(
                parseResult.SuccessfulPackets.Count,
                Path.GetFileName(outputPath));

            return new ConversionOutcome
            {
                OutputPath = outputPath,
                WiresharkPath = _wiresharkLocator.ResolveLaunchPath(settings.WiresharkPath),
                SuccessfulPacketCount = parseResult.SuccessfulPackets.Count
            };
        }

        public void OpenInWireshark(string wiresharkPath, string capturePath)
        {
            _wiresharkLocator.OpenCapture(wiresharkPath, capturePath);
        }

        private static string BuildSummaryText(int successCount, string fileName)
        {
            return string.Format("\u6210\u529F\u5BFC\u51FA {0} \u4E2A | {1}", successCount, fileName);
        }
    }
}
