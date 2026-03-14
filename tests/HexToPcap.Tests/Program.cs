using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HexToPcap.Core.Models;
using HexToPcap.Core.Services;

namespace HexToPcap.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var failures = new List<string>();
            var tests = new[]
            {
                new KeyValuePair<string, Action>("ParsesSingleIpv4Frame", ParsesSingleIpv4Frame),
                new KeyValuePair<string, Action>("ParsesMultiLineIpv4Frame", ParsesMultiLineIpv4Frame),
                new KeyValuePair<string, Action>("ParsesBlankSeparatedFrames", ParsesBlankSeparatedFrames),
                new KeyValuePair<string, Action>("SplitsConcatenatedIpv4FramesWithoutBlankLines", SplitsConcatenatedIpv4FramesWithoutBlankLines),
                new KeyValuePair<string, Action>("SplitsIpv6FramesWithoutBlankLines", SplitsIpv6FramesWithoutBlankLines),
                new KeyValuePair<string, Action>("SplitsArpFramesWithoutBlankLines", SplitsArpFramesWithoutBlankLines),
                new KeyValuePair<string, Action>("SplitsVlanFramesWithoutBlankLines", SplitsVlanFramesWithoutBlankLines),
                new KeyValuePair<string, Action>("SplitsStackedVlanFramesWithoutBlankLines", SplitsStackedVlanFramesWithoutBlankLines),
                new KeyValuePair<string, Action>("ParsesSingleTcpdumpPacket", ParsesSingleTcpdumpPacket),
                new KeyValuePair<string, Action>("ParsesMultipleTcpdumpPacketsWithOffsetReset", ParsesMultipleTcpdumpPacketsWithOffsetReset),
                new KeyValuePair<string, Action>("RejectsInvalidCharactersAndOddHex", RejectsInvalidCharactersAndOddHex),
                new KeyValuePair<string, Action>("RejectsUnknownEtherTypeConcatenation", RejectsUnknownEtherTypeConcatenation),
                new KeyValuePair<string, Action>("CapturesResidualBytesAsError", CapturesResidualBytesAsError),
                new KeyValuePair<string, Action>("WritesClassicPcapHeader", WritesClassicPcapHeader)
            };

            for (var index = 0; index < tests.Length; index++)
            {
                var test = tests[index];
                try
                {
                    test.Value();
                    Console.WriteLine("[PASS] " + test.Key);
                }
                catch (Exception ex)
                {
                    failures.Add(test.Key + ": " + ex.Message);
                    Console.WriteLine("[FAIL] " + test.Key + " - " + ex.Message);
                }
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("All tests passed.");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("Failures:");
            for (var index = 0; index < failures.Count; index++)
            {
                Console.WriteLine(" - " + failures[index]);
            }

            return 1;
        }

        private static void ParsesSingleIpv4Frame()
        {
            var parser = new HexInputParser();
            var frame = BuildIpv4Frame(0x01, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            var result = parser.Parse(ToPlainHexLines(frame, frame.Length));

            AssertCounts(result, 1, 0);
            AssertSequenceEqual(frame, result.SuccessfulPackets[0], "单个 IPv4 报文解析结果不一致。");
        }

        private static void ParsesMultiLineIpv4Frame()
        {
            var parser = new HexInputParser();
            var frame = BuildIpv4Frame(0x02, Enumerable.Range(1, 12).Select(value => (byte)value).ToArray());
            var result = parser.Parse(ToPlainHexLines(frame, 8));

            AssertCounts(result, 1, 0);
            AssertSequenceEqual(frame, result.SuccessfulPackets[0], "跨多行 IPv4 报文解析结果不一致。");
        }

        private static void ParsesBlankSeparatedFrames()
        {
            var parser = new HexInputParser();
            var frame1 = BuildIpv4Frame(0x03, new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var frame2 = BuildIpv4Frame(0x04, new byte[] { 0x05, 0x06, 0x07, 0x08 });
            var input = ToPlainHexLines(frame1, 10) + Environment.NewLine + Environment.NewLine + ToPlainHexLines(frame2, 10);
            var result = parser.Parse(input);

            AssertCounts(result, 2, 0);
        }

        private static void SplitsConcatenatedIpv4FramesWithoutBlankLines()
        {
            var parser = new HexInputParser();
            var frame1 = BuildIpv4Frame(0x05, new byte[] { 0x10, 0x11, 0x12, 0x13 });
            var frame2 = BuildIpv4Frame(0x06, new byte[] { 0x20, 0x21, 0x22, 0x23, 0x24, 0x25 });
            var result = parser.Parse(ToPlainHexLines(Concat(frame1, frame2), 16));

            AssertCounts(result, 2, 0);
            AssertSequenceEqual(frame1, result.SuccessfulPackets[0], "第一个 IPv4 报文拆分错误。");
            AssertSequenceEqual(frame2, result.SuccessfulPackets[1], "第二个 IPv4 报文拆分错误。");
        }

        private static void SplitsIpv6FramesWithoutBlankLines()
        {
            var parser = new HexInputParser();
            var frame1 = BuildIpv6Frame(0x11, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
            var frame2 = BuildIpv6Frame(0x12, new byte[] { 0xEE, 0xFF, 0x01, 0x02, 0x03, 0x04 });
            var result = parser.Parse(ToPlainHexLines(Concat(frame1, frame2), 24));

            AssertCounts(result, 2, 0);
        }

        private static void SplitsArpFramesWithoutBlankLines()
        {
            var parser = new HexInputParser();
            var frame1 = BuildArpFrame(0x21);
            var frame2 = BuildArpFrame(0x22);
            var result = parser.Parse(ToPlainHexLines(Concat(frame1, frame2), 21));

            AssertCounts(result, 2, 0);
        }

        private static void SplitsVlanFramesWithoutBlankLines()
        {
            var parser = new HexInputParser();
            var frame1 = BuildVlanIpv4Frame(0x31, new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var frame2 = BuildVlanIpv6Frame(0x32, new byte[] { 0x05, 0x06, 0x07, 0x08 });
            var result = parser.Parse(ToPlainHexLines(Concat(frame1, frame2), 16));

            AssertCounts(result, 2, 0);
        }

        private static void SplitsStackedVlanFramesWithoutBlankLines()
        {
            var parser = new HexInputParser();
            var frame1 = BuildStackedVlanIpv4Frame(0x33, new byte[] { 0x10, 0x11, 0x12, 0x13 }, 0x88A8, 0x8100);
            var frame2 = BuildStackedVlanIpv6Frame(0x34, new byte[] { 0x20, 0x21, 0x22, 0x23, 0x24 }, 0x9100, 0x88A8, 0x8100);
            var result = parser.Parse(ToPlainHexLines(Concat(frame1, frame2), 20));

            AssertCounts(result, 2, 0);
            AssertSequenceEqual(frame1, result.SuccessfulPackets[0], "澶氬眰 VLAN IPv4 鎶ユ枃鎷嗗垎閿欒銆?");
            AssertSequenceEqual(frame2, result.SuccessfulPackets[1], "澶氬眰 VLAN IPv6 鎶ユ枃鎷嗗垎閿欒銆?");
        }

        private static void ParsesSingleTcpdumpPacket()
        {
            var parser = new HexInputParser();
            var frame = BuildIpv4Frame(0x41, Encoding.ASCII.GetBytes("abcdef"));
            var result = parser.Parse(ToTcpdumpHex(frame));

            AssertCounts(result, 1, 0);
            AssertSequenceEqual(frame, result.SuccessfulPackets[0], "tcpdump 单包解析结果不一致。");
        }

        private static void ParsesMultipleTcpdumpPacketsWithOffsetReset()
        {
            var parser = new HexInputParser();
            var frame1 = BuildIpv4Frame(0x42, new byte[] { 0x10, 0x20, 0x30, 0x40 });
            var frame2 = BuildIpv6Frame(0x43, new byte[] { 0x50, 0x60, 0x70, 0x80 });
            var input = ToTcpdumpHex(frame1) + ToTcpdumpHex(frame2);
            var result = parser.Parse(input);

            AssertCounts(result, 2, 0);
        }

        private static void RejectsInvalidCharactersAndOddHex()
        {
            var parser = new HexInputParser();

            var invalidChar = parser.Parse("00 11 22 ZZ");
            AssertCounts(invalidChar, 0, 1);

            var oddHex = parser.Parse("00 11 223");
            AssertCounts(oddHex, 0, 1);
        }

        private static void RejectsUnknownEtherTypeConcatenation()
        {
            var parser = new HexInputParser();
            var frame1 = BuildUnknownEtherTypeFrame(0x51, new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var frame2 = BuildUnknownEtherTypeFrame(0x52, new byte[] { 0x05, 0x06, 0x07, 0x08 });
            var result = parser.Parse(ToPlainHexLines(Concat(frame1, frame2), 16));

            AssertCounts(result, 0, 1);
        }

        private static void CapturesResidualBytesAsError()
        {
            var parser = new HexInputParser();
            var frame1 = BuildIpv4Frame(0x61, new byte[] { 0x90, 0x91, 0x92, 0x93 });
            var frame2 = BuildIpv4Frame(0x62, new byte[] { 0xA0, 0xA1, 0xA2, 0xA3 });
            var truncated = frame2.Take(10).ToArray();
            var result = parser.Parse(ToPlainHexLines(Concat(frame1, truncated), 20));

            AssertCounts(result, 1, 1);
        }

        private static void WritesClassicPcapHeader()
        {
            var writer = new PcapWriter();
            var frame = BuildIpv4Frame(0x71, new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var tempDirectory = Path.Combine(Path.GetTempPath(), "HexToPcapTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var outputPath = writer.Write(tempDirectory, new List<byte[]> { frame });
                var fileName = Path.GetFileName(outputPath);
                if (!Regex.IsMatch(fileName, @"^\d{14}-1\.pcap$"))
                {
                    throw new InvalidOperationException("输出文件名不符合 yyyyMMddHHmmss-个数.pcap 格式。");
                }

                var bytes = File.ReadAllBytes(outputPath);
                if (bytes.Length <= 24)
                {
                    throw new InvalidOperationException("pcap 文件长度异常。");
                }

                AssertEqual(0xD4, bytes[0], "pcap magic number 字节 0 错误。");
                AssertEqual(0xC3, bytes[1], "pcap magic number 字节 1 错误。");
                AssertEqual(0xB2, bytes[2], "pcap magic number 字节 2 错误。");
                AssertEqual(0xA1, bytes[3], "pcap magic number 字节 3 错误。");
                AssertEqual(1u, BitConverter.ToUInt32(bytes, 20), "pcap network 字段不是 Ethernet。");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }

        private static void AssertCounts(ParseResult result, int successCount, int errorCount)
        {
            AssertEqual(successCount, result.SuccessfulPackets.Count, "成功报文数量不符合预期。");
            AssertEqual(errorCount, result.Errors.Count, "失败报文数量不符合预期。");
        }

        private static void AssertSequenceEqual(byte[] expected, byte[] actual, string message)
        {
            if (expected.Length != actual.Length)
            {
                throw new InvalidOperationException(message + " 长度不一致。");
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] != actual[index])
                {
                    throw new InvalidOperationException(message + " 索引 " + index.ToString(CultureInfo.InvariantCulture) + " 不一致。");
                }
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(message + " 期望=" + expected + " 实际=" + actual);
            }
        }

        private static byte[] BuildIpv4Frame(byte marker, byte[] payload)
        {
            var ethernet = BuildEthernetHeader(marker, 0x0800);
            var ip = new byte[20];
            ip[0] = 0x45;
            ip[1] = 0x00;
            var totalLength = (ushort)(20 + payload.Length);
            ip[2] = (byte)(totalLength >> 8);
            ip[3] = (byte)(totalLength & 0xFF);
            ip[4] = 0x00;
            ip[5] = marker;
            ip[6] = 0x00;
            ip[7] = 0x00;
            ip[8] = 0x40;
            ip[9] = 0x01;
            ip[10] = 0x00;
            ip[11] = 0x00;
            ip[12] = 192;
            ip[13] = 168;
            ip[14] = 1;
            ip[15] = marker;
            ip[16] = 192;
            ip[17] = 168;
            ip[18] = 1;
            ip[19] = (byte)(marker + 1);
            return Concat(ethernet, ip, payload);
        }

        private static byte[] BuildIpv6Frame(byte marker, byte[] payload)
        {
            var ethernet = BuildEthernetHeader(marker, 0x86DD);
            var ip = new byte[40];
            ip[0] = 0x60;
            ip[1] = 0x00;
            ip[2] = 0x00;
            ip[3] = 0x00;
            ip[4] = (byte)(payload.Length >> 8);
            ip[5] = (byte)(payload.Length & 0xFF);
            ip[6] = 59;
            ip[7] = 64;
            for (var index = 0; index < 16; index++)
            {
                ip[8 + index] = (byte)(0x20 + index + marker);
                ip[24 + index] = (byte)(0x40 + index + marker);
            }

            return Concat(ethernet, ip, payload);
        }

        private static byte[] BuildArpFrame(byte marker)
        {
            var ethernet = BuildEthernetHeader(marker, 0x0806);
            var arp = new byte[]
            {
                0x00, 0x01,
                0x08, 0x00,
                0x06,
                0x04,
                0x00, 0x01,
                0x00, 0x11, 0x22, 0x33, 0x44, marker,
                192, 168, 1, marker,
                0x66, 0x77, 0x88, 0x99, 0xAA, (byte)(marker + 1),
                192, 168, 1, (byte)(marker + 1)
            };
            return Concat(ethernet, arp);
        }

        private static byte[] BuildVlanIpv4Frame(byte marker, byte[] payload)
        {
            return WrapWithVlanTags(BuildIpv4Frame(marker, payload), 0x8100);
        }

        private static byte[] BuildVlanIpv6Frame(byte marker, byte[] payload)
        {
            return WrapWithVlanTags(BuildIpv6Frame(marker, payload), 0x8100);
        }

        private static byte[] BuildStackedVlanIpv4Frame(byte marker, byte[] payload, params ushort[] vlanEtherTypes)
        {
            return WrapWithVlanTags(BuildIpv4Frame(marker, payload), vlanEtherTypes);
        }

        private static byte[] BuildStackedVlanIpv6Frame(byte marker, byte[] payload, params ushort[] vlanEtherTypes)
        {
            return WrapWithVlanTags(BuildIpv6Frame(marker, payload), vlanEtherTypes);
        }

        private static byte[] BuildUnknownEtherTypeFrame(byte marker, byte[] payload)
        {
            return Concat(BuildEthernetHeader(marker, 0x88B5), payload);
        }

        private static byte[] BuildEthernetHeader(byte marker, ushort etherType)
        {
            return new byte[]
            {
                0x00, 0x11, 0x22, 0x33, 0x44, marker,
                0x66, 0x77, 0x88, 0x99, 0xAA, (byte)(marker + 1),
                (byte)(etherType >> 8), (byte)(etherType & 0xFF)
            };
        }

        private static byte[] WrapWithVlanTags(byte[] frame, params ushort[] vlanEtherTypes)
        {
            if (vlanEtherTypes == null || vlanEtherTypes.Length == 0)
            {
                return frame;
            }

            var originalEtherType = (ushort)((frame[12] << 8) | frame[13]);
            var ethernet = new byte[14];
            Buffer.BlockCopy(frame, 0, ethernet, 0, 12);
            ethernet[12] = (byte)(vlanEtherTypes[0] >> 8);
            ethernet[13] = (byte)(vlanEtherTypes[0] & 0xFF);

            var tags = new byte[vlanEtherTypes.Length * 4];
            for (var index = 0; index < vlanEtherTypes.Length; index++)
            {
                var tagOffset = index * 4;
                var nextEtherType = index == vlanEtherTypes.Length - 1
                    ? originalEtherType
                    : vlanEtherTypes[index + 1];

                tags[tagOffset] = 0x00;
                tags[tagOffset + 1] = (byte)(index + 1);
                tags[tagOffset + 2] = (byte)(nextEtherType >> 8);
                tags[tagOffset + 3] = (byte)(nextEtherType & 0xFF);
            }

            return Concat(ethernet, tags, frame.Skip(14).ToArray());
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            var totalLength = arrays.Sum(array => array.Length);
            var result = new byte[totalLength];
            var offset = 0;
            for (var index = 0; index < arrays.Length; index++)
            {
                Buffer.BlockCopy(arrays[index], 0, result, offset, arrays[index].Length);
                offset += arrays[index].Length;
            }

            return result;
        }

        private static string ToPlainHexLines(byte[] data, int bytesPerLine)
        {
            var builder = new StringBuilder();
            for (var offset = 0; offset < data.Length; offset += bytesPerLine)
            {
                var lineLength = Math.Min(bytesPerLine, data.Length - offset);
                for (var index = 0; index < lineLength; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(data[offset + index].ToString("X2", CultureInfo.InvariantCulture));
                }

                if (offset + lineLength < data.Length)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private static string ToTcpdumpHex(byte[] data)
        {
            var builder = new StringBuilder();
            builder.AppendLine("12:00:00.000000 IP sample > sample: payload");

            for (var offset = 0; offset < data.Length; offset += 16)
            {
                var lineLength = Math.Min(16, data.Length - offset);
                builder.Append("        0x");
                builder.Append(offset.ToString("x4", CultureInfo.InvariantCulture));
                builder.Append(":  ");

                for (var index = 0; index < lineLength; index += 2)
                {
                    if (index > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(data[offset + index].ToString("x2", CultureInfo.InvariantCulture));
                    if (index + 1 < lineLength)
                    {
                        builder.Append(data[offset + index + 1].ToString("x2", CultureInfo.InvariantCulture));
                    }
                }

                builder.Append("  ");
                for (var index = 0; index < lineLength; index++)
                {
                    var value = data[offset + index];
                    builder.Append(value >= 32 && value <= 126 ? (char)value : '.');
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
