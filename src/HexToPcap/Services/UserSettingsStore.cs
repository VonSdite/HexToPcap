using System.Configuration;

namespace HexToPcap.Services
{
    internal sealed class UserSettingsStore : ApplicationSettingsBase
    {
        private static readonly UserSettingsStore DefaultInstance =
            (UserSettingsStore)Synchronized(new UserSettingsStore());

        public static UserSettingsStore Default
        {
            get { return DefaultInstance; }
        }

        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string OutputDirectory
        {
            get { return (string)this["OutputDirectory"]; }
            set { this["OutputDirectory"] = value; }
        }

        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string WiresharkPath
        {
            get { return (string)this["WiresharkPath"]; }
            set { this["WiresharkPath"] = value; }
        }
    }
}

