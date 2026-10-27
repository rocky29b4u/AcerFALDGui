using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FaldSync.UI;

public sealed class MainForm : Form
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const byte BrightnessVcp = 0x10;
    private const byte ContrastVcp = 0x12;
    private const byte PictureModeVcp = 0x14;
    private const byte VolumeVcp = 0x62;
    private const byte InputVcp = 0x60;

    private readonly Config _config;
    private readonly float _scale;
    private FaldEngine? _engine;
    private PowerMonitor? _power;
    private NotifyIcon? _tray;
    private ContextMenuStrip? _trayMenu;
    private ToolStripMenuItem? _themeMenuItem;
    private Icon? _appIcon;
    private readonly ToolTip _toolTip = new();

    private readonly SemaphoreSlim _ddcGate = new(1, 1);
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _readTimer = new() { Interval = 2500 };
    private readonly System.Windows.Forms.Timer _brightnessDebounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _contrastDebounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _sdrWhiteDebounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _volumeDebounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer _statusResetTimer = new() { Interval = 6000 };

    private Label _monitorModelLabel = null!;
    private Label _monitorDetailLabel = null!;
    private Label _brightnessValueLabel = null!;
    private Label _contrastValueLabel = null!;
    private Label _sdrWhiteValueLabel = null!;
    private Label _volumeValueLabel = null!;
    private Label _autoSourceHintLabel = null!;
    private Label _hdrValueLabel = null!;
    private Label _modeValueLabel = null!;
    private Label _faldValueLabel = null!;
    private Label _statusLabel = null!;
    private Label _gammaHintLabel = null!;
    private ModernSlider _brightnessSlider = null!;
    private ModernSlider _contrastSlider = null!;
    private ModernSlider _sdrWhiteSlider = null!;
    private ModernSlider _volumeSlider = null!;
    private ToggleSwitch _autoSourceToggle = null!;
    private Button _toggleHdrButton = null!;
    private Button _gammaButton = null!;
    private Button _reapplyButton = null!;
    private Button _restoreButton = null!;
    private Button _openLogButton = null!;
    private Button _exitButton = null!;
    private Button _themeButton = null!;
    private Label _headerPill = null!;
    private Label _hdrStatePill = null!;
    private readonly Dictionary<uint, Button> _inputButtons = new();
    private readonly List<RoundedPanel> _cards = new();

    private bool _suppressSliderEvents;
    private bool _brightnessDragging;
    private bool _contrastDragging;
    private bool _sdrWhiteDragging;
    private bool _volumeDragging;
    private int _brightnessPending;
    private int _contrastPending;
    private int _sdrWhitePending;
    private int _volumePending;
    private int _brightnessApplying;
    private int _contrastApplying;
    private int _sdrWhiteApplying;
    private int _volumeApplying;
    private int _brightnessApplied = -1;
    private int _contrastApplied = -1;
    private int _sdrWhiteApplied = -1;
    private int _volumeApplied = -1;
    private int _busy;
    private bool _reallyExit;
    private bool _trayHintShown;
    private bool _shownOnce;
    private bool _gammaErrorShown;
    private bool _monitorAway;
    private bool _connectingEngine;
    private int _readFailureCount;
    private int _gammaPollTicks;
    private bool? _lastHdr;
    private bool? _faldActive;
    private uint? _pictureMode;
    private uint? _currentInput;

    public MainForm()
    {
        _config = Config.Load();
        _scale = GetUiScale();
        Theme.SetDark(!string.Equals(_config.Theme, "light", StringComparison.OrdinalIgnoreCase));
        Theme.Changed += OnThemeChanged;

        InitializeWindow();
        BuildUi();
        SetupTray();
        ConnectEngine();
        InitializeSlidersAndStatus();
        WireEvents();
        UpdateControlAvailability();
    }

    private void InitializeWindow()
    {
        Text = "AcerFALDv2";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        Font = Theme.BaseFont;
        AutoScaleMode = AutoScaleMode.None;

        // The full layout is about 1030 px tall at 100% scaling. On a shorter
        // desktop the content panel scrolls instead of putting controls off-screen.
        var wanted = S(1030);
        var available = SystemInformation.WorkingArea.Height - S(40);
        ClientSize = new Size(S(600), Math.Max(S(640), Math.Min(wanted, available)));
    }

    private static float GetUiScale()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return graphics.DpiX > 0 ? graphics.DpiX / 96F : 1F;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowTheme();
    }

    private void ApplyWindowTheme()
    {
        if (!IsHandleCreated) return;
        var dark = Theme.IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkModeLegacy, ref dark, sizeof(int));
    }

    private int S(int value) => (int)Math.Round(value * _scale);
    private Size Sz(int width, int height) => new(S(width), S(height));
    private Point P(int x, int y) => new(S(x), S(y));
    private Padding Pad(int left, int top, int right, int bottom) =>
        new(S(left), S(top), S(right), S(bottom));

    private void BuildUi()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = S(88),
            BackColor = Theme.Header
        };
        Theme.Bind(header, ThemeColor.Header);

        var title = MakeLabel("AcerFALDv2", 24, 13, Theme.TitleFont, ThemeColor.HeaderText, ThemeColor.Header);
        var subtitle = MakeLabel(
            "HDR + local dimming control for Acer XV275K P5",
            26,
            50,
            Theme.SubtitleFont,
            ThemeColor.HeaderMuted,
            ThemeColor.Header,
            338,
            18);

        _headerPill = MakePillLabel(
            "SDR",
            376,
            30,
            88,
            26,
            ThemeColor.HeaderPill,
            ThemeColor.HeaderPillText);

        _themeButton = MakeButton(
            Theme.IsDark ? "LIGHT" : "DARK",
            476,
            30,
            100,
            26,
            ThemeColor.HeaderPill,
            ThemeColor.HeaderPillMuted,
            ThemeColor.HeaderPillMuted,
            ThemeColor.HeaderPillText);
        _themeButton.Font = Theme.PillFont;
        _themeButton.AccessibleName = "Switch theme";
        _themeButton.Click += (_, _) => ToggleTheme();
        UpdateThemeButton();

        var accentLine = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = S(3),
            BackColor = Theme.Accent
        };
        Theme.Bind(accentLine, ThemeColor.Accent);

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_headerPill);
        header.Controls.Add(_themeButton);
        header.Controls.Add(accentLine);

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            AutoScroll = true,
            AutoScrollMinSize = Sz(0, 900)
        };
        Theme.Bind(content, ThemeColor.Background);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = S(42),
            BackColor = Theme.Background
        };
        Theme.Bind(footer, ThemeColor.Background);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.StatusFont,
            AutoEllipsis = true,
            Padding = Pad(24, 0, 0, 0),
            Text = "Ready."
        };
        Theme.Bind(_statusLabel, ThemeColor.Background, ThemeColor.Muted);

        var versionLabel = new Label
        {
            Dock = DockStyle.Right,
            Width = S(130),
            TextAlign = ContentAlignment.MiddleRight,
            Font = Theme.SmallFont,
            Padding = Pad(0, 0, 24, 0),
            Text = "AcerFALDv2 2.2"
        };
        Theme.Bind(versionLabel, ThemeColor.Background, ThemeColor.Muted);

        footer.Controls.Add(_statusLabel);
        footer.Controls.Add(versionLabel);

        Controls.Add(content);
        Controls.Add(footer);
        Controls.Add(header);

        _cards.Clear();
        _inputButtons.Clear();

        var monitorCard = BuildMonitorCard();
        var brightnessCard = BuildBrightnessCard();
        var contrastCard = BuildContrastCard();
        var sdrWhiteCard = BuildSdrWhiteCard();
        var hdrCard = BuildHdrCard();
        var audioInputCard = BuildAudioInputCard();
        var actions = BuildActions();

        content.Controls.Add(monitorCard);
        content.Controls.Add(brightnessCard);
        content.Controls.Add(contrastCard);
        content.Controls.Add(sdrWhiteCard);
        content.Controls.Add(hdrCard);
        content.Controls.Add(audioInputCard);
        content.Controls.Add(actions);
    }

    private RoundedPanel NewCard(int x, int y, int width, int height)
    {
        var card = new RoundedPanel
        {
            Location = P(x, y),
            Size = Sz(width, height),
            CornerRadius = S(14)
        };
        _cards.Add(card);
        return card;
    }

    private RoundedPanel BuildMonitorCard()
    {
        var card = NewCard(24, 16, 552, 66);

        var section = MakeLabel("MONITOR", 16, 9, Theme.SectionFont, ThemeColor.Muted, ThemeColor.Card);
        _monitorModelLabel = MakeLabel(
            "Detecting monitor…",
            16,
            24,
            Theme.CardTitleFont,
            ThemeColor.Text,
            ThemeColor.Card,
            520,
            20,
            ContentAlignment.MiddleLeft);
        _monitorDetailLabel = MakeLabel(
            "DDC/CI",
            16,
            44,
            Theme.SmallFont,
            ThemeColor.Muted,
            ThemeColor.Card,
            520,
            16,
            ContentAlignment.MiddleLeft);
        _monitorDetailLabel.AutoEllipsis = true;

        card.Controls.Add(section);
        card.Controls.Add(_monitorModelLabel);
        card.Controls.Add(_monitorDetailLabel);
        return card;
    }

    private RoundedPanel BuildBrightnessCard()
    {
        var card = NewCard(24, 92, 552, 104);

        var section = MakeLabel("BRIGHTNESS", 16, 9, Theme.SectionFont, ThemeColor.Muted, ThemeColor.Card);
        _brightnessValueLabel = MakeLabel(
            "65%",
            440,
            1,
            Theme.ValueFont,
            ThemeColor.Text,
            ThemeColor.Card,
            96,
            32,
            ContentAlignment.MiddleRight);

        _brightnessSlider = new ModernSlider
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 1,
            LargeChange = 5,
            Location = P(12, 30),
            Size = Sz(528, 40),
            TabIndex = 0
        };
        Theme.Bind(_brightnessSlider, ThemeColor.Card);

        var min = MakeLabel("0", 16, 74, Theme.SmallFont, ThemeColor.Muted, ThemeColor.Card, 36, 14, ContentAlignment.MiddleLeft);
        var max = MakeLabel("100", 496, 74, Theme.SmallFont, ThemeColor.Muted, ThemeColor.Card, 40, 14, ContentAlignment.MiddleRight);

        card.Controls.Add(section);
        card.Controls.Add(_brightnessValueLabel);
        card.Controls.Add(_brightnessSlider);
        card.Controls.Add(min);
        card.Controls.Add(max);
        return card;
    }

    private RoundedPanel BuildContrastCard()
    {
        var card = NewCard(24, 204, 552, 104);

        var section = MakeLabel("CONTRAST", 16, 9, Theme.SectionFont, ThemeColor.Muted, ThemeColor.Card);
        _contrastValueLabel = MakeLabel(
            "50%",
            440,
            1,
            Theme.ValueFont,
            ThemeColor.Text,
            ThemeColor.Card,
            96,
            32,
            ContentAlignment.MiddleRight);

        _contrastSlider = new ModernSlider
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 1,
            LargeChange = 5,
            Location = P(12, 30),
            Size = Sz(528, 40),
            TabIndex = 1
        };
        Theme.Bind(_contrastSlider, ThemeColor.Card);

        var min = MakeLabel("0", 16, 74, Theme.SmallFont, ThemeColor.Muted, ThemeColor.Card, 36, 14, ContentAlignment.MiddleLeft);
        var max = MakeLabel("100", 496, 74, Theme.SmallFont, ThemeColor.Muted, ThemeColor.Card, 40, 14, ContentAlignment.MiddleRight);

        card.Controls.Add(section);
        card.Controls.Add(_contrastValueLabel);
        card.Controls.Add(_contrastSlider);
        card.Controls.Add(min);
        card.Controls.Add(max);
        return card;
    }

    private RoundedPanel BuildSdrWhiteCard()
    {
        var card = NewCard(24, 316, 552, 142);

        var section = MakeLabel("SDR WHITE (IN HDR)", 16, 9, Theme.SectionFont, ThemeColor.Muted, ThemeColor.Card);
        _sdrWhiteValueLabel = MakeLabel(
            "120 nits",
            392,
            1,
            Theme.ValueFont,
            ThemeColor.Text,
            ThemeColor.Card,
            144,
            32,
            ContentAlignment.MiddleRight);

        _sdrWhiteSlider = new ModernSlider
        {
            Minimum = 80,
            Maximum = 480,
            SmallChange = 4,
            LargeChange = 40,
            Location = P(12, 30),
            Size = Sz(528, 40),
            TabIndex = 2
        };
        Theme.Bind(_sdrWhiteSlider, ThemeColor.Card);

        var min = MakeLabel("80", 16, 74, Theme.SmallFont, ThemeColor.Muted, ThemeColor.Card, 36, 14, ContentAlignment.MiddleLeft);
        var max = MakeLabel("480", 496, 74, Theme.SmallFont, ThemeColor.Muted, ThemeColor.Card, 40, 14, ContentAlignment.MiddleRight);

        _gammaButton = MakeButton(
            "GAMMA 2.2: OFF",
            16,
            96,
            168,
            30,
            ThemeColor.Secondary,
            ThemeColor.SecondaryHover,
            ThemeColor.SecondaryPressed,
            ThemeColor.Text);

        _gammaHintLabel = MakeLabel(
            "Applies while HDR is on",
            194,
            99,
            Theme.SmallFont,
            ThemeColor.Muted,
            ThemeColor.Card,
            342,
            24,
            ContentAlignment.MiddleLeft);
        _gammaHintLabel.AutoEllipsis = true;

        card.Controls.Add(section);
        card.Controls.Add(_sdrWhiteValueLabel);
        card.Controls.Add(_sdrWhiteSlider);
        card.Controls.Add(min);
        card.Controls.Add(max);
        card.Controls.Add(_gammaButton);
        card.Controls.Add(_gammaHintLabel);
        return card;
    }

    private RoundedPanel BuildHdrCard()
    {
        var card = NewCard(24, 466, 552, 146);

        var section = MakeLabel("HDR + LOCAL DIMMING", 16, 9, Theme.SectionFont, ThemeColor.Muted, ThemeColor.Card);
        _hdrStatePill = MakePillLabel(
            "SDR",
            440,
            7,
            96,
            26,
            ThemeColor.AmberSoft,
            ThemeColor.Amber);

        var hdrCaption = MakeLabel("Windows HDR", 16, 36, Theme.BaseFont, ThemeColor.Muted, ThemeColor.Card);
        _hdrValueLabel = MakeLabel("Off", 136, 35, Theme.ButtonFont, ThemeColor.Text, ThemeColor.Card);

        var modeCaption = MakeLabel("Picture mode", 16, 56, Theme.BaseFont, ThemeColor.Muted, ThemeColor.Card);
        _modeValueLabel = MakeLabel("Unknown", 136, 55, Theme.ButtonFont, ThemeColor.Text, ThemeColor.Card);

        var faldCaption = MakeLabel("Local dimming", 16, 76, Theme.BaseFont, ThemeColor.Muted, ThemeColor.Card);
        _faldValueLabel = MakeLabel("Unknown", 136, 75, Theme.ButtonFont, ThemeColor.Text, ThemeColor.Card);

        _toggleHdrButton = MakeButton(
            "Turn HDR on",
            16,
            102,
            144,
            32,
            ThemeColor.Accent,
            ThemeColor.AccentHover,
            ThemeColor.AccentPressed,
            ThemeColor.OnAccent);

        _reapplyButton = MakeButton(
            "Re-apply HDR / FALD",
            170,
            102,
            158,
            32,
            ThemeColor.Secondary,
            ThemeColor.SecondaryHover,
            ThemeColor.SecondaryPressed,
            ThemeColor.Text);

        card.Controls.Add(section);
        card.Controls.Add(_hdrStatePill);
        card.Controls.Add(hdrCaption);
        card.Controls.Add(_hdrValueLabel);
        card.Controls.Add(modeCaption);
        card.Controls.Add(_modeValueLabel);
        card.Controls.Add(faldCaption);
        card.Controls.Add(_faldValueLabel);
        card.Controls.Add(_toggleHdrButton);
        card.Controls.Add(_reapplyButton);
        return card;
    }

    private RoundedPanel BuildAudioInputCard()
    {
        var card = NewCard(24, 620, 552, 208);

        var audioSection = MakeLabel("MONITOR AUDIO", 16, 9, Theme.SectionFont, ThemeColor.Muted, ThemeColor.Card);
        var volumeCaption = MakeLabel("Volume", 16, 32, Theme.BaseFont, ThemeColor.Muted, ThemeColor.Card);
        _volumeValueLabel = MakeLabel(
            "100%",
            400,
            22,
            Theme.ValueFont,
            ThemeColor.Text,
            ThemeColor.Card,
            136,
            32,
            ContentAlignment.MiddleRight);

        _volumeSlider = new ModernSlider
        {
            Minimum = 0,
            Maximum = 100,
            SmallChange = 1,
            LargeChange = 5,
            Location = P(12, 56),
            Size = Sz(528, 38),
            TabIndex = 3
        };
        Theme.Bind(_volumeSlider, ThemeColor.Card);

        var min = MakeLabel("0", 16, 96, Theme.SmallFont, ThemeColor.Muted, ThemeColor.Card, 36, 12, ContentAlignment.MiddleLeft);
        var max = MakeLabel("100", 496, 96, Theme.SmallFont, ThemeColor.Muted, ThemeColor.Card, 40, 12, ContentAlignment.MiddleRight);

        var inputSection = MakeLabel("INPUT SOURCE", 16, 116, Theme.SectionFont, ThemeColor.Muted, ThemeColor.Card);

        var x = 16;
        foreach (var input in MonitorInputs.All)
        {
            var button = MakeButton(
                input.Short,
                x,
                134,
                124,
                30,
                ThemeColor.Secondary,
                ThemeColor.SecondaryHover,
                ThemeColor.SecondaryPressed,
                ThemeColor.Text);
            var value = input.Value;
            button.Click += async (_, _) => await SafeAsync(() => SwitchInputAsync(value));
            _inputButtons[value] = button;
            card.Controls.Add(button);
            x += 132;
        }

        _autoSourceToggle = new ToggleSwitch
        {
            Location = P(16, 176),
            Size = Sz(40, 22),
            TabIndex = 8
        };
        Theme.Bind(_autoSourceToggle, ThemeColor.Card);

        var autoLabel = MakeLabel(
            "Auto switch to input",
            64,
            174,
            Theme.BaseFont,
            ThemeColor.Text,
            ThemeColor.Card,
            176,
            24);
        _autoSourceHintLabel = MakeLabel(
            "Off",
            244,
            174,
            Theme.SmallFont,
            ThemeColor.Muted,
            ThemeColor.Card,
            292,
            24,
            ContentAlignment.MiddleRight);
        _autoSourceHintLabel.AutoEllipsis = true;

        card.Controls.Add(audioSection);
        card.Controls.Add(volumeCaption);
        card.Controls.Add(_volumeValueLabel);
        card.Controls.Add(_volumeSlider);
        card.Controls.Add(min);
        card.Controls.Add(max);
        card.Controls.Add(inputSection);
        card.Controls.Add(_autoSourceToggle);
        card.Controls.Add(autoLabel);
        card.Controls.Add(_autoSourceHintLabel);
        return card;
    }

    private Panel BuildActions()
    {
        var actions = new Panel
        {
            Location = P(24, 840),
            Size = Sz(552, 34),
            BackColor = Theme.Background
        };
        Theme.Bind(actions, ThemeColor.Background);

        _restoreButton = MakeButton(
            "Restore SDR",
            0,
            0,
            180,
            34,
            ThemeColor.Secondary,
            ThemeColor.SecondaryHover,
            ThemeColor.SecondaryPressed,
            ThemeColor.Text);

        _openLogButton = MakeButton(
            "Open log",
            190,
            0,
            110,
            34,
            ThemeColor.Secondary,
            ThemeColor.SecondaryHover,
            ThemeColor.SecondaryPressed,
            ThemeColor.Text);

        _exitButton = MakeButton(
            "Exit app",
            454,
            0,
            98,
            34,
            ThemeColor.RedSoft,
            ThemeColor.RedSoft,
            ThemeColor.RedSoft,
            ThemeColor.Red);

        actions.Controls.Add(_restoreButton);
        actions.Controls.Add(_openLogButton);
        actions.Controls.Add(_exitButton);
        return actions;
    }

    private Label MakeLabel(
        string text,
        int x,
        int y,
        Font font,
        ThemeColor foreColor,
        ThemeColor backColor,
        int width = 0,
        int height = 0,
        ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var label = new Label
        {
            Text = text,
            Location = P(x, y),
            Font = font,
            UseMnemonic = false
        };

        if (width > 0)
        {
            label.AutoSize = false;
            label.Width = S(width);
            label.Height = S(height > 0 ? height : 18);
            label.TextAlign = align;
        }
        else
        {
            label.AutoSize = true;
        }

        Theme.Bind(label, backColor, foreColor);
        return label;
    }

    private Label MakePillLabel(
        string text,
        int x,
        int y,
        int width,
        int height,
        ThemeColor fillColor,
        ThemeColor textColor)
    {
        var label = new Label
        {
            Text = text,
            Location = P(x, y),
            Size = Sz(width, height),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.PillFont,
            UseMnemonic = false
        };

        label.Resize += (_, _) => RoundControl(label, S(12));
        Theme.Bind(label, fillColor, textColor);
        RoundControl(label, S(12));
        return label;
    }

    private Button MakeButton(
        string text,
        int x,
        int y,
        int width,
        int height,
        ThemeColor fillColor,
        ThemeColor hoverColor,
        ThemeColor pressedColor,
        ThemeColor foreColor)
    {
        var button = new Button
        {
            Text = text,
            Location = P(x, y),
            Size = Sz(width, height),
            Font = Theme.ButtonFont,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };

        Theme.Bind(button, fillColor, foreColor, hoverColor, pressedColor);
        button.Resize += (_, _) => RoundControl(button, S(8));
        RoundControl(button, S(8));
        return button;
    }

    private static void RoundControl(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        using var path = Theme.RoundedRect(
            new Rectangle(0, 0, control.Width, control.Height),
            Math.Max(1, radius));
        var old = control.Region;
        control.Region = new Region(path);
        old?.Dispose();
    }

    private void SetupTray()
    {
        try
        {
            _appIcon = AppIcon.Create();
            Icon = _appIcon;

            _trayMenu = new ContextMenuStrip();
            var open = new ToolStripMenuItem("Open AcerFALDv2");
            open.Click += (_, _) => ShowFromTray();

            var reapply = new ToolStripMenuItem("Re-apply HDR / FALD");
            reapply.Click += (_, _) => RunFromTray(ReapplyAsync);

            var restore = new ToolStripMenuItem("Restore SDR");
            restore.Click += (_, _) => RunFromTray(RestoreSdrAsync);

            var reconnect = new ToolStripMenuItem("Reconnect to monitor");
            reconnect.Click += (_, _) => RunFromTray(ReconnectAsync);

            var theme = new ToolStripMenuItem(Theme.IsDark ? "Switch to light theme" : "Switch to dark theme");
            theme.Click += (_, _) => ToggleTheme();
            _themeMenuItem = theme;

            var exit = new ToolStripMenuItem("Exit");
            exit.Click += (_, _) => ExitApp();

            _trayMenu.Items.Add(open);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(reapply);
            _trayMenu.Items.Add(restore);
            _trayMenu.Items.Add(reconnect);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(theme);
            _trayMenu.Items.Add(exit);

            _tray = new NotifyIcon
            {
                Icon = _appIcon,
                Text = "AcerFALDv2 - HDR + FALD",
                Visible = true,
                ContextMenuStrip = _trayMenu,
                BalloonTipIcon = ToolTipIcon.Info
            };
            _tray.DoubleClick += (_, _) => ShowFromTray();
        }
        catch (Exception ex)
        {
            _tray = null;
            _trayMenu?.Dispose();
            _trayMenu = null;
            _statusLabel.Text = "Tray icon unavailable: " + ex.Message;
        }
    }

    private void ConnectEngine()
    {
        try
        {
            var monitor = MonitorDiscovery.Find(_config.ModelMatch);
            if (monitor == null)
            {
                _monitorModelLabel.Text = "No DDC/CI monitor yet";
                _monitorDetailLabel.Text = "Looking for the display again every few seconds…";
                return;
            }

            _engine = new FaldEngine(monitor, _config);
            ShowMonitorIdentity(monitor);

            try
            {
                _power = new PowerMonitor();
                _power.Start();

                // Registration can deliver the current display state as a "wake".
                // The console watcher discards it; the UI must do the same.
                _power.ConsumeWake();
            }
            catch
            {
                _power = null;
            }
        }
        catch (Exception ex)
        {
            _monitorModelLabel.Text = "Monitor setup failed";
            _monitorDetailLabel.Text = ex.Message;
        }
    }

    private void ShowMonitorIdentity(PhysicalMonitor monitor)
    {
        _monitorModelLabel.Text = monitor.Model ?? "DDC/CI monitor";
        _monitorDetailLabel.Text = $"{monitor.GdiDevice}  ·  {monitor.Description}";
        _monitorModelLabel.ForeColor = Theme.Text;
        _monitorDetailLabel.ForeColor = Theme.Muted;
    }

    /// <summary>
    /// Late engine creation for the case where the panel was not attached at
    /// startup. Cheap enough to call from the read timer; one attempt at a time.
    /// </summary>
    private void TryConnectEngine()
    {
        if (_engine != null || _connectingEngine) return;

        _connectingEngine = true;
        try
        {
            ConnectEngine();
            if (_engine == null) return;

            _pollTimer.Start();
            _ = InitializeEngineAsync();
        }
        finally
        {
            _connectingEngine = false;
        }
    }

    /// <summary>
    /// Re-acquire the DDC handle until the panel answers, or the timeout expires.
    /// Used after wake and for the tray reconnect action.
    /// </summary>
    private async Task<bool> EnsureEngineConnectedAsync(string reason, TimeSpan timeout)
    {
        var engine = _engine;
        if (engine == null) return false;

        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        var attempt = 0;
        while (true)
        {
            // Serialise with every other DDC operation: re-enumeration disposes the
            // old physical-monitor handle, which must not happen under a read.
            await _ddcGate.WaitAsync();
            bool connected;
            try
            {
                connected = await Task.Run(() =>
                    engine.EnsureConnected(reason, minIntervalMs: 0, force: attempt > 0));
            }
            finally
            {
                _ddcGate.Release();
            }

            if (connected) return true;
            if (Environment.TickCount64 >= deadline) return false;

            await Task.Delay(500);
            attempt++;
        }
    }

    private async Task ReconnectAsync()
    {
        if (!TryBeginBusy("Looking for the monitor again…")) return;

        try
        {
            if (_engine == null) ConnectEngine();

            var engine = _engine;
            if (engine == null)
            {
                UpdateStatus(
                    "No DDC/CI monitor found. Check the cable and that DDC/CI is enabled in the OSD.",
                    sticky: true);
                return;
            }

            bool ok;
            await _ddcGate.WaitAsync();
            try
            {
                ok = await Task.Run(() => engine.EnsureConnected("tray reconnect", minIntervalMs: 0, force: true));
            }
            finally
            {
                _ddcGate.Release();
            }

            if (!ok)
            {
                OnMonitorAway();
                UpdateStatus(
                    "The monitor is still not answering. Switch it back to this input with the joystick, then reconnect again.",
                    sticky: true);
                return;
            }

            if (_monitorAway) OnMonitorBack();
            else ShowMonitorIdentity(engine.Monitor);

            await RefreshStateAsync(updateSliders: true);
            UpdateStatus("Reconnected to the monitor.");
        }
        catch (Exception ex)
        {
            UpdateStatus("Reconnect failed: " + ex.Message, sticky: true);
        }
        finally
        {
            EndBusy();
        }
    }

    /// <summary>
    /// The panel stopped answering every VCP read. Most often it is showing
    /// another input, so keep the window usable and say so instead of leaving
    /// dead controls and silently failing writes.
    /// </summary>
    private void OnMonitorAway()
    {
        if (_monitorAway) return;
        _monitorAway = true;

        _monitorModelLabel.Text = "Monitor not answering";
        _monitorDetailLabel.Text = "Showing another input or asleep - waiting for it to come back…";
        _monitorModelLabel.ForeColor = Theme.Amber;
        _monitorDetailLabel.ForeColor = Theme.Amber;

        UpdateStatus(
            "Monitor is not answering on this input. Switch it back with the joystick; " +
            "the app reconnects on its own.",
            sticky: true);
        UpdateControlAvailability();
    }

    private void OnMonitorBack()
    {
        if (!_monitorAway) return;
        _monitorAway = false;
        _readFailureCount = 0;

        if (_engine != null) ShowMonitorIdentity(_engine.Monitor);
        UpdateStatus("Monitor is answering again.");
        UpdateControlAvailability();
    }

    private void InitializeSlidersAndStatus()
    {
        var hdr = _engine == null ? null : Hdr.IsEnabled(_engine.Monitor.GdiDevice);
        _lastHdr = hdr;

        var brightness = hdr == true ? _config.HdrBrightness : _config.SdrBrightness;
        var contrast = hdr == true ? _config.HdrContrast : _config.SdrContrast;

        SetSliderValueSilently(_brightnessSlider, _brightnessValueLabel, (int)brightness);
        SetSliderValueSilently(_contrastSlider, _contrastValueLabel, (int)contrast);

        var sdrWhite = NormalizeSdrWhite((int)Math.Clamp(_config.SdrWhiteNits, 80u, 480u));
        _config.SdrWhiteNits = (uint)sdrWhite;
        SetSliderValueSilently(_sdrWhiteSlider, _sdrWhiteValueLabel, sdrWhite, " nits");
        Volatile.Write(ref _sdrWhitePending, _sdrWhiteSlider.Value);
        Volatile.Write(ref _sdrWhiteApplied, _sdrWhiteSlider.Value);

        SetSliderValueSilently(_volumeSlider, _volumeValueLabel, 50);
        Volatile.Write(ref _volumePending, _volumeSlider.Value);
        Volatile.Write(ref _volumeApplied, _volumeSlider.Value);

        Volatile.Write(ref _brightnessPending, _brightnessSlider.Value);
        Volatile.Write(ref _brightnessApplied, _brightnessSlider.Value);
        Volatile.Write(ref _contrastPending, _contrastSlider.Value);
        Volatile.Write(ref _contrastApplied, _contrastSlider.Value);

        _autoSourceToggle.Checked = _config.AutoSwitchInput;
        UpdateAutoSourceHint();
        UpdateInputUi(null);

        UpdateHdrUi(hdr);
        UpdateStatus(
            _engine == null
                ? "No DDC/CI monitor found."
                : "Ready. Slider changes apply automatically.",
            _engine == null);
    }

    private void WireEvents()
    {
        Shown += OnShown;
        Resize += OnResize;
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;

        _brightnessSlider.ValueChanged += (_, _) => OnSliderChanged(isBrightness: true);
        _contrastSlider.ValueChanged += (_, _) => OnSliderChanged(isBrightness: false);
        _sdrWhiteSlider.ValueChanged += (_, _) => OnSdrWhiteChanged();

        _brightnessSlider.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _brightnessDragging = true;
        };
        _brightnessSlider.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _brightnessDragging = false;
            FlushBrightness();
        };
        _brightnessSlider.MouseCaptureChanged += (_, _) =>
        {
            if (!_brightnessDragging) return;
            _brightnessDragging = false;
            FlushBrightness();
        };

        _contrastSlider.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _contrastDragging = true;
        };
        _contrastSlider.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _contrastDragging = false;
            FlushContrast();
        };
        _contrastSlider.MouseCaptureChanged += (_, _) =>
        {
            if (!_contrastDragging) return;
            _contrastDragging = false;
            FlushContrast();
        };

        _sdrWhiteSlider.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _sdrWhiteDragging = true;
        };
        _sdrWhiteSlider.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _sdrWhiteDragging = false;
            FlushSdrWhite();
        };
        _sdrWhiteSlider.MouseCaptureChanged += (_, _) =>
        {
            if (!_sdrWhiteDragging) return;
            _sdrWhiteDragging = false;
            FlushSdrWhite();
        };

        _volumeSlider.ValueChanged += (_, _) => OnVolumeChanged();
        _volumeSlider.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _volumeDragging = true;
        };
        _volumeSlider.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _volumeDragging = false;
            FlushVolume();
        };
        _volumeSlider.MouseCaptureChanged += (_, _) =>
        {
            if (!_volumeDragging) return;
            _volumeDragging = false;
            FlushVolume();
        };

        _autoSourceToggle.Click += (_, _) => OnAutoSourceToggled();

        _brightnessDebounce.Tick += (_, _) =>
        {
            _brightnessDebounce.Stop();
            FlushBrightness();
        };
        _contrastDebounce.Tick += (_, _) =>
        {
            _contrastDebounce.Stop();
            FlushContrast();
        };
        _sdrWhiteDebounce.Tick += (_, _) =>
        {
            _sdrWhiteDebounce.Stop();
            FlushSdrWhite();
        };
        _volumeDebounce.Tick += (_, _) =>
        {
            _volumeDebounce.Stop();
            FlushVolume();
        };

        _toggleHdrButton.Click += async (_, _) => await SafeAsync(ToggleHdrAsync);
        _gammaButton.Click += (_, _) => ToggleGamma();
        _reapplyButton.Click += async (_, _) => await SafeAsync(ReapplyAsync);
        _restoreButton.Click += async (_, _) => await SafeAsync(RestoreSdrAsync);
        _openLogButton.Click += (_, _) => OpenLog();
        _exitButton.Click += (_, _) => ExitApp();

        _pollTimer.Tick += (_, _) => PollHdrState();
        _readTimer.Tick += (_, _) =>
        {
            // The panel came back on another input (or was plugged in late):
            // keep trying to build the engine so the window recovers without a restart.
            if (_engine == null)
            {
                TryConnectEngine();
                return;
            }

            if (Volatile.Read(ref _busy) != 0) return;
            if (Volatile.Read(ref _brightnessApplying) != 0 || Volatile.Read(ref _contrastApplying) != 0) return;
            if (Volatile.Read(ref _volumeApplying) != 0) return;
            if (_brightnessDragging || _contrastDragging || _volumeDragging) return;
            if (_brightnessDebounce.Enabled || _contrastDebounce.Enabled || _volumeDebounce.Enabled) return;

            // Slider values changed in the monitor's own OSD must flow back into
            // the window, otherwise the app and the panel drift apart.
            _ = RefreshStateAsync(updateSliders: true);
        };

        _statusResetTimer.Tick += (_, _) =>
        {
            _statusResetTimer.Stop();
            if (Volatile.Read(ref _busy) != 0) return;
            UpdateStatus(
                _engine == null
                    ? "No DDC/CI monitor found."
                    : "Ready. Slider changes apply automatically.");
        };
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (_shownOnce) return;
        _shownOnce = true;

        // Off-screen render harness: lay out the window without starting DDC work.
        if (Environment.GetEnvironmentVariable("FALDSYNC_UI_RENDER") == "1") return;

        _pollTimer.Start();
        _readTimer.Start();

        if (_engine == null)
        {
            // The panel may be switched to another input or still be enumerating;
            // the read timer keeps trying, so this is not fatal any more.
            UpdateStatus(
                "No DDC/CI monitor found yet - still looking. Connect the display or " +
                "switch it back with the joystick; AcerFALDv2 picks it up automatically.",
                sticky: true);
            return;
        }

        _ = InitializeEngineAsync();
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && _tray != null && !_reallyExit)
            HideToTray();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyExit) return;
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) return;
        if (_tray == null) return;

        e.Cancel = true;
        HideToTray();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _pollTimer.Stop();
        _readTimer.Stop();
        _brightnessDebounce.Stop();
        _contrastDebounce.Stop();
        _sdrWhiteDebounce.Stop();
        _volumeDebounce.Stop();
        _statusResetTimer.Stop();

        _pollTimer.Dispose();
        _readTimer.Dispose();
        _brightnessDebounce.Dispose();
        _contrastDebounce.Dispose();
        _sdrWhiteDebounce.Dispose();
        _volumeDebounce.Dispose();
        _statusResetTimer.Dispose();

        Theme.Changed -= OnThemeChanged;
        _toolTip.Dispose();

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;

        _power?.Dispose();
        _power = null;

        if (_engine != null)
            GammaRamp.Restore(_engine.Monitor.GdiDevice, _config.GammaPower);

        _engine?.Monitor.Dispose();
        _engine = null;

        _appIcon?.Dispose();
        _appIcon = null;

        _config.Save();
        _ddcGate.Dispose();
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            UpdateStatus("Operation failed: " + ex.Message, sticky: true);
        }
    }

    private void RunFromTray(Func<Task> action)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RunFromTray(action));
            return;
        }

        _ = SafeAsync(action);
    }

    private async Task InitializeEngineAsync()
    {
        if (_engine == null) return;
        if (!TryBeginBusy("Checking HDR and FALD…")) return;

        try
        {
            var hdr = Hdr.IsEnabled(_engine.Monitor.GdiDevice);
            _lastHdr = hdr;
            UpdateHdrUi(hdr);

            if (hdr == true)
            {
                _faldActive = null;
                await _ddcGate.WaitAsync();
                try
                {
                    var ok = await Task.Run(() => _engine.ApplyHdrOnWithRetry());
                    _faldActive = ok ? true : null;
                }
                finally
                {
                    _ddcGate.Release();
                }
            }
            else if (hdr == false)
            {
                var state = await ReadStateAsync();
                var looksLikeHdr = state?.HasMode == true && state.Value.Mode == _config.HdrPictureMode;

                if (looksLikeHdr)
                {
                    await _ddcGate.WaitAsync();
                    try
                    {
                        await Task.Run(() => _engine.ApplyHdrOffWithRetry());
                    }
                    finally
                    {
                        _ddcGate.Release();
                    }
                }

                _faldActive = false;

                // A previous run may have exited before restoring the scanout ramp.
                GammaRamp.Restore(_engine.Monitor.GdiDevice, _config.GammaPower);
            }
            else
            {
                _faldActive = null;
            }

            UpdateHdrUi(hdr);
            await RefreshStateAsync(updateSliders: true);

            if (_config.AutoSwitchInput)
                await EnsurePreferredInputAsync("startup");

            UpdateStatus(
                hdr switch
                {
                    true => "HDR is on. FALD armed; sliders are ready.",
                    false => "HDR is off. SDR controls are ready.",
                    _ => "HDR state is unreadable; AcerFALDv2 will keep checking."
                });
        }
        catch (Exception ex)
        {
            UpdateStatus("Startup sync failed: " + ex.Message, sticky: true);
        }
        finally
        {
            // Any display-state notification raised while the startup sequence was
            // running is stale now; do not immediately start a second recovery.
            _power?.ConsumeWake();
            EndBusy();
        }
    }

    private void PollHdrState()
    {
        var engine = _engine;
        if (engine == null || Volatile.Read(ref _busy) != 0) return;

        bool? hdr;
        try
        {
            hdr = Hdr.IsEnabled(engine.Monitor.GdiDevice);
        }
        catch
        {
            return;
        }

        if (hdr != null)
        {
            if (_lastHdr == null)
            {
                _lastHdr = hdr;
                UpdateHdrUi(hdr);
            }
            else if (hdr != _lastHdr)
            {
                _ = HandleHdrEdgeAsync(hdr.Value);
            }
        }

        if (_power?.ConsumeWake() == true)
            _ = HandleWakeAsync();

        // Windows can reset the scanout ramp after sleep, unlock, or opening
        // Display Settings. Check a few times a minute and re-apply if needed.
        if (++_gammaPollTicks >= 5)
        {
            _gammaPollTicks = 0;
            EnsureGamma();
        }
    }

    private void EnsureGamma()
    {
        var engine = _engine;
        if (engine == null || !_config.GammaFix || _lastHdr != true) return;
        if (Volatile.Read(ref _busy) != 0) return;

        var applied = GammaRamp.IsApplied(engine.Monitor.GdiDevice, _config.GammaPower);
        if (applied != false) return;

        if (GammaRamp.Apply(engine.Monitor.GdiDevice, _config.GammaPower))
        {
            if (_gammaErrorShown) UpdateStatus("Gamma 2.2 re-applied.");
            _gammaErrorShown = false;
            return;
        }

        if (_gammaErrorShown) return;
        _gammaErrorShown = true;
        UpdateStatus("Could not re-apply gamma 2.2. Toggle it off and on.", sticky: true);
    }

    private async Task HandleHdrEdgeAsync(bool hdrOn)
    {
        if (_engine == null) return;
        if (!TryBeginBusy(hdrOn ? "HDR on - forcing the HDR preset…" : "HDR off - restoring the User preset…"))
            return;

        try
        {
            _lastHdr = hdrOn;
            UpdateHdrUi(hdrOn);

            bool ok;
            await _ddcGate.WaitAsync();
            try
            {
                ok = await Task.Run(() => hdrOn
                    ? _engine.ApplyHdrOnWithRetry()
                    : _engine.ApplyHdrOffWithRetry());
            }
            finally
            {
                _ddcGate.Release();
            }

            _faldActive = hdrOn ? (ok ? true : null) : false;
            UpdateHdrUi(hdrOn);
            await RefreshStateAsync(updateSliders: true);
            UpdateStatus(
                ok
                    ? (hdrOn ? "HDR on - FALD armed." : "HDR off - User preset restored.")
                    : "HDR transition did not fully verify.",
                sticky: !ok);
        }
        catch (Exception ex)
        {
            UpdateStatus("HDR transition failed: " + ex.Message, sticky: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task HandleWakeAsync()
    {
        if (_engine == null) return;

        // The panel may still be waking when the power event arrives. Without this
        // guard the re-apply runs against a dead DDC handle and reports a failure
        // even though the screen comes back a second later.
        if (!await EnsureEngineConnectedAsync("display wake", TimeSpan.FromSeconds(8)))
        {
            UpdateStatus("Display woke, but the monitor does not answer DDC yet.", sticky: true);
            return;
        }

        if (!TryBeginBusy("Display wake - re-applying HDR and FALD…")) return;

        try
        {
            var hdr = Hdr.IsEnabled(_engine.Monitor.GdiDevice);
            _lastHdr = hdr;
            UpdateHdrUi(hdr);

            if (hdr == null)
            {
                UpdateStatus("Display woke, but the HDR state is unreadable.", sticky: true);
                return;
            }

            bool ok;
            await _ddcGate.WaitAsync();
            try
            {
                ok = await Task.Run(() => _engine.ReapplyAfterWake(hdr, "UI display wake"));
            }
            finally
            {
                _ddcGate.Release();
            }

            _faldActive = hdr == true ? (ok ? true : null) : false;
            _lastHdr = hdr;
            UpdateHdrUi(hdr);
            await RefreshStateAsync(updateSliders: true);

            if (_config.AutoSwitchInput)
                await EnsurePreferredInputAsync("display wake");

            UpdateStatus(
                ok
                    ? "Display wake - HDR and FALD re-applied."
                    : "Display wake recovery did not finish. Use Re-apply or Restore SDR.",
                sticky: !ok);
        }
        catch (Exception ex)
        {
            UpdateStatus("Display wake recovery failed: " + ex.Message, sticky: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task ToggleHdrAsync()
    {
        if (_engine == null) return;

        var current = Hdr.IsEnabled(_engine.Monitor.GdiDevice);
        if (current == null)
        {
            UpdateStatus("HDR state is unreadable; cannot toggle it.", sticky: true);
            return;
        }

        var target = !current.Value;
        if (!TryBeginBusy(target ? "Turning Windows HDR on…" : "Turning Windows HDR off…"))
            return;

        try
        {
            if (!Hdr.Set(_engine.Monitor.GdiDevice, target))
            {
                UpdateStatus("Could not change the Windows HDR state.", sticky: true);
                return;
            }

            await WaitForHdrStateAsync(target, 8000);

            bool ok;
            await _ddcGate.WaitAsync();
            try
            {
                ok = await Task.Run(() => target
                    ? _engine.ApplyHdrOnWithRetry()
                    : _engine.ApplyHdrOffWithRetry());
            }
            finally
            {
                _ddcGate.Release();
            }

            _lastHdr = target;
            _faldActive = target ? (ok ? true : null) : false;
            UpdateHdrUi(target);
            await RefreshStateAsync(updateSliders: true);
            UpdateStatus(
                ok
                    ? (target ? "HDR on - FALD armed." : "HDR off - User preset restored.")
                    : "HDR toggle did not fully verify.",
                sticky: !ok);
        }
        catch (Exception ex)
        {
            UpdateStatus("HDR toggle failed: " + ex.Message, sticky: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void ToggleGamma()
    {
        var engine = _engine;
        if (engine == null) return;

        var enable = !_config.GammaFix;
        _config.GammaFix = enable;
        _config.Save();
        _gammaErrorShown = false;

        if (!enable)
        {
            var restored = GammaRamp.Restore(engine.Monitor.GdiDevice, _config.GammaPower);
            UpdateGammaUi();
            UpdateStatus(
                restored
                    ? "Gamma 2.2 off; display ramp restored."
                    : "Gamma 2.2 off, but the display ramp could not be restored.",
                sticky: !restored);
            return;
        }

        if (_lastHdr == true)
        {
            var applied = GammaRamp.Apply(engine.Monitor.GdiDevice, _config.GammaPower);
            UpdateGammaUi();
            UpdateStatus(
                applied
                    ? "Gamma 2.2 applied to the HDR desktop."
                    : "Gamma 2.2 could not be applied to this display.",
                sticky: !applied);
        }
        else
        {
            UpdateGammaUi();
            UpdateStatus("Gamma 2.2 enabled; it will apply when HDR is on.");
        }
    }

    private async Task ReapplyAsync()
    {
        if (_engine == null) return;
        if (!TryBeginBusy("Re-applying HDR and FALD…")) return;

        try
        {
            var hdr = Hdr.IsEnabled(_engine.Monitor.GdiDevice);
            if (hdr == null)
            {
                UpdateStatus("HDR state is unreadable; cannot re-apply.", sticky: true);
                return;
            }

            _lastHdr = hdr;
            UpdateHdrUi(hdr);

            bool ok;
            await _ddcGate.WaitAsync();
            try
            {
                ok = await Task.Run(() => hdr.Value
                    ? _engine.ApplyHdrOnWithRetry()
                    : _engine.ApplyHdrOffWithRetry());
            }
            finally
            {
                _ddcGate.Release();
            }

            _faldActive = hdr.Value ? (ok ? true : null) : false;
            UpdateHdrUi(hdr);
            await RefreshStateAsync(updateSliders: true);
            UpdateStatus(
                ok ? "HDR and FALD re-applied." : "Re-apply did not fully verify.",
                sticky: !ok);
        }
        catch (Exception ex)
        {
            UpdateStatus("Re-apply failed: " + ex.Message, sticky: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task RestoreSdrAsync()
    {
        if (_engine == null) return;
        if (!TryBeginBusy("Restoring the SDR / User preset…")) return;

        try
        {
            var hdr = Hdr.IsEnabled(_engine.Monitor.GdiDevice);
            if (hdr != false)
            {
                if (!Hdr.Set(_engine.Monitor.GdiDevice, false))
                {
                    UpdateStatus("Could not turn Windows HDR off.", sticky: true);
                    return;
                }
                await WaitForHdrStateAsync(false, 8000);
            }

            bool ok;
            await _ddcGate.WaitAsync();
            try
            {
                ok = await Task.Run(() => _engine.ApplyHdrOffWithRetry());
            }
            finally
            {
                _ddcGate.Release();
            }

            _lastHdr = false;
            _faldActive = false;
            UpdateHdrUi(false);
            await RefreshStateAsync(updateSliders: true);
            UpdateStatus(
                ok ? "SDR / User preset restored." : "SDR restore did not fully verify.",
                sticky: !ok);
        }
        catch (Exception ex)
        {
            UpdateStatus("SDR restore failed: " + ex.Message, sticky: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task<bool> WaitForHdrStateAsync(bool target, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var engine = _engine;
            if (engine == null) return false;
            if (Hdr.IsEnabled(engine.Monitor.GdiDevice) == target) return true;
            await Task.Delay(200);
        }
        return false;
    }

    private void OnSliderChanged(bool isBrightness)
    {
        if (_suppressSliderEvents) return;

        if (isBrightness)
        {
            var value = _brightnessSlider.Value;
            _brightnessValueLabel.Text = $"{value}%";
            Volatile.Write(ref _brightnessPending, value);
            if (_lastHdr == true) _config.HdrBrightness = (uint)value;
            else _config.SdrBrightness = (uint)value;

            _brightnessDebounce.Stop();
            _brightnessDebounce.Start();
        }
        else
        {
            var value = _contrastSlider.Value;
            _contrastValueLabel.Text = $"{value}%";
            Volatile.Write(ref _contrastPending, value);
            if (_lastHdr == true) _config.HdrContrast = (uint)value;
            else _config.SdrContrast = (uint)value;

            _contrastDebounce.Stop();
            _contrastDebounce.Start();
        }
    }

    private void OnSdrWhiteChanged()
    {
        if (_suppressSliderEvents) return;

        var value = NormalizeSdrWhite(_sdrWhiteSlider.Value);
        if (value != _sdrWhiteSlider.Value)
            SetSliderValueSilently(_sdrWhiteSlider, _sdrWhiteValueLabel, value, " nits");
        else
            _sdrWhiteValueLabel.Text = $"{value} nits";

        Volatile.Write(ref _sdrWhitePending, value);
        _config.SdrWhiteNits = (uint)value;

        _sdrWhiteDebounce.Stop();
        _sdrWhiteDebounce.Start();
    }

    private void FlushSdrWhite()
    {
        _sdrWhiteDebounce.Stop();
        if (_engine == null || Volatile.Read(ref _busy) != 0) return;
        _ = ApplySdrWhiteAsync();
    }

    private void OnVolumeChanged()
    {
        if (_suppressSliderEvents) return;

        var value = _volumeSlider.Value;
        _volumeValueLabel.Text = $"{value}%";
        Volatile.Write(ref _volumePending, value);

        _volumeDebounce.Stop();
        _volumeDebounce.Start();
    }

    private void FlushVolume()
    {
        _volumeDebounce.Stop();
        if (_engine == null || Volatile.Read(ref _busy) != 0) return;
        _ = ApplyVolumeAsync();
    }

    private async Task ApplyVolumeAsync()
    {
        var engine = _engine;
        if (engine == null) return;
        if (Interlocked.CompareExchange(ref _volumeApplying, 1, 0) != 0) return;

        var failed = false;
        try
        {
            while (true)
            {
                var value = Volatile.Read(ref _volumePending);
                if (value == Volatile.Read(ref _volumeApplied)) break;

                bool ok;
                await _ddcGate.WaitAsync();
                try
                {
                    ok = await Task.Run(() => engine.SetVolume((uint)value));
                }
                finally
                {
                    _ddcGate.Release();
                }

                if (!ok)
                {
                    failed = true;
                    UpdateStatus("Volume write did not verify.", sticky: true);
                    break;
                }

                Volatile.Write(ref _volumeApplied, value);
                UpdateStatus($"Volume {value}% applied.");

                if (Volatile.Read(ref _volumePending) == value) break;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            UpdateStatus("Volume failed: " + ex.Message, sticky: true);
        }
        finally
        {
            Interlocked.Exchange(ref _volumeApplying, 0);
            if (!failed && _engine != null && !_volumeDebounce.Enabled &&
                _volumeSlider.Value != Volatile.Read(ref _volumeApplied))
            {
                _ = ApplyVolumeAsync();
            }
        }
    }

    private void FlushBrightness()
    {
        _brightnessDebounce.Stop();
        if (_engine == null || Volatile.Read(ref _busy) != 0) return;
        _ = ApplyBrightnessAsync();
    }

    private void FlushContrast()
    {
        _contrastDebounce.Stop();
        if (_engine == null || Volatile.Read(ref _busy) != 0) return;
        _ = ApplyContrastAsync();
    }

    private async Task ApplyBrightnessAsync()
    {
        var engine = _engine;
        if (engine == null) return;
        if (Interlocked.CompareExchange(ref _brightnessApplying, 1, 0) != 0) return;

        var failed = false;
        try
        {
            while (true)
            {
                var value = Volatile.Read(ref _brightnessPending);
                if (value == Volatile.Read(ref _brightnessApplied)) break;

                bool ok;
                await _ddcGate.WaitAsync();
                try
                {
                    ok = await Task.Run(() => engine.SetBrightness((uint)value));
                }
                finally
                {
                    _ddcGate.Release();
                }

                if (!ok)
                {
                    failed = true;
                    UpdateStatus("Brightness write did not verify.", sticky: true);
                    break;
                }

                Volatile.Write(ref _brightnessApplied, value);
                _config.Save();
                UpdateStatus($"Brightness {value}% applied.");

                if (Volatile.Read(ref _brightnessPending) == value) break;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            UpdateStatus("Brightness failed: " + ex.Message, sticky: true);
        }
        finally
        {
            Interlocked.Exchange(ref _brightnessApplying, 0);
            if (!failed && _engine != null && !_brightnessDebounce.Enabled &&
                _brightnessSlider.Value != Volatile.Read(ref _brightnessApplied))
            {
                _ = ApplyBrightnessAsync();
            }
        }
    }

    private async Task ApplyContrastAsync()
    {
        var engine = _engine;
        if (engine == null) return;
        if (Interlocked.CompareExchange(ref _contrastApplying, 1, 0) != 0) return;

        var failed = false;
        try
        {
            while (true)
            {
                var value = Volatile.Read(ref _contrastPending);
                if (value == Volatile.Read(ref _contrastApplied)) break;

                bool ok;
                await _ddcGate.WaitAsync();
                try
                {
                    ok = await Task.Run(() => engine.SetContrast((uint)value));
                }
                finally
                {
                    _ddcGate.Release();
                }

                if (!ok)
                {
                    failed = true;
                    UpdateStatus("Contrast write did not verify.", sticky: true);
                    break;
                }

                Volatile.Write(ref _contrastApplied, value);
                _config.Save();
                UpdateStatus($"Contrast {value}% applied.");

                if (Volatile.Read(ref _contrastPending) == value) break;
            }
        }
        catch (Exception ex)
        {
            failed = true;
            UpdateStatus("Contrast failed: " + ex.Message, sticky: true);
        }
        finally
        {
            Interlocked.Exchange(ref _contrastApplying, 0);
            if (!failed && _engine != null && !_contrastDebounce.Enabled &&
                _contrastSlider.Value != Volatile.Read(ref _contrastApplied))
            {
                _ = ApplyContrastAsync();
            }
        }
    }

    private async Task ApplySdrWhiteAsync()
    {
        var engine = _engine;
        if (engine == null) return;
        if (Interlocked.CompareExchange(ref _sdrWhiteApplying, 1, 0) != 0) return;

        var failed = false;
        try
        {
            while (true)
            {
                var value = Volatile.Read(ref _sdrWhitePending);
                if (value == Volatile.Read(ref _sdrWhiteApplied)) break;

                _config.SdrWhiteNits = (uint)value;

                // The Windows SDR white level only means anything while HDR is on.
                // Save the target and let ApplyHdrOn apply it after the next HDR entry.
                if (_lastHdr != true)
                {
                    _config.Save();
                    Volatile.Write(ref _sdrWhiteApplied, value);
                    UpdateStatus($"SDR white saved at {value} nits; it will apply when HDR is on.");
                    if (Volatile.Read(ref _sdrWhitePending) == value) break;
                    continue;
                }

                bool ok;
                try
                {
                    var gdiDevice = engine.Monitor.GdiDevice;
                    ok = await Task.Run(() => Hdr.SetSdrWhiteNits(gdiDevice, (uint)value));
                }
                catch (Exception ex)
                {
                    failed = true;
                    UpdateStatus("SDR white failed: " + ex.Message, sticky: true);
                    break;
                }

                if (!ok)
                {
                    failed = true;
                    _config.Save();
                    Volatile.Write(ref _sdrWhiteApplied, value);
                    UpdateStatus("SDR white could not be changed. Try again after HDR settles.", sticky: true);
                    break;
                }

                _config.Save();
                Volatile.Write(ref _sdrWhiteApplied, value);
                UpdateStatus($"SDR white {value} nits applied.");

                if (Volatile.Read(ref _sdrWhitePending) == value) break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sdrWhiteApplying, 0);
            if (!failed && _engine != null && !_sdrWhiteDebounce.Enabled &&
                _sdrWhiteSlider.Value != Volatile.Read(ref _sdrWhiteApplied))
            {
                _ = ApplySdrWhiteAsync();
            }
        }
    }

    private async Task RefreshStateAsync(bool updateSliders)
    {
        try
        {
            var state = await ReadStateAsync();
            if (state == null) return;              // the DDC gate was busy; skip this round

            if (state.Value.HasAnyValue)
            {
                _readFailureCount = 0;
                if (_monitorAway) OnMonitorBack();
                ApplyMonitorState(state.Value, updateSliders);
                return;
            }

            // Nothing answered. One cycle can be a transient collision with a
            // write; two in a row means the panel is gone or the handle is stale.
            _readFailureCount++;
            if (_readFailureCount >= 2) OnMonitorAway();
        }
        catch (Exception ex)
        {
            UpdateStatus("Monitor read failed: " + ex.Message, sticky: true);
        }
    }

    private Task<MonitorState?> ReadStateAsync()
    {
        var engine = _engine;
        if (engine == null) return Task.FromResult<MonitorState?>(null);

        return Task.Run(async () =>
        {
            if (!await _ddcGate.WaitAsync(0).ConfigureAwait(false))
                return (MonitorState?)null;

            try
            {
                var state = ReadMonitorState(engine.Monitor);
                if (state.HasAnyValue) return state;

                // The handle is stale in almost every real "no answer" case: the
                // panel was switched to another source in its OSD, or the display
                // was re-enumerated by the driver. Swap it and ask once more.
                if (!engine.EnsureConnected("monitor read", minIntervalMs: 3000))
                    return state;

                Thread.Sleep(250);
                return ReadMonitorState(engine.Monitor);
            }
            finally
            {
                _ddcGate.Release();
            }
        });
    }

    private static MonitorState ReadMonitorState(PhysicalMonitor monitor)
    {
        var hasBrightness = monitor.TryGet(BrightnessVcp, out var brightness);
        var hasContrast = monitor.TryGet(ContrastVcp, out var contrast);
        var hasMode = monitor.TryGet(PictureModeVcp, out var mode);

        // Give the panel a moment between feature reads: this firmware
        // aliases values when several VCP requests arrive back to back.
        Thread.Sleep(40);
        var hasInput = monitor.TryGet(InputVcp, out var input);
        Thread.Sleep(40);
        var hasVolume = monitor.TryGet(VolumeVcp, out var volume);

        return new MonitorState(
            hasBrightness,
            (int)brightness,
            hasContrast,
            (int)contrast,
            hasMode,
            mode,
            hasInput,
            input,
            hasVolume,
            (int)volume);
    }

    private void ApplyMonitorState(MonitorState state, bool updateSliders)
    {
        if (state.HasMode) _pictureMode = state.Mode;

        if (updateSliders)
        {
            if (state.HasBrightness && !_brightnessDragging && !_brightnessDebounce.Enabled &&
                Volatile.Read(ref _brightnessApplying) == 0)
            {
                SetSliderValueSilently(_brightnessSlider, _brightnessValueLabel, state.Brightness);
                Volatile.Write(ref _brightnessPending, _brightnessSlider.Value);
                Volatile.Write(ref _brightnessApplied, _brightnessSlider.Value);
                if (_lastHdr == true) _config.HdrBrightness = (uint)_brightnessSlider.Value;
                else if (_lastHdr == false) _config.SdrBrightness = (uint)_brightnessSlider.Value;
            }

            if (state.HasContrast && !_contrastDragging && !_contrastDebounce.Enabled &&
                Volatile.Read(ref _contrastApplying) == 0)
            {
                SetSliderValueSilently(_contrastSlider, _contrastValueLabel, state.Contrast);
                Volatile.Write(ref _contrastPending, _contrastSlider.Value);
                Volatile.Write(ref _contrastApplied, _contrastSlider.Value);
                if (_lastHdr == true) _config.HdrContrast = (uint)_contrastSlider.Value;
                else if (_lastHdr == false) _config.SdrContrast = (uint)_contrastSlider.Value;
            }

            if (state.HasVolume && !_volumeDragging && !_volumeDebounce.Enabled &&
                Volatile.Read(ref _volumeApplying) == 0)
            {
                SetSliderValueSilently(_volumeSlider, _volumeValueLabel, state.Volume);
                Volatile.Write(ref _volumePending, _volumeSlider.Value);
                Volatile.Write(ref _volumeApplied, _volumeSlider.Value);
            }
        }

        if (state.HasInput)
        {
            _currentInput = state.Input;
            UpdateInputUi(_currentInput);
        }

        if (_lastHdr == true && _faldActive == null &&
            state.HasMode && state.Mode == _config.HdrPictureMode)
        {
            _faldActive = true;
        }

        UpdateHdrUi(_lastHdr);
    }

    private static int NormalizeSdrWhite(int nits)
    {
        nits = Math.Clamp(nits, 80, 480);
        return ((nits + 2) / 4) * 4;
    }

    private void SetSliderValueSilently(ModernSlider slider, Label label, int value, string suffix = "%")
    {
        var clamped = Math.Clamp(value, slider.Minimum, slider.Maximum);
        _suppressSliderEvents = true;
        try
        {
            slider.Value = clamped;
        }
        finally
        {
            _suppressSliderEvents = false;
        }
        label.Text = $"{clamped}{suffix}";
    }

    private void UpdateHdrUi(bool? hdr)
    {
        _lastHdr = hdr;

        var isOn = hdr == true;
        var isKnown = hdr != null;

        _headerPill.Text = isOn ? "HDR ON" : isKnown ? "SDR" : "UNKNOWN";
        _headerPill.BackColor = isOn
            ? Theme.Get(ThemeColor.Accent)
            : isKnown
                ? Theme.Get(ThemeColor.HeaderPill)
                : Theme.Get(ThemeColor.HeaderPillMuted);
        _headerPill.ForeColor = isOn
            ? Theme.Get(ThemeColor.OnAccent)
            : isKnown
                ? Theme.Get(ThemeColor.HeaderPillText)
                : Theme.Get(ThemeColor.HeaderPillMutedText);
        _headerPill.Invalidate();

        _hdrStatePill.Text = isOn ? "HDR ON" : isKnown ? "SDR" : "UNKNOWN";
        _hdrStatePill.BackColor = isOn
            ? Theme.Get(ThemeColor.GreenSoft)
            : isKnown
                ? Theme.Get(ThemeColor.AmberSoft)
                : Theme.Get(ThemeColor.GraySoft);
        _hdrStatePill.ForeColor = isOn
            ? Theme.Get(ThemeColor.Green)
            : isKnown
                ? Theme.Get(ThemeColor.Amber)
                : Theme.Get(ThemeColor.Muted);
        _hdrStatePill.Invalidate();

        _hdrValueLabel.Text = isOn ? "On" : isKnown ? "Off" : "Unreadable";
        _hdrValueLabel.ForeColor = isOn ? Theme.Green : isKnown ? Theme.Text : Theme.Amber;

        _toggleHdrButton.Text = isOn ? "Turn HDR off" : "Turn HDR on";

        if (_pictureMode is uint mode)
        {
            if (isOn)
                _modeValueLabel.Text = $"HDR preset (0x14={mode})";
            else if (mode == _config.HdrPictureMode)
                _modeValueLabel.Text = $"HDR preset - stale (0x14={mode})";
            else if (_config.SdrDisplayMode == 1)
                _modeValueLabel.Text = $"Standard preset (0x14={mode})";
            else
                _modeValueLabel.Text = $"User preset (0x14={mode})";
        }
        else
        {
            _modeValueLabel.Text = "Unknown (0x14=?)";
        }

        if (!isKnown)
        {
            _faldValueLabel.Text = "Unknown";
            _faldValueLabel.ForeColor = Theme.Amber;
        }
        else if (!isOn)
        {
            _faldValueLabel.Text = "Off (SDR signal)";
            _faldValueLabel.ForeColor = Theme.Muted;
        }
        else if (_faldActive == true)
        {
            _faldValueLabel.Text = "Active (latched)";
            _faldValueLabel.ForeColor = Theme.Green;
        }
        else if (_faldActive == false)
        {
            _faldValueLabel.Text = "Not armed";
            _faldValueLabel.ForeColor = Theme.Red;
        }
        else
        {
            _faldValueLabel.Text = "Checking…";
            _faldValueLabel.ForeColor = Theme.Amber;
        }

        UpdateGammaUi();
    }

    private void UpdateGammaUi()
    {
        var on = _config.GammaFix;

        _gammaButton.Text = on ? "GAMMA 2.2: ON" : "GAMMA 2.2: OFF";
        _gammaButton.BackColor = on ? Theme.Accent : Theme.Secondary;
        _gammaButton.FlatAppearance.MouseOverBackColor = on ? Theme.AccentHover : Theme.SecondaryHover;
        _gammaButton.FlatAppearance.MouseDownBackColor = on ? Theme.AccentPressed : Theme.SecondaryPressed;
        _gammaButton.ForeColor = on ? Theme.OnAccent : Theme.Text;
        _gammaButton.Invalidate();

        _gammaHintLabel.Text = !on
            ? "Off - Windows sRGB shadows"
            : _lastHdr == true
                ? "Applied - deeper SDR shadows"
                : "Applies while HDR is on";
    }

    private void OnThemeChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(OnThemeChanged);
            return;
        }

        BackColor = Theme.Background;
        Theme.Apply(this);
        ApplyWindowTheme();

        _themeButton.Text = Theme.IsDark ? "LIGHT" : "DARK";
        UpdateThemeButton();
        if (_themeMenuItem != null)
            _themeMenuItem.Text = Theme.IsDark ? "Switch to light theme" : "Switch to dark theme";

        UpdateHdrUi(_lastHdr);
        UpdateGammaUi();
        UpdateInputUi(_currentInput);
        UpdateAutoSourceHint();
        Invalidate(true);
    }

    private void UpdateThemeButton()
    {
        if (_themeButton == null) return;

        var target = Theme.IsDark ? "light" : "dark";
        _themeButton.Text = target.ToUpperInvariant();
        _themeButton.AccessibleName = $"Switch to {target} theme";
        _toolTip.SetToolTip(_themeButton, $"Switch to the {target} theme");
    }

    private void ToggleTheme()
    {
        var dark = !Theme.IsDark;
        _config.Theme = dark ? "dark" : "light";
        _config.Save();
        Theme.SetDark(dark);
        UpdateStatus(dark ? "Dark theme on." : "Light theme on.");
    }

    private void UpdateInputUi(uint? current)
    {
        _currentInput = current;

        foreach (var pair in _inputButtons)
        {
            var active = current == pair.Key;
            Theme.Bind(
                pair.Value,
                active ? ThemeColor.Accent : ThemeColor.Secondary,
                active ? ThemeColor.OnAccent : ThemeColor.Text,
                active ? ThemeColor.AccentHover : ThemeColor.SecondaryHover,
                active ? ThemeColor.AccentPressed : ThemeColor.SecondaryPressed);
        }
    }

    private void UpdateAutoSourceHint()
    {
        if (_autoSourceHintLabel == null) return;

        _autoSourceHintLabel.Text = _config.AutoSwitchInput
            ? $"Re-selects {MonitorInputs.ShortName(_config.SelectedInput)} after wake"
            : "Off";
    }

    private void OnAutoSourceToggled()
    {
        _config.AutoSwitchInput = _autoSourceToggle.Checked;

        // Turning the feature on pins the input the panel is actually on. Forcing a
        // stored DP while the user is looking at HDMI 2 would switch the screen away
        // from them on the next wake; to target another input, click it first.
        if (_config.AutoSwitchInput && _currentInput is { } current)
            _config.SelectedInput = current;

        _config.Save();
        UpdateAutoSourceHint();

        if (_config.AutoSwitchInput)
        {
            UpdateStatus(
                $"Auto switch on; {MonitorInputs.Name(_config.SelectedInput)} will be re-selected after wake.");
            _ = SafeAsync(() => EnsurePreferredInputAsync("auto switch enabled"));
        }
        else
        {
            UpdateStatus("Auto switch off. The monitor's own Auto Source setting is unchanged.");
        }
    }

    private async Task EnsurePreferredInputAsync(string reason)
    {
        var engine = _engine;
        if (engine == null || !_config.AutoSwitchInput) return;

        await _ddcGate.WaitAsync();
        try
        {
            var selected = _config.SelectedInput;
            var ok = await Task.Run(() => engine.EnsureInputSource(selected, reason));
            if (ok && _currentInput != selected)
            {
                _currentInput = selected;
                UpdateInputUi(_currentInput);
            }
        }
        finally
        {
            _ddcGate.Release();
        }
    }

    private async Task SwitchInputAsync(uint value)
    {
        var engine = _engine;
        if (engine == null)
        {
            UpdateStatus("No monitor connection yet; the app is still looking for the display.", sticky: true);
            return;
        }

        if (_currentInput == value && !_monitorAway)
        {
            // Clicking the input we are already on is how you choose it as the
            // auto-switch target, so record it even though nothing is written.
            _config.SelectedInput = value;
            _config.Save();
            UpdateAutoSourceHint();
            UpdateStatus($"Monitor is already on {MonitorInputs.Name(value)}.");
            return;
        }

        // A poll or HDR re-apply may hold the operation lock for a moment. Wait
        // briefly instead of silently ignoring the click.
        if (!await WaitForIdleAsync(TimeSpan.FromSeconds(5)))
        {
            UpdateStatus("Another command is still running; try the input switch again in a moment.", sticky: true);
            return;
        }

        var name = MonitorInputs.Name(value);
        var answer = MessageBox.Show(
            this,
            $"Switch the monitor to {name}?\r\n\r\n" +
            "If nothing is connected to that input, the screen will go blank. " +
            "Use the monitor joystick to switch back.",
            "Change monitor input",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        if (!TryBeginBusy($"Switching monitor input to {name}…"))
        {
            UpdateStatus("Another command is still running; try the input switch again in a moment.", sticky: true);
            return;
        }

        try
        {
            bool ok;
            if (!await _ddcGate.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                UpdateStatus("The monitor is busy; the input was not changed. Try again.", sticky: true);
                return;
            }

            try
            {
                ok = await Task.Run(() => engine.SetInputSource(value));
            }
            finally
            {
                _ddcGate.Release();
            }

            if (ok)
            {
                _config.SelectedInput = value;
                _config.Save();
                _currentInput = value;
                _monitorAway = false;
                _readFailureCount = 0;
                UpdateInputUi(_currentInput);
                UpdateAutoSourceHint();
                UpdateStatus($"Monitor input set to {name}.");
            }
            else
            {
                // DDC/CI cannot report why a write failed. The common case is a
                // stale handle after the panel re-enumerated, which the engine
                // already retries once, so point at the joystick rather than
                // claiming the panel refused.
                UpdateStatus(
                    $"Could not confirm the switch to {name}. If the screen is on another input, " +
                    "use the monitor joystick; the app reconnects automatically.",
                    sticky: true);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Input change failed: " + ex.Message, sticky: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private async Task<bool> WaitForIdleAsync(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Volatile.Read(ref _busy) != 0)
        {
            if (Environment.TickCount64 >= deadline) return false;
            await Task.Delay(100);
        }
        return true;
    }

    private bool TryBeginBusy(string message)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
        UpdateControlAvailability();
        UpdateStatus(message, sticky: true);
        return true;
    }

    private void EndBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
        UpdateControlAvailability();

        if (_engine == null) return;

        if (!_brightnessDebounce.Enabled && Volatile.Read(ref _brightnessApplying) == 0 &&
            _brightnessSlider.Value != Volatile.Read(ref _brightnessApplied))
        {
            _ = ApplyBrightnessAsync();
        }

        if (!_contrastDebounce.Enabled && Volatile.Read(ref _contrastApplying) == 0 &&
            _contrastSlider.Value != Volatile.Read(ref _contrastApplied))
        {
            _ = ApplyContrastAsync();
        }

        if (!_sdrWhiteDebounce.Enabled && Volatile.Read(ref _sdrWhiteApplying) == 0 &&
            _sdrWhiteSlider.Value != Volatile.Read(ref _sdrWhiteApplied))
        {
            _ = ApplySdrWhiteAsync();
        }

        if (!_volumeDebounce.Enabled && Volatile.Read(ref _volumeApplying) == 0 &&
            _volumeSlider.Value != Volatile.Read(ref _volumeApplied))
        {
            _ = ApplyVolumeAsync();
        }
    }

    private void UpdateControlAvailability()
    {
        var connected = _engine != null;
        var ready = connected && Volatile.Read(ref _busy) == 0 && !_monitorAway;
        _brightnessSlider.Enabled = ready;
        _contrastSlider.Enabled = ready;
        _sdrWhiteSlider.Enabled = ready;
        _volumeSlider.Enabled = ready;
        _autoSourceToggle.Enabled = ready;
        _toggleHdrButton.Enabled = ready;
        _gammaButton.Enabled = ready;
        _reapplyButton.Enabled = ready;
        _restoreButton.Enabled = ready;
        _openLogButton.Enabled = true;
        _exitButton.Enabled = true;
        _themeButton.Enabled = true;

        // Input buttons stay live whenever a monitor object exists: switching back
        // is exactly what you need while the panel is showing another source and
        // every other VCP write would fail.
        foreach (var button in _inputButtons.Values) button.Enabled = connected;
    }

    private void UpdateStatus(string text, bool sticky = false)
    {
        _statusLabel.Text = text;
        _statusResetTimer.Stop();
        if (!sticky) _statusResetTimer.Start();
    }

    private void OpenLog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "faldsync.log");
        if (!File.Exists(path))
        {
            UpdateStatus("No log file exists yet.", sticky: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateStatus("Could not open the log: " + ex.Message, sticky: true);
        }
    }

    private void HideToTray()
    {
        if (_tray == null) return;

        Hide();
        ShowInTaskbar = false;

        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray.BalloonTipTitle = "AcerFALDv2 is still running";
        _tray.BalloonTipText = "Use the notification-area icon to reopen the controls.";
        _tray.ShowBalloonTip(2500);
    }

    private void ShowFromTray()
    {
        if (IsDisposed) return;

        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Show();
        Activate();
        BringToFront();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        if (_tray != null) _tray.Visible = false;
        Close();
        Application.Exit();
    }

    private readonly record struct MonitorState(
        bool HasBrightness,
        int Brightness,
        bool HasContrast,
        int Contrast,
        bool HasMode,
        uint Mode,
        bool HasInput,
        uint Input,
        bool HasVolume,
        int Volume)
    {
        /// <summary>True when at least one VCP read came back; false means the
        /// panel answered nothing at all, which is the "on another input or asleep"
        /// case rather than a single unsupported code.</summary>
        public bool HasAnyValue => HasBrightness || HasContrast || HasMode || HasInput || HasVolume;
    }
}
