AcerFALDv2
===========

Portable build 2.2.0 (repository release v1.2.0).

Self-contained version of FaldSync for the Acer Nitro XV275K P5.

No .NET installation is required. Everything needed is inside AcerFALDv2.exe.

Run
---
Installed copy: run the AcerFALDv2 setup program. It installs to C:\AcerFALDv2
by default and asks whether you want a desktop shortcut, a Start Menu shortcut
and AcerFALDv2 to start with Windows. Remove it again from Settings > Apps.

Zip: extract it to a folder you can write to first, then double-click
AcerFALDv2.exe. Nothing is installed; to start it at login, put a shortcut to
AcerFALDv2.exe in shell:startup (Win+R, type shell:startup, Enter).

Settings and the log are written next to the exe:

  faldsync.json   brightness, contrast, SDR white, gamma, input, theme, timing
  faldsync.log    HDR/FALD transition log

What is in the window
---------------------
  Brightness / Contrast   live DDC/CI sliders, applied as you drag
  SDR White (in HDR)      Windows SDR content level, 80-480 nits
  Gamma 2.2               reversible GPU correction, only while HDR is on
  HDR + Local Dimming     HDR on/off, picture mode, re-apply, FALD status
  Monitor Audio           speaker volume over VCP 0x62
  Input Source            DP / HDMI 1 / HDMI 2 / DP ALT over VCP 0x60
  Auto switch to input    re-selects the chosen input after startup and wake
  LIGHT / DARK            theme switch, top right; the choice is remembered

Current defaults in this build:
  HDR brightness 50, HDR contrast 55
  SDR brightness 15, SDR contrast 15
  SDR white 180 nits, gamma 2.2 on, theme dark, input DisplayPort

Every value is adjustable in the window and saved to faldsync.json.

Input switching
---------------
Changing the input is confirmed first, because a wrong choice can blank the
screen. If that happens, use the monitor joystick to pick the input again.

The app keeps its DDC/CI link alive: after an input change or any display
re-enumeration it re-acquires the handle, reconnects on its own when the panel
answers again, and keeps reading brightness, contrast and volume back so changes
made with the monitor joystick appear in the window. If a switch does not take,
wait a moment and try again, or use "Reconnect to monitor" in the tray menu.

The panel's own "Auto Source" option lives in its OSD; Acer publishes no
DDC/CI code for it. "Auto switch to input" here is this app's own behaviour:
after startup and after a display wake it re-selects the input you chose, and
it never blind-writes to a monitor it cannot read.

Important
---------
Do not run AcerFALDv2 together with the original FaldSync UI, Acer Display
Widget, or TwinkleTray. They all drive the same DDC/CI channel and will fight
over brightness and picture-mode writes. AcerFALDv2 warns and exits if the
original FaldSync UI is already running.

The exe is unsigned, so Windows SmartScreen may show "Unknown publisher".
Choose "More info" -> "Run anyway" if you trust this build.

Source
------
The source for this portable build is in prototype\AcerFALDv2.
It shares the FaldSync engine project and keeps the same config/log names,
but uses its own output folder and tray identity.
