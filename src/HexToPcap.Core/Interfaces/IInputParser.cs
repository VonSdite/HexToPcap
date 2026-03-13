using HexToPcap.Core.Models;

namespace HexToPcap.Core.Interfaces
{
    public interface IInputParser
    {
        ParseResult Parse(string input);
    }
}

