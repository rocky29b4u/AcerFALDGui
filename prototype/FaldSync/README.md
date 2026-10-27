# FaldSync (prototype)

Keeps an Acer Mini-LED panel's **local dimming** engaged by keeping the monitor's display
mode in sync with Windows HDR, keeps **brightness/contrast adjustable while HDR is on**, sets
the **Windows SDR-content white level** so ordinary white windows are not blinding, and can
correct the **SDR-in-HDR tone curve to gamma 2.2**.

This is the engine, deliberately shipped as a console app. The tray UI wraps the same code.

---

## Why this exists

On the XV275K P5, local dimming needs **both**:

1. an HDR signal from Windows, and
2. the monitor in its `HDR` display mode (`VCP 0xE2 = 11`).

Windows HDR alone leaves the panel in Standard mode — the washed-out "bad screen".
The Acer Display Widget does not automate this, and it cannot keep brightness/contrast usable
once the panel is in HDR.

`0xE2` is the vendor display-mode setter (User = `0`, Standard = `1`, HDR = `11`). It is
write-only; `0x14` reflects HDR (`6`) and Standard (`5`), but it does **not** report the User
preset. Writing `0x14` alone does not arm FALD — that was the original bug.

FaldSync handles all of it:

- on HDR-on: force `0xE2=0` (User) then `0xE2=11` (HDR) while the signal is live, wait for the
  panel's preset, then re-apply your HDR brightness/contrast, the Windows SDR white level, and
  the optional gamma 2.2 ramp;
- on HDR-off: write `0xE2=0` (User), restore your SDR brightness/contrast, and put the original
  display gamma ramp back;
- writing brightness/contrast **does not** drop the FALD latch once the preset has settled.
  The OSD Picture Mode label may flip to User when those values are written; FALD stays on.

### SDR white in HDR

A full-screen white SDR window in an HDR container was mapped to **220 nits** on this machine.
That is the Windows **SDR content brightness** setting, not the panel's HDR peak. FaldSync now
sets it through `DisplayConfigSetDeviceInfo` (the undocumented
`DISPLAYCONFIG_DEVICE_INFO_SET_SDR_WHITE_LEVEL` type used by HDR brightness tools) to the
configured value, **120 nits** by default, range **80–480**, step **4**.

This lowers ordinary white windows (browser, Explorer, Notepad) without touching HDR highlights
or the FALD latch. The main brightness slider still controls the panel and the HDR peak. The UI
saves the target while HDR is off and re-applies it when HDR turns on; the engine also re-applies
it after display sleep and system resume.

### Gamma 2.2 correction

Windows 11 maps SDR content inside an HDR container with the piecewise sRGB transfer function,
not gamma 2.2. That curve lifts the shadows, so a desktop graded for gamma 2.2 looks washed out.
The engine can load a 256-entry GPU scanout ramp with `SetDeviceGammaRamp` that converts the
SDR sRGB code values to a gamma 2.2 response. This is the AMD curve used by the public
HDR-Gamma-Fix approach:

```
out = srgbInvEotf(pow(in, gamma))       # black level 0, gamma = 2.2
srgbInvEotf(L) = L <= 0.00313066844250063
                 ? L * 12.92
                 : 1.055 * pow(L, 1/2.4) - 0.055
```

The ramp is applied per GDI display, after the HDR preset and SDR white write. It is independent
of DDC, so FALD and the monitor's picture mode are untouched. The first apply captures the
current ramp; `Restore` puts it back only if the current ramp is still our correction, so a
custom SDR calibration that Windows has already reinstated is not overwritten. If the process
crashed while the correction was active, the next apply treats the old correction as the
default and restores the identity ramp when turned off.

`gammaFix` is saved in `faldsync.json`. The UI exposes it as the **GAMMA 2.2** button and
re-checks every few seconds while HDR is on, because Windows can reset the ramp after sleep,
unlock, or opening Display Settings. The engine's `gamma on|off|status` command does the same
for console users.

Caveats:

- the correction affects **all** content in the HDR container, including native HDR games;
  toggle it off for a native-HDR title if the deeper shadows are unwanted;
- the curve above is the **AMD** method. NVIDIA's driver needs a PQ-based curve; this build is
  tuned for the AMD RX 6650 XT;
- BitBlt/`CopyFromScreen` screenshots capture the framebuffer before the scanout ramp, so they
  do not show the correction. Verify with `gamma status`, `state`, or visually;
- the monitor's own gamma setting, ICC profile, and any other ramp-changing tool still apply
  on top; keep those stable while evaluating the correction.

The forced User → HDR step works around a known XV275K P5 firmware bug: the first HDR entry can
leave FALD off until the picture mode is changed away from HDR and back. Using User instead of
Standard keeps your own SDR preset instead of the factory Standard preset.

---

## Build

Requires the .NET 8 SDK (or runtime for the published build).

```powershell
cd prototype\FaldSync
dotnet build -c Release
```

The exe lands in `bin\Release\net8.0-windows\FaldSync.exe`.

