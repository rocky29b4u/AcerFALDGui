using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace FaldSync.UI;

/// <summary>Named colour slots. Controls keep a role, not a fixed colour, so a
/// theme switch can repaint the whole window without rebuilding it.</summary>
internal enum ThemeColor
{
    Background,
    Header,
    HeaderText,
    HeaderMuted,
    Card,
    Border,
    Text,
    Muted,
    Accent,
    AccentHover,
    AccentPressed,
    OnAccent,
    Secondary,
    SecondaryHover,
    SecondaryPressed,
    Green,
    GreenSoft,
    Amber,
    AmberSoft,
    Red,
    RedSoft,
    GraySoft,
    Track,
    TrackFill,
    Thumb,
    HeaderPill,
    HeaderPillText,
    HeaderPillMuted,
    HeaderPillMutedText
}

internal sealed class ThemePalette
{
    public required Color Background { get; init; }
    public required Color Header { get; init; }
    public required Color HeaderText { get; init; }
    public required Color HeaderMuted { get; init; }
    public required Color Card { get; init; }
    public required Color Border { get; init; }
    public required Color Text { get; init; }
    public required Color Muted { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentPressed { get; init; }
    public required Color OnAccent { get; init; }
    public required Color Secondary { get; init; }
    public required Color SecondaryHover { get; init; }
    public required Color SecondaryPressed { get; init; }
    public required Color Green { get; init; }
    public required Color GreenSoft { get; init; }
    public required Color Amber { get; init; }
    public required Color AmberSoft { get; init; }
    public required Color Red { get; init; }
    public required Color RedSoft { get; init; }
    public required Color GraySoft { get; init; }
    public required Color Track { get; init; }
    public required Color TrackFill { get; init; }
    public required Color Thumb { get; init; }
    public required Color HeaderPill { get; init; }
    public required Color HeaderPillText { get; init; }
    public required Color HeaderPillMuted { get; init; }
    public required Color HeaderPillMutedText { get; init; }

    public Color Get(ThemeColor color) => color switch
    {
        ThemeColor.Background => Background,
        ThemeColor.Header => Header,
        ThemeColor.HeaderText => HeaderText,
        ThemeColor.HeaderMuted => HeaderMuted,
        ThemeColor.Card => Card,
        ThemeColor.Border => Border,
        ThemeColor.Text => Text,
        ThemeColor.Muted => Muted,
        ThemeColor.Accent => Accent,
        ThemeColor.AccentHover => AccentHover,
        ThemeColor.AccentPressed => AccentPressed,
        ThemeColor.OnAccent => OnAccent,
        ThemeColor.Secondary => Secondary,
        ThemeColor.SecondaryHover => SecondaryHover,
        ThemeColor.SecondaryPressed => SecondaryPressed,
        ThemeColor.Green => Green,
        ThemeColor.GreenSoft => GreenSoft,
        ThemeColor.Amber => Amber,
        ThemeColor.AmberSoft => AmberSoft,
        ThemeColor.Red => Red,
        ThemeColor.RedSoft => RedSoft,
        ThemeColor.GraySoft => GraySoft,
        ThemeColor.Track => Track,
        ThemeColor.TrackFill => TrackFill,
        ThemeColor.Thumb => Thumb,
        ThemeColor.HeaderPill => HeaderPill,
        ThemeColor.HeaderPillText => HeaderPillText,
        ThemeColor.HeaderPillMuted => HeaderPillMuted,
        ThemeColor.HeaderPillMutedText => HeaderPillMutedText,
        _ => Text
    };
}

internal sealed class ThemeBinding
{
    public ThemeColor? Back { get; init; }
    public ThemeColor? Fore { get; init; }
    public ThemeColor? Hover { get; init; }
    public ThemeColor? Pressed { get; init; }
}

internal static class Theme
{
    private static readonly ThemePalette DarkPalette = new()
    {
        Background = Color.FromArgb(11, 18, 32),
        Header = Color.FromArgb(15, 23, 42),
        HeaderText = Color.FromArgb(248, 250, 252),
        HeaderMuted = Color.FromArgb(148, 163, 184),
        Card = Color.FromArgb(17, 28, 46),
        Border = Color.FromArgb(30, 47, 74),
        Text = Color.FromArgb(230, 237, 247),
        Muted = Color.FromArgb(140, 160, 188),
        Accent = Color.FromArgb(34, 211, 238),
        AccentHover = Color.FromArgb(6, 182, 212),
        AccentPressed = Color.FromArgb(8, 145, 178),
        OnAccent = Color.FromArgb(4, 18, 28),
        Secondary = Color.FromArgb(24, 38, 60),
        SecondaryHover = Color.FromArgb(33, 52, 79),
        SecondaryPressed = Color.FromArgb(42, 65, 98),
        Green = Color.FromArgb(52, 211, 153),
        GreenSoft = Color.FromArgb(15, 46, 38),
        Amber = Color.FromArgb(251, 191, 36),
        AmberSoft = Color.FromArgb(51, 39, 11),
        Red = Color.FromArgb(248, 113, 113),
        RedSoft = Color.FromArgb(54, 26, 31),
        GraySoft = Color.FromArgb(27, 41, 64),
        Track = Color.FromArgb(35, 52, 80),
        TrackFill = Color.FromArgb(34, 211, 238),
        Thumb = Color.FromArgb(230, 237, 247),
        HeaderPill = Color.FromArgb(30, 41, 59),
        HeaderPillText = Color.FromArgb(103, 232, 249),
        HeaderPillMuted = Color.FromArgb(51, 65, 85),
        HeaderPillMutedText = Color.FromArgb(148, 163, 184)
    };

