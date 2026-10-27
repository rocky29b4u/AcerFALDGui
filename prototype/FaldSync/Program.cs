using System.Runtime.InteropServices;
using System.Text;

namespace FaldSync;

// ---------------------------------------------------------------------------
// Win32 / dxva2 interop
// ---------------------------------------------------------------------------

internal static class Native
{
    public const uint QDC_ONLY_ACTIVE_PATHS = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprc, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetCapabilitiesStringLength(IntPtr hMonitor, out uint length);

    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr hMonitor, [Out] StringBuilder reply, uint length);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, out uint type, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);

    // ----- DisplayConfig -----------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_INFO
    {
        public PATH_SOURCE_INFO sourceInfo;
        public PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SOURCE_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ADVANCED_COLOR_INFO
    {
        public DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SET_ADVANCED_COLOR_STATE
    {
        public DEVICE_INFO_HEADER header;
        public uint value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDR_WHITE_LEVEL
    {
        public DEVICE_INFO_HEADER header;
        public uint SDRWhiteLevel;
    }

    // Windows exposes the getter publicly, but the setter is still an
    // undocumented display-config type used by Twinkle Tray and other HDR tools.
    // The trailing finalValue byte commits the change.
    [StructLayout(LayoutKind.Sequential)]
    public struct SET_SDR_WHITE_LEVEL
    {
        public DEVICE_INFO_HEADER header;
        public uint SDRWhiteLevel;
        public byte finalValue;
    }

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPath, out uint numMode);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPath, [Out] PATH_INFO[] paths,
        ref uint numMode, [Out] MODE_INFO[] modes, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME info);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR_INFO info);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref SET_ADVANCED_COLOR_STATE info);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref SDR_WHITE_LEVEL info);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigSetDeviceInfo(ref SET_SDR_WHITE_LEVEL info);
}

// ---------------------------------------------------------------------------
// A physical monitor plus a small, retrying DDC/CI client
// ---------------------------------------------------------------------------

public sealed class PhysicalMonitor : IDisposable
{
    private readonly Native.PHYSICAL_MONITOR[] _native;
    private readonly object _gate = new();
    private bool _disposed;

    public string GdiDevice { get; }
    public string Description { get; }
    public string Capabilities { get; }
    public IntPtr Handle => _native[0].hPhysicalMonitor;

    internal PhysicalMonitor(string gdiDevice, Native.PHYSICAL_MONITOR[] native, string caps)
    {
        GdiDevice = gdiDevice;
        _native = native;
        Description = native[0].szPhysicalMonitorDescription;
        Capabilities = caps;
    }

    public string? Model
    {
        get
        {
            var m = System.Text.RegularExpressions.Regex.Match(Capabilities, @"model\(([^)]*)\)");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
    }

    /// <summary>Read a VCP code with retries. Serialised: this panel aliases values under load.</summary>
    public bool TryGet(byte code, out uint current, out uint maximum, int attempts = 3)
    {
        lock (_gate)
        {
            for (var i = 0; i < attempts; i++)
            {
                uint type;
                if (Native.GetVCPFeatureAndVCPFeatureReply(Handle, code, out type, out current, out maximum))
                    return true;
                Thread.Sleep(70);
            }
            current = 0;
            maximum = 0;
            return false;
        }
    }

    public bool TryGet(byte code, out uint current) => TryGet(code, out current, out _, 3);

    /// <summary>Write a VCP code, then read it back. Retries on mismatch.</summary>
    public bool Set(byte code, uint value, bool verify = true, int attempts = 3)
    {
        lock (_gate)
        {
            for (var i = 0; i < attempts; i++)
            {
                if (!Native.SetVCPFeature(Handle, code, value))
                {
                    Thread.Sleep(140);
                    continue;
                }
                Thread.Sleep(130);
                if (!verify) return true;

                uint current, maximum;
                if (Native.GetVCPFeatureAndVCPFeatureReply(Handle, code, out _, out current, out maximum))
                {
                    // Some codes report a different max after a write; compare the value only.
                    if (current == value) return true;
                }
                Thread.Sleep(160);
            }

            if (!verify) return false;

            // This panel can take a second to settle after a mode switch, and its readback
            // occasionally returns a stale/aliased value. Do a slow final confirmation.
            for (var i = 0; i < 5; i++)
            {
                Thread.Sleep(220);
                uint current, maximum;
                if (Native.GetVCPFeatureAndVCPFeatureReply(Handle, code, out _, out current, out maximum) && current == value)
                    return true;
            }
            return false;
        }
    }

    public bool WaitUntilResponsive(int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (TryGet(0x10, out _, out _, 1)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Native.DestroyPhysicalMonitors((uint)_native.Length, _native); } catch { /* ignore */ }
    }
}

public static class MonitorDiscovery
{
    public static List<PhysicalMonitor> Enumerate()
    {
        var found = new List<PhysicalMonitor>();

        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref Native.RECT rect, IntPtr data) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (!Native.GetMonitorInfo(hMon, ref info)) return true;

            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out var count) || count == 0) return true;
            var phys = new Native.PHYSICAL_MONITOR[count];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, count, phys)) return true;

            var caps = ReadCapabilities(phys[0].hPhysicalMonitor);
            found.Add(new PhysicalMonitor(info.szDevice, phys, caps));
            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static string ReadCapabilities(IntPtr handle)
    {
        var caps = string.Empty;
        for (var attempt = 0; attempt < 3 && caps.Length == 0; attempt++)
        {
            if (Native.GetCapabilitiesStringLength(handle, out var len) && len > 0)
            {
                var sb = new StringBuilder((int)len + 1);
                if (Native.CapabilitiesRequestAndCapabilitiesReply(handle, sb, len))
                    caps = sb.ToString();
            }
            if (caps.Length == 0) Thread.Sleep(250);
        }
        return caps;
    }

    public static PhysicalMonitor? Find(string modelContains)
    {
        var all = Enumerate();
        var match = all.FirstOrDefault(m =>
            (m.Model?.Contains(modelContains, StringComparison.OrdinalIgnoreCase) ?? false) ||
            m.Capabilities.Contains(modelContains, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            foreach (var other in all.Where(m => !ReferenceEquals(m, match))) other.Dispose();
            return match;
        }

        var fallback = all.FirstOrDefault();
        foreach (var other in all.Where(m => !ReferenceEquals(m, fallback))) other.Dispose();
        return fallback;
    }
}

// ---------------------------------------------------------------------------
// Windows HDR (advanced colour) state, per GDI display
// ---------------------------------------------------------------------------

