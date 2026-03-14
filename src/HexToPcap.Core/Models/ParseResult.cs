using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HexToPcap.Core.Models
{
    public sealed class ParseResult
    {
        public ParseResult(IList<byte[]> successfulPackets)
        {
            SuccessfulPackets = new ReadOnlyCollection<byte[]>(successfulPackets);
        }

        public ReadOnlyCollection<byte[]> SuccessfulPackets { get; private set; }
    }
}
