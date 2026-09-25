# KeeAutoUpdate

A KeePass 2.x plugin that checks for new KeePass releases and can download and
install them automatically.

## How it works

1. **Version check** — polls the official KeePass SourceForge project's public
   RSS file feed (`https://sourceforge.net/projects/keepass/rss?path=/KeePass%202.x`)
   and picks the highest version that has a published `KeePass-<version>-Setup.exe`.

   KeePass's own built-in update checker (`keepass.info/update/version2x.txt`) uses
   a proprietary, undocumented encoding — not something a third-party plugin should
   reverse-engineer and depend on. The SourceForge RSS feed is a stable, documented,
   publicly-supported format instead.

2. **Download + verification** — the installer is downloaded to a temp file and its
   Authenticode signature is verified two ways before anything is executed:
   - `WinVerifyTrust` (the real Windows trust decision — checks the signature is
     cryptographically valid and chains to a trusted root, with full revocation
     checking).
   - The signer's certificate subject must contain the expected publisher name
     (`Dominik Reichl` by default, configurable via plugin settings).

   If either check fails, the file is deleted and **nothing is executed** — the user
   is shown the failure reason and pointed to the official download page instead.

3. **Install** — the verified installer is launched with Inno Setup's silent flags
   (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`).
   `/CLOSEAPPLICATIONS` + `/RESTARTAPPLICATIONS` let the installer close the running
   KeePass process itself (via Windows Restart Manager) and relaunch it once the
   update is done — no separate watcher process needed.

## Settings (Tools ▸ KeeAutoUpdate)

- **Check for KeePass update now…** — runs a check immediately and reports the
  result (up to date / update available / error), with a prompt to install.
- **Automatically check for updates** — background check once per hour (only
  actually re-checks against the feed once every 24h; on by default).
- **Install updates automatically (no prompt)** — skips the "install now?" prompt
  and installs as soon as a newer, verified release is found (off by default).

Settings are persisted in KeePass's own config file via `IPluginHost.CustomConfig`.

## Build

Requires the .NET Framework 4.7.2 targeting pack (ships with Visual Studio, or via
the .NET SDK's bundled reference assemblies) and a local KeePass 2.x install to
build against.

```powershell
.\build.ps1
# or, if KeePass isn't at the default path:
.\build.ps1 -KeePassDir "D:\Apps\KeePass"
```

Output: `src\KeeAutoUpdate\bin\Release\net472\KeeAutoUpdate.dll`

## Deploy / test locally

KeePass must be closed (it locks its own Plugins folder's DLLs while running), and
writing into `Program Files` needs an elevated prompt:

```powershell
.\deploy.ps1
```

Then start KeePass and check **Tools ▸ KeeAutoUpdate**.

## Notes / limitations

- Windows only (Authenticode/`WinVerifyTrust` is a Windows API).
- Targets the classic `.exe` installer flow (Inno Setup). Portable/zip installs and
  the MSI variant aren't handled by the silent-install step.
- The plugin is a plain DLL (not a `.plgx`), so it must be rebuilt against the exact
  KeePass version you run if KeePass's plugin-loading assembly version checks are
  strict; in practice KeePass DLL plugins tolerate the host's minor version updates
  fine, and this plugin's whole purpose is to keep itself current alongside KeePass.