public static class Hdr
{
    private static List<(Native.PATH_INFO path, string? gdi)> Query()
    {
        var result = new List<(Native.PATH_INFO, string?)>();
        if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out var numPath, out var numMode) != 0)
            return result;

        var paths = new Native.PATH_INFO[numPath];
        var modes = new Native.MODE_INFO[numMode];
        if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref numPath, paths, ref numMode, modes, IntPtr.Zero) != 0)
            return result;

        for (var i = 0; i < numPath; i++)
        {
            var name = new Native.SOURCE_DEVICE_NAME();
            name.header.type = 1; // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
            name.header.size = (uint)Marshal.SizeOf<Native.SOURCE_DEVICE_NAME>();
            name.header.adapterId = paths[i].sourceInfo.adapterId;
            name.header.id = paths[i].sourceInfo.id;
            var ok = Native.DisplayConfigGetDeviceInfo(ref name) == 0;
            result.Add((paths[i], ok ? name.viewGdiDeviceName : null));
        }
        return result;
    }

    private static bool TryReadAdvancedColor(Native.PATH_INFO path, out Native.ADVANCED_COLOR_INFO ac)
    {
        ac = new Native.ADVANCED_COLOR_INFO();
        ac.header.type = 9; // DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO
        ac.header.size = (uint)Marshal.SizeOf<Native.ADVANCED_COLOR_INFO>();
        ac.header.adapterId = path.targetInfo.adapterId;
        ac.header.id = path.targetInfo.id;
        return Native.DisplayConfigGetDeviceInfo(ref ac) == 0;
    }

    /// <summary>
    /// Resolve the path for a GDI display. The source name can move during an HDR
    /// reconfiguration, so fall back to the only active target when the lookup misses.
    /// </summary>
    private static Native.PATH_INFO? FindPath(string gdiDevice)
    {
        var paths = Query();

        foreach (var (path, gdi) in paths)
        {
            if (string.Equals(gdi, gdiDevice, StringComparison.OrdinalIgnoreCase) &&
                path.targetInfo.targetAvailable != 0)
                return path;
        }

        var active = paths.Where(p => p.path.targetInfo.targetAvailable != 0).ToList();
        return active.Count == 1 ? active[0].path : null;
    }

    public static bool? IsEnabled(string gdiDevice)
    {
        var path = FindPath(gdiDevice);
        if (path == null) return null;
        if (!TryReadAdvancedColor(path.Value, out var ac)) return null;

        var supported = (ac.value & 1) != 0;
        var enabled = (ac.value & 2) != 0;
        return supported && enabled;
    }

    public static bool Set(string gdiDevice, bool enable)
    {
        var path = FindPath(gdiDevice);
        if (path == null) return false;

        var state = new Native.SET_ADVANCED_COLOR_STATE();
        state.header.type = 10; // DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE
        state.header.size = (uint)Marshal.SizeOf<Native.SET_ADVANCED_COLOR_STATE>();
        state.header.adapterId = path.Value.targetInfo.adapterId;
        state.header.id = path.Value.targetInfo.id;
        state.value = enable ? 1u : 0u;
        return Native.DisplayConfigSetDeviceInfo(ref state) == 0;
    }

    /// <summary>
    /// The Windows SDR-content white level used while HDR is on, in nits.
    /// This is the brightness of ordinary white windows, not the HDR peak.
    /// </summary>
    public static uint? GetSdrWhiteNits(string gdiDevice)
    {
        var path = FindPath(gdiDevice);
        if (path == null) return null;

        var info = new Native.SDR_WHITE_LEVEL();
        info.header.type = 11; // DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL
        info.header.size = (uint)Marshal.SizeOf<Native.SDR_WHITE_LEVEL>();
        info.header.adapterId = path.Value.targetInfo.adapterId;
        info.header.id = path.Value.targetInfo.id;
        if (Native.DisplayConfigGetDeviceInfo(ref info) != 0) return null;

        // The API stores nits as a multiple of 80, multiplied by 1000.
        return (info.SDRWhiteLevel * 80u + 500u) / 1000u;
    }

    /// <summary>
    /// Sets the Windows SDR-content white level. Valid range is 80-480 nits and
    /// the API requires a multiple of 4. This only affects SDR content inside the
    /// HDR container; HDR highlights still use the panel's peak brightness.
    /// </summary>
    public static bool SetSdrWhiteNits(string gdiDevice, uint nits)
    {
        var path = FindPath(gdiDevice);
        if (path == null) return false;

        nits = Math.Clamp(nits, 80u, 480u);
        nits = ((nits + 2u) / 4u) * 4u;

        var info = new Native.SET_SDR_WHITE_LEVEL();
        info.header.type = 0xFFFFFFEE; // DISPLAYCONFIG_DEVICE_INFO_SET_SDR_WHITE_LEVEL
        info.header.size = (uint)Marshal.SizeOf<Native.SET_SDR_WHITE_LEVEL>();
        info.header.adapterId = path.Value.targetInfo.adapterId;
        info.header.id = path.Value.targetInfo.id;
        info.SDRWhiteLevel = nits * 1000u / 80u;
        info.finalValue = 1;
        return Native.DisplayConfigSetDeviceInfo(ref info) == 0;
    }

    public static string Describe(string gdiDevice)
    {
        var sb = new StringBuilder();
        var paths = Query();
        sb.AppendLine($"display paths: {paths.Count}");
        foreach (var (path, gdi) in paths)
        {
            var match = string.Equals(gdi, gdiDevice, StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
            sb.AppendLine($"  source={gdi ?? "(name unavailable)"} target={path.targetInfo.id} " +
                          $"available={path.targetInfo.targetAvailable} match={match}");
            if (TryReadAdvancedColor(path, out var ac))
            {
                sb.AppendLine($"    advancedColor: supported={((ac.value & 1) != 0)} " +
                              $"enabled={((ac.value & 2) != 0)} bits={ac.bitsPerColorChannel} " +
                              $"encoding={ac.colorEncoding} raw=0x{ac.value:X}");
            }
            else
            {
                sb.AppendLine("    advancedColor: query failed");
            }
        }
        return sb.ToString().TrimEnd();
    }
}

// ---------------------------------------------------------------------------
// Display gamma ramp (SDR-in-HDR tone-curve correction)
// ---------------------------------------------------------------------------

/// <summary>
/// Applies an sRGB-to-gamma-2.2 correction to the GPU scanout ramp. Windows 11
/// renders ordinary SDR content inside an HDR container with the piecewise sRGB
/// curve; this ramp darkens the SDR shadows so they follow gamma 2.2 instead.
///
/// This is the AMD curve: output = sRGB-inverse-EOTF(input ^ gamma), with a
/// black level of zero. It is applied per GDI display and is independent of the
/// monitor's DDC controls, so the HDR preset and FALD are untouched.
/// </summary>
public static class GammaRamp
{
    public const double DefaultGamma = 2.2;

