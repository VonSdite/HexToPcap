using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HexToPcap.Core.Interfaces;
using HexToPcap.Core.Models;

namespace HexToPcap.Core.Services
{
    public sealed class HexInputParser : IInputParser
    {
        private static readonly Regex TcpdumpOffsetRegex = new Regex(
            @"^\s*0x(?<offset>[0-9A-Fa-f]+):\s*(?<rest>.*)$",
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
            var packetIndex = 1;

            foreach (var block in blocks)
            {
                if (ContainsTcpdumpOffsets(block))
                {
                    ParseTcpdumpBlock(block, packets, errors, ref packetIndex);
                }
                else
                {
                    ParsePlainBlock(block, packets, errors, ref packetIndex);
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
            var previousWasBlank = false;

            for (var index = 0; index < lines.Length; index++)
            {
                var line = (lines[index] ?? string.Empty).Trim();
                if (line.Length == 0)
                {
                    if (current != null)
                    {
                        current.HasTrailingBlank = true;
                        current = null;
                    }

                    previousWasBlank = true;
                    continue;
                }

                if (current == null)
                {
                    current = new TextBlock();
                    current.HasLeadingBlank = previousWasBlank;
                    blocks.Add(current);
                }

                current.Lines.Add(line);
                previousWasBlank = false;
            }

            if (blocks.Count == 1)
            {
                blocks[0].IsOnlyBlock = true;
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

        private static void ParseTcpdumpBlock(
            TextBlock block,
            IList<byte[]> packets,
            IList<PacketParseError> errors,
            ref int packetIndex)
        {
            var currentBytes = new List<byte>();
            var previewLines = new List<string>();

            for (var index = 0; index < block.Lines.Count; index++)
            {
                var line = block.Lines[index];
                var match = TcpdumpOffsetRegex.Match(line);
                if (!match.Success)
                {
                    if (previewLines.Count > 0)
                    {
                        previewLines.Add(line);
                    }

                    continue;
                }

                int offset;
                if (!int.TryParse(match.Groups["offset"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset))
                {
                    AddError(errors, ref packetIndex, "tcpdump 偏移地址无法解析。", line);
                    currentBytes.Clear();
                    previewLines.Clear();
                    continue;
                }

                if (offset == 0 && currentBytes.Count > 0)
                {
                    FinalizeTcpdumpPacket(currentBytes, previewLines, packets, errors, ref packetIndex);
                    currentBytes = new List<byte>();
                    previewLines = new List<string>();
                }

                if (offset != currentBytes.Count)
                {
                    if (currentBytes.Count == 0 && offset != 0)
                    {
                        AddError(errors, ref packetIndex, "tcpdump 报文偏移没有从 0x0000 开始。", line);
                        continue;
                    }

                    AddError(errors, ref packetIndex, "tcpdump 偏移与已解析字节数不一致。", BuildPreview(previewLines));
                    currentBytes.Clear();
                    previewLines.Clear();

                    if (offset != 0)
                    {
                        continue;
                    }
                }

                List<byte> lineBytes;
                string lineError;
                if (!TryParseTcpdumpHex(match.Groups["rest"].Value, out lineBytes, out lineError))
                {
                    AddError(errors, ref packetIndex, lineError, line);
                    currentBytes.Clear();
                    previewLines.Clear();
                    continue;
                }

                previewLines.Add(line);
                currentBytes.AddRange(lineBytes);
            }

            if (currentBytes.Count > 0)
            {
                FinalizeTcpdumpPacket(currentBytes, previewLines, packets, errors, ref packetIndex);
            }
        }

        private static void FinalizeTcpdumpPacket(
            List<byte> bytes,
            List<string> previewLines,
            IList<byte[]> packets,
            IList<PacketParseError> errors,
            ref int packetIndex)
        {
            var packetBytes = bytes.ToArray();
            string reason;
            if (ValidatePacket(packetBytes, false, out reason))
            {
                packets.Add(packetBytes);
            }
            else
            {
                errors.Add(new PacketParseError(packetIndex, reason, BuildPreview(previewLines)));
            }

            packetIndex++;
        }

        private static void ParsePlainBlock(
            TextBlock block,
            IList<byte[]> packets,
            IList<PacketParseError> errors,
            ref int packetIndex)
        {
            var allBytes = new List<byte>();

            for (var index = 0; index < block.Lines.Count; index++)
            {
                string normalizedHex;
                string lineError;
                if (!TryNormalizePlainHex(block.Lines[index], out normalizedHex, out lineError))
                {
                    AddError(errors, ref packetIndex, lineError, block.Lines[index]);
                    return;
                }

                allBytes.AddRange(ParseHexString(normalizedHex));
            }

            if (allBytes.Count == 0)
            {
                return;
            }

            var data = allBytes.ToArray();
            var offset = 0;
            var splitCount = 0;
            string splitFailureReason = null;

            while (offset < data.Length)
            {
                int frameLength;
                string inferReason;
                if (!TryGetInferableFrameLength(data, offset, out frameLength, out inferReason))
                {
                    splitFailureReason = inferReason;
                    break;
                }

                packets.Add(Slice(data, offset, frameLength));
                packetIndex++;
                splitCount++;
                offset += frameLength;
            }

            if (offset == data.Length && splitCount > 0)
            {
                return;
            }

            if (offset > 0 && offset < data.Length)
            {
                errors.Add(new PacketParseError(
                    packetIndex,
                    splitFailureReason ?? "剩余字节无法继续推断报文边界。",
                    BuildPreview(Slice(data, offset, data.Length - offset))));
                packetIndex++;
                return;
            }

            if (splitCount == 0 && block.HasExplicitBoundary)
            {
                string validationReason;
                if (ValidatePacket(data, false, out validationReason))
                {
                    packets.Add(data);
                }
                else
                {
                    errors.Add(new PacketParseError(packetIndex, validationReason, BuildPreview(block.Lines)));
                }

                packetIndex++;
                return;
            }

            errors.Add(new PacketParseError(
                packetIndex,
                splitFailureReason ?? "无法根据已知协议长度推断报文边界。",
                BuildPreview(block.Lines)));
            packetIndex++;
        }

        private static bool TryNormalizePlainHex(string line, out string normalizedHex, out string error)
        {
            var builder = new StringBuilder();

            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                if (char.IsWhiteSpace(current))
                {
                    continue;
                }

                if (!IsHexDigit(current))
                {
                    normalizedHex = null;
                    error = "普通十六进制文本中包含非十六进制字符。";
                    return false;
                }

                builder.Append(current);
            }

            if (builder.Length % 2 != 0)
            {
                normalizedHex = null;
                error = "十六进制字符数量为奇数，无法组成完整字节。";
                return false;
            }

            normalizedHex = builder.ToString();
            error = null;
            return true;
        }

        private static bool TryParseTcpdumpHex(string rest, out List<byte> bytes, out string error)
        {
            bytes = new List<byte>();
            var tokens = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            for (var index = 0; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (!IsValidTcpdumpHexToken(token))
                {
                    break;
                }

                for (var charIndex = 0; charIndex < token.Length; charIndex += 2)
                {
                    bytes.Add(byte.Parse(
                        token.Substring(charIndex, 2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));
                }
            }

            if (bytes.Count == 0)
            {
                error = "tcpdump 行中没有找到可识别的十六进制字节。";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsValidTcpdumpHexToken(string token)
        {
            if (token.Length == 0 || token.Length > 4 || token.Length % 2 != 0)
            {
                return false;
            }

            for (var index = 0; index < token.Length; index++)
            {
                if (!IsHexDigit(token[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidatePacket(byte[] data, bool requireInferableLength, out string reason)
        {
            if (data == null || data.Length < 14)
            {
                reason = "报文字节数不足 14，无法构成完整的 Ethernet 帧。";
                return false;
            }

            int inferredLength;
            string inferReason;
            if (TryGetInferableFrameLength(data, 0, out inferredLength, out inferReason))
            {
                reason = null;
                return true;
            }

            if (requireInferableLength)
            {
                reason = inferReason;
                return false;
            }

            ushort etherType;
            int l3Offset;
            if (!TryGetEtherType(data, 0, out etherType, out l3Offset, out reason))
            {
                return false;
            }

            switch (etherType)
            {
                case 0x0800:
                    return ValidateIpv4(data, l3Offset, out reason);
                case 0x86dd:
                    return ValidateIpv6(data, l3Offset, out reason);
                case 0x0806:
                    return ValidateArp(data, l3Offset, out reason);
                default:
                    reason = null;
                    return true;
            }
        }

        private static bool TryGetInferableFrameLength(byte[] data, int offset, out int frameLength, out string reason)
        {
            frameLength = 0;

            ushort etherType;
            int l3Offset;
            if (!TryGetEtherType(data, offset, out etherType, out l3Offset, out reason))
            {
                return false;
            }

            switch (etherType)
            {
                case 0x0800:
                    return TryGetIpv4FrameLength(data, offset, l3Offset, out frameLength, out reason);
                case 0x86dd:
                    return TryGetIpv6FrameLength(data, offset, l3Offset, out frameLength, out reason);
                case 0x0806:
                    return TryGetArpFrameLength(data, offset, l3Offset, out frameLength, out reason);
                default:
                    reason = string.Format(
                        CultureInfo.InvariantCulture,
                        "无法根据 EtherType 0x{0:X4} 推断报文长度。",
                        etherType);
                    return false;
            }
        }

        private static bool TryGetEtherType(byte[] data, int offset, out ushort etherType, out int l3Offset, out string reason)
        {
            etherType = 0;
            l3Offset = 0;

            if (data.Length - offset < 14)
            {
                reason = "报文字节数不足 14，无法构成完整的 Ethernet 帧。";
                return false;
            }

            etherType = (ushort)((data[offset + 12] << 8) | data[offset + 13]);
            l3Offset = offset + 14;

            if (IsVlanEtherType(etherType))
            {
                if (data.Length - offset < 18)
                {
                    reason = "VLAN 报文头部不完整。";
                    return false;
                }

                etherType = (ushort)((data[offset + 16] << 8) | data[offset + 17]);
                l3Offset = offset + 18;
            }

            reason = null;
            return true;
        }

        private static bool TryGetIpv4FrameLength(byte[] data, int offset, int l3Offset, out int frameLength, out string reason)
        {
            frameLength = 0;
            if (!ValidateIpv4(data, l3Offset, out reason))
            {
                return false;
            }

            var totalLength = (data[l3Offset + 2] << 8) | data[l3Offset + 3];
            frameLength = l3Offset + totalLength - offset;
            return true;
        }

        private static bool TryGetIpv6FrameLength(byte[] data, int offset, int l3Offset, out int frameLength, out string reason)
        {
            frameLength = 0;
            if (!ValidateIpv6(data, l3Offset, out reason))
            {
                return false;
            }

            var payloadLength = (data[l3Offset + 4] << 8) | data[l3Offset + 5];
            frameLength = l3Offset + 40 + payloadLength - offset;
            return true;
        }

        private static bool TryGetArpFrameLength(byte[] data, int offset, int l3Offset, out int frameLength, out string reason)
        {
            frameLength = 0;
            if (!ValidateArp(data, l3Offset, out reason))
            {
                return false;
            }

            var hardwareLength = data[l3Offset + 4];
            var protocolLength = data[l3Offset + 5];
            frameLength = l3Offset + 8 + (hardwareLength * 2) + (protocolLength * 2) - offset;
            return true;
        }

        private static bool ValidateIpv4(byte[] data, int l3Offset, out string reason)
        {
            if (data.Length < l3Offset + 20)
            {
                reason = "IPv4 报文头部不完整。";
                return false;
            }

            var version = (data[l3Offset] >> 4) & 0x0f;
            var headerLength = (data[l3Offset] & 0x0f) * 4;
            var totalLength = (data[l3Offset + 2] << 8) | data[l3Offset + 3];

            if (version != 4)
            {
                reason = "IPv4 报文版本字段非法。";
                return false;
            }

            if (headerLength < 20)
            {
                reason = "IPv4 首部长度字段非法。";
                return false;
            }

            if (totalLength < headerLength)
            {
                reason = "IPv4 Total Length 小于首部长度。";
                return false;
            }

            if (l3Offset + totalLength > data.Length)
            {
                reason = "IPv4 报文字节数少于声明长度。";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool ValidateIpv6(byte[] data, int l3Offset, out string reason)
        {
            if (data.Length < l3Offset + 40)
            {
                reason = "IPv6 报文头部不完整。";
                return false;
            }

            var version = (data[l3Offset] >> 4) & 0x0f;
            var payloadLength = (data[l3Offset + 4] << 8) | data[l3Offset + 5];

            if (version != 6)
            {
                reason = "IPv6 报文版本字段非法。";
                return false;
            }

            if (l3Offset + 40 + payloadLength > data.Length)
            {
                reason = "IPv6 报文字节数少于声明长度。";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool ValidateArp(byte[] data, int l3Offset, out string reason)
        {
            if (data.Length < l3Offset + 8)
            {
                reason = "ARP 报文头部不完整。";
                return false;
            }

            var hardwareLength = data[l3Offset + 4];
            var protocolLength = data[l3Offset + 5];
            var arpLength = 8 + (hardwareLength * 2) + (protocolLength * 2);

            if (l3Offset + arpLength > data.Length)
            {
                reason = "ARP 报文字节数少于声明长度。";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool IsVlanEtherType(ushort etherType)
        {
            return etherType == 0x8100 || etherType == 0x88A8 || etherType == 0x9100;
        }

        private static byte[] ParseHexString(string normalizedHex)
        {
            var bytes = new byte[normalizedHex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = byte.Parse(
                    normalizedHex.Substring(index * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            }

            return bytes;
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(source, offset, result, 0, length);
            return result;
        }

        private static string BuildPreview(IEnumerable<string> lines)
        {
            return BuildPreview(string.Join(" ", lines.Where(line => !string.IsNullOrWhiteSpace(line)).Take(4).ToArray()));
        }

        private static string BuildPreview(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            var previewLength = Math.Min(bytes.Length, 24);
            var builder = new StringBuilder();
            for (var index = 0; index < previewLength; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
            }

            if (bytes.Length > previewLength)
            {
                builder.Append(" ...");
            }

            return builder.ToString();
        }

        private static string BuildPreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var compact = Regex.Replace(text.Trim(), @"\s+", " ");
            if (compact.Length <= 120)
            {
                return compact;
            }

            return compact.Substring(0, 117) + "...";
        }

        private static void AddError(ICollection<PacketParseError> errors, ref int packetIndex, string reason, string preview)
        {
            errors.Add(new PacketParseError(packetIndex, reason, BuildPreview(preview)));
            packetIndex++;
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

            public bool HasLeadingBlank { get; set; }

            public bool HasTrailingBlank { get; set; }

            public bool IsOnlyBlock { get; set; }

            public bool HasExplicitBoundary
            {
                get { return HasLeadingBlank || HasTrailingBlank; }
            }
        }
    }
}
