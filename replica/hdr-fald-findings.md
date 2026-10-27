# HDR / FALD findings — Acer XV275K P5

Everything here was measured on the target machine on 2026-10-21, with the monitor on
DisplayPort, Windows 11 build 28000, AMD RX 6650 XT. No vendor binary was decompiled; all
values come from the public DDC/CI channel, the Windows display configuration API, and a
runtime trace of the installed app's exported monitor calls.

---

## 1. What the panel exposes

The monitor answers DDC/CI with MCCS 2.1 and this capability string:

```
(prot(monitor)type(lcd)model(XV275K P5)cmds(01 02 03 07 0C E3 F3)
 vcp(02 04 05 08 0B 0C 10 12 14(05 06 08 0B) 16 18 1A 52 54(00 01) 59 5A 5B 5C 5D 5E
     60(11 12 0F 10) 62 9B 9C 9D 9E 9F A0 AC AE B6 C0 C6 C8 C9 CC(...) D6(01 04 05)
     DF E3 E5 E7(00 01 02 03) E8(00 01 02 03) E0(00 04 05 06) E1(00 01 02))
 mswhql(1) asset_eep(32) mccs_ver(2.1))
```

There is **no standard VCP for local dimming** and no standard VCP for HDR mode. The
manufacturer-specific codes are the only way in.

---

## 2. Two registers: `0xE2` sets the display mode, `0x14` reflects it

This was the missing piece. The installed app was traced at runtime (Frida hooks on
`Monitor_MCCS_API.dll` and `dxva2.dll`):

| app action | exported call | DDC write |
| --- | --- | --- |
| click the **HDR** tile | `Monitor_Set_DisplayMode(11)` | `SetVCPFeature(0xE2, 11)` |
| click the **Standard** tile | `Monitor_Set_DisplayMode(1)` | `SetVCPFeature(0xE2, 1)` |
| click the **User** tile | `Monitor_Set_DisplayMode(0)` | `SetVCPFeature(0xE2, 0)` |

The app writes **only `0xE2`**. It does not write brightness, contrast, or `0x14` as part of
the mode change. The panel itself runs the stored preset: HDR forces brightness to 100 and
turns FALD on.

- `0xE2` is a vendor display-mode setter, not advertised in the capability string.
- `0xE2` is **write-only**: a standard read returns `0` with max `27`. Verify HDR/Standard
  through `0x14`; the User preset has no distinct reflection.
- `0x14` is the **reflection** of the applied mode: `6` after `0xE2=11` (HDR), `5` after
  `0xE2=1` (Standard). Writing `0x14` alone changes the label but does **not** run the HDR
  preset and does **not** arm FALD.
- `0xE2=0` selects the monitor's **User** preset. The OSD Picture Mode confirms it, but `0x14`
  does **not** report it: it can stay at `5` or `6`, so there is nothing reliable to poll.
  FaldSync treats the accepted `0xE2=0` write as success and waits `userSettleMs` (1.2 s).
- Changing brightness or contrast while HDR is active also flips the OSD Picture Mode label to
  **User**, even though the HDR preset and FALD latch stay active. This is why the final OSD
  can read `User` after FaldSync applies HDR brightness 65 - it is still the HDR preset with a
  manual brightness override, not a different SDR mode.
- Re-writing `0xE2=11` while already in HDR is a re-apply, but on this unit it is **not always
  enough** after a signal switch: the P5 can enter HDR with FALD still off. The reliable fix is
  to write a different picture mode (now `0xE2=0`, User), wait ~1 s, then `0xE2=11` (HDR) while
  the HDR signal is live. This is the documented "change picture mode away and back" workaround,
  using the real setter.
- HDR entry resets brightness to 100, so do the forced transition on an HDR edge, at startup,
  or after resume - never on every slider move.
- `0x14` can be a false negative for HDR: after `0xE2=11`, the OSD read HDR with FALD on while
  `0x14` still returned `5`. FaldSync confirms HDR with either `0x14=6` or the preset's
  brightness jump to 100.

---

## 3. The FALD gate

All rows tested on this unit. "FALD" means visibly deeper blacks plus haloing around bright
objects on a dark test desktop.

