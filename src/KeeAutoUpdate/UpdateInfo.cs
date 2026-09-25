using System;

namespace KeeAutoUpdate
{
    /// <summary>Describes an available KeePass release, as discovered from the SourceForge file feed.</summary>
    public sealed class UpdateInfo
    {
        public Version Version { get; }
        public string DownloadUrl { get; }

        public UpdateInfo(Version version, string downloadUrl)
        {
            Version = version;
            DownloadUrl = downloadUrl;
        }
    }
}