    private const int RampSize = 256;
    private const int MaxChannelDelta = 1;
    private const double SrgbLinearCutoff = 0.00313066844250063;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Ramp> Saved = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = RampSize)]
        public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = RampSize)]
        public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = RampSize)]
        public ushort[] Blue;
    }

    private sealed class Ramp
    {
        public ushort[] Red { get; init; } = new ushort[RampSize];
        public ushort[] Green { get; init; } = new ushort[RampSize];
        public ushort[] Blue { get; init; } = new ushort[RampSize];

        public Ramp Clone() => new()
        {
            Red = (ushort[])Red.Clone(),
            Green = (ushort[])Green.Clone(),
            Blue = (ushort[])Blue.Clone()
        };
    }

    private static class Gdi32
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateDC(string? driver, string? device, string? port, IntPtr pdm);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool SetDeviceGammaRamp(IntPtr hdc, ref NativeRamp ramp);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool GetDeviceGammaRamp(IntPtr hdc, ref NativeRamp ramp);
    }

    private sealed class DisplayDc : IDisposable
    {
        public IntPtr Handle { get; }

        private DisplayDc(IntPtr handle) => Handle = handle;

        public static DisplayDc? Open(string gdiDevice)
        {
            var hdc = Gdi32.CreateDC("DISPLAY", gdiDevice, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                hdc = Gdi32.CreateDC("DISPLAY", null, null, IntPtr.Zero);
            return hdc == IntPtr.Zero ? null : new DisplayDc(hdc);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) Gdi32.DeleteDC(Handle);
        }
    }

    /// <summary>
    /// Apply the correction to the scanout ramp. The current ramp is saved once
    /// per display so it can be restored when the fix is turned off. If the
    /// current ramp already looks like our curve (for example after a crash),
    /// the saved original is treated as the identity ramp.
    /// </summary>
    public static bool Apply(string gdiDevice, double gamma = DefaultGamma)
    {
        if (string.IsNullOrWhiteSpace(gdiDevice)) return false;

        lock (Gate)
        {
            using var dc = DisplayDc.Open(gdiDevice);
            if (dc == null) return false;

            var current = Read(dc.Handle);
            if (current == null) return false;

            var curve = BuildCurve(gamma);
            if (!Saved.ContainsKey(gdiDevice))
                Saved[gdiDevice] = RampsEqual(current, curve) ? Identity() : current.Clone();

            if (RampsEqual(current, curve)) return true;

            if (!Write(dc.Handle, curve)) return false;
            var readback = Read(dc.Handle);
            return readback != null && RampsEqual(readback, curve);
        }
    }

    /// <summary>
    /// Put the saved ramp back, but only if the current ramp is still our
    /// correction. This avoids overwriting a custom SDR calibration that Windows
    /// or the user has already restored after leaving HDR.
    /// </summary>
    public static bool Restore(string gdiDevice, double gamma = DefaultGamma)
    {
        if (string.IsNullOrWhiteSpace(gdiDevice)) return false;

        lock (Gate)
        {
            using var dc = DisplayDc.Open(gdiDevice);
            if (dc == null) return false;

            var current = Read(dc.Handle);
            if (current == null) return false;

            var curve = BuildCurve(gamma);
            if (!RampsEqual(current, curve))
            {
                // Windows or a colour profile already replaced our ramp. The
                // saved original is stale now; capture a fresh one next time.
                Saved.Remove(gdiDevice);
                return true;
            }

            var target = Saved.TryGetValue(gdiDevice, out var saved) ? saved : Identity();
            if (!Write(dc.Handle, target)) return false;
            var readback = Read(dc.Handle);
            if (readback != null && !RampsEqual(readback, target)) return false;

            Saved.Remove(gdiDevice);
            return true;
        }
    }

    /// <summary>True when our correction is the current scanout ramp.</summary>
    public static bool? IsApplied(string gdiDevice, double gamma = DefaultGamma)
    {
        if (string.IsNullOrWhiteSpace(gdiDevice)) return null;

        lock (Gate)
        {
            using var dc = DisplayDc.Open(gdiDevice);
            if (dc == null) return null;

            var current = Read(dc.Handle);
            if (current == null) return null;
            return RampsEqual(current, BuildCurve(gamma));
        }
    }

    public static string Describe(string gdiDevice, double gamma = DefaultGamma)
    {
        return IsApplied(gdiDevice, gamma) switch
        {
            null => "unreadable",
            true => "gamma 2.2 applied",
            false => "not applied"
        };
    }

    private static Ramp BuildCurve(double gamma)
    {
        if (double.IsNaN(gamma) || double.IsInfinity(gamma) || gamma <= 0)
            gamma = DefaultGamma;
        gamma = Math.Clamp(gamma, 1.0, 4.0);

        var ramp = new Ramp();
        for (var i = 0; i < RampSize; i++)
        {
            var input = i / (double)(RampSize - 1);
            var output = i == 0 ? 0.0 : SrgbInvEotf(Math.Pow(input, gamma));
            var value = (ushort)Math.Round(Math.Clamp(output, 0.0, 1.0) * 65535.0);
            ramp.Red[i] = value;
            ramp.Green[i] = value;
            ramp.Blue[i] = value;
        }
        return ramp;
    }

    private static double SrgbInvEotf(double linear)
    {
        return linear <= SrgbLinearCutoff
            ? linear * 12.92
            : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
    }

    private static Ramp Identity()
    {
        var ramp = new Ramp();
        for (var i = 0; i < RampSize; i++)
        {
            var value = (ushort)(i * 257);
            ramp.Red[i] = value;
            ramp.Green[i] = value;
            ramp.Blue[i] = value;
        }
        return ramp;
    }

    private static Ramp? Read(IntPtr hdc)
    {
        var native = NewNativeRamp();
        if (!Gdi32.GetDeviceGammaRamp(hdc, ref native)) return null;
        return new Ramp { Red = native.Red, Green = native.Green, Blue = native.Blue };
    }

    private static bool Write(IntPtr hdc, Ramp ramp)
    {
        var native = new NativeRamp
        {
            Red = ramp.Red,
            Green = ramp.Green,
            Blue = ramp.Blue
        };
        return Gdi32.SetDeviceGammaRamp(hdc, ref native);
    }

    private static NativeRamp NewNativeRamp() => new()
    {
        Red = new ushort[RampSize],
        Green = new ushort[RampSize],
        Blue = new ushort[RampSize]
    };

    private static bool RampsEqual(Ramp a, Ramp b)
    {
        return ChannelEquals(a.Red, b.Red) &&
               ChannelEquals(a.Green, b.Green) &&
               ChannelEquals(a.Blue, b.Blue);
    }

    private static bool ChannelEquals(ushort[] a, ushort[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > MaxChannelDelta) return false;
        return true;
    }
}

// ---------------------------------------------------------------------------
// Config
// ---------------------------------------------------------------------------

public sealed class Config
{
    public string ModelMatch { get; set; } = "XV275K";

    // 0xE2 is the vendor display-mode setter. It is write-only (reading it returns 0)
    // and is not advertised in the capability string, but the panel accepts it and
    // runs the matching preset. 0x14 only reflects the applied mode.
    public byte DisplayModeVcp { get; set; } = 0xE2;
    public uint HdrDisplayMode { get; set; } = 11;
    public uint SdrDisplayMode { get; set; } = 0;   // User preset; Standard is 1
    public uint HdrPictureMode { get; set; } = 6;   // 0x14 reflection after 0xE2=11
    // 0x14 does not report the User preset: it stays at 5 or 6. Leave this null to
    // accept the write without a reflection check. Set it to 5 if you switch
    // sdrDisplayMode back to Standard (1).
    public uint? SdrPictureMode { get; set; } = null;

    public uint HdrBrightness { get; set; } = 65;
    public uint HdrContrast { get; set; } = 50;
    public uint SdrBrightness { get; set; } = 40;
    public uint SdrContrast { get; set; } = 50;

    // Windows SDR-content white level while HDR is on, in nits (80-480, multiple of 4).
    // This is what makes ordinary white windows comfortable without crushing HDR highlights.
    public uint SdrWhiteNits { get; set; } = 120;

    // Correct the SDR-in-HDR piecewise-sRGB curve to gamma 2.2 on the GPU scanout
    // ramp. Only active while Windows HDR is on; the original ramp is restored when
    // the fix or HDR turns off.
    public bool GammaFix { get; set; } = false;
    public double GammaPower { get; set; } = GammaRamp.DefaultGamma;

    // Preferred monitor input (VCP 0x60). 0x0F is DisplayPort and matches this
    // panel's readback when the PC is connected over DP.
    public uint SelectedInput { get; set; } = MonitorInputs.DisplayPort;

    // App-managed "auto switch": re-select SelectedInput after startup and after
    // a display wake. The panel's own Auto Source setting is OSD-only.
    public bool AutoSwitchInput { get; set; } = false;

    // "dark" or "light". Dark is the default for new installs; existing configs
    // without the field also start dark.
    public string Theme { get; set; } = "dark";

    public int PollMs { get; set; } = 1000;
    public int SettleMs { get; set; } = 3000;
    public int PreSettleMs { get; set; } = 1200;
    public int UserSettleMs { get; set; } = 1200;   // wait after 0xE2=0 before the next mode write

    public static Config Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "faldsync.json");
        if (!File.Exists(path)) return new Config();
        try
        {
            var cfg = System.Text.Json.JsonSerializer.Deserialize<Config>(
                File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return cfg ?? new Config();
        }
        catch (Exception ex)
        {
            Log.Warn($"could not read {path}: {ex.Message} - using defaults");
            return new Config();
        }
    }

    /// <summary>Persist the current values next to the exe. Used by the UI for slider persistence.</summary>
    public void Save()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "faldsync.json");
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Log.Warn($"could not save {path}: {ex.Message}");
        }
    }
}

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------

internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "faldsync.log");

    public static void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
    public static void Ok(string message) => Write(" OK ", message, ConsoleColor.Green);
    public static void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
    public static void Error(string message) => Write("ERR ", message, ConsoleColor.Red);

    private static void Write(string level, string message, ConsoleColor color)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {level} {message}";
        lock (Gate)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = previous;
            try { File.AppendAllText(FilePath, line + Environment.NewLine); } catch { /* ignore */ }
        }
    }
}

// ---------------------------------------------------------------------------
// Power notifications: display on/off, system suspend/resume
// ---------------------------------------------------------------------------

public sealed class PowerMonitor : IDisposable
{
    private const uint WM_POWERBROADCAST = 0x0218;
    private const uint WM_CLOSE = 0x0010;
    private const int PBT_APMSUSPEND = 0x0004;
    private const int PBT_APMRESUMESUSPEND = 0x0007;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    private static readonly Guid GuidConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    private static readonly Guid GuidMonitorPowerOn = new("02731015-4510-4526-99E6-E5A17EBD1AEA");

    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private WndProcDelegate? _wndProc;
    private IntPtr _hwnd;
    private IntPtr _displayNotify;
    private IntPtr _monitorNotify;
    private int _wake;
    private volatile bool _displayOn = true;

    public bool DisplayOn => _displayOn;

    public void Start()
    {
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "FaldSync.PowerMonitor"
        };
        _thread.Start();

        if (!_ready.Wait(3000) || _hwnd == IntPtr.Zero)
            throw new InvalidOperationException("power notification window was not created");
    }

    /// <summary>Returns true once when the display or system has just resumed.</summary>
    public bool ConsumeWake() => Interlocked.Exchange(ref _wake, 0) != 0;

    private void SignalWake() => Interlocked.Exchange(ref _wake, 1);

    private void MessageLoop()
    {
        try
        {
            _wndProc = WndProc;
            var className = "FaldSyncPower_" + Guid.NewGuid().ToString("N");
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = className
            };

            if (RegisterClassEx(ref wc) == 0)
            {
                Log.Warn("power monitor: RegisterClassEx failed");
                _ready.Set();
                return;
            }

            _hwnd = CreateWindowEx(0, className, "FaldSyncPower", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                Log.Warn("power monitor: CreateWindowEx failed");
                _ready.Set();
                return;
            }

            var displayGuid = GuidConsoleDisplayState;
            _displayNotify = RegisterPowerSettingNotification(_hwnd, ref displayGuid, DEVICE_NOTIFY_WINDOW_HANDLE);
            if (_displayNotify == IntPtr.Zero)
                Log.Warn("power monitor: display-state registration failed");

            var monitorGuid = GuidMonitorPowerOn;
            _monitorNotify = RegisterPowerSettingNotification(_hwnd, ref monitorGuid, DEVICE_NOTIFY_WINDOW_HANDLE);
            if (_monitorNotify == IntPtr.Zero)
                Log.Warn("power monitor: monitor-power registration failed");

            _ready.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"power monitor: {ex.Message}");
            _ready.Set();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_POWERBROADCAST)
        {
            var evt = (int)wParam.ToInt64();

            if (evt == PBT_POWERSETTINGCHANGE)
            {
                var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
                if (setting.PowerSetting == GuidConsoleDisplayState)
                {
                    var state = setting.DataLength >= 4 ? Marshal.ReadInt32(lParam, 20) : setting.Data;
                    _displayOn = state != 0;
                    if (state == 1) SignalWake();
                }
                else if (setting.PowerSetting == GuidMonitorPowerOn)
                {
                    var state = setting.DataLength >= 4 ? Marshal.ReadInt32(lParam, 20) : setting.Data;
                    _displayOn = state != 0;
                    if (state != 0) SignalWake();
                }
                return IntPtr.Zero;
            }

            if (evt is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND)
            {
                SignalWake();
                return IntPtr.Zero;
            }

            if (evt == PBT_APMSUSPEND)
                return IntPtr.Zero;
        }

        if (msg == WM_CLOSE)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        try
        {
            if (_displayNotify != IntPtr.Zero) UnregisterPowerSettingNotification(_displayNotify);
            if (_monitorNotify != IntPtr.Zero) UnregisterPowerSettingNotification(_monitorNotify);
            if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* ignore */ }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}

// ---------------------------------------------------------------------------
// The engine: keep the monitor's picture mode in sync with Windows HDR
// ---------------------------------------------------------------------------

/// <summary>
/// Input-source values exposed by the XV275K P5 capability string:
/// 60(11 12 0F 10). The manual lists HDMI 1, HDMI 2, DP and DP ALT, and a
/// read of 0x60 returned 0x0F while the PC was on DisplayPort.
/// </summary>
public static class MonitorInputs
{
    public const uint DisplayPort = 0x0F;
    public const uint Hdmi1 = 0x11;
    public const uint Hdmi2 = 0x12;
    public const uint DpAlt = 0x10;

    public static readonly (uint Value, string Short, string Name)[] All =
    {
        (DisplayPort, "DP", "DisplayPort"),
        (Hdmi1, "HDMI 1", "HDMI 1"),
        (Hdmi2, "HDMI 2", "HDMI 2"),
        (DpAlt, "DP ALT", "DP ALT (USB-C)")
    };

    public static string ShortName(uint value)
    {
        foreach (var input in All)
            if (input.Value == value) return input.Short;
        return $"0x{value:X2}";
    }

    public static string Name(uint value)
    {
        foreach (var input in All)
            if (input.Value == value) return input.Name;
        return $"input 0x{value:X2}";
    }

