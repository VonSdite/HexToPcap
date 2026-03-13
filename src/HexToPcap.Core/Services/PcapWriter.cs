using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HexToPcap.Core.Interfaces;

namespace HexToPcap.Core.Services
{
    public sealed class PcapWriter : IPcapWriter
    {
        private const uint MagicNumber = 0xa1b2c3d4;
        private const ushort MajorVersion = 2;
        private const ushort MinorVersion = 4;
        private const uint SnapshotLength = 65535;
        private const uint EthernetLinkType = 1;

        public string Write(string outputDirectory, IList<byte[]> packets)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("输出目录不能为空。", "outputDirectory");
            }

            if (packets == null)
            {
                throw new ArgumentNullException("packets");
            }

            if (packets.Count == 0)
            {
                throw new InvalidOperationException("没有可写入的报文。");
            }

            Directory.CreateDirectory(outputDirectory);

            var fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}.pcap",
                DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                packets.Count);
            var filePath = Path.Combine(outputDirectory, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                WriteGlobalHeader(writer);

                var timestamp = DateTime.Now;
                for (var index = 0; index < packets.Count; index++)
                {
                    WritePacket(writer, packets[index], timestamp.AddMilliseconds(index));
                }
            }

            return filePath;
        }

        private static void WriteGlobalHeader(BinaryWriter writer)
        {
            writer.Write(MagicNumber);
            writer.Write(MajorVersion);
            writer.Write(MinorVersion);
            writer.Write(0);
            writer.Write(0u);
            writer.Write(SnapshotLength);
            writer.Write(EthernetLinkType);
        }

        private static void WritePacket(BinaryWriter writer, byte[] packet, DateTime timestamp)
        {
            var dateTimeOffset = new DateTimeOffset(timestamp);
            var unixTime = dateTimeOffset.ToUnixTimeSeconds();
            var seconds = (uint)unixTime;
            var microseconds = (uint)((dateTimeOffset.Ticks % TimeSpan.TicksPerSecond) / 10L);
            var length = (uint)packet.Length;

            writer.Write(seconds);
            writer.Write(microseconds);
            writer.Write(length);
            writer.Write(length);
            writer.Write(packet);
        }
    }
}
