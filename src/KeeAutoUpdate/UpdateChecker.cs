using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace KeeAutoUpdate
{
    /// <summary>
    /// Finds the latest published KeePass 2.x release.
    ///
    /// KeePass's own built-in update-check endpoint (keepass.info/update/version2x.txt) uses a
    /// proprietary, undocumented encoding that isn't safe to reverse-engineer for a third-party
    /// plugin. Instead this reads the official SourceForge project's public RSS file feed, which
    /// is a stable, documented format and lists every published file (including the Windows
    /// installer) with its version folder.
    /// </summary>
    public static class UpdateChecker
    {
        private const string FeedUrl = "https://sourceforge.net/projects/keepass/rss?path=/KeePass%202.x";

        // Matches "/KeePass 2.x/<version>/KeePass-<version>-Setup.exe"
        private static readonly Regex SetupTitleRegex =
            new Regex(@"^/KeePass 2\.x/(?<ver>\d+(?:\.\d+){1,3})/KeePass-\k<ver>-Setup\.exe$", RegexOptions.Compiled);

        public static Version GetRunningVersion()
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
            return new Version(fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart);
        }

        /// <summary>Fetches the feed and returns the newest published Setup.exe release, or null if none was found.</summary>
        public static async Task<UpdateInfo> GetLatestAsync()
        {
            string xml;
            using (var handler = new HttpClientHandler { AllowAutoRedirect = true })
            using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("KeeAutoUpdate-Plugin/1.0");
                xml = await http.GetStringAsync(FeedUrl).ConfigureAwait(false);
            }

            XDocument doc = XDocument.Parse(xml);
            XNamespace rss = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            UpdateInfo best = null;
            foreach (XElement item in doc.Descendants(rss + "item"))
            {
                string title = ((string)item.Element(rss + "title"))?.Trim();
                string link = ((string)item.Element(rss + "link"))?.Trim();
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(link)) continue;

                Match m = SetupTitleRegex.Match(title);
                if (!m.Success) continue;

                if (!Version.TryParse(m.Groups["ver"].Value, out Version v)) continue;

                if (best == null || v > best.Version)
                    best = new UpdateInfo(v, link);
            }

            return best;
        }
    }
}
