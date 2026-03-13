namespace HexToPcap.Core.Models
{
    public sealed class PacketParseError
    {
        public PacketParseError(int index, string reason, string sourcePreview)
        {
            Index = index;
            Reason = reason;
            SourcePreview = sourcePreview;
        }

        public int Index { get; private set; }

        public string Reason { get; private set; }

        public string SourcePreview { get; private set; }
    }
}

