# AcerFALDv2 installer

An Inno Setup 6 wizard that installs the portable build in `AcerFALDv2\` to
`C:\AcerFALDv2` by default and lets the user choose what to create.

Output: `dist\AcerFALDv2-Setup-<version>.exe` (single file, payload embedded).

## What the wizard offers

| Step | What it does |
| --- | --- |
| Destination folder | Defaults to `C:\AcerFALDv2`. A drive root (`C:\`) is refused, as is the Windows folder. |
| Additional shortcuts | `AcerFALDv2` on the desktop and in the Start Menu, each optional. |
| Startup | `AcerFALDv2` in the current user's Startup folder, optional. |
| Finish | Checkbox to start AcerFALDv2 right away. |

All three shortcuts are optional and all three are ticked by default, so the plain
"Next, Next, Install" path gives desktop, Start Menu and start-with-Windows. Unticking one
skips it, and unticking one during an upgrade removes the shortcut the previous install made.

The installer always writes:

- `C:\AcerFALDv2\AcerFALDv2.exe`
- `C:\AcerFALDv2\README.txt`
- `C:\AcerFALDv2\faldsync.json` — only when it does not exist yet
- an entry in Settings → Apps, plus the `App Paths` key
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\AcerFALDv2.exe` so `Win+R` → `AcerFALDv2`
  works

## Why `C:\AcerFALDv2` and not `C:\`

The app writes `faldsync.json` and `faldsync.log` next to its exe. That rules out `Program Files`
without a virtualisation layer, and it rules out the drive root because an uninstaller that owns
`C:\` could delete files it did not create. A folder of its own under `C:\` keeps writes
unprivileged and keeps uninstall clean.

The installer therefore grants `Users` modify rights on `{app}` (`Permissions: users-modify`).
That is what allows a standard user to save settings without an elevation prompt, and it is the
one trade-off to know about: the installed exe is writable by every local user. On a shared machine
prefer the portable zip in a per-user folder.

## Upgrades

- `AcerFALDv2.exe` and `README.txt` are replaced.
- `faldsync.json` is only written when missing, so brightness, contrast, input, theme and timing
  settings survive a reinstall.
- The app is closed first: it holds `Local\AcerFALDv2.SingleInstance`, which Setup and the
  uninstaller both check.
- Shortcut choices are remembered (`UsePreviousTasks`), and a task unticked during an upgrade
  deletes the shortcut the previous version created.

## Uninstall

Settings → Apps → Installed apps → **AcerFALDv2** → Uninstall, or
`C:\AcerFALDv2\unins000.exe`.

The exe, `README.txt`, `faldsync.log`, shortcuts, the Start Menu folder, the `App Paths` key and the
Apps entry are removed. `faldsync.json` is kept by default; the uninstaller asks
"Also delete your saved settings?" and the default answer is No, so a later reinstall starts where
you left off. A silent uninstall keeps the settings without asking.

## Silent install

```powershell
# everything, default folder
.\AcerFALDv2-Setup-2.2.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

# pick folder and entries; /TASKS="" installs with no shortcuts at all
.\AcerFALDv2-Setup-2.2.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART `
  /DIR="D:\Tools\AcerFALDv2" /TASKS="desktopicon,startmenuicon,startupicon"
```

Task names: `desktopicon`, `startmenuicon`, `startupicon`.

Useful switches: `/LOG="C:\path\setup.log"`, `/CURRENTUSER` is not supported (the install is
machine-wide), `/NORESTART` keeps Setup from rebooting if a file was in use.

## Build

Requirements: Windows 10/11 x64, [Inno Setup 6](https://jrsoftware.org/isdl.php) (`ISCC.exe`), and
the payload in `AcerFALDv2\`.

```powershell
winget install --id JRSoftware.InnoSetup --exact
cd installer
.\build-installer.ps1
```

The script:

1. finds `ISCC.exe` (LocalAppData, `Program Files (x86)`, `Program Files`, or `-IsccPath`),
2. reads the version from `AcerFALDv2\AcerFALDv2.exe` (override with `-Version x.y.z`),
3. compiles `AcerFALDv2.iss` into `dist\`, and
4. prints the payload and installer SHA-256 hashes for the release notes.

Override the payload with `/DMyPayloadDir="..."` if you compile the `.iss` by hand.

## Design notes

- `AppId={{E109B4D6-6B77-459B-9BAA-5DE19BEC50C9}` is fixed forever; it is how upgrades and
  uninstalls find the existing install.
- `PrivilegesRequired=admin` is needed for `C:\` and `HKLM`; there is no non-admin override.
- `MinVersion=10.0.17763` matches the .NET 8 floor (`ArchitecturesAllowed=x64compatible`).
- The exe is unsigned, so the setup is unsigned too; SmartScreen may warn on first run.
- `[Code]` keeps three guards: the destination may not be a drive root or `{win}` (also enforced on
  silent installs, where the wizard pages never run), and the uninstall prompt is suppressed when
  the uninstaller is silent — a message box there leaves the uninstaller's second phase waiting
  forever.

## Verified

Tested with Inno Setup 6.7.3 on Windows 11: silent install with every task combination, hash match
against the payload, shortcut targets and working directory, settings preserved across a reinstall,
shortcut removal when a task is unticked, drive-root rejection, and both silent and interactive
uninstall (including the keep-your-settings prompt).
