namespace HexToPcap.Models
{
    public sealed class ConversionOutcome
    {
        public string OutputPath { get; set; }

        public string WiresharkPath { get; set; }

        public int SuccessfulPacketCount { get; set; }

        public int FailedPacketCount { get; set; }
    }
}

