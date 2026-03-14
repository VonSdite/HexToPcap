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
        private static readonly Regex LeadingTokenWithColonRegex = new Regex(
            @"^\s*(?<token>\S+:)\s*(?<rest>.*)$",
            RegexOptions.Compiled);

        public ParseResult Parse(string input)
        {
            var packets = new List<byte[]>();

            if (string.IsNullOrWhiteSpace(input))
            {
                return new ParseResult(packets);
            }

            var blocks = SplitIntoBlocks(input);
            for (var index = 0; index < blocks.Count; index++)
            {
                ParseBlock(blocks[index], packets);
            }

            return new ParseResult(packets);
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

        private static void ParseBlock(TextBlock block, ICollection<byte[]> packets)
        {
            List<byte> currentPacket = null;

            for (var index = 0; index < block.Lines.Count; index++)
            {
                var parsedLine = ParseLine(block.Lines[index]);
                if (parsedLine.Bytes.Length == 0)
                {
                    continue;
                }

                if (currentPacket == null)
                {
                    currentPacket = new List<byte>();
                }
                else if (StartsWithRecognizedEthernetHeader(parsedLine.Bytes))
                {
                    packets.Add(currentPacket.ToArray());
                    currentPacket = new List<byte>();
                }

                currentPacket.AddRange(parsedLine.Bytes);
            }

            if (currentPacket != null && currentPacket.Count > 0)
            {
                packets.Add(currentPacket.ToArray());
            }
        }

        private static ParsedLine ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return new ParsedLine(new byte[0]);
            }

            var remaining = StripLeadingTokenWithColon(line);
            var tokens = remaining.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return new ParsedLine(ExtractBytesFromTokens(tokens));
        }

        private static string StripLeadingTokenWithColon(string line)
        {
            var match = LeadingTokenWithColonRegex.Match(line);
            return match.Success ? match.Groups["rest"].Value : line;
        }

        private static byte[] ExtractBytesFromTokens(string[] tokens)
        {
            var bytes = new List<byte>();
            var hasParsedHex = false;

            for (var index = 0; index < tokens.Length; index++)
            {
                var normalizedToken = NormalizeHexToken(tokens[index]);
                if (normalizedToken == null)
                {
                    if (hasParsedHex)
                    {
                        break;
                    }

                    continue;
                }

                hasParsedHex = true;
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

        private sealed class ParsedLine
        {
            public ParsedLine(byte[] bytes)
            {
                Bytes = bytes ?? new byte[0];
            }

            public byte[] Bytes { get; private set; }
        }
    }
}
