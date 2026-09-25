using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace KeeAutoUpdate
{
    public static class UpdateInstaller
    {
        public const string DefaultExpectedSigner = "Dominik Reichl";

        public sealed class DownloadResult
        {
            public bool Success { get; set; }
            public string LocalPath { get; set; }
            public string SignerSubject { get; set; }
            public string Error { get; set; }
        }

        /// <summary>
        /// Downloads the installer to a temp file and refuses to hand back a path unless its
        /// Authenticode signature validates and is signed by <paramref name="expectedSigner"/>.
        /// The caller must not execute anything this method did not return as Success.
        /// </summary>
        public static async Task<DownloadResult> DownloadAndVerifyAsync(UpdateInfo update, string expectedSigner)
        {
            string tempPath = Path.Combine(Path.GetTempPath(),
                $"KeePass-{update.Version}-Setup-{Guid.NewGuid():N}.exe");

            try
            {
                using (var handler = new HttpClientHandler { AllowAutoRedirect = true })
                using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) })
                using (HttpResponseMessage resp = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream dst = File.Create(tempPath))
                    {
                        await src.CopyToAsync(dst).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                TryDelete(tempPath);
                return new DownloadResult { Success = false, Error = "Download failed: " + ex.Message };
            }

            AuthenticodeVerifier.Result sig = AuthenticodeVerifier.Verify(tempPath, expectedSigner);
            if (!sig.IsTrusted)
            {
                TryDelete(tempPath);
                return new DownloadResult { Success = false, Error = "Signature check failed: " + sig.FailureReason };
            }

            return new DownloadResult
            {
                Success = true,
                LocalPath = tempPath,
                SignerSubject = sig.SubjectName
            };
        }

        /// <summary>
        /// Launches the verified Inno Setup installer silently. The installer's own
        /// /CLOSEAPPLICATIONS + /RESTARTAPPLICATIONS handling closes the running KeePass
        /// process (via Windows Restart Manager) and relaunches it once the update completes.
        /// </summary>
        public static void LaunchInstallerAndExit(string installerPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort cleanup */ }
        }
    }
}