    private static readonly ThemePalette LightPalette = new()
    {
        Background = Color.FromArgb(243, 246, 250),
        Header = Color.FromArgb(15, 23, 42),
        HeaderText = Color.FromArgb(248, 250, 252),
        HeaderMuted = Color.FromArgb(148, 163, 184),
        Card = Color.White,
        Border = Color.FromArgb(220, 229, 240),
        Text = Color.FromArgb(15, 23, 42),
        Muted = Color.FromArgb(100, 116, 139),
        // Darker than the dark-theme cyan so white button text clears 4.5:1.
        Accent = Color.FromArgb(14, 116, 144),
        AccentHover = Color.FromArgb(21, 94, 117),
        AccentPressed = Color.FromArgb(15, 76, 95),
        OnAccent = Color.White,
        Secondary = Color.FromArgb(238, 243, 249),
        SecondaryHover = Color.FromArgb(226, 232, 240),
        SecondaryPressed = Color.FromArgb(203, 213, 225),
        Green = Color.FromArgb(22, 163, 74),
        GreenSoft = Color.FromArgb(220, 252, 231),
        Amber = Color.FromArgb(180, 83, 9),
        AmberSoft = Color.FromArgb(254, 243, 199),
        Red = Color.FromArgb(185, 28, 28),
        RedSoft = Color.FromArgb(254, 226, 226),
        GraySoft = Color.FromArgb(226, 232, 240),
        Track = Color.FromArgb(220, 229, 240),
        TrackFill = Color.FromArgb(8, 145, 178),
        Thumb = Color.White,
        HeaderPill = Color.FromArgb(30, 41, 59),
        HeaderPillText = Color.FromArgb(103, 232, 249),
        HeaderPillMuted = Color.FromArgb(51, 65, 85),
        HeaderPillMutedText = Color.FromArgb(148, 163, 184)
    };

    private static ThemePalette _current = DarkPalette;

    public static bool IsDark { get; private set; } = true;
    public static ThemePalette Current => _current;
    public static event Action? Changed;

    public static void SetDark(bool dark)
    {
        if (IsDark == dark) return;
        IsDark = dark;
        _current = dark ? DarkPalette : LightPalette;
        Changed?.Invoke();
    }

    public static Color Get(ThemeColor color) => _current.Get(color);

    // Convenience accessors kept for the existing layout code.
    public static Color Background => _current.Background;
    public static Color Header => _current.Header;
    public static Color HeaderText => _current.HeaderText;
    public static Color HeaderMuted => _current.HeaderMuted;
    public static Color Card => _current.Card;
    public static Color Border => _current.Border;
    public static Color Text => _current.Text;
    public static Color Muted => _current.Muted;
    public static Color Accent => _current.Accent;
    public static Color AccentHover => _current.AccentHover;
    public static Color AccentPressed => _current.AccentPressed;
    public static Color OnAccent => _current.OnAccent;
    public static Color Secondary => _current.Secondary;
    public static Color SecondaryHover => _current.SecondaryHover;
    public static Color SecondaryPressed => _current.SecondaryPressed;
    public static Color Green => _current.Green;
    public static Color GreenSoft => _current.GreenSoft;
    public static Color Amber => _current.Amber;
    public static Color AmberSoft => _current.AmberSoft;
    public static Color Red => _current.Red;
    public static Color RedSoft => _current.RedSoft;
    public static Color GraySoft => _current.GraySoft;

