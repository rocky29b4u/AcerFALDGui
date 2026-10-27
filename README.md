# Acer Display Widget → FaldSync

A clean-room Windows utility that keeps an Acer Mini-LED monitor's **local dimming** engaged
while HDR is on, keeps **brightness/contrast controllable in HDR**, lowers the **Windows SDR
white level**, and optionally corrects the **SDR-in-HDR tone curve to gamma 2.2** so ordinary
desktop content is comfortable without crushing HDR highlights — the things the Acer Display
Widget does not do. It also controls the monitor's **speaker volume** and **input source** over
DDC/CI, and ships a dark-first UI with a working light theme.

Target hardware: Acer Nitro **XV275K P5** (4K 160 Hz, Mini-LED FALD, DisplayHDR 1000).
The approach is generic DDC/CI + the Windows display configuration API, so other MCCS monitors
can be supported later.

**Repository:** [AcerFALDGui](https://github.com/rocky29b4u/AcerFALDGui) · **Release:** v1.2.0 ·
**Portable build:** AcerFALDv2 2.2.0 · **Tray UI:** 1.3.0

---

## Download

Two ways to run it, both self-contained — no .NET installation is required:

**Installer (recommended)** — `AcerFALDv2-Setup-2.2.0.exe`

1. Run the setup. Windows asks for administrator rights, because it writes to `C:\AcerFALDv2`.
2. Choose the folder, then tick the shortcuts you want: **desktop**, **Start Menu** and
   **start with Windows**. All three are offered in the wizard and all three start ticked.
3. Uninstall from **Settings → Apps → Installed apps → AcerFALDv2**. Your brightness, input and
   theme settings are kept unless you answer Yes to the question at uninstall time.

The installer defaults to `C:\AcerFALDv2` — a folder of its own, not the drive root — so
uninstalling can never take anything else with it. It gives `Users` write access to that folder
only, because the app saves `faldsync.json` and `faldsync.log` next to its exe and must be able to
do that without elevation. Silent install for scripts:

```powershell
.\AcerFALDv2-Setup-2.2.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART `
  /DIR="C:\AcerFALDv2" /TASKS="desktopicon,startmenuicon,startupicon"
```

**Portable zip** — `AcerFALDv2-portable.zip`

1. Extract the zip to a folder you can write to.
2. Run `AcerFALDv2.exe`.
3. Optional: put a shortcut to it in `shell:startup` to start it at login.

Both are attached to the
[latest release](https://github.com/rocky29b4u/AcerFALDGui/releases/latest).
Settings and the log stay next to the exe (`faldsync.json`, `faldsync.log`) in both cases. The exe
is unsigned, so Windows SmartScreen may say "Unknown publisher"; choose **More info → Run anyway**
if you trust the build. To build from source instead, see [Run it](#run-it).

---

## The short version

Local dimming on this panel requires **an HDR signal AND the monitor's HDR display mode**.
Windows HDR alone gives you the washed-out "bad screen". The Acer app's HDR button fixes the
picture but not the automation, and brightness/contrast become unreliable afterwards. FaldSync
now uses the monitor's User preset for SDR and for the forced transition, not Standard.

FaldSync watches Windows HDR and does the sequence for you:

```
HDR on  → VCP 0xE2 = 0 (User) → VCP 0xE2 = 11 (HDR) → settle → re-apply brightness/contrast
          + SDR white + optional gamma 2.2 ramp
HDR off → VCP 0xE2 = 0 (User) → restore SDR brightness/contrast + the original display gamma ramp
```

`0xE2` is the vendor display-mode setter; `0x14` reflects HDR/Standard (`6`/`5`) but does not
report the User preset, and it can read `5` while the OSD says HDR. Writing `0x14` alone does
not arm FALD.

The extra `0xE2=0 → 0xE2=11` step is deliberate. The XV275K P5 has a known firmware bug:
entering HDR for the first time can leave FALD off until the picture mode is changed away
from HDR and back. FaldSync forces that transition while the HDR signal is already live.
The User preset is used instead of Standard so your own SDR settings are kept; the brightness
write afterwards may make the OSD label read User while FALD stays on.

Sleep/resume is handled too. FaldSync listens for display on/off and system suspend/resume,
re-acquires the DDC handle (the old one goes stale after display sleep), then re-applies the
HDR mode, brightness, and the Windows SDR white level. Some panels keep answering reads after
wake but ignore writes for a while, so the re-apply is retried until it verifies.

While HDR is on, the **SDR WHITE (IN HDR)** slider controls Windows' SDR content brightness in
nits (80–480, step 4). It changes the white point of ordinary SDR windows — browsers, Explorer,
Notepad — not HDR highlights. The default is **120 nits**; the live UI config on this machine is
**180 nits**, and the previous Windows value was 220 nits. When HDR is off the
slider only stores the target; it is re-applied on the next HDR-on transition and after wake.
The main brightness slider still controls the panel and the HDR peak.

The **GAMMA 2.2** button in the same card switches the GPU scanout ramp from Windows' piecewise
sRGB SDR curve to gamma 2.2. Windows maps SDR content inside an HDR container with the sRGB
curve, which lifts shadows and makes a gamma-2.2-graded desktop look washed out. The correction
is a 256-entry `SetDeviceGammaRamp` LUT (AMD curve) and is applied only while HDR is on. It is
removed when the toggle is turned off, when HDR turns off, or when the UI exits; the UI also
checks every few seconds and re-applies it if Windows resets the ramp after sleep, unlock, or
Display Settings. The original ramp is captured once and restored.

The correction is intentionally a toggle: it also affects native HDR games, where deeper
shadows may be undesirable. Turn it off for a native-HDR title if you prefer the untouched HDR
curve. Screenshots taken with BitBlt/`CopyFromScreen` do **not** include the scanout ramp, so a
screenshot cannot prove whether the correction is active; use the UI button and your eyes.

True SDR-signal local dimming is **not possible** on this firmware. The honest route is the
HDR container with SDR content mapped into it. See `replica/hdr-fald-findings.md`.

FALD **zone-response speed** is fixed by P5 firmware. Unlike the P3, the P5 has no Adaptive
Dimming (`Low`/`Average`/`Fast`) setting and no known DDC control for it. A dark-gray desktop,
some bias light, and slightly lower contrast or SDR white make the zone transitions less
obvious; they do not make the backlight respond faster.

---

## What is here

```
replica/
  recon.md                 screen inventory, flows, components, data model, DDC map
  features.csv             feature matrix (original vs clone, priorities)
  hdr-fald-findings.md     the experiments, the gate, the recipe, the limits

prototype/FaldSync/        .NET 8 console engine (state, sync, watch, set, white, gamma, hdr,
                           volume, input, probe)
prototype/FaldSync.UI/     .NET 8 tray UI (theme toggle, brightness/contrast/SDR-white/volume
                           sliders, gamma toggle, HDR toggle, input buttons, wake recovery)
prototype/AcerFALDv2/      source for the portable self-contained UI build
AcerFALDv2/                runnable portable build: AcerFALDv2.exe + faldsync.json + README
AcerFALDv2-portable.zip    zipped copy of the portable build
installer/                 Inno Setup script and build script for the Windows installer
prototype/Start FaldSync.cmd       console watcher start
prototype/Stop FaldSync.cmd        console watcher stop
prototype/Start FaldSync UI.cmd    tray UI start
prototype/Stop FaldSync UI.cmd     tray UI stop
```

---

## Documentation

| Document | What it covers |
| --- | --- |
| [CHANGELOG.md](CHANGELOG.md) | release history; source of truth for the repository version |
| [docs/releases/v1.1.0.md](docs/releases/v1.1.0.md) | v1.1.0 release notes: installer, checksums, upgrade notes |
| [docs/releases/v1.0.0.md](docs/releases/v1.0.0.md) | v1.0.0 release notes, install steps and checksums |
| [installer/README.md](installer/README.md) | installer behaviour, wizard options, silent switches, how to build |
| [prototype/FaldSync/README.md](prototype/FaldSync/README.md) | engine CLI, config keys, safety notes, known limits |
| [prototype/AcerFALDv2/README.md](prototype/AcerFALDv2/README.md) | portable build, publish steps and output layout |
| [AcerFALDv2/README.txt](AcerFALDv2/README.txt) | the quick-start shipped inside the portable zip and installer |
| [replica/hdr-fald-findings.md](replica/hdr-fald-findings.md) | hardware experiments, FALD gate, gamma formula, P5 limits |
| [replica/recon.md](replica/recon.md) | screen inventory, flows, components and DDC map |

Requirements: Windows 10/11 x64, a monitor with DDC/CI (MCCS) enabled, and — for source builds —
the .NET 8 SDK. The portable release needs no runtime.

---

## Run it

Build the engine and the UI:

```powershell
cd prototype\FaldSync
dotnet build -c Release

cd ..\FaldSync.UI
dotnet build -c Release
```

Start the tray UI:

```powershell
cd ..
.\"Start FaldSync UI.cmd"
```

Closing the window hides it to the notification area; **Exit** in the tray menu really exits.
The tray menu also has Open, Re-apply HDR / FALD, Restore SDR, Reconnect to monitor, and the
theme switch. Brightness, contrast, SDR white, and volume changes apply automatically and are
saved to `faldsync.json` next to the UI executable. SDR white is the Windows SDR-content white
level while HDR is on; it is applied after HDR entry and after wake. The **GAMMA 2.2** button is
saved as `gammaFix` and applies only while HDR is on.

The slider values are also read back from the panel every 2.5 s, so brightness, contrast, volume,
and the active input changed with the monitor's own joystick/OSD appear in the window and are
saved with the rest of the settings.

The window is dark by default. The **LIGHT / DARK** button in the header switches themes in
place, repaints every control, and stores the choice as `theme` in the config.

**MONITOR AUDIO** has the speaker volume slider (VCP `0x62`) and four input buttons — DP, HDMI 1,
HDMI 2, DP ALT — that write VCP `0x60`: `0x0F`, `0x11`, `0x12`, `0x10`, matching the panel's
capability string and a live readback on DisplayPort. Changing input asks for confirmation
first, because selecting an input with no signal blanks the screen; the monitor joystick is the
way back. The write is not read back on purpose: DDC/CI usually stops answering the moment the
panel leaves this input.

Windows recreates the DDC handle whenever the display is re-enumerated — which is exactly what an
input change does — and the old handle then fails every call until it is replaced. The app pings
the panel on every read, re-acquires the handle when it stops answering, and retries an input
write once after reconnecting, so a switch back no longer needs an app restart. The four input
buttons stay clickable while the panel is away; if the display is genuinely on another source the
window says so, and once the joystick brings it back the reads resume and the sliders re-sync. The
tray menu's **Reconnect to monitor** forces the same recovery by hand.

**Auto switch to input** (`autoSwitchInput`) is app-managed. When it is on, the app re-selects
`selectedInput` after startup and after a display wake, and only when the panel reports a
different input — a failed read is left alone instead of blind-writing. The panel's own
**Auto Source** option is OSD-only: Acer documents no DDC/CI code for it, and no vendor code is
guessed here.

The console engine is still available for diagnostics:

```powershell
cd prototype\FaldSync
.\bin\Release\net8.0-windows\FaldSync.exe state
```

For continuous console-only operation, use `..\"Start FaldSync.cmd"` instead of the UI. The
engine also has `gamma on|off|status` for the same correction, plus:

```powershell
FaldSync.exe volume [0-100]              # read or set the monitor speaker volume
FaldSync.exe input dp|hdmi1|hdmi2|dpalt  # read or switch the monitor input
```

### Portable build (no .NET required)

`AcerFALDv2\AcerFALDv2.exe` is a self-contained single-file build of the tray UI (version 2.2).
It carries its own .NET/WinForms runtime, so the target PC does not need .NET installed. It has
the same dark-first UI, theme toggle, volume, input, and auto-switch features as the main UI,
plus its own single-instance mutex and tray identity. Settings and the log live next to the exe,
in that folder's `faldsync.json` and `faldsync.log`.

Rebuild it with:

```powershell
dotnet publish prototype\AcerFALDv2\AcerFALDv2.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o prototype\AcerFALDv2\publish
```

A plain `dotnet build -c Release` in `prototype\AcerFALDv2` also works; the RID and
self-contained flags belong to the publish command above, not to the project file.

### Installer

`AcerFALDv2-Setup-<version>.exe` wraps the same portable build in an Inno Setup wizard: install
folder page, optional desktop / Start Menu / startup entries, an entry in Settings → Apps and an
uninstaller. Requires Windows 10 1809 or later and administrator rights, because it writes to `C:\`
and to `HKLM`.

```powershell
winget install --id JRSoftware.InnoSetup --exact   # once, for ISCC.exe
cd installer
.\build-installer.ps1
```

The build script takes the version from the payload exe, writes
`dist\AcerFALDv2-Setup-<version>.exe` and prints the payload and installer SHA-256 hashes. Refresh
`AcerFALDv2\` first if the exe changed. Wizard options, silent switches and uninstall behaviour are
documented in [installer/README.md](installer/README.md).

Do not run AcerFALDv2 and the original FaldSync UI at the same time; both drive the same DDC
channel. AcerFALDv2 checks for the original process and exits with a warning.

`state` should show:

```
windows HDR: ON
SDR white  : 180 nits (Windows SDR content brightness)
gamma fix  : enabled, ramp=gamma 2.2 applied
  0x10 brightness    : 50 (max 100)
  0x12 contrast      : 55 (max 100)
  0x14 picture mode  : 6 (max 11)
  0x60 input source  : DisplayPort (0x0F)
  0x62 volume        : 100 (max 100)
FALD gate  : HDR signal + HDR mode (0x14=6, set via 0xE2=11)
```

---

## Coexistence warning

Do **not** run Acer Display Widget or TwinkleTray at the same time. Both reassert their cached
brightness on display-change events and will undo FaldSync's writes (observed: brightness
snapped back to 40 after an HDR toggle). FaldSync is intended to replace them.

Do **not** run the console `watch` command and the tray UI together. They drive the same DDC
channel; the UI already polls HDR state and re-applies after display sleep or system resume.

---

## Status

Working and verified on the target machine:

- HDR-on detection via `Win+Alt+B`, forced User → HDR transition, brightness/contrast applied
- HDR-off restore to User (`0xE2=0`) and SDR brightness
- brightness/contrast writes in HDR without dropping the FALD latch (verified at 65/50, 45/50,
  and the current 50/55); the OSD label can flip to User while FALD stays on
- HDR confirmation accepts `0x14=6` or the preset's brightness jump to 100, because `0x14`
  can lag or alias
- display sleep/wake and system resume: power notifications, fresh DDC handle, retry until applied
- DDC retries, late confirmation, and `restore` safety command
- desktop tray UI with debounced brightness/contrast/SDR-white sliders, HDR toggle, Re-apply,
  Restore SDR, close/minimize-to-tray, single-instance guard, and persisted slider values
- Windows SDR content brightness: 120 nits default (80–480, step 4), applied on HDR-on and wake;
  SDR whites dropped from the previous 220 nits while HDR highlights and FALD stay intact
- SDR-in-HDR gamma 2.2 correction: 256-entry AMD `SetDeviceGammaRamp` LUT, HDR-only toggle,
  restored on HDR-off/exit, re-applied after a simulated reset; ramp readback matched
  `mid=33153 / quarter=15867`, and FALD stayed active while it was on
- UI wake recovery and background HDR polling using the same engine
- dark-first UI with an in-place light theme, persisted as `theme`; both palettes render
  correctly at 600 px width and the layout scrolls on shorter desktops
- monitor speaker volume over VCP `0x62` (read/write verified at 60 then restored to 100)
- monitor input source over VCP `0x60` with the documented P5 values
  (`0x0F` DP, `0x11` HDMI 1, `0x12` HDMI 2, `0x10` DP ALT); the write is not verified because
  DDC/CI drops when the panel changes input
- app-managed auto-switch: re-selects `selectedInput` after startup and wake when the panel
  reports a different input, never blind-writing on a failed read
- OSD/DDC sync: slider values are read back from the panel every 2.5 s, and a stale DDC handle
  after an input change or re-enumeration is re-acquired automatically (verified by disposing the
  live handle and recovering reads without a restart)
- portable AcerFALDv2 2.2 rebuilt from the same UI with the same features, verified with an
  empty `DOTNET_ROOT` and `DOTNET_MULTILEVEL_LOOKUP=0` (`--self-test` exits 0)

Not built yet:

- global hotkeys and profiles
- per-app HDR rules
- NVIDIA-specific gamma method (the current LUT uses the AMD curve)
- live input switching test: it would drop this PC's DP signal, so it is left to the first
  deliberate use with the monitor joystick as the fallback

Next step: global hotkeys/profiles.

---

## Versioning

The repository follows [Semantic Versioning](https://semver.org/) through
[CHANGELOG.md](CHANGELOG.md); **1.0.0** was the first tracked release, **1.1.0** added the Windows
installer and **1.2.0** is the first public release of this repository. Component builds keep their
own numbers — AcerFALDv2 2.2.0 is the portable exe and the installer payload, and FaldSync UI 1.3.0
is the framework-dependent tray app — and both are listed in the release notes.