| # | Windows | Display mode | `0x14` | HDR signal | FALD | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| A | SDR | `0xE2=1` Standard | `5` | no | no | baseline |
| B | SDR | `0xE2=11` HDR | `6` | no | **no** | forcing the mode is not enough |
| C | HDR | `0xE2=1` Standard | `5` | yes | **no** | the washed-out "bad screen" |
| D | HDR | `0xE2=11` HDR | `6` | yes | **sometimes no** | first HDR entry can leave FALD off (P5 bug) |
| D2 | HDR | `0xE2=0` → `0xE2=11` forced | `5` or `6` | yes | **yes** | User intermediate re-runs the preset; fixes the first-entry bug |
| D3 | HDR | `0xE2=0` User | `5` or `6` | yes | on/off | OSD says User; the latch can survive the write until the next transition |
| E | HDR | HDR + brightness 65 / contrast 50 | `6` (usually) | yes | **yes** | OSD label flips to User on the brightness write; FALD stays on |
| F | SDR after E | — | — | no | **no** | latch dies with the HDR signal |

The OSD label and the internal preset can disagree: writing brightness/contrast in HDR changes
the label to **User** while FALD remains latched. Trust FALD plus the OSD for the mode, and treat
`0x14` as advisory.

Conclusion: **FALD requires the HDR preset to run while the HDR signal is live** (`0xE2=11`),
and the HDR signal has to stay on. The display mode alone does nothing; the signal alone does
nothing. Once latched, the preset survives brightness/contrast writes, and the OSD label can
show User. On the P5, the first HDR entry can still leave FALD off even in state D; the forced
transition (D2) re-runs the preset and latches it. This matches the firmware behaviour reported
on RTINGS and the Acer Community thread asking for an SDR local-dimming update.

---

## 4. Brightness/contrast in HDR

While in state D/D2:

1. Force the HDR preset: `SET 0xE2=0` (User), wait ~1.2 s, `SET 0xE2=11` → brightness 100
   and FALD on.
2. `SET 0x10 = 65` → readback `65`, picture brightness changes. The OSD Picture Mode label
   changes to **User** at this point.
3. `SET 0x12 = 50` → readback `50`, contrast changes.
4. `0x14` usually reads `6`; it was also observed reading `5` while the OSD said HDR and FALD
   was on.
5. FALD remains active (user-confirmed at 65/50).

So the clone does **not** need a special HDR brightness protocol. It needs to:

- force `0xE2 = 0` (User) → `0xE2 = 11` when HDR turns on,
- wait for the preset (brightness reaches 100) and give it about 3 s,
- then write `0x10` / `0x12` normally,
- and avoid re-writing `0xE2` or `0x14` on every brightness change (that would force
  brightness back to 100 and re-run the preset).

---

## 5. What does not work

| attempt | result |
| --- | --- |
| Write `0x14=6` while Windows HDR is on | label changes, no FALD — `0x14` is inert for the preset |
| Write `0xE2=11` while Windows SDR | no FALD (state B) |
| Turn Windows HDR on and leave the monitor in Standard | no FALD, washed-out image (state C) |
| Latch FALD in state D, then turn Windows HDR off | FALD turns off within the transition |
| Write `0xE7`/`0xE8` (candidate Adaptive Dimming) | accepted, no observable FALD on SDR |
| Use the Acer app's HDR button with Windows SDR | same as state B |

There is no host-side DDC command that unlocks FALD on a true SDR signal on this firmware. Any
tool claiming otherwise is either changing the Windows HDR state or selecting the HDR display
mode while an HDR signal is already present.

### FALD zone-response speed (P5: not adjustable)

The P5 runs local dimming only while HDR is active, and its user manual exposes no Adaptive
Dimming or local-dimming speed control. The P3's `Low` / `Average` / `Fast` Adaptive Dimming
setting is not present on the P5; reviews of the P5 describe it as having no local-dimming
settings at all. No standard VCP and no documented vendor VCP exposes the zone-response speed.
`0xE7` / `0xE8` writes are accepted, but they did not change FALD behaviour in SDR, and their
readbacks alias (`0xE8` returned the contrast value), so they are not a reliable speed control.
This is firmware behaviour; there is no host-side fix.

What helps perceptually: a dark-gray rather than pure-black desktop, dark mode in browsers,
some bias/room light, and slightly lower contrast or SDR white to reduce the size of the zone
transitions. Keeping HDR on avoids the separate HDR re-arm delay. Check Acer support for a P5
firmware update; none was public at the time of testing. Reducing Windows refresh rate does not
help: the target machine is already at 4K 160 Hz.

---

## 6. The recipe the clone implements

### On Windows HDR → on

