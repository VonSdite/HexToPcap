using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HexToPcap.Core.Interfaces;
using HexToPcap.Core.Models;

namespace HexToPcap.Core.Services
{
    public sealed class HexInputParser : IInputParser
    {
        private static readonly Regex TcpdumpOffsetRegex = new Regex(
            @"^\s*0[xX](?<offset>[0-9A-Fa-f]+):\s*(?<rest>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex LineOffsetPrefixRegex = new Regex(
            @"^\s*0[xX][0-9A-Fa-f]+:\s*",
            RegexOptions.Compiled);

        public ParseResult Parse(string input)
        {
            var packets = new List<byte[]>();
            var errors = new List<PacketParseError>();

            if (string.IsNullOrWhiteSpace(input))
            {
                return new ParseResult(packets, errors);
            }

            var blocks = SplitIntoBlocks(input);
            for (var index = 0; index < blocks.Count; index++)
            {
                var block = blocks[index];
                if (ContainsTcpdumpOffsets(block))
                {
                    ParseTcpdumpBlock(block, packets);
                }
                else
                {
                    ParsePlainBlock(block, packets);
                }
            }

            return new ParseResult(packets, errors);
        }

        private static List<TextBlock> SplitIntoBlocks(string input)
        {
            var normalized = input.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var blocks = new List<TextBlock>();
            TextBlock current = null;

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                {
                    current = null;
                    continue;
                }

                if (current == null)
                {
                    current = new TextBlock();
                    blocks.Add(current);
                }

                current.Lines.Add(line.Trim());
            }

            return blocks;
        }

        private static bool ContainsTcpdumpOffsets(TextBlock block)
        {
            for (var index = 0; index < block.Lines.Count; index++)
            {
                if (TcpdumpOffsetRegex.IsMatch(block.Lines[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ParsePlainBlock(TextBlock block, ICollection<byte[]> packets)
        {
            List<byte> currentPacket = null;

            for (var index = 0; index < block.Lines.Count; index++)
            {
                var lineBytes = ExtractPlainLineBytes(block.Lines[index]);
                if (lineBytes.Length == 0)
                {
                    continue;
                }

                if (currentPacket == null)
                {
                    currentPacket = new List<byte>();
                }
                else if (StartsWithRecognizedEthernetHeader(lineBytes))
                {
                    packets.Add(currentPacket.ToArray());
                    currentPacket = new List<byte>();
                }

                currentPacket.AddRange(lineBytes);
            }

            if (currentPacket != null && currentPacket.Count > 0)
            {
                packets.Add(currentPacket.ToArray());
            }
        }

        private static void ParseTcpdumpBlock(TextBlock block, ICollection<byte[]> packets)
        {
            List<byte> currentPacket = null;

            for (var index = 0; index < block.Lines.Count; index++)
            {
                var line = block.Lines[index];
                var match = TcpdumpOffsetRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                int offset;
                if (!int.TryParse(match.Groups["offset"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset))
                {
                    offset = 0;
                }

                var lineBytes = ExtractTcpdumpLineBytes(match.Groups["rest"].Value);
                if (lineBytes.Length == 0)
                {
                    continue;
                }

                if (currentPacket != null && currentPacket.Count > 0 && offset == 0)
                {
                    packets.Add(currentPacket.ToArray());
                    currentPacket = new List<byte>();
                }
                else if (currentPacket == null)
                {
                    currentPacket = new List<byte>();
                }

                currentPacket.AddRange(lineBytes);
            }

            if (currentPacket != null && currentPacket.Count > 0)
            {
                packets.Add(currentPacket.ToArray());
            }
        }

        private static byte[] ExtractPlainLineBytes(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return new byte[0];
            }

            var normalizedLine = LineOffsetPrefixRegex.Replace(line, string.Empty);
            var tokens = normalizedLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return ExtractBytesFromTokens(tokens, false);
        }

        private static byte[] ExtractTcpdumpLineBytes(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return new byte[0];
            }

            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return ExtractBytesFromTokens(tokens, true);
        }

        private static byte[] ExtractBytesFromTokens(string[] tokens, bool stopOnInvalidToken)
        {
            var bytes = new List<byte>();

            for (var index = 0; index < tokens.Length; index++)
            {
                var normalizedToken = NormalizeHexToken(tokens[index]);
                if (normalizedToken == null)
                {
                    if (stopOnInvalidToken)
                    {
                        break;
                    }

                    continue;
                }

                AppendHexBytes(normalizedToken, bytes);
            }

            return bytes.ToArray();
        }

        private static string NormalizeHexToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var value = token.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(2);
            }

            if (value.Length == 0)
            {
                return null;
            }

            for (var index = 0; index < value.Length; index++)
            {
                if (!IsHexDigit(value[index]))
                {
                    return null;
                }
            }

            if (value.Length % 2 != 0)
            {
                value += "0";
            }

            return value;
        }

        private static void AppendHexBytes(string normalizedToken, ICollection<byte> bytes)
        {
            for (var index = 0; index < normalizedToken.Length; index += 2)
            {
                bytes.Add(byte.Parse(
                    normalizedToken.Substring(index, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture));
            }
        }

        private static bool StartsWithRecognizedEthernetHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 14)
            {
                return false;
            }

            var etherType = (ushort)((bytes[12] << 8) | bytes[13]);
            return IsRecognizedEtherType(etherType);
        }

        private static bool IsRecognizedEtherType(ushort etherType)
        {
            return
                etherType == 0x0800 ||
                etherType == 0x86DD ||
                etherType == 0x0806 ||
                etherType == 0x8100 ||
                etherType == 0x88A8 ||
                etherType == 0x9100;
        }

        private static bool IsHexDigit(char value)
        {
            return
                (value >= '0' && value <= '9') ||
                (value >= 'a' && value <= 'f') ||
                (value >= 'A' && value <= 'F');
        }

        private sealed class TextBlock
        {
            public TextBlock()
            {
                Lines = new List<string>();
            }

            public List<string> Lines { get; private set; }
        }
    }
}
