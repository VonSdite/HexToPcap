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
                new KeyValuePair<string, Action>("ConstructsParseResultWithoutErrorsCollection", ConstructsParseResultWithoutErrorsCollection),
                new KeyValuePair<string, Action>("ParsesSingleIpv4Frame", ParsesSingleIpv4Frame),
                new KeyValuePair<string, Action>("ParsesMultiLineIpv4Frame", ParsesMultiLineIpv4Frame),
                new KeyValuePair<string, Action>("ParsesBlankSeparatedFrames", ParsesBlankSeparatedFrames),
                new KeyValuePair<string, Action>("SplitsPacketsWhenRecognizedEthernetHeaderStartsNewLine", SplitsPacketsWhenRecognizedEthernetHeaderStartsNewLine),
                new KeyValuePair<string, Action>("IgnoresOffsetPrefixesInPlainInput", IgnoresOffsetPrefixesInPlainInput),
                new KeyValuePair<string, Action>("PadsOddHexTokensInsteadOfFailing", PadsOddHexTokensInsteadOfFailing),
                new KeyValuePair<string, Action>("OutputsIncompletePacketsWithoutErrors", OutputsIncompletePacketsWithoutErrors),
                new KeyValuePair<string, Action>("ParsesSingleTcpdumpPacket", ParsesSingleTcpdumpPacket),
                new KeyValuePair<string, Action>("ParsesMultipleTcpdumpPacketsWithOffsetReset", ParsesMultipleTcpdumpPacketsWithOffsetReset),
                new KeyValuePair<string, Action>("IgnoresTcpdumpAsciiAndOffsetPrefixBytes", IgnoresTcpdumpAsciiAndOffsetPrefixBytes),
                new KeyValuePair<string, Action>("KeepsTcpdumpPacketsWhenOffsetsJump", KeepsTcpdumpPacketsWhenOffsetsJump),
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

        private static void ConstructsParseResultWithoutErrorsCollection()
        {
            var packet = new byte[] { 0x00, 0x11 };
            var result = new ParseResult(new List<byte[]> { packet });

            AssertEqual(1, result.SuccessfulPackets.Count, "ParseResult should expose packet collection without an errors list.");
            AssertSequenceEqual(packet, result.SuccessfulPackets[0], "ParseResult packet bytes do not match.");
        }

        private static void ParsesSingleIpv4Frame()
        {
            var parser = new HexInputParser();
            var frame = BuildIpv4Frame(0x01, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            var result = parser.Parse(ToPlainHexLines(frame, frame.Length));

            AssertCounts(result, 1);
            AssertSequenceEqual(frame, result.SuccessfulPackets[0], "Single IPv4 frame bytes do not match.");
        }

        private static void ParsesMultiLineIpv4Frame()
        {
            var parser = new HexInputParser();
            var frame = BuildIpv4Frame(0x02, Enumerable.Range(1, 12).Select(value => (byte)value).ToArray());
            var result = parser.Parse(ToPlainHexLines(frame, 8));

            AssertCounts(result, 1);
            AssertSequenceEqual(frame, result.SuccessfulPackets[0], "Multi-line IPv4 frame bytes do not match.");
        }

        private static void ParsesBlankSeparatedFrames()
        {
            var parser = new HexInputParser();
            var frame1 = BuildIpv4Frame(0x03, new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var frame2 = BuildIpv4Frame(0x04, new byte[] { 0x05, 0x06, 0x07, 0x08 });
            var input = ToPlainHexLines(frame1, 10) + Environment.NewLine + Environment.NewLine + ToPlainHexLines(frame2, 10);
            var result = parser.Parse(input);

            AssertCounts(result, 2);
            AssertSequenceEqual(frame1, result.SuccessfulPackets[0], "First blank-separated frame bytes do not match.");
            AssertSequenceEqual(frame2, result.SuccessfulPackets[1], "Second blank-separated frame bytes do not match.");
        }

        private static void SplitsPacketsWhenRecognizedEthernetHeaderStartsNewLine()
        {
            var parser = new HexInputParser();
            var fragment = new byte[] { 0xAA, 0xBB, 0xCC };
            var frame = BuildIpv4Frame(0x05, new byte[] { 0x10, 0x11, 0x12, 0x13 });
            var input = ToPlainHexLines(fragment, fragment.Length) + Environment.NewLine + ToPlainHexLines(frame, frame.Length);
            var result = parser.Parse(input);

            AssertCounts(result, 2);
            AssertSequenceEqual(fragment, result.SuccessfulPackets[0], "Leading fragment should be exported as the first packet.");
            AssertSequenceEqual(frame, result.SuccessfulPackets[1], "Recognized Ethernet frame should start a new packet.");
        }

        private static void IgnoresOffsetPrefixesInPlainInput()
        {
            var parser = new HexInputParser();
            var expected = new byte[] { 0x00, 0x11, 0x22, 0x33 };
            var result = parser.Parse("0x0000: 00 11 22 33");

            AssertCounts(result, 1);
            AssertSequenceEqual(expected, result.SuccessfulPackets[0], "Offset prefix should not become packet bytes.");
        }

        private static void PadsOddHexTokensInsteadOfFailing()
        {
            var parser = new HexInputParser();
            var expected = new byte[] { 0xA0, 0xBB, 0x12, 0x30 };
            var result = parser.Parse("A BB 123");

            AssertCounts(result, 1);
            AssertSequenceEqual(expected, result.SuccessfulPackets[0], "Odd-length tokens should be padded with trailing zeroes.");
        }

        private static void OutputsIncompletePacketsWithoutErrors()
        {
            var parser = new HexInputParser();
            var truncated = BuildIpv4Frame(0x06, new byte[] { 0x20, 0x21, 0x22, 0x23 }).Take(18).ToArray();
            var result = parser.Parse(ToPlainHexLines(truncated, truncated.Length));

            AssertCounts(result, 1);
            AssertSequenceEqual(truncated, result.SuccessfulPackets[0], "Incomplete packet should still be exported.");
        }

        private static void ParsesSingleTcpdumpPacket()
        {
            var parser = new HexInputParser();
            var frame = BuildIpv4Frame(0x41, Encoding.ASCII.GetBytes("abcdef"));
            var result = parser.Parse(ToTcpdumpHex(frame));

            AssertCounts(result, 1);
            AssertSequenceEqual(frame, result.SuccessfulPackets[0], "Tcpdump packet bytes do not match.");
        }

        private static void ParsesMultipleTcpdumpPacketsWithOffsetReset()
        {
            var parser = new HexInputParser();
            var frame1 = BuildIpv4Frame(0x42, new byte[] { 0x10, 0x20, 0x30, 0x40 });
            var frame2 = BuildIpv6Frame(0x43, new byte[] { 0x50, 0x60, 0x70, 0x80 });
            var input = ToTcpdumpHex(frame1) + ToTcpdumpHex(frame2);
            var result = parser.Parse(input);

            AssertCounts(result, 2);
            AssertSequenceEqual(frame1, result.SuccessfulPackets[0], "First tcpdump packet bytes do not match.");
            AssertSequenceEqual(frame2, result.SuccessfulPackets[1], "Second tcpdump packet bytes do not match.");
        }

        private static void IgnoresTcpdumpAsciiAndOffsetPrefixBytes()
        {
            var parser = new HexInputParser();
            var expected = new byte[] { 0x00, 0x11, 0x22, 0x33 };
            var input =
                "12:00:00.000000 IP sample > sample: payload" + Environment.NewLine +
                "        0x0000:  0011 2233  ..\"3";
            var result = parser.Parse(input);

            AssertCounts(result, 1);
            AssertSequenceEqual(expected, result.SuccessfulPackets[0], "Tcpdump ASCII preview should be ignored.");
        }

        private static void KeepsTcpdumpPacketsWhenOffsetsJump()
        {
            var parser = new HexInputParser();
            var expected = new byte[] { 0x00, 0x11, 0x22, 0x33 };
            var input =
                "12:00:00.000000 IP sample > sample: payload" + Environment.NewLine +
                "        0x0010:  0011 2233  ..\"3";
            var result = parser.Parse(input);

            AssertCounts(result, 1);
            AssertSequenceEqual(expected, result.SuccessfulPackets[0], "Tcpdump offsets should not be validated before export.");
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
                    throw new InvalidOperationException("Output file name does not match yyyyMMddHHmmss-count.pcap.");
                }

                var bytes = File.ReadAllBytes(outputPath);
                if (bytes.Length <= 24)
                {
                    throw new InvalidOperationException("Pcap file is unexpectedly short.");
                }

                AssertEqual(0xD4, bytes[0], "pcap magic number byte 0 is incorrect.");
                AssertEqual(0xC3, bytes[1], "pcap magic number byte 1 is incorrect.");
                AssertEqual(0xB2, bytes[2], "pcap magic number byte 2 is incorrect.");
                AssertEqual(0xA1, bytes[3], "pcap magic number byte 3 is incorrect.");
                AssertEqual(1u, BitConverter.ToUInt32(bytes, 20), "pcap network field should be Ethernet.");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }

        private static void AssertCounts(ParseResult result, int successCount)
        {
            AssertEqual(successCount, result.SuccessfulPackets.Count, "Unexpected successful packet count.");
        }

        private static void AssertSequenceEqual(byte[] expected, byte[] actual, string message)
        {
            if (expected.Length != actual.Length)
            {
                throw new InvalidOperationException(message + " Length mismatch.");
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] != actual[index])
                {
                    throw new InvalidOperationException(message + " Byte mismatch at index " + index.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(message + " Expected=" + expected + " Actual=" + actual);
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

        private static byte[] BuildEthernetHeader(byte marker, ushort etherType)
        {
            return new byte[]
            {
                0x00, 0x11, 0x22, 0x33, 0x44, marker,
                0x66, 0x77, 0x88, 0x99, 0xAA, (byte)(marker + 1),
                (byte)(etherType >> 8), (byte)(etherType & 0xFF)
            };
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
