using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HexToPcap.Core.Models
{
    public sealed class ParseResult
    {
        public ParseResult(IList<byte[]> successfulPackets, IList<PacketParseError> errors)
        {
            SuccessfulPackets = new ReadOnlyCollection<byte[]>(successfulPackets);
            Errors = new ReadOnlyCollection<PacketParseError>(errors);
        }

        public ReadOnlyCollection<byte[]> SuccessfulPackets { get; private set; }

        public ReadOnlyCollection<PacketParseError> Errors { get; private set; }
    }
}