    public static readonly Font BaseFont = new("Segoe UI", 9F);
    public static readonly Font SmallFont = new("Segoe UI", 8.25F);
    public static readonly Font SectionFont = new("Segoe UI", 7.5F, FontStyle.Bold);
    public static readonly Font CardTitleFont = new("Segoe UI Semibold", 11.5F);
    public static readonly Font ValueFont = new("Segoe UI Semibold", 21F);
    public static readonly Font TitleFont = new("Segoe UI Semibold", 20F);
    public static readonly Font SubtitleFont = new("Segoe UI", 9F);
    public static readonly Font ButtonFont = new("Segoe UI Semibold", 9F);
    public static readonly Font PillFont = new("Segoe UI Semibold", 7.5F);
    public static readonly Font StatusFont = new("Segoe UI", 8.5F);

    public static void Bind(
        Control control,
        ThemeColor? back = null,
        ThemeColor? fore = null,
        ThemeColor? hover = null,
        ThemeColor? pressed = null)
    {
        control.Tag = new ThemeBinding
        {
            Back = back,
            Fore = fore,
            Hover = hover,
            Pressed = pressed
        };
        ApplyTo(control);
    }

    public static void Apply(Control root)
    {
        ApplyTo(root);
        foreach (Control child in root.Controls) Apply(child);
    }

    private static void ApplyTo(Control control)
    {
        if (control is RoundedPanel panel) panel.ApplyTheme();

        if (control.Tag is ThemeBinding binding)
        {
            if (binding.Back is { } back) control.BackColor = Get(back);
            if (binding.Fore is { } fore) control.ForeColor = Get(fore);

            if (control is Button button)
            {
                if (binding.Hover is { } hover) button.FlatAppearance.MouseOverBackColor = Get(hover);
                if (binding.Pressed is { } pressed) button.FlatAppearance.MouseDownBackColor = Get(pressed);
            }
        }

        if (control is ModernSlider or ToggleSwitch) control.Invalidate();
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2;
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = 14;
    public ThemeColor FillRole { get; set; } = ThemeColor.Card;
    public ThemeColor BorderRole { get; set; } = ThemeColor.Border;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Get(FillRole);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, CornerRadius);
        using var brush = new SolidBrush(Theme.Get(FillRole));
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Theme.Get(BorderRole), 1F);
        e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }
}