```
1. Detect the advanced-color edge (DisplayConfigGetDeviceInfo, type 9).
2. Wait for the link to settle: poll DDC 0x10 until it answers, then wait ~1.2 s.
3. SET 0xE2 = 0 (User) while the HDR signal is already live.
4. Wait userSettleMs (~1.2 s). Do not poll 0x14: it has no distinct User value.
5. SET 0xE2 = 11 (vendor HDR mode, write-only).
6. Confirm HDR with 0x14=6 OR the preset's brightness jump to 100. Either is enough;
   0x14 can stay at 5 while the OSD already says HDR.
7. Wait ~3 s for the HDR preset (it forces brightness to 100 and enables FALD).
8. Re-apply the user's HDR brightness/contrast: SET 0x10, SET 0x12.
9. Set the Windows SDR white level (80-480 nits, step 4).
10. If `gammaFix` is on, load the 256-entry gamma 2.2 scanout ramp for this GDI display.
11. Verify readback; retry the sequence once if the HDR write was not confirmed.
12. Log the transition; update the tray badge to "FALD on".
```

The User step (3-4) is not cosmetic: it is the documented workaround for the P5
first-entry FALD-off bug. Do it while the HDR signal is live, not across the signal edge.
Using User instead of Standard keeps the user's own SDR colour/brightness preset.

### On Windows HDR → off

```
1. Detect the edge.
2. Restore the original display gamma ramp (no-op if the current ramp is not ours).
3. SET 0xE2 = 0 (User) or the configured SDR mode.
4. Do not require a 0x14 reflection; the User preset has none. Wait userSettleMs.
5. Restore the configured SDR brightness/contrast (these become the User preset values).
6. Update the badge to "FALD off".
```

### On display wake or system resume

```
1. Re-acquire the physical-monitor handle (display sleep invalidates the cached one).
2. Read the current Windows HDR state.
3. If HDR is on, run the HDR-on recipe; if off, run the HDR-off recipe.
4. If a mode/brightness write does not verify, wait ~5 s and retry the sequence. Some panels
   answer reads but ignore writes for a while after wake (up to ~2 min in a forced off/on test).
5. Reopen the handle periodically during the retry loop.
6. Log the wake reason and the final verified state.
```

### On a manual brightness/contrast change while HDR is on

```
1. SET 0x10 / 0x12 only.
2. Do not touch 0xE2 or 0x14; the HDR mode and FALD latch stay in place.
3. The OSD Picture Mode label will flip to User. This is expected; FALD stays on.
4. If a write fails, re-read it once. Only if FALD actually drops should you re-apply
   0xE2=11 — and then re-apply brightness/contrast because the preset resets them.
```

### SDR content inside the HDR container

FALD is on, but the desktop is still SDR content mapped by Windows. To keep it looking like SDR:

- set the Windows **SDR content brightness** slider (undocumented
  `DISPLAYCONFIG_DEVICE_INFO_SET_SDR_WHITE_LEVEL`; 80–480 nits, step 4). FaldSync owns this:
  default **120 nits**, live UI config **180 nits**, applied on HDR-on and after wake. The panel
  was at 220 nits;
- apply a per-display **gamma 2.2 correction ramp** (Windows maps SDR with piecewise sRGB in HDR,
  which lifts shadows) — implemented; see below;
- optionally use the Windows HDR Calibration profile so black is 0 nits and peak is 1000 nits.

This is the only honest way to get "local dimming in SDR content" on this panel.

### Gamma 2.2 ramp (implemented)

Windows 11 uses the piecewise sRGB transfer function for SDR content inside an HDR container,
not gamma 2.2. The sRGB curve is lighter in the shadows, so a desktop graded for gamma 2.2 looks
washed out. The fix is a 256-entry GPU scanout LUT loaded with `SetDeviceGammaRamp` on the GDI
display (`\\.\DISPLAY1` here), independent of the monitor's DDC controls.

The AMD curve (black level 0, target gamma 2.2) is:

```
out = srgbInvEotf(pow(i / 255, 2.2))
srgbInvEotf(L) = L <= 0.00313066844250063
                 ? L * 12.92
                 : 1.055 * pow(L, 1/2.4) - 0.055
ramp[i] = round(out * 65535)
```

Measured on the target machine:

| ramp | mid (i=128) | quarter (i=64) | last (i=255) |
| --- | --- | --- | --- |
| identity | 32896 | 16448 | 65535 |
| gamma 2.2 | 33153 | 15867 | 65535 |