---

## Desktop UI

The tray UI lives in `..\FaldSync.UI` and references this project, so the engine stays the
single source of behavior.

```powershell
cd ..\FaldSync.UI
dotnet build -c Release
cd ..
.\"Start FaldSync UI.cmd"
```

The UI adds brightness/contrast/SDR-white/volume sliders, a **GAMMA 2.2** toggle, an HDR on/off
button, Re-apply HDR / FALD, Restore SDR, input-source buttons, an app-managed auto-switch, and
an Open log action. The window is dark by default and the **LIGHT / DARK** header button switches
the palette in place. Closing or minimizing the window hides it to the notification area; use
**Exit** in the tray menu to quit. Slider changes are debounced, written through the same DDC gate
as the watcher (SDR white uses the Windows display-config API instead), and persisted to the
`faldsync.json` next to `FaldSync.UI.exe` (the UI project copies this folder's config at build
time). The gamma toggle and the theme choice are saved there too; turning gamma off, turning HDR
off, or exiting the UI restores the display ramp captured when the correction was first applied.

Monitor volume (`0x62`) and input source (`0x60`) are plain VCP features. Input changes ask for
confirmation first and are not read back, because DDC/CI stops answering as soon as the panel
leaves the PC's input. **Auto switch to input** is app-managed: it re-selects the chosen input
after startup and after a display wake, and only when the panel reports a different one. The
panel's own Auto Source option is OSD-only and is never written over DDC.

Windows destroys and recreates the physical-monitor handle whenever the display is re-enumerated,
which includes an input change. The engine now treats a failed ping as a stale handle: it
re-enumerates, refuses to bind a different model, and retries the operation once. The UI uses the
same check on every 2.5 s read, so it recovers without a restart, keeps the four input buttons
clickable while the panel is away, and reads brightness/contrast/volume/input back from the panel
so OSD changes show up in the window. The tray menu's **Reconnect to monitor** forces the recovery
by hand.

Do not run `FaldSync.exe watch` and the UI at the same time.

---

## Commands

```powershell
FaldSync state                     # HDR state, VCP values, FALD gate
FaldSync sync                      # apply the correct state once
FaldSync watch                     # follow Windows HDR until Ctrl+C
FaldSync set -b 65 -c 50           # brightness / contrast (works in HDR)
FaldSync white                     # read the Windows SDR white level in HDR
FaldSync white 120                 # set it (80-480 nits, step 4)
FaldSync gamma on|off|status        # toggle the SDR-in-HDR gamma 2.2 correction
FaldSync volume [0-100]            # read or set the monitor speaker volume (0x62)
FaldSync input                     # read the input source (0x60)
FaldSync input dp|hdmi1|hdmi2|dpalt # switch it (0x0F, 0x11, 0x12, 0x10)
FaldSync hdr on                    # toggle Windows HDR, then sync the monitor
FaldSync hdr off
FaldSync restore                   # return the panel to SDR mode + SDR brightness
FaldSync probe --code 0xE1         # read any VCP code (add --value to write)
```

`state` is the one to run first:

```
monitor    : XV275K P5  (\\.\DISPLAY1)
windows HDR: ON
SDR white  : 180 nits (Windows SDR content brightness)
gamma fix  : enabled, ramp=gamma 2.2 applied
  0x10 brightness    : 50 (max 100)
  0x12 contrast      : 55 (max 100)
  0x14 picture mode  : 6 (max 11)
  0xE2 display mode : write-only (HDR=11, SDR/User=0)
  0x60 input source  : DisplayPort (0x0F)
  0x62 volume        : 100 (max 100)
FALD gate  : HDR signal + HDR mode (0x14=6, set via 0xE2=11)
```

`0x14` is advisory. It can read `5` even when the OSD says HDR, and it never reports User.

---

## Config

`faldsync.json`, next to the exe:

| key | default | meaning |
| --- | --- | --- |
| `modelMatch` | `XV275K` | substring of the DDC model name |
| `displayModeVcp` | `226` (0xE2) | vendor display-mode setter (write-only) |
| `hdrDisplayMode` | `11` | `0xE2` value for HDR |
| `sdrDisplayMode` | `0` | `0xE2` value for SDR: `0` = User, `1` = Standard |
| `hdrPictureMode` | `6` | expected `0x14` reflection for HDR (advisory; brightness=100 is also accepted) |
| `sdrPictureMode` | `null` | expected `0x14` reflection for SDR; leave null for User, set `5` if you switch to Standard |
| `hdrBrightness` | `65` | re-applied after the HDR preset |
| `hdrContrast` | `50` | |
| `sdrBrightness` | `40` | restored when HDR turns off (also becomes part of the User preset) |
| `sdrContrast` | `50` | |
| `sdrWhiteNits` | `120` | Windows SDR-content white level while HDR is on (80–480 nits, step 4) |
| `gammaFix` | `false` | apply the SDR-in-HDR gamma 2.2 correction while HDR is on |
| `gammaPower` | `2.2` | target gamma; the ramp uses the AMD sRGB→gamma curve |
| `selectedInput` | `15` (0x0F) | preferred input: `15` DP, `17` HDMI 1, `18` HDMI 2, `16` DP ALT |
| `autoSwitchInput` | `false` | re-select `selectedInput` after startup and display wake (app-managed) |
| `theme` | `dark` | UI palette: `dark` or `light` |
| `pollMs` | `1000` | HDR state polling interval |
| `settleMs` | `3000` | wait after `0xE2=11` before re-applying brightness (the preset/FALD latch needs it) |
| `preSettleMs` | `1200` | wait after the HDR signal edge before writing the mode |
| `userSettleMs` | `1200` | wait after `0xE2=0` before the next mode write; `0x14` cannot confirm User |

