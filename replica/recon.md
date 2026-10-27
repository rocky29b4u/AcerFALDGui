# Recon map — Acer Display Widget → "FaldSync" clone

Recon date: 2026-10-21
Target machine: Windows 11 Pro 10.0.28000 · AMD Radeon RX 6650 XT
Display: Acer Nitro **XV275K P5** (`ACR0F71`, serial 2527000D04HA1, 2025) · 27" 4K 160 Hz · Mini-LED FALD · DisplayHDR 1000
Original app: **Acer Display Widget 4.0.2.0** (`C:\Program Files (x86)\Acer\Acer Display Widget\`)

---

## 0. Verdict first

**The clone is worth building, but not as a "prettier Acer Display Widget".** The original's
DDC surface is small and its state handling is poor. The product is the **HDR/FALD orchestration
layer the original does not have**.

Five facts drive everything:

1. **FALD needs two things at once**: an HDR signal from Windows **and** the monitor in its
   `HDR` display mode. The mode is selected with the vendor code `VCP 0xE2 = 11`
   (User = `0xE2 = 0`, Standard = `0xE2 = 1`); `0x14` reflects HDR/Standard (`6`/`5`) but
   does **not** report User. Windows HDR alone leaves the panel in Standard, which is exactly
   the washed-out "bad screen".
2. **The mode must be forced through User.** The P5's first HDR entry can leave FALD off.
   Writing `0xE2 = 0`, waiting ~1.2 s, then `0xE2 = 11` while the HDR signal is live re-runs
   the preset and latches FALD. A single `0xE2 = 11` write is not reliable. Standard (`1`) is
   still available in config but is no longer the default.
3. **Brightness/contrast are writable in HDR.** Writing `0x10`/`0x12` after the forced preset
   and a ~3 s settle keeps FALD latched while the HDR signal is live. Verified at 65 / 50.
   The OSD Picture Mode label flips to User when those values are written; FALD stays on.
4. **Wake needs a fresh DDC handle and retries.** Display sleep invalidates the cached handle;
   the panel may also accept reads but ignore writes for a while after waking. Re-acquire the
   handle and retry the mode/brightness sequence until it verifies.
5. **True SDR-signal FALD is firmware-locked.** Writing `0xE2 = 11` while Windows is in SDR
   produces no local dimming, and dropping Windows to SDR after latching FALD turns FALD off.
   Tested on this unit. Therefore "local dimming in SDR content" is delivered by keeping the
   **HDR container** and letting Windows tone-map SDR into it — not by a DDC trick.

The clone's core loop: **watch Windows HDR state → select the matching display mode via `0xE2` →
expose brightness/contrast/FALD controls that actually work in HDR → per-app automation.**

---

## 1. Scope

| | |
| --- | --- |
| Platform | Windows 10/11 desktop, tray app + small main window |
| Slice | DDC/CI monitor control + HDR/FALD orchestration for one primary Acer Mini-LED |
| User | You, on this machine; later any Mini-LED/HDR monitor owner |
| Out of scope | Acer branding/assets, firmware updates, USB display, overclocking, FaceID |

**Not** cloning: the Acer look, the Acer logo, the vendor updater, the USB-display Android
mirroring stack, or the overclock flow. Those are either brand assets, hardware-specific, or
liabilities.

---

## 2. Sources

| source | what it gave us |
| --- | --- |
| Installed app 4.0.2 (UI Automation tree + DDC observation) | every screen, control, label, and the VCP values each action writes |
| Acer XV275K user manual (PDF, global-download.acer.com) | OSD feature list, HDR Off/Auto, modes; no Adaptive Dimming or local-dimming speed control on the P5 |
| RTINGS — Nitro XV275K P5 review | "can't use local dimming in SDR"; HDR black-level bug |
| Acer Community — "local dimming in SDR" thread | no official firmware fix; user demand confirmed |
| displayspecifications — XV275K P5 | 1152 dimming zones, HDR1000, 4K160 |
| Microsoft — HDR settings in Windows | SDR content brightness semantics |
| ddcutil / MCCS 2.1 docs | VCP code semantics, capability string format |
| sunshine_hdr_helper (GitHub) | undocumented Windows API for SDR content brightness |
| HDR Gamma Fix (GitHub) | sRGB→gamma 2.2 LUT approach for washed-out SDR on HDR |

No binaries were decompiled and no vendor source or private API was read.

---

## 3. Screen inventory

Reference screenshots were captured during recon for analysis only — they are never shipped and are
not included in this repository. The original watermarks its own UI with a repeating
"Acer Display Widget" pattern.

| ID | screen | how you get there | purpose | key components | states seen |
| --- | --- | --- | --- | --- | --- |
| S01 | GameView | app launch | pick a picture mode | 12 mode tiles (Action, Racing, Sports, User, Standard, ECO, Graphic, Movie, HDR, sRGB, AdobeRGB, Rec.709, Reading, Darkroom), Advanced Settings, Import/Export, monitor switcher (4 slots) | loaded, selected, unsupported monitor, empty custom slots |
| S02 | Advanced Settings | S01 → Advanced Settings | tune the current mode | RGB gain, Brightness, Contrast, Black Boost, Saturation, Gamma, Color Temp, Low Blue Light, Over Drive, Volume, Refresh Rate Num, Factory Reset | User Mode only; some controls disabled by mode; OK/Cancel disabled until change |
| S03 | GameView Sync | tab | bind a mode to an app/game | app list, mode assignment, enable toggles | empty, populated, app running |
| S04 | Split Screen | tab | window layout zones | layout picker, zone grid, apply | idle, applied |
| S05 | Game Assistant | tab | aiming overlays | Aim Point (on/off, 6 styles, color, position H/V), Sniper (on/off, scope size/scale), Night Vision, Gaming Advance | all off, aim point on, sniper on |
| S06 | Hotkey | tab | bind actions | keyboard column, joystick column, Aim Point / Sniper / Night Vision rows, add (+) | unbound, bound, conflict |
| S07 | System / Overclock | gear icon | app + panel system actions | ECO Pro toggle, rotation (Portrait/Landscape/Windows Rotate), Overclock (disabled at 160 Hz), restart prompt | disabled, enabled, confirmation |
| S08 | Monitor selector | S01 bottom | choose which display to control | 4 monitor buttons + model labels | 1 connected, 3 disabled |
| S09 | Import / Export | S01 User tile | move a profile | file picker, validation | success, corrupt file, unsupported format |
| S10 | Error / warning | any failure | tell the user | "Selected Monitor Not Supported", "file format not supported", OK | modal |

Notable UX defects in the original, all worth fixing:

- No live readback. The app showed Brightness 80 while the monitor was at 65.
- No HDR→picture-mode automation. The user must click HDR manually after every Windows toggle.
- Brightness/contrast are unreliable in HDR; the app's state machine and the monitor disagree.
- No FALD status anywhere.
- No per-app HDR rules; GameView Sync is mode-only.
- Destructive "Factory Reset" sits one click from a slider.

---

## 4. User flows

**F01 Switch picture mode** — S01 → tap tile → DDC write → tile selected.
4 clicks. Edge: monitor busy, write fails, mode not supported.

**F02 Tune the picture** — S01 → Advanced Settings → move slider → OK.
5+ interactions; no live preview until OK; stale values if another tool wrote first.

**F03 Windows HDR on → monitor follows (NEW, must)** — Win+Alt+B → app detects advanced-color
edge → waits for signal settle → writes `0xE2=0` (User) → `0xE2=11` (forced transition for the
P5 first-entry bug) → panel runs its HDR preset and latches FALD → re-applies HDR
brightness/contrast. 0 clicks. Edge: DDC unresponsive during re-sync; user manually picked
another mode; HDR off during settle; `0x14` false-negative (accept the brightness jump to 100).

**F04 Brightness/contrast in HDR (NEW, must)** — tray slider or hotkey → `0x10`/`0x12` write →
FALD stays latched and the HDR preset stays active. The OSD Picture Mode label flips to User
when the value is written; that is expected. 1 interaction. Edge: a mode change resets
brightness to 100; re-apply only the user values, never re-write `0x14`.

**F05 Hotkey an action** — S06 → bind → press. Edge: conflict, elevated app has focus.

**F06 Split screen** — S03 → pick layout → apply. Edge: monitor removed, DPI change.

**F07 Aim point / sniper** — S05 → toggle → overlay appears. Software overlay, not DDC.

**F08 Save/load profile** — S01 → Export → file. Edge: corrupt, newer schema, wrong monitor.

**F09 Multi-monitor** — S08 → pick monitor → all controls retarget. Edge: hot-plug, mixed DDC support.

**F10 Recover from a failed DDC write** — timeout → retry with backoff → surface "monitor busy" →
never leave the panel in an unknown mode without a Restore button.

**F11 Resume from display/system sleep (NEW, must)** — display off/on or suspend/resume →
power notification → re-acquire DDC handle → read Windows HDR → force User → HDR (or restore
User/SDR) → retry until the mode and brightness verify. Edge: DDC answers reads but ignores
writes for up to ~2 minutes after wake; HDR state changed while waking; handle is stale.

---

## 5. Components

| component | variants | states | used by |
| --- | --- | --- | --- |
| Mode tile | icon + label, selected, disabled | hover, selected, busy | S01 |
| Slider | 0-100, 0-10, stepped | enabled, disabled, dirty, writing, error | S02, tray |
| Radio group | gamma, color temp, low blue light, overdrive | selected, disabled | S02 |
| Toggle | aim point, sniper, night vision, eco | on, off, disabled | S05, S07 |
| Tab bar | 5 tabs + settings | selected, hover | S01-S06 |
| Monitor chip | model + slot | connected, empty, unsupported | S08 |
| Profile dialog | import/export | idle, invalid, success | S09 |
| Toast / inline error | info, warn, error | auto-dismiss, sticky | global |
| Tray menu | sliders, modes, HDR, quit | — | new |
| FALD badge | on / off / unknown | live | new |

---

## 6. Inferred data model

```
Monitor          id, friendly_name, edid_mfr, edid_model, device_path, connector,
                 caps_string, mccs_version, is_hdr_capable, last_seen
                 evidence: WmiMonitorID + DISPLAYCONFIG_TARGET_DEVICE_NAME + caps string
                 confidence: high

VcpSetting       monitor_id, code, name, kind (continuous|enum|bool), min, max, step,
                 values_json, readable, writable, confidence, last_value, last_read_at
                 evidence: capability string + read/write probes
                 confidence: high for read, medium for semantics

PictureMode      id, name, vcp_map_json (0xE2 display mode, 0x10, 0x12, 0xE1, 0xE5, 0xE0 ...),
                 builtin, monitor_model
                 evidence: app tiles + DDC diffs; exact preset fingerprints still partial
                 confidence: medium

Profile          id, name, monitor_model, mode_id, vcp_map_json, windows_state
                 (hdr, sdr_brightness), created_at
                 evidence: export/import UI
                 confidence: high

Rule             id, trigger (hdr_on|hdr_off|app_launch|app_focus|hotkey|schedule),
                 match_json, actions_json, enabled, priority
                 evidence: GameView Sync + missing HDR automation
                 confidence: new design

Hotkey           id, scope (global|app), combo, action, enabled
                 evidence: S06
                 confidence: high

LogEntry         ts, monitor_id, code, requested, readback, ok, latency_ms, error
                 evidence: need for diagnostics; original has none
                 confidence: new design
```

---

## 7. DDC map (measured on this unit)

Capability string (exact):

```
(prot(monitor)type(lcd)model(XV275K P5)cmds(01 02 03 07 0C E3 F3)
 vcp(02 04 05 08 0B 0C 10 12 14(05 06 08 0B) 16 18 1A 52 54(00 01) 59 5A 5B 5C 5D 5E
     60(11 12 0F 10) 62 9B 9C 9D 9E 9F A0 AC AE B6 C0 C6 C8 C9 CC(...) D6(01 04 05)
     DF E3 E5 E7(00 01 02 03) E8(00 01 02 03) E0(00 04 05 06) E1(00 01 02))
 mswhql(1) asset_eep(32) mccs_ver(2.1))
```

| VCP | meaning | evidence | confidence |
| --- | --- | --- | --- |
| `0x10` | Brightness 0-100 | read/write; app slider tracks it | **high** |
| `0x12` | Contrast 0-100 | read/write; app slider tracks it | **high** |
| `0xE2` | **Display mode setter (vendor)** | Traced from the installed app: HDR tile → `SetVCPFeature(0xE2, 11)`; Standard tile → `SetVCPFeature(0xE2, 1)`; User tile → `SetVCPFeature(0xE2, 0)`. Not advertised in the capability string; write-only (a standard read returns `0`, max `27`). | **high** |
| `0x14` | Picture-mode reflection | Reads `6` after `0xE2=11` (HDR) and `5` after `0xE2=1` (Standard); values 5/6/8/11. It does **not** report the User preset (`0xE2=0`) and can read `5` while the OSD says HDR. Writing it does **not** run the HDR preset or arm FALD. | **high** for 5/6; advisory only |
| `0x16/18/1A` | R/G/B gain 0-100 | 50/50/50 at defaults | **high** |
| `0x60` | Input source | `15` on DisplayPort | **high** |
| `0x62` | Speaker volume 0-100 | app volume slider | **high** |
| `0x6C/6E/70` | R/G/B black level 0-100 | 50 at defaults | medium |
| `0xD6` | Power mode | `1` = on, `4` = standby | **high** |
| `0xE1` | Over Drive | `0→1` when Extreme selected, max became 2 | medium |
| `0xE5` | **Black Boost 0-10** | slider 5→8→5 read back exactly | **high** |
| `0xE0` | Color space / HDR (0,4,5,6) | capability values only, reads fail | **low** |
| `0xE7` | Unknown enum 0-3 | writable, no reliable readback | low |
| `0xE8` | Unknown enum 0-3 | mirrored `0xE5` on every mode; possibly alias | low |
| `0xE3` | Unknown | returns 452 | low |
| `0xF0/F2` | Unknown | 0/1 and 10/4 | low |

**Key finding:** `0xE2` is the real display-mode selector; `0x14` is a reflection. The Acer app
writes only `0xE2` when you pick HDR, Standard, or User, and the panel itself then applies the
preset (HDR forces brightness to 100 and turns FALD on). Every automation attempt that wrote
`0x14` alone was inert — that was the root cause of the "HDR but no FALD" state.

**Firmware bug / forced transition:** the XV275K P5 can enter HDR with FALD still off on the
first HDR use. The documented workaround is to change the picture mode away from HDR and back.
FaldSync does this with the real setter: `0xE2=0` (User), wait ~1.2 s, `0xE2=11` (HDR), all
while the HDR signal is already live. A single `0xE2=11` write is not enough after a signal
switch. The forced transition is used on HDR-on, on startup, and after sleep/resume. Standard
(`0xE2=1`) remains a config option but is no longer the default.

**Wake behaviour:** display sleep invalidates the cached physical-monitor handle — reads may
"work" but return no usable data, so the handle must be re-acquired. Even with a fresh handle,
the panel can answer reads while ignoring writes for a while (observed up to ~2 minutes after a
forced `SC_MONITORPOWER` off/on). A robust client must retry the mode/brightness sequence until
the HDR preset's brightness jump to 100 (or `0x14=6`) and the brightness readback verify.

**Reliability warning:** this monitor's DDC stack aliases values under rapid reads. `0xE1`
sometimes returned `50` (the contrast value) and `0xE8` mirrored `0xE5` exactly. A robust client
must: serialise requests, retry 3×, validate readback against the requested value, and never
treat a single read as truth.

Not exposed by the original app at all: **local dimming / Adaptive Dimming**, FALD status,
HDR state, SDR content brightness, per-app rules.

---

## 8. HDR / FALD findings (the reason to build this)

See `hdr-fald-findings.md` for the full experiment log. Summary:

| state | FALD? |
| --- | --- |
| Windows SDR, Standard (`0xE2=1`, `0x14=5`) | no |
| Windows SDR, forced HDR (`0xE2=11`, `0x14=6`) | **no** — firmware gates on signal |
| Windows HDR, Standard (`0xE2=1`, `0x14=5`) | **no** — this is the "bad screen" |
| Windows HDR, HDR first entry (`0xE2=11`, `0x14=6`) | **sometimes no** — P5 first-entry firmware bug |
| Windows HDR, forced User → HDR (`0xE2=0` → `0xE2=11` while HDR live) | **yes** — re-runs the preset and latches FALD |
| Windows HDR, User preset (`0xE2=0`, OSD User) | latch may stay on until the next transition |
| Windows HDR, HDR, then DDC brightness/contrast write | **yes** — latch survives at 65/50; OSD label flips to User |
| then drop Windows to SDR | **no** — latch dies with the HDR signal |

Consequences for the product:

- The clone cannot give you FALD on a true SDR signal. Nobody can, on this firmware.
- It can give you FALD **while you look at SDR content**, by keeping the HDR container and
  making SDR content look right inside it (Windows SDR→HDR mapping + gamma 2.2 correction +
  SDR content brightness).
- It can remove the bad-screen window: on HDR-on, force User → HDR automatically, which also
  works around the first-entry FALD-off bug.
- It can keep brightness/contrast usable in HDR by writing `0x10`/`0x12` after the mode preset.
- It can restore the same state after display sleep or system suspend/resume by listening for
  power notifications, re-acquiring the DDC handle, and retrying until the writes verify.

---

## 9. Feature matrix

`features.csv` — 38 rows, priorities must/should/could. The clone column tracks the current
prototype: SDR white and the gamma 2.2 correction are implemented; the remaining rows are
planned.

---

## 10. What cannot be cloned

| item | why |
| --- | --- |
| Acer logo, icons, mode artwork, watermark pattern | trademark/copyright; use original assets |
| Exact Acer preset fingerprints | partly vendor-tuned; we can approximate with our own calibrated profiles |
| Firmware FALD behaviour | closed firmware; cannot be changed from the host |
| USB display / Android mirroring | bundles scrcpy/ffmpeg; out of scope and a security surface |
| Overclock flow | monitor/firmware specific; GPU driver dependent |
| FaceID | disabled on this machine, no hardware |
| Auto-update channel | vendor infrastructure; use our own signed updater or none |

---

## 11. Size and risk

**Size: M** (2-3 weeks for a solid v1).

Hard parts, in order:

1. **DDC reliability** — aliased reads, monitor unresponsive during mode changes. Needs a
   serialised queue, retries, verification, and a "restore last known good" path.
2. **HDR transition timing** — the monitor needs a moment after the signal switch; writing too
   early fails silently. Edge-trigger + settle + verify.
3. **SDR-in-HDR colour** — Windows maps SDR with a piecewise sRGB curve; without correction the
   desktop looks washed out. A per-display gamma 2.2 ramp is the practical fix; FaldSync
   implements it with `SetDeviceGammaRamp` (AMD curve, 256 entries) and exposes it as a
   HDR-only toggle. The Windows HDR calibration profile remains an alternative/complement.
4. **Fighting the user** — never re-assert the mode on every poll; only on HDR edges and explicit
   user actions.

Risks: firmware differences across XV275K P5 batches; AMD vs NVIDIA DDC behaviour; Windows
build changes to the undocumented SDR brightness API.

5. **Third-party brightness tools** — TwinkleTray and Acer Display Widget both reassert their
   cached brightness on display-change events. Observed on the target machine: after an HDR
   toggle, brightness snapped back from 65 to 40 while TwinkleTray was running. FaldSync must
   detect these processes at startup, warn, and offer an exclusive-control mode. It is intended
   to replace them, not to run beside them.

---

## 12. Next step

`/replica-architect` to turn this into a schema, module layout, and a build order. The first
vertical slice is already specified: **HDR-on → forced User → HDR → HDR brightness/contrast**,
with a tray fallback and a log. That slice is the product.

---

## 13. Prototype status

`prototype/FaldSync/` (engine) and `prototype/FaldSync.UI/` (tray UI) are built and verified on
the target machine. The UI is a thin wrapper over the same engine; both are current.

Verified end-to-end:

- HDR-on → `0xE2=0` → `0xE2=11` → HDR brightness/contrast, SDR white, and optional gamma ramp.
  Verified at 65/50, 45/50, and the current 50/55.
- HDR-off → original gamma ramp restored → `0xE2=0` (User) → SDR brightness/contrast restored
  (current 20/20).
- Brightness/contrast writes in HDR keep the FALD latch; the OSD label flips to User.
- Forced User → HDR fixes the P5 first-entry FALD-off bug.
- `0x14` is advisory: it stayed at `5` while the OSD said HDR; HDR entry is also confirmed by
  the preset's brightness jump to 100.
- Display off/on: power notification → fresh DDC handle → retry until the mode and brightness
  verify (user-confirmed FALD on after wake).
- DDC retries + late confirmation survive the panel's flaky readback during signal switches.
- `FaldSync restore` is the safety path back to User/SDR.
- SDR white is set to 140 nits in HDR on this machine; FALD stays active.
- Gamma 2.2 correction: applied only while HDR is on; ramp readback matched
  `mid=33153 / quarter=15867 / last=65535`; FALD stayed active. Toggling it off restored the
  ramp without dropping HDR/FALD. HDR-off and UI exit also restored the ramp.
- A simulated Windows ramp reset (identity written behind the app's back) was re-applied within
  7 seconds by the UI's 5-second check.
- The UI is 560×882 logical pixels, with the gamma toggle in the SDR-white card; the HDR card
  and action row moved down to fit.
- The vendor display mode is confirmed by tracing the installed app: `Monitor_Set_DisplayMode(11)`
  → `SetVCPFeature(0xE2, 11)`, `Monitor_Set_DisplayMode(1)` → `SetVCPFeature(0xE2, 1)`,
  `Monitor_Set_DisplayMode(0)` → `SetVCPFeature(0xE2, 0)`.

Commands: `state`, `sync`, `watch`, `set -b/-c`, `white [nits]`, `gamma on|off|status`,
`hdr on|off`, `restore`, `probe`.
Config: `faldsync.json` (`displayModeVcp`, `hdrDisplayMode`, `sdrDisplayMode`, `hdrPictureMode`,
`sdrPictureMode`, `hdrBrightness`, `hdrContrast`, `sdrBrightness`, `sdrContrast`, `sdrWhiteNits`,
`gammaFix`, `gammaPower`, `pollMs`, `settleMs`, `preSettleMs`, `userSettleMs`, `modelMatch`).

Start the UI with `prototype\Start FaldSync UI.cmd` (stop with `prototype\Stop FaldSync UI.cmd`).
The console engine can be started with `prototype\Start FaldSync.cmd`. Do not run TwinkleTray or
Acer Display Widget alongside either.
