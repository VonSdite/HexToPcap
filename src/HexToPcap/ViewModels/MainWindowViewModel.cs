using System;
using System.Collections.ObjectModel;
using System.IO;
using HexToPcap.Core.Interfaces;
using HexToPcap.Core.Models;
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
            Errors = new ObservableCollection<PacketParseError>();
            ReloadSettings();
            SummaryText = "就绪";
        }

        public ObservableCollection<PacketParseError> Errors { get; private set; }

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
            Errors.Clear();
            LastOutputPath = null;

            var settings = _settingsService.Load();
            OutputDirectory = settings.OutputDirectory;

            var parseResult = _inputParser.Parse(InputText ?? string.Empty);
            for (var index = 0; index < parseResult.Errors.Count; index++)
            {
                Errors.Add(parseResult.Errors[index]);
            }

            if (parseResult.SuccessfulPackets.Count == 0)
            {
                SummaryText = parseResult.Errors.Count == 0
                    ? "未识别到报文"
                    : string.Format("失败 {0}", parseResult.Errors.Count);

                return new ConversionOutcome
                {
                    FailedPacketCount = parseResult.Errors.Count
                };
            }

            var outputPath = _pcapWriter.Write(settings.OutputDirectory, parseResult.SuccessfulPackets);
            LastOutputPath = outputPath;

            SummaryText = BuildSummaryText(
                parseResult.SuccessfulPackets.Count,
                parseResult.Errors.Count,
                Path.GetFileName(outputPath));

            return new ConversionOutcome
            {
                OutputPath = outputPath,
                WiresharkPath = _wiresharkLocator.ResolveLaunchPath(settings.WiresharkPath),
                SuccessfulPacketCount = parseResult.SuccessfulPackets.Count,
                FailedPacketCount = parseResult.Errors.Count
            };
        }

        public void OpenInWireshark(string wiresharkPath, string capturePath)
        {
            _wiresharkLocator.OpenCapture(wiresharkPath, capturePath);
        }

        private static string BuildSummaryText(int successCount, int errorCount, string fileName)
        {
            if (errorCount == 0)
            {
                return string.Format("成功 {0} | {1}", successCount, fileName);
            }

            return string.Format("成功 {0}，失败 {1} | {2}", successCount, errorCount, fileName);
        }
    }
}
