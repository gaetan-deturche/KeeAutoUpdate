using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KeePass.Plugins;

namespace KeeAutoUpdate
{
    public sealed class KeeAutoUpdateExt : Plugin
    {
        private const string CfgAutoCheck = "KeeAutoUpdate.AutoCheckEnabled";
        private const string CfgAutoInstall = "KeeAutoUpdate.AutoInstallEnabled";
        private const string CfgExpectedSigner = "KeeAutoUpdate.ExpectedSigner";
        private const string CfgCheckIntervalHours = "KeeAutoUpdate.CheckIntervalHours";
        private const string CfgLastCheckTicks = "KeeAutoUpdate.LastCheckUtcTicks";

        private IPluginHost _host;
        private ToolStripMenuItem _menu;
        private System.Threading.Timer _timer;
        private int _checkInProgress;

        public override bool Initialize(IPluginHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            BuildMenu();

            // First automatic check is delayed so it never competes with KeePass's own startup work.
            _timer = new System.Threading.Timer(OnTimerTick, null, TimeSpan.FromSeconds(20), TimeSpan.FromHours(1));

            return true;
        }

        public override void Terminate()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
        {
            return t == PluginMenuType.Main ? _menu : null;
        }

        private void BuildMenu()
        {
            _menu = new ToolStripMenuItem("KeeAutoUpdate");

            var itemCheckNow = new ToolStripMenuItem("Check for KeePass update now…");
            itemCheckNow.Click += (s, e) => _ = RunCheckAsync(interactive: true);
            _menu.DropDownItems.Add(itemCheckNow);

            _menu.DropDownItems.Add(new ToolStripSeparator());

            var itemAutoCheck = new ToolStripMenuItem("Automatically check for updates") { CheckOnClick = true };
            itemAutoCheck.Checked = _host.CustomConfig.GetBool(CfgAutoCheck, true);
            itemAutoCheck.CheckedChanged += (s, e) => _host.CustomConfig.SetBool(CfgAutoCheck, itemAutoCheck.Checked);
            _menu.DropDownItems.Add(itemAutoCheck);

            var itemAutoInstall = new ToolStripMenuItem("Install updates automatically (no prompt)") { CheckOnClick = true };
            itemAutoInstall.Checked = _host.CustomConfig.GetBool(CfgAutoInstall, false);
            itemAutoInstall.CheckedChanged += (s, e) => _host.CustomConfig.SetBool(CfgAutoInstall, itemAutoInstall.Checked);
            _menu.DropDownItems.Add(itemAutoInstall);
        }

        private void OnTimerTick(object state)
        {
            if (!_host.CustomConfig.GetBool(CfgAutoCheck, true)) return;

            int intervalHours = (int)_host.CustomConfig.GetULong(CfgCheckIntervalHours, 24);
            long lastTicks = _host.CustomConfig.GetLong(CfgLastCheckTicks, 0);
            DateTime last = lastTicks > 0 ? new DateTime(lastTicks, DateTimeKind.Utc) : DateTime.MinValue;
            if (DateTime.UtcNow - last < TimeSpan.FromHours(intervalHours)) return;

            _ = RunCheckAsync(interactive: false);
        }

