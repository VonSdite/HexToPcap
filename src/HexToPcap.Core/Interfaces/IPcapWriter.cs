using System.Collections.Generic;

namespace HexToPcap.Core.Interfaces
{
    public interface IPcapWriter
    {
        string Write(string outputDirectory, IList<byte[]> packets);
    }
}

