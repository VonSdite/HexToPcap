using HexToPcap.Core.Models;

namespace HexToPcap.Services
{
    public interface ISettingsService
    {
        AppSettings Load();

        void Save(AppSettings settings);
    }
}