    public static bool TryParse(string text, out uint value)
    {
        var cleaned = text.Trim();
        foreach (var (candidate, _, _) in All)
        {
            if (string.Equals(cleaned, ShortName(candidate), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cleaned, Name(candidate), StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        var hex = cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? cleaned[2..] : cleaned;
        if (hex.All(Uri.IsHexDigit) && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value))
            return true;

        value = 0;
        return false;
    }
}

public sealed class FaldEngine
{
    private PhysicalMonitor _monitor;
    private readonly Config _config;

    public FaldEngine(PhysicalMonitor monitor, Config config)
    {
        _monitor = monitor;
        _config = config;
    }

    /// <summary>The current DDC handle. Changes after a wake/re-open, so read it each time.</summary>
    public PhysicalMonitor Monitor => _monitor;

    public Config Configuration => _config;

    public void PrintState()
    {
        var hdr = Hdr.IsEnabled(_monitor.GdiDevice);

        Console.WriteLine($"monitor    : {_monitor.Model ?? "unknown"}  ({_monitor.GdiDevice})");
        Console.WriteLine($"descriptor : {_monitor.Description}");
        Console.WriteLine($"windows HDR: {(hdr == null ? "unknown" : hdr.Value ? "ON" : "OFF")}");
        var sdrWhite = Hdr.GetSdrWhiteNits(_monitor.GdiDevice);
        Console.WriteLine($"SDR white  : {(sdrWhite == null ? "unknown" : $"{sdrWhite} nits (Windows SDR content brightness)")}");
        Console.WriteLine($"gamma fix  : {(_config.GammaFix ? "enabled" : "disabled")}, ramp={GammaRamp.Describe(_monitor.GdiDevice, _config.GammaPower)}");
        Console.WriteLine($"caps model : {_monitor.Model ?? "(no capability string)"}");
        Console.WriteLine();
        PrintVcp("brightness", 0x10);
        PrintVcp("contrast", 0x12);
        PrintVcp("picture mode", 0x14);
        Console.WriteLine($"  0x{_config.DisplayModeVcp:X2} display mode : write-only (HDR={_config.HdrDisplayMode}, SDR/User={_config.SdrDisplayMode})");
        PrintVcp("overdrive", 0xE1);
        PrintVcp("black boost", 0xE5);
        if (_monitor.TryGet(0x60, out var input))
            Console.WriteLine($"  0x60 input source  : {MonitorInputs.Name(input)} (0x{input:X2})");
        else
            Console.WriteLine("  0x60 input source  : no reply");
        PrintVcp("volume", 0x62);
        Console.WriteLine();

        uint mode;
        var hasMode = _monitor.TryGet(0x14, out mode);
        var fald = hdr == true && hasMode && mode == _config.HdrPictureMode;
        Console.WriteLine($"FALD gate  : {(fald ? $"HDR signal + HDR mode (0x14={_config.HdrPictureMode}, set via 0x{_config.DisplayModeVcp:X2}={_config.HdrDisplayMode})" : "NOT ARMED")}");
        if (hdr == true && hasMode && mode != _config.HdrPictureMode)
            Console.WriteLine("             HDR signal is on but the monitor is in SDR picture mode - this is the washed-out state.");
    }

    private void PrintVcp(string name, byte code)
    {
        if (_monitor.TryGet(code, out var current, out var maximum))
            Console.WriteLine($"  0x{code:X2} {name,-14}: {current} (max {maximum})");
        else
            Console.WriteLine($"  0x{code:X2} {name,-14}: no reply");
    }

    public void Sync()
    {
        var hdr = Hdr.IsEnabled(_monitor.GdiDevice);
        if (hdr == null)
        {
            Log.Error("could not read the Windows HDR state for this display");
            return;
        }

        if (hdr.Value) ApplyHdrOnWithRetry();
        else ApplyHdrOffWithRetry();
    }

    public bool ApplyHdrOn()
    {
        Log.Info("HDR signal is ON - forcing the HDR preset (User -> HDR)");

        if (!_monitor.WaitUntilResponsive(8000))
            Log.Warn("monitor did not answer DDC after the HDR switch; trying anyway");

        // Let the link finish re-syncing before touching the mode. A write during the
        // black screen is accepted but the panel may not run its HDR preset (FALD stays off).
        if (_config.PreSettleMs > 0) Thread.Sleep(_config.PreSettleMs);

        // The XV275K P5 has a firmware bug where the first HDR entry leaves FALD off.
        // The documented fix is to change the picture mode away from HDR and back.
        // Do that with the real setter (0xE2), while the HDR signal is already live.
        // Use the monitor's User preset rather than Standard: the User preset keeps the
        // user's own SDR colour/brightness settings, so the brief intermediate mode is
        // not the washed-out Standard preset.
        var userOk = SetDisplayMode(_config.SdrDisplayMode, _config.SdrPictureMode);
        if (!userOk)
        {
            Log.Warn("panel did not accept the User mode; skipping this attempt");
            return false;
        }

        // When sdrPictureMode is null, SetDisplayMode already waited UserSettleMs.
        // Keep the older guard for a Standard/custom mode that does report 0x14.
        if (_config.SdrPictureMode is not null) Thread.Sleep(1000);

        var hdrOk = SetHdrDisplayMode();
        if (!hdrOk)
        {
            Log.Warn("panel did not accept the HDR mode; skipping brightness this attempt");
            return false;
        }

        // The HDR preset forces brightness to 100. Let it settle, then re-apply the user's values.
        Thread.Sleep(_config.SettleMs);

        var brightnessOk = ApplyVcp(0x10, _config.HdrBrightness, "brightness");
        var contrastOk = ApplyVcp(0x12, _config.HdrContrast, "contrast");

        if (!Hdr.SetSdrWhiteNits(_monitor.GdiDevice, _config.SdrWhiteNits))
            Log.Warn($"could not set the Windows SDR white level to {_config.SdrWhiteNits} nits");
        else
            Log.Ok($"Windows SDR white = {_config.SdrWhiteNits} nits");

        // The gamma ramp is independent of the monitor's DDC controls. Apply it last
        // so the SDR white change above cannot reset it.
        if (_config.GammaFix)
        {
            if (GammaRamp.Apply(_monitor.GdiDevice, _config.GammaPower))
                Log.Ok($"SDR gamma {_config.GammaPower:0.0} ramp applied");
            else
                Log.Warn("could not apply the SDR gamma ramp");
        }
        else
        {
            GammaRamp.Restore(_monitor.GdiDevice, _config.GammaPower);
        }

        return brightnessOk && contrastOk;
    }

    public bool ApplyHdrOnWithRetry()
    {
        if (ApplyHdrOn()) return true;

        Log.Warn("HDR apply was incomplete; retrying once after a short settle");
        Thread.Sleep(2000);
        return ApplyHdrOn();
    }

    public bool ApplyHdrOffWithRetry()
    {
        if (ApplyHdrOff()) return true;

        Log.Warn("SDR apply was incomplete; retrying once after a short settle");
        Thread.Sleep(2000);
        return ApplyHdrOff();
    }

    /// <summary>
    /// Selects a vendor display mode by writing 0xE2. HDR/Standard are confirmed through
    /// 0x14; the User preset (0xE2=0) has no distinct 0x14 value, so it is accepted after
    /// the write and a short settle. Writing the same 0xE2 value again is a valid re-apply
    /// after a signal switch.
    /// </summary>
    private bool SetDisplayMode(uint mode, uint? expectedReflection)
    {
        var code = _config.DisplayModeVcp;
        Log.Info($"setting display mode 0x{code:X2}={mode}");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                Log.Warn($"display mode 0x{code:X2}={mode} not confirmed; retrying once");
                Thread.Sleep(500);
            }

            if (!_monitor.Set(code, mode, verify: false))
                continue;

            // User (0xE2=0) has no distinct 0x14 reflection. The panel leaves 0x14 at 5
            // or 6, so polling for a value is pointless. The write is accepted; give the
            // preset time to run before the caller writes HDR or brightness.
            if (expectedReflection is null)
            {
                Thread.Sleep(Math.Max(500, _config.UserSettleMs));
                Log.Ok($"display mode 0x{code:X2}={mode} sent (User preset; 0x14 has no distinct value)");
                return true;
            }

            // 0xE2 reads back as 0, so poll the reflection until the panel has applied the preset.
            // 3 s is deliberately longer than the old 2 s: 0x14 can lag a mode switch.
            for (var i = 0; i < 12; i++)
            {
                Thread.Sleep(250);
                if (_monitor.TryGet(0x14, out var reflected) && reflected == expectedReflection.Value)
                {
                    Log.Ok($"display mode 0x{code:X2}={mode} applied (0x14={reflected})");
                    return true;
                }
            }
        }

        if (expectedReflection is null)
            Log.Warn($"display mode 0x{code:X2}={mode} could not be written");
        else
            Log.Warn($"display mode 0x{code:X2}={mode} sent, but 0x14 did not reflect {expectedReflection}");
        return false;
    }

    /// <summary>
    /// Writes the HDR display mode and confirms it without relying only on 0x14.
    /// On the XV275K P5, 0x14 can stay at 5 even when the OSD already says HDR.
    /// The HDR preset also forces brightness to 100, so a brightness jump is used as
    /// a second confirmation. This still catches the post-wake case where the panel
    /// answers reads but silently ignores the mode write (brightness stays at SDR 40).
    /// </summary>
    private bool SetHdrDisplayMode()
    {
        var code = _config.DisplayModeVcp;
        Log.Info($"setting display mode 0x{code:X2}={_config.HdrDisplayMode}");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                Log.Warn($"display mode 0x{code:X2}={_config.HdrDisplayMode} not confirmed; retrying once");
                Thread.Sleep(500);
            }

            if (!_monitor.Set(code, _config.HdrDisplayMode, verify: false))
                continue;

