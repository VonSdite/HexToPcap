using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HexToPcap.Services
{
    public sealed class WiresharkLocator : IWiresharkLocator
    {
        public string ResolveLaunchPath(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return Path.GetFullPath(configuredPath);
            }

            var progId = ReadUserChoiceProgId() ?? ReadRegisteredProgId();
            if (string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }

            var command = ReadCommandForProgId(progId);
            if (string.IsNullOrWhiteSpace(command) || command.IndexOf("wireshark.exe", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var executablePath = ExtractExecutablePath(command);
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            return executablePath;
        }

        public void OpenCapture(string wiresharkPath, string capturePath)
        {
            if (string.IsNullOrWhiteSpace(wiresharkPath))
            {
                throw new ArgumentException("Wireshark 路径不能为空。", "wiresharkPath");
            }

            if (string.IsNullOrWhiteSpace(capturePath))
            {
                throw new ArgumentException("PCAP 文件路径不能为空。", "capturePath");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = wiresharkPath,
                Arguments = "\"" + capturePath + "\"",
                UseShellExecute = false
            };

            Process.Start(startInfo);
        }

        private static string ReadUserChoiceProgId()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pcap\UserChoice"))
            {
                return key == null ? null : key.GetValue("ProgId") as string;
            }
        }

        private static string ReadRegisteredProgId()
        {
            using (var key = Registry.ClassesRoot.OpenSubKey(@".pcap"))
            {
                return key == null ? null : key.GetValue(null) as string;
            }
        }

        private static string ReadCommandForProgId(string progId)
        {
            using (var key = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
            {
                return key == null ? null : key.GetValue(null) as string;
            }
        }

        private static string ExtractExecutablePath(string command)
        {
            var quotedMatch = Regex.Match(command, "^\"(?<path>[^\"]+\\.exe)\"", RegexOptions.IgnoreCase);
            if (quotedMatch.Success)
            {
                return quotedMatch.Groups["path"].Value;
            }

            var unquotedMatch = Regex.Match(command, @"^(?<path>\S+\.exe)", RegexOptions.IgnoreCase);
            if (unquotedMatch.Success)
            {
                return unquotedMatch.Groups["path"].Value;
            }

            return null;
        }
    }
}