        private async Task RunCheckAsync(bool interactive)
        {
            if (Interlocked.Exchange(ref _checkInProgress, 1) == 1)
            {
                if (interactive) ShowStatus("A KeePass update check is already running.");
                return;
            }

            try
            {
                ShowStatus("Checking for KeePass updates…");

                UpdateInfo latest;
                try
                {
                    latest = await UpdateChecker.GetLatestAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ShowStatus("KeePass update check failed.");
                    if (interactive) ShowError("Could not check for updates:\n\n" + ex.Message);
                    return;
                }

                _host.CustomConfig.SetLong(CfgLastCheckTicks, DateTime.UtcNow.Ticks);

                if (latest == null)
                {
                    ShowStatus("No KeePass release information found.");
                    if (interactive) ShowInfo("No release information could be found on the KeePass SourceForge feed.");
                    return;
                }

                Version running = UpdateChecker.GetRunningVersion();
                if (latest.Version <= running)
                {
                    ShowStatus("KeePass is up to date (" + running + ").");
                    if (interactive) ShowInfo($"KeePass is up to date (version {running}).");
                    return;
                }

                bool autoInstall = _host.CustomConfig.GetBool(CfgAutoInstall, false);
                bool proceed = autoInstall || AskYesNo(
                    $"A new KeePass version is available: {latest.Version} (you have {running}).\n\n" +
                    "Download and install it now? The installer is verified before it runs, and KeePass " +
                    "will close and restart automatically once the update completes.");

                if (!proceed)
                {
                    ShowStatus($"KeePass update {latest.Version} available (not installed).");
                    return;
                }

                ShowStatus($"Downloading KeePass {latest.Version}…");
                string expectedSigner = _host.CustomConfig.GetString(CfgExpectedSigner, UpdateInstaller.DefaultExpectedSigner);
                UpdateInstaller.DownloadResult dl = await UpdateInstaller.DownloadAndVerifyAsync(latest, expectedSigner).ConfigureAwait(false);

                if (!dl.Success)
                {
                    ShowStatus("KeePass update failed verification and was not installed.");
                    ShowError("The downloaded installer was NOT run because it failed verification:\n\n" + dl.Error +
                               "\n\nYou can download the update manually from https://keepass.info/download.html");
                    return;
                }

                ShowStatus($"Installing KeePass {latest.Version}…");
                RunOnUiThread(() =>
                {
                    try
                    {
                        if (!System.IO.File.Exists(dl.LocalPath))
                        {
                            ShowStatus("KeePass update failed: the downloaded installer is gone.");
                            ShowError("The verified installer file disappeared before it could be run " +
                                      "(a security tool may have removed it):\n\n" + dl.LocalPath);
                            return;
                        }

                        string appExePath = Process.GetCurrentProcess().MainModule.FileName;
                        UpdateInstaller.LaunchInstallerAndRestart(dl.LocalPath, appExePath);

                        // The installer can't replace KeePass.exe while this process still has it
                        // open, and KeePass's installer has no AppMutex for /CLOSEAPPLICATIONS to
                        // find and close it automatically -- so we close it ourselves. A detached
                        // watcher (started above) relaunches KeePass once the installer finishes.
                        ForceExitKeePass();
                    }
                    catch (Exception ex)
                    {
                        ShowStatus("KeePass update failed to launch.");
                        ShowError("Could not start the installer:\n\n" + ex);
                    }
                });
            }
            finally
            {
                Interlocked.Exchange(ref _checkInProgress, 0);
            }
        }

        /// <summary>
        /// KeePass's public Close() on the main window just minimizes to tray if that option is
        /// enabled -- only the private OnFileExit handler behind "File > Exit" actually exits
        /// (it sets a private m_bForceExitOnce flag before calling Close()). There's no public
        /// API for this, so we invoke that same handler via reflection instead of duplicating
        /// its exact preconditions/flag-setting ourselves.
        /// </summary>
        private void ForceExitKeePass()
        {
            Form mainWindow = _host.MainWindow;
            MethodInfo mi = mainWindow.GetType().GetMethod("OnFileExit", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null)
                mi.Invoke(mainWindow, new object[] { this, EventArgs.Empty });
            else
                mainWindow.Close();
        }

        private void ShowStatus(string text) =>
            RunOnUiThread(() => _host.MainWindow.SetStatusEx(text));

        private void ShowInfo(string text) =>
            RunOnUiThread(() => MessageBox.Show(_host.MainWindow, text, "KeeAutoUpdate", MessageBoxButtons.OK, MessageBoxIcon.Information));

        private void ShowError(string text) =>
            RunOnUiThread(() => MessageBox.Show(_host.MainWindow, text, "KeeAutoUpdate", MessageBoxButtons.OK, MessageBoxIcon.Warning));

        private bool AskYesNo(string text)
        {
            DialogResult r = DialogResult.No;
            RunOnUiThreadBlocking(() =>
            {
                r = MessageBox.Show(_host.MainWindow, text, "KeeAutoUpdate", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            });
            return r == DialogResult.Yes;
        }

        private void RunOnUiThread(Action action)
        {
            Form f = _host.MainWindow;
            if (f == null || f.IsDisposed) return;
            if (f.InvokeRequired) f.BeginInvoke(action);
            else action();
        }

        private void RunOnUiThreadBlocking(Action action)
        {
            Form f = _host.MainWindow;
            if (f == null || f.IsDisposed) return;
            if (f.InvokeRequired) f.Invoke(action);
            else action();
        }
    }
}
