# AcerFALDv2 — portable source

Self-contained WinForms build of FaldSync (version 2.2). It reuses the `FaldSync` engine project
and the same UI source, with its own assembly name, single-instance mutex, tray identity, and
output folder.

Everything the main UI has is here: dark-first theme with an in-place light theme, brightness,
contrast, SDR white, gamma 2.2, HDR + FALD control, monitor volume, input source, and the
app-managed auto-switch.

## Publish

```powershell
dotnet publish .\prototype\AcerFALDv2\AcerFALDv2.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=false -p:DebugType=none `
  -o .\prototype\AcerFALDv2\publish
```

The RID and self-contained flags live on the publish command, not in the csproj, so a plain
`dotnet build -c Release` in this folder also succeeds (framework-dependent, for quick checks).

The publish folder contains `AcerFALDv2.exe`, `faldsync.json`, and a harmless
`FaldSync.runtimeconfig.json` copied from the engine reference. The exe is single-file and does
not need a .NET runtime on the target PC; the extra runtimeconfig is not required (verified by
running the exe from a folder containing only the exe).

The checked-in `faldsync.json` mirrors the main UI settings: HDR 50/55, SDR 20/20, SDR white
180 nits, gamma 2.2 on, input DisplayPort (`selectedInput: 15`), auto-switch off, theme dark.
Note that `theme` only affects this app's window; it is a normal config field saved on toggle.

## Output

The runnable copy lives at `AcerFALDv2\` in the repo root:

```
AcerFALDv2.exe
faldsync.json
README.txt
```

`AcerFALDv2-portable.zip` is the same three files under an `AcerFALDv2\` folder.

## Notes

- Do not run this and `FaldSync.UI.exe` together; both drive the same DDC channel.
- `--self-test` loads the self-contained runtime and exits without touching DDC.
- `--allow-parallel` is a test/emergency escape hatch for the original-process check.
- Input switching is confirmed in the UI first; a wrong input blanks the screen, and the
  monitor joystick is the way back. The panel's own "Auto Source" is OSD-only and is not
  written over DDC.
- The app re-acquires a stale DDC handle after an input change or display re-enumeration, reads
  brightness/contrast/volume/input back from the panel every 2.5 s, and keeps the input buttons
  usable while the panel is on another source. Reconnect immediately from the tray menu.