/// <summary>Owner-drawn slider: a rounded two-tone track and a thumb, so it looks
/// right in both the light and dark palettes.</summary>
internal sealed class ModernSlider : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private bool _dragging;

    public event EventHandler? ValueChanged;

    public ModernSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        Height = 40;
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum) _maximum = _minimum;
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = Math.Clamp(_value, _minimum, _maximum);
            Invalidate();
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, _minimum, _maximum);
            if (next == _value) return;
            _value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SmallChange { get; set; } = 1;
    public int LargeChange { get; set; } = 5;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);

        var scale = DeviceDpi / 96F;
        var pad = (int)Math.Round(10 * scale);
        var thickness = Math.Max(4, (int)Math.Round(6 * scale));
        var radius = Math.Max(5, (int)Math.Round(8 * scale));
        var center = Height / 2;

        var trackRect = new Rectangle(pad, center - thickness / 2, Math.Max(1, Width - pad * 2), thickness);
        using (var path = Theme.RoundedRect(trackRect, thickness / 2))
        using (var brush = new SolidBrush(Theme.Get(ThemeColor.Track)))
            g.FillPath(brush, path);

        var fraction = _maximum > _minimum
            ? (_value - _minimum) / (double)(_maximum - _minimum)
            : 0;
        var position = pad + (int)Math.Round((Width - pad * 2) * fraction);

        var fillColor = Enabled ? Theme.Get(ThemeColor.TrackFill) : Theme.Get(ThemeColor.Muted);
        if (position > pad + 1)
        {
            var fillRect = new Rectangle(pad, center - thickness / 2, position - pad, thickness);
            using var fillPath = Theme.RoundedRect(fillRect, thickness / 2);
            using var fillBrush = new SolidBrush(fillColor);
            g.FillPath(fillBrush, fillPath);
        }

        var thumbRect = new Rectangle(position - radius, center - radius, radius * 2, radius * 2);
        if (Focused && Enabled)
        {
            using var halo = new SolidBrush(Color.FromArgb(55, fillColor));
            g.FillEllipse(halo, thumbRect.X - radius / 2, thumbRect.Y - radius / 2,
                thumbRect.Width + radius, thumbRect.Height + radius);
        }

        using (var thumbBrush = new SolidBrush(Enabled ? Theme.Get(ThemeColor.Thumb) : Theme.Get(ThemeColor.GraySoft)))
            g.FillEllipse(thumbBrush, thumbRect);
        using (var thumbPen = new Pen(fillColor, Math.Max(1.5F, 2F * scale)))
            g.DrawEllipse(thumbPen, thumbRect);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && (e.Button & MouseButtons.Left) != 0) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!Enabled) return;
        Value += e.Delta > 0 ? SmallChange : -SmallChange;
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End ||
        base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled)
        {
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    Value -= SmallChange;
                    e.Handled = true;
                    break;
                case Keys.Right:
                case Keys.Up:
                    Value += SmallChange;
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    Value -= LargeChange;
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    Value += LargeChange;
                    e.Handled = true;
                    break;
                case Keys.Home:
                    Value = Minimum;
                    e.Handled = true;
                    break;
                case Keys.End:
                    Value = Maximum;
                    e.Handled = true;
                    break;
            }
        }
        base.OnKeyDown(e);
    }

    private void SetFromX(int x)
    {
        var scale = DeviceDpi / 96F;
        var pad = (int)Math.Round(10 * scale);
        var span = Math.Max(1, Width - pad * 2);
        var fraction = Math.Clamp((x - pad) / (double)span, 0.0, 1.0);
        Value = Minimum + (int)Math.Round(fraction * (Maximum - Minimum));
    }
}

/// <summary>A compact on/off switch used for Auto Switch to Input Source.</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked;

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        Size = new Size(40, 22);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Card);

        var scale = DeviceDpi / 96F;
        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        var fill = !Enabled
            ? Theme.Get(ThemeColor.GraySoft)
            : _checked
                ? Theme.Get(ThemeColor.Accent)
                : Theme.Get(ThemeColor.SecondaryPressed);
        var border = !Enabled
            ? Theme.Get(ThemeColor.Border)
            : _checked
                ? Theme.Get(ThemeColor.AccentHover)
                : Theme.Get(ThemeColor.Border);

        using (var path = Theme.RoundedRect(track, Height / 2))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);
        using (var path = Theme.RoundedRect(track, Height / 2))
        using (var pen = new Pen(border, 1F))
            g.DrawPath(pen, path);

        var pad = Math.Max(2, (int)Math.Round(3 * scale));
        var diameter = Height - pad * 2;
        var x = _checked ? Width - diameter - pad : pad;
        using var knob = new SolidBrush(Enabled ? Theme.Get(ThemeColor.Thumb) : Theme.Get(ThemeColor.Muted));
        g.FillEllipse(knob, x, pad, diameter, diameter);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        Checked = !Checked;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

internal static class AppIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Create()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using (var background = new SolidBrush(Theme.Header))
        using (var backgroundPath = Theme.RoundedRect(new Rectangle(2, 2, 60, 60), 14))
            graphics.FillPath(background, backgroundPath);

        using (var pen = new Pen(Color.FromArgb(34, 211, 238), 4F)
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round
               })
        {
            graphics.DrawRectangle(pen, 15, 18, 34, 23);
            graphics.DrawLine(pen, 24, 49, 40, 49);
            graphics.DrawLine(pen, 32, 41, 32, 49);
        }

        using (var dot = new SolidBrush(Color.FromArgb(34, 211, 238)))
            graphics.FillEllipse(dot, 28, 28, 8, 8);

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
