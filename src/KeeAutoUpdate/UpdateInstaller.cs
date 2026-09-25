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
        /// Launches the verified Inno Setup installer silently and arranges for
        /// <paramref name="appExePath"/> to be relaunched once it finishes.
        ///
        /// The KeePass installer's Inno Setup script does not declare an AppMutex, so
        /// /CLOSEAPPLICATIONS has nothing to detect and silently does nothing -- KeePass.exe
        /// stays locked, the file just doesn't get replaced, and Setup still reports success.
        /// So instead of relying on that, the caller must close the running KeePass process
        /// itself (releasing the file lock) right after this returns; a small detached
        /// watcher process (spawned here, independent of this process) waits for the
        /// installer to exit and then starts KeePass back up.
        /// </summary>
        public static void LaunchInstallerAndRestart(string installerPath, string appExePath)
        {
            var installerPsi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true
            };

            using (Process installer = Process.Start(installerPsi))
            {
                if (installer == null)
                    throw new InvalidOperationException("The installer process could not be started.");

                StartRestartWatcher(installer.Id, appExePath);
            }
        }

        private static void StartRestartWatcher(int installerPid, string appExePath)
        {
            string script =
                $"Wait-Process -Id {installerPid} -ErrorAction SilentlyContinue; " +
                "Start-Sleep -Seconds 1; " +
                $"Start-Process -FilePath '{appExePath.Replace("'", "''")}'";

            var watcherPsi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command " +
                            "\"" + script.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(watcherPsi);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort cleanup */ }
        }
    }
}