The checked-in config for this machine uses 180 nits, `gammaFix: true`, HDR 50/55, SDR 20/20,
DisplayPort (`selectedInput: 15`), auto-switch off, and the dark theme.
The table above lists the code defaults, not the live values.

---

## Run at login

Create a shortcut in `shell:startup`:

```
Target : C:\...\FaldSync.exe
Args   : watch
Run    : Minimized
```

Or a hidden scheduled task at logon:

```powershell
schtasks /Create /TN "FaldSync" /TR "\"C:\path\FaldSync.exe\" watch" /SC ONLOGON /RL LIMITED /F
```

For the UI, put a shortcut to `FaldSync.UI.exe` in `shell:startup` instead. Do not register both.

---

## Safety

- `FaldSync restore` always brings the panel back to the User preset and SDR brightness.
- The watcher is **edge-triggered**: it reacts to HDR on/off, display wake, and system resume.
  It does not fight manual picture-mode or brightness changes between those events.
- On startup, if HDR is on it forces the User → HDR transition, re-applies brightness, the
  Windows SDR white level, and the gamma ramp. If HDR is off it leaves the SDR desktop alone and
  clears a gamma ramp left behind by a previous crash.
- After display sleep, system suspend, an input change, or any driver re-enumeration, it
  re-acquires the DDC handle (the old one goes stale), then retries the mode/brightness/SDR-white
  sequence until it verifies. The UI also re-applies the gamma ramp if Windows resets it.
- SDR white and `gammaFix` are saved as targets while HDR is off; both are applied on the next
  HDR-on transition. The panel's HDR peak is still controlled by `hdrBrightness`, not by
  `sdrWhiteNits`.
- `gammaFix` is restored on HDR-off, on `gamma off`, on `FaldSync restore`, and when the UI
  exits. The original ramp is captured once per display; if it is unavailable, the identity ramp
  is used.

---

## Known limits (honest)

- **True SDR-signal local dimming is impossible on this firmware.** Writing `0xE2=11` while
  Windows is in SDR produces no FALD. Dropping Windows to SDR after latching FALD turns it off.
  The only route is the HDR container with SDR content mapped into it.
- The panel's DDC readback is flaky: values alias under rapid reads and fail during mode
  switches. `0x14` can read `5` while the OSD shows HDR, and it never reports User. The client
  serialises, retries, and re-confirms late. A false "did not verify" warning is possible but
  harmless.
- While the panel is showing another input, DDC/CI over this PC's cable may stop answering
  completely; no software can force the switch back in that state. The app keeps retrying, says
  so in the window, and reconnects as soon as the monitor joystick returns it to this input.
- After a monitor power-off/on, the panel may accept DDC reads but ignore writes for a while
  (observed up to ~2 minutes with a forced `SC_MONITORPOWER` cycle). The wake path keeps
  retrying for up to 3 minutes; the screen can stay in the wrong SDR mode until it succeeds.
  HDR entry is re-confirmed by the preset's brightness jump to 100, not only by `0x14`.
- The gamma correction is a **best-effort display-ramp** fix, not a Windows compositor change.
  It affects all HDR-container content, can be reset by Windows after sleep/unlock/Display
  Settings (the UI re-applies it), and is bypassed by some fullscreen exclusive modes. Use the
  toggle if a native-HDR title looks too dark.
- The implemented curve is the **AMD** method. NVIDIA needs a different PQ-based curve, so the
  LUT is only correct on the AMD RX 6650 XT used for development.
- BitBlt/`CopyFromScreen` screenshots do not include the scanout ramp. A screenshot that looks
  unchanged does not mean the correction failed; check `gamma status`, `state`, or the display.
- A custom SDR ICC/gamma calibration is preserved only if the ramp captured at first apply is
  still the current one. If Windows replaced it while the correction was active, toggling off
  falls back to the captured ramp; re-apply your SDR profile if necessary.
- `sdrWhiteNits` lowers SDR whites only. A genuinely HDR white frame can still reach the
  panel's HDR peak; use `hdrBrightness` (the UI brightness slider) to cap the panel if an HDR
  scene is too bright.
- `0xE7`/`0xE8` (possible Adaptive Dimming) are writable but have no reliable readback and no
  observed FALD effect on SDR. Left out until verified.
