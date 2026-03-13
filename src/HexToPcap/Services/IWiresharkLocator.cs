namespace HexToPcap.Services
{
    public interface IWiresharkLocator
    {
        string ResolveLaunchPath(string configuredPath);

        void OpenCapture(string wiresharkPath, string capturePath);
    }
}