            for (var i = 0; i < 12; i++)
            {
                Thread.Sleep(250);

                var hasMode = _monitor.TryGet(0x14, out var reflected);
                if (hasMode && reflected == _config.HdrPictureMode)
                {
                    Log.Ok($"display mode 0x{code:X2}={_config.HdrDisplayMode} applied (0x14={reflected})");
                    return true;
                }

                if (_monitor.TryGet(0x10, out var brightness, out _) && brightness >= 95)
                {
                    var modeText = hasMode ? reflected.ToString() : "?";
                    Log.Ok($"display mode 0x{code:X2}={_config.HdrDisplayMode} applied " +
                           $"(brightness={brightness}, 0x14={modeText})");
                    return true;
                }
            }
        }

        Log.Warn($"display mode 0x{code:X2}={_config.HdrDisplayMode} sent, " +
                 "but neither 0x14 nor the HDR brightness jump confirmed it");
        return false;
    }

    private bool ApplyVcp(byte code, uint value, string label)
    {
        if (_monitor.Set(code, value))
        {
            Log.Ok($"{label} = {value}");
            return true;
        }

        // A false negative here is common when the panel is still re-syncing. Re-read once more.
        Thread.Sleep(500);
        if (_monitor.TryGet(code, out var after) && after == value)
        {
            Log.Ok($"{label} = {value} (confirmed late)");
            return true;
        }

        Log.Warn($"{label} write did not verify");
        return false;
    }

    public bool ApplyHdrOff()
    {
        Log.Info("HDR signal is OFF - restoring the User display mode");

        // Do this before any DDC work so a failed mode write cannot leave the
        // SDR desktop with the HDR-only gamma correction still active.
        GammaRamp.Restore(_monitor.GdiDevice, _config.GammaPower);

        if (!_monitor.WaitUntilResponsive(8000))
            Log.Warn("monitor did not answer DDC after the HDR switch; trying anyway");

        var modeOk = SetDisplayMode(_config.SdrDisplayMode, _config.SdrPictureMode);
        if (!modeOk)
        {
            Log.Warn("panel did not accept the User mode; skipping this attempt");
            return false;
        }

        // Let the User preset finish before overriding its brightness.
        Thread.Sleep(Math.Max(600, _config.SettleMs / 3));

        var brightnessOk = ApplyVcp(0x10, _config.SdrBrightness, "brightness");
        var contrastOk = ApplyVcp(0x12, _config.SdrContrast, "contrast");

        return brightnessOk && contrastOk;
    }

    public bool SetBrightness(uint value)
    {
        if (value > 100) throw new ArgumentOutOfRangeException(nameof(value), "brightness is 0-100");
        return ApplyVcp(0x10, value, "brightness");
    }

    public bool SetContrast(uint value)
    {
        if (value > 100) throw new ArgumentOutOfRangeException(nameof(value), "contrast is 0-100");
        return ApplyVcp(0x12, value, "contrast");
    }

    public bool SetVolume(uint value)
    {
        if (value > 100) throw new ArgumentOutOfRangeException(nameof(value), "volume is 0-100");
        return ApplyVcp(0x62, value, "volume");
    }

    /// <summary>
    /// Switches the monitor input. Verification is skipped on purpose: the moment
    /// the panel changes input, DDC/CI from this PC usually stops answering until
    /// the same input is active again, so a read-back would report a false failure.
    /// </summary>
    public bool SetInputSource(uint value)
    {
        if (_monitor.Set(0x60, value, verify: false))
        {
            Log.Ok($"input source = {MonitorInputs.Name(value)} (0x{value:X2})");
            return true;
        }

        // The handle goes stale whenever the panel is re-enumerated, and coming
        // back from another input is exactly that case. Re-acquire once and retry
        // before reporting a failure, otherwise a single stale handle looks like a
        // monitor that refuses to switch back.
        if (EnsureConnected("input switch", force: true) &&
            _monitor.Set(0x60, value, verify: false))
        {
            Log.Ok($"input source = {MonitorInputs.Name(value)} (0x{value:X2}) after reconnect");
            return true;
        }

        Log.Warn($"input source 0x{value:X2} write failed");
        return false;
    }

    /// <summary>
    /// Re-selects the preferred input only when the panel reports a different one.
    /// A failed read is left alone rather than blind-writing: if the monitor is on
    /// an input owned by another device, a write from here may never arrive.
    /// </summary>
    public bool EnsureInputSource(uint value, string reason)
    {
        if (!_monitor.TryGet(0x60, out var current))
        {
            // One reconnect attempt: the read may be failing only because the
            // handle is stale after an OSD input change.
            if (!EnsureConnected(reason, force: true) || !_monitor.TryGet(0x60, out current))
            {
                Log.Warn($"{reason}: could not read the input source; leaving it alone");
                return false;
            }
        }

        if (current == value)
        {
            Log.Info($"{reason}: input is already {MonitorInputs.Name(value)}");
            return true;
        }

        Log.Info($"{reason}: input {MonitorInputs.Name(current)} -> {MonitorInputs.Name(value)}");
        return SetInputSource(value);
    }

    public void Watch()
    {
        Log.Info("watching Windows HDR state - Ctrl+C to stop");
        Console.CancelKeyPress += (_, _) =>
            GammaRamp.Restore(_monitor.GdiDevice, _config.GammaPower);
        PrintState();

        PowerMonitor? power = null;
        try
        {
            power = new PowerMonitor();
            power.Start();
            Log.Info("power notifications active (display off/on, suspend/resume)");
        }
        catch (Exception ex)
        {
            Log.Warn($"power notifications unavailable: {ex.Message}");
            power = null;
        }

        bool? last = null;
        var warnedNull = false;
        var ticks = 0;

        var hdr = Hdr.IsEnabled(_monitor.GdiDevice);

        // On startup always re-apply the HDR display mode when HDR is on. A readback of
        // 0x14=6 does not prove that FALD was latched after a previous signal switch;
        // the forced User -> HDR transition makes the firmware re-run its HDR preset.
        if (hdr == true)
        {
            ApplyHdrOnWithRetry();
            last = true;
        }
        else if (hdr == false)
        {
            Log.Info("HDR is OFF at startup - leaving the SDR picture settings alone");
            last = false;
        }
        else
        {
            Log.Warn("HDR state is unreadable at startup - will keep polling");
        }

        // Discard the initial state notification that registration can deliver.
        power?.ConsumeWake();

        var lastLoopUtc = DateTime.UtcNow;
        long? ddcDownSince = null;

        while (true)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var gap = nowUtc - lastLoopUtc;
                lastLoopUtc = nowUtc;

                var current = Hdr.IsEnabled(_monitor.GdiDevice);

                if (power?.ConsumeWake() == true)
                {
                    Log.Info("power resume notification - re-checking HDR state");
                    ReapplyAfterWake(current, "power resume");
                    power?.ConsumeWake();
                    last = current;
                    ddcDownSince = null;
                    lastLoopUtc = DateTime.UtcNow;
                    Thread.Sleep(Math.Max(250, _config.PollMs));
                    continue;
                }

                // System sleep/hibernate stops this loop completely. When it resumes, the
                // wall clock has advanced by far more than one poll interval.
                if (gap > TimeSpan.FromSeconds(30))
                {
                    Log.Info($"system resumed after {gap.TotalSeconds:F0}s");
                    ReapplyAfterWake(current, "system resume");
                    power?.ConsumeWake();
                    last = current;
                    ddcDownSince = null;
                    lastLoopUtc = DateTime.UtcNow;
                    Thread.Sleep(Math.Max(250, _config.PollMs));
                    continue;
                }

                // Monitor sleep: DDC stops answering while the panel is off. Short dropouts
                // also happen during an HDR mode switch, so require at least 5 s of downtime.
                var responsive = _monitor.TryGet(0x10, out _, out _, 1);
                var nowTick = Environment.TickCount64;
                if (!responsive)
                {
                    ddcDownSince ??= nowTick;

                    // A stale handle never comes back on its own: after an OSD input
                    // change or a driver re-enumeration every call fails forever. Try a
                    // quiet re-acquire so the watcher survives that too, not just sleep.
                    if (nowTick - ddcDownSince.Value >= 15000)
                        responsive = EnsureConnected("watch poll", minIntervalMs: 30000, quiet: true);
                }

                if (responsive && ddcDownSince is { } downSince)
                {
                    var downMs = nowTick - downSince;
                    ddcDownSince = null;
                    if (downMs >= 5000)
                    {
                        Log.Info($"display woke after {downMs / 1000.0:F1}s");
                        ReapplyAfterWake(current, "display wake");
                        power?.ConsumeWake();
                        last = current;
                        lastLoopUtc = DateTime.UtcNow;
                        Thread.Sleep(Math.Max(250, _config.PollMs));
                        continue;
                    }
                }

                if (current == null)
                {
                    if (!warnedNull)
                    {
                        Log.Warn("HDR state unreadable; will keep polling (run 'diag' if this persists)");
                        warnedNull = true;
                    }
                }
                else
                {
                    warnedNull = false;
                    if (current != last)
                    {
                        Log.Info($"HDR edge: {(last == true ? "ON" : "OFF")} -> {(current.Value ? "ON" : "OFF")}");
                        if (current.Value) ApplyHdrOnWithRetry();
                        else ApplyHdrOffWithRetry();
                        power?.ConsumeWake();
                        last = current;

                        // The HDR transition itself makes DDC unresponsive for a few seconds.
                        // Do not let that look like a monitor wake on the next poll.
                        ddcDownSince = null;
                        lastLoopUtc = DateTime.UtcNow;
                    }
                }

                if (++ticks % 30 == 0)
                {
                    var hasMode = _monitor.TryGet(0x14, out var mode);
                    Log.Info($"heartbeat: hdr={(current?.ToString() ?? "null")} mode={(hasMode ? mode.ToString() : "?")}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"watch loop: {ex.Message}");
            }
            Thread.Sleep(Math.Max(250, _config.PollMs));
        }
    }

    public bool ReopenMonitor(bool quiet = false)
    {
        try
        {
            var fresh = MonitorDiscovery.Find(_config.ModelMatch);
            if (fresh == null)
            {
                if (!quiet) Log.Warn("re-open: no matching monitor is present right now");
                return false;
            }

            // Find() falls back to the first DDC monitor when the configured model
            // is not present. Re-binding to an unrelated screen would send every
            // later write to the wrong display, so only accept the same identity
            // we were already driving (or a real match for the configured model).
            var wanted = _config.ModelMatch;
            var expected = _monitor.Model;
            var sameAsBefore = expected != null &&
                               string.Equals(fresh.Model, expected, StringComparison.OrdinalIgnoreCase);
            var sameDevice = string.Equals(fresh.GdiDevice, _monitor.GdiDevice, StringComparison.OrdinalIgnoreCase);
            var matchesConfig =
                (!string.IsNullOrWhiteSpace(wanted) &&
                 (fresh.Model?.Contains(wanted, StringComparison.OrdinalIgnoreCase) ?? false)) ||
                (!string.IsNullOrWhiteSpace(wanted) &&
                 fresh.Capabilities.Contains(wanted, StringComparison.OrdinalIgnoreCase));

            if (!sameAsBefore && !matchesConfig && !sameDevice)
            {
                if (!quiet)
                {
                    Log.Warn(
                        $"re-open: {fresh.Model ?? fresh.Description} does not match " +
                        $"{expected ?? wanted}; not binding to it");
                }
                fresh.Dispose();
                return false;
            }

            var old = _monitor;
            _monitor = fresh;
            old.Dispose();
            Log.Info($"monitor handle re-acquired: {_monitor.GdiDevice} ({_monitor.Description})");
            return true;
        }
        catch (Exception ex)
        {
            if (!quiet) Log.Warn($"could not re-acquire the monitor: {ex.Message}");
            return false;
        }
    }

    private long _lastReopenTicks;

    /// <summary>
    /// Ping the panel and replace the DDC handle when it has stopped answering.
    /// Windows recreates the physical-monitor handle when the display is
    /// re-enumerated (input change in the OSD, cable, driver reset); the old handle
    /// then fails every call until it is swapped out. That stale handle is why the
    /// app used to need a restart after the panel came back from another input.
    /// Throttled so a panel that is simply showing another source is not
    /// re-enumerated on every poll.
    /// </summary>
    public bool EnsureConnected(string reason, int minIntervalMs = 2500, bool force = false, bool quiet = false)
    {
        if (_monitor.TryGet(0x10, out _, out _, 1)) return true;

        var now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastReopenTicks) < minIntervalMs)
            return false;

        Interlocked.Exchange(ref _lastReopenTicks, now);
        if (!quiet) Log.Warn($"{reason}: the panel stopped answering DDC; re-acquiring the handle");

        if (!ReopenMonitor(quiet))
        {
            if (!quiet) Log.Warn($"{reason}: no matching monitor is present right now");
            return false;
        }

        if (!_monitor.TryGet(0x10, out _, out _, 2))
        {
            if (!quiet) Log.Warn($"{reason}: re-acquired {_monitor.GdiDevice} but DDC still does not answer");
            return false;
        }

        Log.Ok($"{reason}: DDC reconnected on {_monitor.GdiDevice}");
        return true;
    }

    public bool ReapplyAfterWake(bool? hdr, string reason)
    {
        if (hdr == null)
        {
            Log.Warn($"{reason}: HDR state unreadable; will retry on the next poll");
            return false;
        }

        // Display sleep invalidates the physical-monitor handle: reads keep "succeeding"
        // with no data. Re-acquire it before doing anything else.
        Log.Info($"{reason}: re-acquiring the monitor and waiting for the panel");
        if (!ReopenMonitor())
            Log.Warn($"{reason}: could not re-acquire the monitor; using the existing handle");

        // The power notification can arrive before the panel's DDC and mode engine are
        // ready. Writing a picture mode in that window is accepted but not applied.
        Thread.Sleep(3000);

        // Some panels take a long time to accept writes after waking even though reads
        // already work. Keep trying instead of giving up on the first failed sequence.
        var deadline = Environment.TickCount64 + 180000;
        var attempt = 0;
        while (Environment.TickCount64 < deadline)
        {
            var now = Hdr.IsEnabled(_monitor.GdiDevice);
            if (now != null && now != hdr)
            {
                Log.Info($"{reason}: HDR state changed while recovering; leaving it to the watcher");
                return false;
            }

            attempt++;
            if (attempt > 1)
            {
                if (attempt % 3 == 1)
                {
                    Log.Warn($"{reason}: panel still not accepting writes; retrying (attempt {attempt})");
                    ReopenMonitor();
                }
                Thread.Sleep(5000);
            }

            var ok = hdr.Value ? ApplyHdrOn() : ApplyHdrOff();
            if (ok)
            {
                Log.Ok($"{reason}: state re-applied");
                return true;
            }
        }

        Log.Error($"{reason}: could not re-apply the state within 180s");
        return false;
    }
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var config = Config.Load();
            using var monitor = MonitorDiscovery.Find(config.ModelMatch);

            if (monitor == null)
            {
                Log.Error("no DDC/CI monitor found");
                return 2;
            }

            if (monitor.Capabilities.Length == 0)
                Log.Warn("monitor returned no capability string; some commands may fail");

            var engine = new FaldEngine(monitor, config);
            var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();

            switch (command)
            {
                case "state":
                    engine.PrintState();
                    return 0;

                case "diag":
                    Console.WriteLine(Hdr.Describe(monitor.GdiDevice));
                    engine.PrintState();
                    return 0;

                case "sync":
                    engine.Sync();
                    return 0;

                case "watch":
                    engine.Watch();
                    return 0;

                case "restore":
                    engine.ApplyHdrOffWithRetry();
                    return 0;

                case "hdr":
                {
                    var value = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                    if (value != "on" && value != "off")
                    {
                        Log.Error("usage: FaldSync hdr on|off");
                        return 1;
                    }
                    if (!Hdr.Set(monitor.GdiDevice, value == "on"))
                    {
                        Log.Error("could not change the Windows HDR state");
                        return 3;
                    }
                    Log.Ok($"Windows HDR {value}");
                    Thread.Sleep(1500);
                    engine.Sync();
                    return 0;
                }

                case "set":
                {
                    uint? brightness = null, contrast = null;
                    for (var i = 1; i < args.Length - 1; i++)
                    {
                        if (args[i] is "--brightness" or "-b" && uint.TryParse(args[i + 1], out var b)) brightness = b;
                        if (args[i] is "--contrast" or "-c" && uint.TryParse(args[i + 1], out var c)) contrast = c;
                    }
                    if (brightness == null && contrast == null)
                    {
                        Log.Error("usage: FaldSync set --brightness 65 --contrast 50");
                        return 1;
                    }
                    if (brightness != null) engine.SetBrightness(brightness.Value);
                    if (contrast != null) engine.SetContrast(contrast.Value);
                    return 0;
                }

                case "volume":
                {
                    if (args.Length < 2)
                    {
                        if (monitor.TryGet(0x62, out var current))
                        {
                            Log.Ok($"volume = {current}");
                            return 0;
                        }
                        Log.Warn("could not read the speaker volume");
                        return 3;
                    }

                    if (!uint.TryParse(args[1], out var volume) || volume > 100)
                    {
                        Log.Error("usage: FaldSync volume [0-100]");
                        return 1;
                    }

                    return engine.SetVolume(volume) ? 0 : 3;
                }

                case "input":
                {
                    if (args.Length < 2 || args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
                    {
                        if (monitor.TryGet(0x60, out var current))
                        {
                            Log.Ok($"input = {MonitorInputs.Name(current)} (0x{current:X2})");
                            return 0;
                        }
                        Log.Warn("could not read the input source");
                        return 3;
                    }

                    if (!MonitorInputs.TryParse(args[1], out var input))
                    {
                        Log.Error("usage: FaldSync input dp|hdmi1|hdmi2|dpalt|0x0F");
                        return 1;
                    }

                    return engine.SetInputSource(input) ? 0 : 3;
                }

                case "white":
                {
                    if (args.Length < 2)
                    {
                        var current = Hdr.GetSdrWhiteNits(monitor.GdiDevice);
                        if (current == null)
                        {
                            Log.Warn("could not read the Windows SDR white level");
                            return 3;
                        }
                        Log.Ok($"Windows SDR white = {current} nits");
                        return 0;
                    }

                    if (!uint.TryParse(args[1], out var nits) || nits is < 80 or > 480)
                    {
                        Log.Error("usage: FaldSync white [80-480]");
                        return 1;
                    }

                    if (!Hdr.SetSdrWhiteNits(monitor.GdiDevice, nits))
                    {
                        Log.Error("could not set the Windows SDR white level");
                        return 3;
                    }

                    var applied = Hdr.GetSdrWhiteNits(monitor.GdiDevice);
                    Log.Ok($"Windows SDR white = {applied?.ToString() ?? nits.ToString()} nits");
                    return 0;
                }

                case "gamma":
                {
                    var value = args.Length > 1 ? args[1].ToLowerInvariant() : "status";

                    if (value is "status" or "")
                    {
                        Console.WriteLine($"gamma fix : {(config.GammaFix ? "enabled" : "disabled")}");
                        Console.WriteLine($"ramp      : {GammaRamp.Describe(monitor.GdiDevice, config.GammaPower)}");
                        return 0;
                    }

                    if (value is not ("on" or "off"))
                    {
                        Log.Error("usage: FaldSync gamma on|off|status");
                        return 1;
                    }

                    var enabled = value == "on";
                    config.GammaFix = enabled;
                    config.Save();

                    if (enabled)
                    {
                        var hdr = Hdr.IsEnabled(monitor.GdiDevice);
                        if (hdr == true)
                        {
                            if (!GammaRamp.Apply(monitor.GdiDevice, config.GammaPower))
                            {
                                Log.Error("could not apply the SDR gamma ramp");
                                return 3;
                            }
                            Log.Ok("gamma 2.2 enabled and applied");
                        }
                        else
                        {
                            Log.Ok("gamma 2.2 enabled; it will apply while HDR is on");
                        }
                    }
                    else
                    {
                        GammaRamp.Restore(monitor.GdiDevice, config.GammaPower);
                        Log.Ok("gamma 2.2 disabled; display ramp restored");
                    }
                    return 0;
                }

                case "probe":
                {
                    byte? code = null;
                    uint? value = null;
                    for (var i = 1; i < args.Length - 1; i++)
                    {
                        if (args[i] == "--code" && TryParseHexByte(args[i + 1], out var parsedCode)) code = parsedCode;
                        if (args[i] == "--value" && uint.TryParse(args[i + 1], out var parsedValue)) value = parsedValue;
                    }
                    if (code == null)
                    {
                        Log.Error("usage: FaldSync probe --code 0xE1 [--value 2]");
                        return 1;
                    }
                    if (value == null)
                    {
                        if (monitor.TryGet(code.Value, out var current, out var maximum))
                            Log.Ok($"0x{code:X2} = {current} (max {maximum})");
                        else
                            Log.Warn($"0x{code:X2} did not reply");
                    }
                    else
                    {
                        // 0xE2 is write-only: a standard read always returns 0, so a
                        // read-back verification would report a false failure.
                        var verify = code.Value != config.DisplayModeVcp;
                        if (monitor.Set(code.Value, value.Value, verify: verify))
                            Log.Ok($"0x{code:X2} set to {value}{(verify ? "" : " (write-only; HDR/Standard reflect via 0x14, User does not)")}");
                        else
                            Log.Warn($"0x{code:X2} write did not verify");
                    }
                    return 0;
                }

                default:
                    PrintHelp();
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex.ToString());
            return 1;
        }
    }

    private static bool TryParseHexByte(string text, out byte value)
    {
        var cleaned = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return byte.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            FaldSync - keep an Acer Mini-LED panel's FALD engaged while Windows is in HDR.

              FaldSync state                     show HDR state, VCP values and the FALD gate
              FaldSync diag                      dump display paths + advanced-colour state
              FaldSync sync                      apply the correct state once
              FaldSync watch                     follow Windows HDR changes until Ctrl+C
              FaldSync set -b 65 -c 50           set brightness / contrast (works in HDR)
              FaldSync volume [0-100]            read or set the monitor speaker volume
              FaldSync input dp|hdmi1|hdmi2|dpalt read or switch the monitor input
              FaldSync white 120                 set Windows SDR white in HDR (80-480 nits)
              FaldSync gamma on|off|status        toggle the SDR-in-HDR gamma 2.2 correction
              FaldSync hdr on|off                toggle Windows HDR and sync the monitor
              FaldSync restore                   return the panel to SDR mode + SDR brightness
              FaldSync probe --code 0xE1         read any VCP code (add --value to write)

            Config: faldsync.json next to the exe (modelMatch, displayModeVcp, hdrDisplayMode,
                    sdrDisplayMode, hdrBrightness, hdrContrast, sdrBrightness, sdrContrast,
                    sdrWhiteNits, gammaFix, gammaPower, selectedInput, autoSwitchInput, theme,
                    pollMs, settleMs, preSettleMs, userSettleMs).
            """);
    }
}