`SetDeviceGammaRamp` returned true and a `GetDeviceGammaRamp` readback matched those values. The
ramp was applied only while HDR was on; HDR-off restored the original ramp and FALD returned to
off. A simulated Windows reset (identity written behind the app's back) was corrected back to
`mid=33153 / quarter=15867` within 7 seconds by the UI's 5-second re-apply check.

Caveats:

- the correction affects all HDR-container content, including native HDR games; toggle it off
  for a native-HDR title if shadows look too dark;
- this is the **AMD** method. NVIDIA needs a different PQ-based curve; the target machine is an
  AMD RX 6650 XT;
- BitBlt/`CopyFromScreen` captures the framebuffer before the scanout ramp, so screenshots do
  not show the correction. Verify with the ramp readback, `FaldSync gamma status`, or visually;
- Windows can reset the ramp after sleep, unlock, or Display Settings. The UI re-applies it;
  the engine applies it on HDR-on and after wake. A custom ICC/ramp is restored from the captured
  original when possible.

---

## 7. Timing and reliability notes

- DDC writes take tens of milliseconds; reads can take longer.
- During an HDR mode switch the monitor may stop answering for 0.5-2 s.
- After `0xE2=11`, allow ~3 s before writing brightness. Writing 65 too early can cancel the
  FALD latch even though `0x14` already reads `6` (observed intermittently with a 1.3 s wait).
- `0xE2` cannot be read back (returns 0). Use `0x14` (`6` = HDR, `5` = Standard) for HDR and
  Standard, with a short poll. The User preset (`0xE2=0`) has no distinct `0x14` value.
- `0x14` itself can still report a stale/aliased value after a transition: a few seconds after
  `0xE2=1` it was observed reading `6` again, and after `0xE2=11` it read `5` while the OSD
  said HDR. The `0xE2` write is authoritative. The engine writes the mode on every HDR edge
  and treats `0x14` as a hint, not a reason to skip the write.
- HDR entry is confirmed by `0x14=6` **or** brightness jumping to 100. The second signal is
  what keeps the post-wake retry useful when `0x14` is aliased.
- Rapid sequential reads alias: `0xE1` returned the contrast value (`50`) and `0xE8` mirrored
  Black Boost (`0xE5`) on several passes. Serialise, delay, retry, and verify.
- Display sleep invalidates the cached physical-monitor handle. After wake, reads may "succeed"
  but return no usable data; re-acquire the handle before doing anything else.
- Windows can also reset the **gamma ramp** on sleep/resume, unlock, or opening Display Settings.
  The ramp is separate from DDC; re-apply it after the HDR state is confirmed. The UI checks
  every 5 seconds while HDR is on, and the engine re-applies it in its wake path.
- Even with a fresh handle, the panel can answer reads while ignoring writes for a long time
  after wake (observed up to ~2 minutes with a forced `SC_MONITORPOWER` off/on). Retry the
  mode/brightness sequence; a single failed write does not mean the panel is unsupported.
- The display on/off and system suspend/resume notifications come from
  `RegisterPowerSettingNotification` (`user32.dll`) on a hidden top-level window. DDC polling
  alone cannot detect monitor sleep on this unit: `0xD6` kept reading `1` while the panel was off.
- Never write `0x60` (input) or `0xD6` (power) as part of a mode change.
- Always keep a "Restore SDR" action reachable from the tray, even if the UI hangs.

**Coexistence:** TwinkleTray and Acer Display Widget both listen for display-change events and
reassert their cached brightness. On this machine, brightness was observed snapping from 65 back
to 40 after an HDR toggle while TwinkleTray was running. Any tool that owns these VCPs must
either be the only writer or detect and warn about the other writers.

---

## 8. Reference implementation (prototype)

`../prototype/FaldSync/` — a .NET 8 console agent that implements the recipes above:

```
FaldSync state
FaldSync sync
FaldSync watch
FaldSync set --brightness 50 --contrast 55
FaldSync white 140
FaldSync gamma on|off|status
FaldSync hdr on|off
FaldSync restore
FaldSync probe --code 0xE1
```

`watch` also listens for display on/off and system suspend/resume, refreshes the monitor handle,
and retries the mode/brightness sequence until it verifies. `gamma` controls the SDR-in-HDR ramp;
`white` controls the Windows SDR-content white level. Both are persisted in `faldsync.json`.

`prototype/FaldSync.UI/` wraps the same engine and adds sliders, a gamma toggle, an HDR button,
wake recovery, and a tray icon. The engine remains the risk-bearing core; the UI is a thin shell.
