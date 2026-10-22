using System.Drawing;
using SpacePilotBridge.Core;
using SpacePilotBridge.Lcd;

namespace SpacePilotBridge.App;

internal sealed class SettingsForm : Form
{
    private static readonly Color Back = Color.FromArgb(24, 26, 29);
    private static readonly Color PanelBack = Color.FromArgb(31, 34, 38);
    private static readonly Color Fore = Color.FromArgb(232, 235, 238);
    private static readonly Color Muted = Color.FromArgb(155, 161, 168);

    private readonly ButtonActionEngine _engine;
    private readonly BridgeSettings _working;
    private readonly Func<DiagnosticsSnapshot> _diagnosticsProvider;
    private readonly System.Windows.Forms.Timer _diagnosticsTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _autoSaveTimer = new() { Interval = 300 };

    private readonly ComboBox _profile = new();
    private readonly ListBox _buttonList = new();
    private readonly ComboBox _behavior = new();
    private readonly ComboBox _assignment = new();
    private readonly Label _defaultTarget = new();
    private readonly Label _liveButton = new();
    private readonly Label _savedLabel = new();

    private readonly NumericUpDown _sensitivity = new();
    private readonly NumericUpDown _step = new();
    private readonly CheckBox _dominant = new();
    private readonly CheckBox _rotation = new();
    private readonly ComboBox _ledPattern = new();
    private readonly CheckBox _startMinimized = new();
    private readonly CheckBox _silent = new();
    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _gamepad = new();
    private readonly ComboBox _leftCarrier = new();
    private readonly ComboBox _backCarrier = new();
    private readonly NumericUpDown _viewHold = new();

    private readonly ListBox _screensavers = new();
    private readonly ComboBox _defaultScreensaver = new();
    private readonly CheckBox _idleSaver = new();
    private readonly NumericUpDown _idleSeconds = new();
    private readonly CheckBox _playlistEnabled = new();
    private readonly NumericUpDown _playlistSeconds = new();
    private readonly CheckBox _playlistShuffle = new();
    private readonly CheckedListBox _playlist = new();
    private readonly ComboBox _dither = new();
    private readonly NumericUpDown _fullRefresh = new();
    private readonly ComboBox[] _widgetSlots = { new(), new(), new(), new() };

    private readonly ComboBox _editorProfile = new();
    private readonly CheckBox _editorEnabled = new() { Text = "Enable mouse/keyboard/editor controls for this profile", AutoSize = true };
    private readonly ComboBox _panStyle = new();
    private readonly ComboBox _rotateStyle = new();
    private readonly CheckBox _lockHorizon = new() { Text = "Lock horizon for native 3D motion (suppress Ry roll)", AutoSize = true };
    private readonly Dictionary<string, AxisEditorRow> _axisRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _newProfileName = new();

    private readonly ListView _apps = new();
    private readonly ComboBox _appProfile = new();
    private readonly Label _appDetails = new();
    private readonly Label _appActive = new();
    private string? _selectedAppKey;
    private string _appsSignature = string.Empty;

    private readonly Label _diag = new();

    private bool _loading;
    private int _selectedRaw = -1;
    private string _selectedButtonProfile = "global";
    private string _selectedEditorProfile = "global";

    public event EventHandler? Saved;

    public SettingsForm(ButtonActionEngine engine, Func<DiagnosticsSnapshot> diagnosticsProvider)
    {
        _engine = engine;
        _working = SettingsStore.Clone(engine.Settings);
        _diagnosticsProvider = diagnosticsProvider;

        Text = "SpacePilotBridge 0.9.2";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(900, 650);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = TrayIconFactory.CreateIcon();
        BackColor = Back;
        ForeColor = Fore;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildButtonsTab());
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildLcdTab());
        tabs.TabPages.Add(BuildAppsTab());
        tabs.TabPages.Add(BuildEditingTab());
        tabs.TabPages.Add(BuildDiagnosticsTab());

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 46, BackColor = PanelBack, ColumnCount = 2, Padding = new Padding(10, 7, 10, 7)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _savedLabel.Text = "Changes save automatically";
        _savedLabel.AutoSize = true;
        _savedLabel.Anchor = AnchorStyles.Left;
        _savedLabel.ForeColor = Muted;
        var close = MakeButton("Close", 90, (_, _) => Close());
        close.Anchor = AnchorStyles.Right;
        bottom.Controls.Add(_savedLabel, 0, 0);
        bottom.Controls.Add(close, 1, 0);

        Controls.Add(tabs);
        Controls.Add(bottom);

        RefreshProfileCombos();
        LoadGeneral();
        RefreshScreensavers();
        _profile.SelectedIndex = 0;
        _buttonList.SelectedIndex = 0;
        ApplyDarkTheme(this);
        BindAutoSaveEvents();

        _autoSaveTimer.Tick += (_, _) => { _autoSaveTimer.Stop(); SaveAll(); };
        _diagnosticsTimer.Tick += (_, _) => RefreshDiagnostics();
        _diagnosticsTimer.Start();
        FormClosing += (_, _) => { if (!_loading) { _autoSaveTimer.Stop(); SaveAll(); } };
        FormClosed += (_, _) => { _autoSaveTimer.Dispose(); _diagnosticsTimer.Dispose(); };
    }

    private TabPage BuildButtonsTab()
    {
        var page = NewPage("Buttons");
        var layout = NewTwoPaneLayout(31);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = PanelBack };
        _profile.DropDownStyle = ComboBoxStyle.DropDownList;
        _profile.Dock = DockStyle.Top;
        _profile.SelectedIndexChanged += (_, _) => SwitchProfile();
        _liveButton.Text = "Press a SpacePilot button";
        _liveButton.Dock = DockStyle.Top;
        _liveButton.Height = 28;
        _liveButton.ForeColor = Muted;
        _buttonList.Dock = DockStyle.Fill;
        foreach (var b in SpacePilotButtons.All) _buttonList.Items.Add($"{b.RawId:00}  {b.Name}");
        _buttonList.SelectedIndexChanged += (_, _) => SelectButton();
        left.Controls.Add(_buttonList);
        left.Controls.Add(_liveButton);
        left.Controls.Add(_profile);

        var editor = NewTable();
        _behavior.DropDownStyle = ComboBoxStyle.DropDownList;
        _behavior.Items.AddRange(new object[] { "Inherit", "Default", "Virtual", "Shortcut", "BuiltIn", "Disabled" });
        _assignment.DropDownStyle = ComboBoxStyle.DropDown;
        _assignment.Items.AddRange(VirtualSpaceMouseButtons.ByName.Keys.Cast<object>().ToArray());
        _assignment.Items.AddRange(new object[] { "IncreaseSensitivity", "DecreaseSensitivity", "ToggleDominant", "ToggleRotationLock", "OpenSettings", "RecoverLcd", "ToggleLed", "CycleLedPattern" });
        _assignment.MinimumSize = new Size(220, 0);
        AddRow(editor, "Default target", _defaultTarget);
        AddRow(editor, "Behavior", _behavior);
        AddRow(editor, "Assignment", _assignment);
        var hint = new Label { AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = Muted, Text = "All 21 physical SpacePilot buttons are available here. 3DxWare only shows the buttons that exist on the emulated C631 SpaceMouse Pro; missing legacy buttons must use a supported carrier slot or a bridge software action." };
        AddWide(editor, hint);

        _leftCarrier.DropDownStyle = ComboBoxStyle.DropDownList;
        _leftCarrier.Items.AddRange(VirtualSpaceMouseButtons.CarrierNames.Cast<object>().ToArray());
        _backCarrier.DropDownStyle = ComboBoxStyle.DropDownList;
        _backCarrier.Items.AddRange(VirtualSpaceMouseButtons.CarrierNames.Cast<object>().ToArray());
        ConfigureNumeric(_viewHold, 250, 1500, 50, 0);
        AddRow(editor, "Left view carrier", _leftCarrier);
        AddRow(editor, "Back view carrier", _backCarrier);
        AddRow(editor, "Long press (ms)", _viewHold);
        var carrierHelp = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            ForeColor = Muted,
            Text = "Recommended for Fusion: assign the 3DxWare Roll +90 slot to Left View and the Menu slot to Back View. Then physical Left = Left View, Right = Right View, and holding Left or Right = Back View."
        };
        AddWide(editor, carrierHelp);
        var reset = MakeButton("Reset profile", 120, (_, _) => ResetCurrentProfile());
        AddWide(editor, reset);

        var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Back, Padding = new Padding(4) };
        right.Controls.Add(editor);
        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildGeneralTab()
    {
        var page = NewPage("General");
        var t = NewTable();
        ConfigureNumeric(_sensitivity, 0.10m, 3.00m, 0.05m, 2);
        ConfigureNumeric(_step, 0.05m, 0.50m, 0.05m, 2);
        _dominant.Text = "Dominant axis mode";
        _rotation.Text = "Lock rotations";
        _startMinimized.Text = "Start minimized to tray";
        _silent.Text = "Silent mode (no balloons)";
        _autoStart.Text = "Start automatically with Windows";
        _gamepad.Text = "Expose SpacePilot as virtual game controller (requires extended virtual driver)";
        _ledPattern.DropDownStyle = ComboBoxStyle.DropDownList;
        _ledPattern.Items.AddRange(new object[] { "Off", "On", "Activity", "Slow Blink", "Fast Blink", "Heartbeat", "Breathe", "Screensaver Pulse" });
        AddRow(t, "Sensitivity", _sensitivity);
        AddRow(t, "Speed step", _step);
        AddRow(t, "LED", _ledPattern);
        AddWide(t, _dominant); AddWide(t, _rotation); AddWide(t, _startMinimized); AddWide(t, _silent); AddWide(t, _autoStart); AddWide(t, _gamepad);
        page.Controls.Add(t);
        return page;
    }

    private TabPage BuildLcdTab()
    {
        var page = NewPage("LCD");
        var layout = NewTwoPaneLayout(34);
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = PanelBack };
        _screensavers.Dock = DockStyle.Fill;
        _screensavers.AllowDrop = true;
        _screensavers.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        _screensavers.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) ImportFiles(files); };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 68, FlowDirection = FlowDirection.LeftToRight, BackColor = PanelBack };
        buttons.Controls.Add(MakeButton("Start", 70, (_, _) => StartSelectedSaver()));
        buttons.Controls.Add(MakeButton("Stop", 70, (_, _) => _engine.StopScreensaver()));
        buttons.Controls.Add(MakeButton("Import", 70, (_, _) => ImportScreensaver()));
        left.Controls.Add(_screensavers); left.Controls.Add(buttons);

        var t = NewTable();
        _defaultScreensaver.DropDownStyle = ComboBoxStyle.DropDownList;
        _idleSaver.Text = "Idle screensaver";
        ConfigureNumeric(_idleSeconds, 5, 3600, 5, 0);
        _playlistEnabled.Text = "Playlist enabled";
        ConfigureNumeric(_playlistSeconds, 10, 3600, 10, 0);
        _playlistShuffle.Text = "Shuffle playlist";
        _playlist.Height = 90; _playlist.Width = 260;
        _dither.DropDownStyle = ComboBoxStyle.DropDownList;
        _dither.Items.AddRange(new object[] { "Threshold", "Bayer 2x2", "Bayer 4x4", "Floyd-Steinberg" });
        ConfigureNumeric(_fullRefresh, 1, 120, 1, 0);
        AddRow(t, "Default saver", _defaultScreensaver);
        AddWide(t, _idleSaver); AddRow(t, "Idle after (sec)", _idleSeconds);
        AddWide(t, _playlistEnabled); AddRow(t, "Change every (sec)", _playlistSeconds); AddWide(t, _playlistShuffle); AddRow(t, "Playlist", _playlist);
        AddRow(t, "Dithering", _dither); AddRow(t, "Full refresh frames", _fullRefresh);
        string[] widgets = { "Speed", "Profile", "Latency", "Buttons", "Motion", "Clock", "LED", "Gamepad", "Now Playing" };
        for (int i = 0; i < _widgetSlots.Length; i++) { _widgetSlots[i].DropDownStyle = ComboBoxStyle.DropDownList; _widgetSlots[i].Items.AddRange(widgets.Cast<object>().ToArray()); AddRow(t, $"Widget {i+1}", _widgetSlots[i]); }
        var note = new Label { AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = Muted, Text = "v0.9 runs LCD USB I/O in an isolated helper process. If Windows wedges a HID write, the helper is killed and restarted without freezing the main app." };
        AddWide(t, note);

        var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Back, Padding = new Padding(4) };
        right.Controls.Add(t);
        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildAppsTab()
    {
        var page = NewPage("Apps");
        var layout = NewTwoPaneLayout(52);

        _apps.Dock = DockStyle.Fill;
        _apps.View = View.Details;
        _apps.FullRowSelect = true;
        _apps.HideSelection = false;
        _apps.MultiSelect = false;
        _apps.BackColor = Color.FromArgb(38, 41, 46);
        _apps.ForeColor = Fore;
        _apps.Columns.Add("Last used", 95);
        _apps.Columns.Add("Application", 140);
        _apps.Columns.Add("Process", 125);
        _apps.Columns.Add("Profile", 115);
        _apps.SelectedIndexChanged += (_, _) => SelectRecentApp();
        _apps.Resize += (_, _) => ResizeAppColumns();
        layout.Controls.Add(_apps, 0, 0);

        var t = NewTable();
        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = Muted,
            Text = "Applications appear here automatically after they have been the foreground app. The list is sorted by last used. Assign the exact bridge profile that should activate for each executable."
        };
        AddWide(t, note);
        _appActive.AutoSize = true;
        _appActive.ForeColor = Fore;
        AddWide(t, _appActive);
        _appDetails.AutoSize = true;
        _appDetails.MaximumSize = new Size(520, 0);
        _appDetails.ForeColor = Muted;
        AddWide(t, _appDetails);

        _appProfile.DropDownStyle = ComboBoxStyle.DropDownList;
        _appProfile.Items.Add("Auto detect");
        _appProfile.SelectedIndexChanged += (_, _) => AssignSelectedAppProfile();
        AddRow(t, "Assigned profile", _appProfile);

        _newProfileName.PlaceholderText = "e.g. Blender Custom";
        var createProfile = MakeButton("Create profile", 120, (_, _) => CreateCustomProfile());
        var createPanel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        createPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        createPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _newProfileName.Dock = DockStyle.Fill;
        createPanel.Controls.Add(_newProfileName, 0, 0);
        createPanel.Controls.Add(createProfile, 1, 0);
        AddRow(t, "New custom profile", createPanel);

        var appButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        appButtons.Controls.Add(MakeButton("Add executable...", 130, (_, _) => AddExecutableApp()));
        appButtons.Controls.Add(MakeButton("Forget selected app", 150, (_, _) => ForgetSelectedApp()));
        AddWide(t, appButtons);
        var explain = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = Muted,
            Text = "Example: Resolve.exe -> DaVinci Resolve. The Controls tab settings for the Resolve profile will then be used whenever Resolve is foreground."
        };
        AddWide(t, explain);

        var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Back, Padding = new Padding(4) };
        right.Controls.Add(t);
        layout.Controls.Add(right, 1, 0);
        page.Controls.Add(layout);
        RefreshRecentApps();
        return page;
    }

    private TabPage BuildEditingTab()
    {
        var page = NewPage("Controls");
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Back };
        var t = NewTable();
        var desc = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Muted,
            Text = "Per-app controls. The bridge NEVER moves the Windows cursor. Pan uses wheel/HWheel or keyboard actions. Native SpaceMouse motion remains separate for CAD/3D apps."
        };
        AddWide(t, desc);

        _editorProfile.DropDownStyle = ComboBoxStyle.DropDownList;
        _editorProfile.SelectedIndexChanged += (_, _) => SwitchEditorProfile();
        AddRow(t, "Profile", _editorProfile);
        _editorEnabled.CheckedChanged += (_, _) => MarkDirty();
        AddWide(t, _editorEnabled);

        _panStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        _panStyle.Items.AddRange(new object[] { "Pointerless" });
        _rotateStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        _rotateStyle.Items.AddRange(new object[] { "None" });
        AddRow(t, "Pan mode", _panStyle);
        AddRow(t, "Rotate mode", _rotateStyle);
        _lockHorizon.CheckedChanged += (_, _) => MarkDirty();
        AddWide(t, _lockHorizon);

        string[] actions =
        {
            "None", "Pan Left", "Pan Right", "Pan Up", "Pan Down", "Zoom In", "Zoom Out",
            "Rotate CW", "Rotate CCW", "Resolve Forward", "Resolve Reverse", "Resolve Stop",
            "Frame Next", "Frame Previous", "Go To In", "Go To Out",
            "Horizontal Scroll +", "Horizontal Scroll -", "Vertical Scroll +", "Vertical Scroll -", "Shortcut"
        };
        foreach (string axis in new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" })
        {
            var row = new AxisEditorRow(axis, actions);
            row.Changed += MarkDirty;
            _axisRows[axis] = row;
            AddWide(t, row.Panel);
        }

        var presets = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        presets.Controls.Add(MakeButton("Resolve preset", 120, (_, _) => ApplyResolvePreset()));
        presets.Controls.Add(MakeButton("Ableton preset", 120, (_, _) => ApplyAbletonPreset()));
        presets.Controls.Add(MakeButton("Illustrator 2D", 130, (_, _) => ApplyIllustratorPreset()));
        presets.Controls.Add(MakeButton("Photoshop 2D", 130, (_, _) => ApplyPhotoshopPreset()));
        AddWide(t, presets);

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Muted,
            Text = "Resolve default: twist Rz = reverse/forward playback; Tx = frame previous/next; Ty = Go To In/Out; physical Top = Space play/pause. Cursor movement is globally disabled. Adobe canvas rotation requires a custom keyboard shortcut/action because the stock Rotate View tools are drag-based."
        };
        AddWide(t, hint);
        host.Controls.Add(t);
        page.Controls.Add(host);
        return page;
    }

    private TabPage BuildDiagnosticsTab()
    {
        var page = NewPage("Diagnostics");
        _diag.Dock = DockStyle.Fill;
        _diag.Padding = new Padding(16);
        _diag.Font = new Font(FontFamily.GenericMonospace, 10f);
        _diag.ForeColor = Fore;
        _diag.BackColor = Back;
        page.Controls.Add(_diag);
        return page;
    }

    public void NotifyPhysicalButton(int raw)
    {
        if (IsDisposed) return;
        BeginInvoke(new Action(() =>
        {
            _liveButton.Text = $"Last: {SpacePilotButtons.GetName(raw)} (raw {raw})";
            int index = SpacePilotButtons.All.ToList().FindIndex(x => x.RawId == raw);
            if (index >= 0) _buttonList.SelectedIndex = index;
        }));
    }

    private void LoadGeneral()
    {
        _sensitivity.Value = (decimal)Math.Clamp(_working.Sensitivity, .10, 3.0);
        _step.Value = (decimal)Math.Clamp(_working.SensitivityStep, .05, .50);
        _dominant.Checked = _working.DominantMode; _rotation.Checked = _working.RotationLocked;
        _startMinimized.Checked = _working.StartMinimized; _silent.Checked = _working.SilentMode; _autoStart.Checked = _working.AutoStart; _gamepad.Checked = _working.GamepadEnabled;
        SetCombo(_leftCarrier, _working.LeftViewCarrier); SetCombo(_backCarrier, _working.BackViewCarrier); _viewHold.Value = Math.Clamp(_working.ViewHoldMilliseconds, 250, 1500);
        SetCombo(_ledPattern, _working.LedPattern); SetCombo(_dither, _working.LcdDitherMode);
        _idleSaver.Checked = _working.IdleScreensaverEnabled; _idleSeconds.Value = _working.IdleScreensaverSeconds;
        _playlistEnabled.Checked = _working.ScreensaverPlaylistEnabled; _playlistSeconds.Value = _working.ScreensaverPlaylistSeconds; _playlistShuffle.Checked = _working.ScreensaverPlaylistShuffle;
        _fullRefresh.Value = _working.LcdFullRefreshFrames;
        for (int i=0;i<4;i++) SetCombo(_widgetSlots[i], _working.LcdWidgets.ElementAtOrDefault(i) ?? "Speed");
        SetCombo(_editorProfile, ProfileResolver.DisplayName(_selectedEditorProfile, _working));
        LoadEditorProfile();
    }

    private void SwitchProfile()
    {
        if (_loading) return;
        CommitCurrentButton();
        _selectedButtonProfile = ProfileKeyFromDisplay(_profile.SelectedItem?.ToString()) ?? "global";
        _selectedRaw = -1;
        SelectButton();
    }

    private void SelectButton()
    {
        if (_loading || _buttonList.SelectedIndex < 0) return;
        CommitCurrentButton();
        var def = SpacePilotButtons.All[_buttonList.SelectedIndex]; _selectedRaw = def.RawId;
        _defaultTarget.Text = def.RawId == 7 ? $"carrier: {_working.LeftViewCarrier}" : def.DefaultVirtualName ?? "software only";
        ButtonBinding? binding = null;
        if (_working.Profiles.TryGetValue(_selectedButtonProfile, out var p) && p.Buttons.TryGetValue(def.RawId, out var found)) binding = found;
        _loading = true;
        try { SetCombo(_behavior, binding?.Mode ?? (_selectedButtonProfile == "global" ? "Default" : "Inherit")); _assignment.Text = binding?.Value ?? ""; }
        finally { _loading = false; }
    }

    private void CommitCurrentButton()
    {
        if (_loading || _selectedRaw < 0) return;
        ProfileSettings p = EnsureProfile(_selectedButtonProfile);
        string mode = _behavior.SelectedItem?.ToString() ?? (_selectedButtonProfile == "global" ? "Default" : "Inherit");
        string value = _assignment.Text.Trim();
        if (mode.Equals("Inherit", StringComparison.OrdinalIgnoreCase) || (_selectedButtonProfile == "global" && mode.Equals("Default", StringComparison.OrdinalIgnoreCase))) p.Buttons.Remove(_selectedRaw);
        else p.Buttons[_selectedRaw] = new ButtonBinding { Mode = mode, Value = value };
    }

    private void SwitchEditorProfile()
    {
        if (_loading) return;
        CommitEditorProfile();
        _selectedEditorProfile = ProfileKeyFromDisplay(_editorProfile.SelectedItem?.ToString()) ?? "global";
        LoadEditorProfile();
    }

    private void SelectActiveEditorProfile()
    {
        DiagnosticsSnapshot d = _diagnosticsProvider();
        string? key = _working.Profiles.FirstOrDefault(kv => ProfileResolver.DisplayName(kv.Key, _working).Equals(d.Profile, StringComparison.OrdinalIgnoreCase)).Key;
        if (string.IsNullOrWhiteSpace(key)) key = "global";
        _selectedEditorProfile = key;
        _loading = true;
        try { SetCombo(_editorProfile, ProfileResolver.DisplayName(key, _working)); }
        finally { _loading = false; }
        LoadEditorProfile();
    }

    private void ApplyResolvePreset()
    {
        _selectedEditorProfile = "resolve";
        ProfileSettings p = EnsureProfile("resolve");
        p.DisplayName = "DaVinci Resolve";
        p.EditorControlsEnabled = true;
        p.EditorMode = "Custom Axes";
        p.PanStyle = "Pointerless"; p.RotateStyle = "None";
        p.AxisBindings.Clear();
        p.AxisBindings["Rz"] = AxisBinding.Pair("Resolve Forward", "Resolve Reverse", "Resolve Stop", 0.12, 1.0);
        p.AxisBindings["Tx"] = AxisBinding.Pair("Frame Next", "Frame Previous", "None", 0.16, 1.0);
        p.AxisBindings["Ty"] = AxisBinding.Pair("Go To Out", "Go To In", "None", 0.22, 1.0, repeat: false);
        p.Buttons[6] = ButtonBinding.Shortcut("SPACE");
        RefreshProfileCombos();
        _loading = true; try { SetCombo(_editorProfile, "DaVinci Resolve"); } finally { _loading = false; }
        LoadEditorProfile(); SaveAll();
    }

    private void ApplyAbletonPreset()
    {
        _selectedEditorProfile = "ableton";
        ProfileSettings p = EnsureProfile("ableton");
        p.DisplayName = "Ableton Live"; p.EditorControlsEnabled = true; p.EditorMode = "Custom Axes"; p.PanStyle = "Pointerless"; p.RotateStyle = "None";
        p.AxisBindings.Clear();
        p.AxisBindings["Rz"] = AxisBinding.Pair("Horizontal Scroll +", "Horizontal Scroll -", "None", 0.12, 1.0);
        RefreshProfileCombos(); _loading = true; try { SetCombo(_editorProfile, "Ableton Live"); } finally { _loading = false; }
        LoadEditorProfile(); SaveAll();
    }

    private void ApplyIllustratorPreset()
    {
        _selectedEditorProfile = "illustrator";
        ProfileSettings p = EnsureProfile("illustrator");
        p.DisplayName = "Adobe Illustrator"; p.EditorControlsEnabled = true; p.EditorMode = "Custom Axes"; p.PanStyle = "Pointerless"; p.RotateStyle = "None";
        p.AxisBindings.Clear();
        p.AxisBindings["Tx"] = AxisBinding.Pair("Horizontal Scroll +", "Horizontal Scroll -", "None", 0.10, 1.0);
        p.AxisBindings["Ty"] = AxisBinding.Pair("Vertical Scroll -", "Vertical Scroll +", "None", 0.10, 1.0);
        p.AxisBindings["Tz"] = AxisBinding.Pair("Zoom Out", "Zoom In", "None", 0.12, 1.0);
        RefreshProfileCombos(); _loading = true; try { SetCombo(_editorProfile, "Adobe Illustrator"); } finally { _loading = false; }
        LoadEditorProfile(); SaveAll();
    }

    private void ApplyPhotoshopPreset()
    {
        _selectedEditorProfile = "photoshop";
        ProfileSettings p = EnsureProfile("photoshop");
        p.DisplayName = "Adobe Photoshop"; p.EditorControlsEnabled = true; p.EditorMode = "Custom Axes"; p.PanStyle = "Pointerless"; p.RotateStyle = "None";
        p.AxisBindings.Clear();
        p.AxisBindings["Tx"] = AxisBinding.Pair("Horizontal Scroll +", "Horizontal Scroll -", "None", 0.10, 1.0);
        p.AxisBindings["Ty"] = AxisBinding.Pair("Vertical Scroll -", "Vertical Scroll +", "None", 0.10, 1.0);
        p.AxisBindings["Tz"] = AxisBinding.Pair("Zoom Out", "Zoom In", "None", 0.12, 1.0);
        RefreshProfileCombos(); _loading = true; try { SetCombo(_editorProfile, "Adobe Photoshop"); } finally { _loading = false; }
        LoadEditorProfile(); SaveAll();
    }

    private void LoadEditorProfile()
    {
        ProfileSettings p = EnsureProfile(_selectedEditorProfile);
        _loading = true;
        try
        {
            _editorEnabled.Checked = p.EditorControlsEnabled == true;
            SetCombo(_panStyle, "Pointerless");
            SetCombo(_rotateStyle, "None");
            _lockHorizon.Checked = p.LockHorizon == true;
            foreach (var kv in _axisRows)
            {
                AxisBinding b = p.AxisBindings.TryGetValue(kv.Key, out var found) ? found : new AxisBinding();
                kv.Value.Load(b);
            }
        }
        finally { _loading = false; }
    }

    private void CommitEditorProfile()
    {
        if (_loading) return;
        ProfileSettings p = EnsureProfile(_selectedEditorProfile);
        p.EditorControlsEnabled = _editorEnabled.Checked;
        p.EditorMode = p.EditorControlsEnabled == true ? "Custom Axes" : "Off";
        p.PanStyle = "Pointerless";
        p.RotateStyle = "None";
        p.LockHorizon = _lockHorizon.Checked;
        p.AxisBindings.Clear();
        foreach (var kv in _axisRows) p.AxisBindings[kv.Key] = kv.Value.Read();
    }

    private ProfileSettings EnsureProfile(string key)
    {
        if (!_working.Profiles.TryGetValue(key, out var p))
        {
            p = new ProfileSettings { DisplayName = ProfileResolver.DisplayName(key) };
            _working.Profiles[key] = p;
        }
        if (string.IsNullOrWhiteSpace(p.DisplayName)) p.DisplayName = ProfileResolver.DisplayName(key);
        return p;
    }

    private void RefreshProfileCombos()
    {
        string? buttonKeep = _profile.SelectedItem?.ToString();
        string? editorKeep = _editorProfile.SelectedItem?.ToString();
        string? appKeep = _appProfile.SelectedItem?.ToString();
        var names = _working.Profiles
            .Select(kv => ProfileResolver.DisplayName(kv.Key, _working))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Equals("Global", StringComparison.OrdinalIgnoreCase) ? "" : x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _loading = true;
        try
        {
            _profile.Items.Clear(); _profile.Items.AddRange(names.Cast<object>().ToArray());
            _editorProfile.Items.Clear(); _editorProfile.Items.AddRange(names.Cast<object>().ToArray());
            _appProfile.Items.Clear(); _appProfile.Items.Add("Auto detect"); _appProfile.Items.AddRange(names.Cast<object>().ToArray());
            SetCombo(_profile, buttonKeep ?? ProfileResolver.DisplayName(_selectedButtonProfile, _working));
            SetCombo(_editorProfile, editorKeep ?? ProfileResolver.DisplayName(_selectedEditorProfile, _working));
            SetCombo(_appProfile, appKeep ?? "Auto detect");
        }
        finally { _loading = false; }
    }

    private string? ProfileKeyFromDisplay(string? display)
    {
        if (string.IsNullOrWhiteSpace(display) || display.Equals("Auto detect", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var kv in _working.Profiles)
            if (ProfileResolver.DisplayName(kv.Key, _working).Equals(display, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return null;
    }

    private void CreateCustomProfile()
    {
        string name = _newProfileName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        string baseKey = ProfileResolver.MakeProfileKey(name);
        string key = baseKey; int n = 2;
        while (_working.Profiles.ContainsKey(key)) key = $"{baseKey}-{n++}";
        _working.Profiles[key] = new ProfileSettings { DisplayName = name, EditorControlsEnabled = true, EditorMode = "Custom Axes", PanStyle = "Pointerless", RotateStyle = "None" };
        _newProfileName.Clear();
        RefreshProfileCombos();
        _loading = true; try { SetCombo(_appProfile, name); SetCombo(_editorProfile, name); SetCombo(_profile, name); } finally { _loading = false; }
        _selectedButtonProfile = key; _selectedEditorProfile = key;
        if (!string.IsNullOrWhiteSpace(_selectedAppKey)) _working.AppProfileMappings[_selectedAppKey] = key;
        LoadEditorProfile(); SelectButton(); SaveAll(); RefreshRecentApps();
    }

    private void ResetCurrentProfile(){string name=ProfileResolver.DisplayName(_selectedButtonProfile,_working);_working.Profiles[_selectedButtonProfile]=new ProfileSettings{DisplayName=name};_selectedRaw=-1;SelectButton();if(_selectedEditorProfile.Equals(_selectedButtonProfile,StringComparison.OrdinalIgnoreCase))LoadEditorProfile();SaveAll();}

    private void SaveAll()
    {
        CommitCurrentButton(); CommitEditorProfile();
        _working.Sensitivity=(double)_sensitivity.Value; _working.SensitivityStep=(double)_step.Value; _working.DominantMode=_dominant.Checked; _working.RotationLocked=_rotation.Checked;
        _working.LedPattern=_ledPattern.SelectedItem?.ToString()??"On"; _working.StartMinimized=_startMinimized.Checked; _working.SilentMode=_silent.Checked; _working.AutoStart=_autoStart.Checked; _working.GamepadEnabled=_gamepad.Checked;
        _working.LeftViewCarrier=_leftCarrier.SelectedItem?.ToString()??"Roll CW"; _working.BackViewCarrier=_backCarrier.SelectedItem?.ToString()??"Menu"; _working.ViewHoldMilliseconds=(int)_viewHold.Value;
        _working.DefaultScreensaver=_defaultScreensaver.SelectedItem?.ToString()??"Starfield"; _working.IdleScreensaverEnabled=_idleSaver.Checked; _working.IdleScreensaverSeconds=(int)_idleSeconds.Value;
        _working.ScreensaverPlaylistEnabled=_playlistEnabled.Checked; _working.ScreensaverPlaylistSeconds=(int)_playlistSeconds.Value; _working.ScreensaverPlaylistShuffle=_playlistShuffle.Checked;
        _working.ScreensaverPlaylist=_playlist.CheckedItems.Cast<object>().Select(x=>x.ToString()!).Where(x=>!string.IsNullOrWhiteSpace(x)).ToList();
        _working.LcdDitherMode=_dither.SelectedItem?.ToString()??"Threshold"; _working.LcdFullRefreshFrames=(int)_fullRefresh.Value; _working.LcdWidgets=_widgetSlots.Select(x=>x.SelectedItem?.ToString()??"Speed").ToList();
        try
        {
            _engine.ReplaceSettings(SettingsStore.Clone(_working));
            _savedLabel.ForeColor = Muted;
            _savedLabel.Text=$"Saved automatically {DateTime.Now:HH:mm:ss}";
            Saved?.Invoke(this,EventArgs.Empty);
        }
        catch(Exception ex)
        {
            _savedLabel.ForeColor = Color.IndianRed;
            _savedLabel.Text = $"Could not save: {ex.Message}";
        }
    }

    private void BindAutoSaveEvents()
    {
        _behavior.SelectedIndexChanged += (_, _) => MarkDirty();
        _assignment.TextChanged += (_, _) => MarkDirty();
        _sensitivity.ValueChanged += (_, _) => MarkDirty();
        _step.ValueChanged += (_, _) => MarkDirty();
        _dominant.CheckedChanged += (_, _) => MarkDirty();
        _rotation.CheckedChanged += (_, _) => MarkDirty();
        _ledPattern.SelectedIndexChanged += (_, _) => MarkDirty();
        _startMinimized.CheckedChanged += (_, _) => MarkDirty();
        _silent.CheckedChanged += (_, _) => MarkDirty();
        _autoStart.CheckedChanged += (_, _) => MarkDirty();
        _gamepad.CheckedChanged += (_, _) => MarkDirty();
        _leftCarrier.SelectedIndexChanged += (_, _) => MarkDirty();
        _backCarrier.SelectedIndexChanged += (_, _) => MarkDirty();
        _viewHold.ValueChanged += (_, _) => MarkDirty();
        _defaultScreensaver.SelectedIndexChanged += (_, _) => MarkDirty();
        _idleSaver.CheckedChanged += (_, _) => MarkDirty();
        _idleSeconds.ValueChanged += (_, _) => MarkDirty();
        _playlistEnabled.CheckedChanged += (_, _) => MarkDirty();
        _playlistSeconds.ValueChanged += (_, _) => MarkDirty();
        _playlistShuffle.CheckedChanged += (_, _) => MarkDirty();
        _playlist.ItemCheck += (_, _) => BeginInvoke(new Action(MarkDirty));
        _dither.SelectedIndexChanged += (_, _) => MarkDirty();
        _fullRefresh.ValueChanged += (_, _) => MarkDirty();
        foreach (ComboBox slot in _widgetSlots) slot.SelectedIndexChanged += (_, _) => MarkDirty();
        _panStyle.SelectedIndexChanged += (_, _) => MarkDirty();
        _rotateStyle.SelectedIndexChanged += (_, _) => MarkDirty();
    }

    private void MarkDirty()
    {
        if (_loading || IsDisposed) return;
        _savedLabel.ForeColor = Muted;
        _savedLabel.Text = "Saving...";
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void RefreshScreensavers(string? select=null)
    {
        string? old=select??_screensavers.SelectedItem?.ToString()??_working.DefaultScreensaver; var names=ScreensaverLibrary.GetAll().Select(x=>x.Name).ToArray();
        _screensavers.Items.Clear();_screensavers.Items.AddRange(names.Cast<object>().ToArray()); _defaultScreensaver.Items.Clear();_defaultScreensaver.Items.AddRange(names.Cast<object>().ToArray());
        _playlist.Items.Clear(); foreach(string n in names){int idx=_playlist.Items.Add(n); if(_working.ScreensaverPlaylist.Contains(n,StringComparer.OrdinalIgnoreCase))_playlist.SetItemChecked(idx,true);} 
        if(old is not null){int i=_screensavers.FindStringExact(old);if(i>=0)_screensavers.SelectedIndex=i;} SetCombo(_defaultScreensaver,_working.DefaultScreensaver); if(_screensavers.SelectedIndex<0&&_screensavers.Items.Count>0)_screensavers.SelectedIndex=0;
    }
    private void StartSelectedSaver(){if(_screensavers.SelectedItem is string n)_engine.StartScreensaver(n);} 
    private void ImportScreensaver(){using var d=new OpenFileDialog{Filter="Screensavers|*.gif;*.png;*.bmp;*.jpg;*.jpeg;*.spbsaver|All files|*.*",Multiselect=true};if(d.ShowDialog(this)==DialogResult.OK)ImportFiles(d.FileNames);} 
    private void ImportFiles(IEnumerable<string> files){string? last=null;foreach(string f in files){try{last=Path.GetFileNameWithoutExtension(ScreensaverLibrary.ImportMedia(f));}catch(Exception ex){MessageBox.Show(this,ex.Message,"Import failed");}}RefreshScreensavers(last);} 

    private void RefreshDiagnostics()
    {
        if(IsDisposed)return; var d=_diagnosticsProvider();
        _appActive.Text = $"ACTIVE\r\n{d.ActiveApp}\r\n{d.ActiveProcess}\r\nProfile: {d.Profile}\r\nEditor: {d.EditorMode}";
        RefreshRecentApps();
        _diag.Text=$"PHYSICAL   {(d.PhysicalConnected?"CONNECTED":"OFFLINE")}\r\nVIRTUAL    {(d.VirtualConnected?"CONNECTED":"OFFLINE")}\r\nGAMEPAD    {(d.GamepadEnabled?(d.GamepadSupported?"ON":"DRIVER UPGRADE NEEDED"):"OFF")}\r\nAPP        {d.ActiveApp}\r\nPROCESS    {d.ActiveProcess}\r\nPROFILE    {d.Profile}\r\nEDITOR     {d.EditorMode}\r\nEDITOR OUT {d.EditorLastAction}  {(d.EditorLastSendSucceeded?"OK":"FAILED")}  #{d.EditorEmitCount}\r\n\r\nTX {d.Tx,7:0.000}   TY {d.Ty,7:0.000}   TZ {d.Tz,7:0.000}\r\nRX {d.Rx,7:0.000}   RY {d.Ry,7:0.000}   RZ {d.Rz,7:0.000}\r\nBUTTONS    {d.Buttons}\r\n\r\nLATENCY    {d.LastLatencyMs,6:0.00} ms\r\nAVERAGE    {d.AverageLatencyMs,6:0.00} ms\r\nPEAK       {d.PeakLatencyMs,6:0.00} ms\r\nREPORTS    {d.ReportsPerSecond,6:0.0} /s\r\n\r\nLCD BUSY   {(d.LcdBusy?"YES":"NO")}\r\nRECOVERIES {d.LcdRecoveries}\r\nSAVER      {d.Screensaver}\r\nRAM        {d.WorkingSetMb:0.0} MB";
    }

    private void RefreshRecentApps()
    {
        if (IsDisposed) return;
        IReadOnlyList<RecentAppEntry> entries = AppDiscoveryStore.GetRecent();
        string signature = string.Join("|", entries.Select(x => $"{x.ProcessKey}:{x.LastSeenUtc.Ticks}:{(_working.AppProfileMappings.TryGetValue(x.ProcessKey, out string? m) ? m : "auto")}"));
        if (signature == _appsSignature) return;
        _appsSignature = signature;
        string? keep = _selectedAppKey;
        _loading = true;
        try
        {
            _apps.BeginUpdate();
            _apps.Items.Clear();
            foreach (RecentAppEntry entry in entries)
            {
                string explicitProfile = _working.AppProfileMappings.TryGetValue(entry.ProcessKey, out string? mapped)
                    ? ProfileResolver.DisplayName(mapped, _working)
                    : $"Auto ({ProfileResolver.DisplayName(ProfileResolver.SuggestedFromNames(entry.ProcessName, entry.DisplayName, entry.WindowTitle), _working)})";
                var item = new ListViewItem(entry.LastSeenUtc.ToLocalTime().ToString("HH:mm:ss")) { Tag = entry };
                item.SubItems.Add(entry.DisplayName);
                item.SubItems.Add(entry.ExecutableName);
                item.SubItems.Add(explicitProfile);
                _apps.Items.Add(item);
                if (!string.IsNullOrWhiteSpace(keep) && entry.ProcessKey.Equals(keep, StringComparison.OrdinalIgnoreCase))
                    item.Selected = true;
            }
            _apps.EndUpdate();
            ResizeAppColumns();
        }
        finally { _loading = false; }
    }

    private void ResizeAppColumns()
    {
        if (_apps.Columns.Count < 4 || _apps.ClientSize.Width <= 0) return;
        int w = Math.Max(320, _apps.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        _apps.Columns[0].Width = Math.Max(80, (int)(w * 0.18));
        _apps.Columns[1].Width = Math.Max(120, (int)(w * 0.30));
        _apps.Columns[2].Width = Math.Max(110, (int)(w * 0.24));
        _apps.Columns[3].Width = Math.Max(110, w - _apps.Columns[0].Width - _apps.Columns[1].Width - _apps.Columns[2].Width);
    }

    private void SelectRecentApp()
    {
        if (_loading || _apps.SelectedItems.Count == 0) return;
        if (_apps.SelectedItems[0].Tag is not RecentAppEntry entry) return;
        _selectedAppKey = entry.ProcessKey;
        string suggested = ProfileResolver.SuggestedFromNames(entry.ProcessName, entry.DisplayName, entry.WindowTitle);
        _appDetails.Text = $"{entry.ExecutableName}\r\n{entry.WindowTitle}\r\nSuggested: {ProfileResolver.DisplayName(suggested, _working)}\r\nLast used: {entry.LastSeenUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        _loading = true;
        try
        {
            if (_working.AppProfileMappings.TryGetValue(entry.ProcessKey, out string? mapped))
                SetCombo(_appProfile, ProfileResolver.DisplayName(mapped, _working));
            else
                SetCombo(_appProfile, "Auto detect");
        }
        finally { _loading = false; }
    }

    private void AssignSelectedAppProfile()
    {
        if (_loading || string.IsNullOrWhiteSpace(_selectedAppKey)) return;
        string display = _appProfile.SelectedItem?.ToString() ?? "Auto detect";
        string? key = ProfileKeyFromDisplay(display);
        if (key is null) _working.AppProfileMappings.Remove(_selectedAppKey);
        else _working.AppProfileMappings[_selectedAppKey] = key;
        _appsSignature = string.Empty;
        SaveAll();
        RefreshRecentApps();
    }

    private void AddExecutableApp()
    {
        using var dialog = new OpenFileDialog { Filter = "Programs|*.exe|All files|*.*", Title = "Add application" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AppDiscoveryStore.AddExecutable(dialog.FileName);
        _appsSignature = string.Empty;
        RefreshRecentApps();
    }

    private void ForgetSelectedApp()
    {
        if (string.IsNullOrWhiteSpace(_selectedAppKey)) return;
        AppDiscoveryStore.Forget(_selectedAppKey);
        _working.AppProfileMappings.Remove(_selectedAppKey);
        _selectedAppKey = null;
        _appDetails.Text = string.Empty;
        _appsSignature = string.Empty;
        SaveAll();
        RefreshRecentApps();
    }

    private sealed class AxisEditorRow
    {
        public string Axis { get; }
        public TableLayoutPanel Panel { get; } = new();
        public ComboBox Positive { get; } = new();
        public ComboBox Negative { get; } = new();
        public ComboBox Neutral { get; } = new();
        public TextBox PositiveValue { get; } = new();
        public TextBox NegativeValue { get; } = new();
        public TextBox NeutralValue { get; } = new();
        public NumericUpDown Deadzone { get; } = new();
        public NumericUpDown Rate { get; } = new();
        public CheckBox Repeat { get; } = new();
        public event Action? Changed;

        public AxisEditorRow(string axis, IEnumerable<string> actions)
        {
            Axis = axis;
            Panel.AutoSize = true;
            Panel.Dock = DockStyle.Top;
            Panel.ColumnCount = 8;
            Panel.RowCount = 3;
            Panel.Padding = new Padding(6);
            Panel.Margin = new Padding(3, 4, 3, 4);
            Panel.BackColor = PanelBack;
            Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
            Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));

            var label = new Label { Text = axis, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Fore, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            Panel.Controls.Add(label, 0, 0);

            object[] items = actions.Cast<object>().ToArray();
            foreach (var c in new[] { Positive, Negative, Neutral })
            {
                c.DropDownStyle = ComboBoxStyle.DropDownList;
                c.Items.AddRange(items);
                c.Dock = DockStyle.Fill;
                c.SelectedIndexChanged += (_, _) => Changed?.Invoke();
            }
            Panel.Controls.Add(Positive, 1, 0);
            Panel.Controls.Add(Negative, 2, 0);
            Panel.Controls.Add(Neutral, 3, 0);

            Deadzone.Minimum = 0.02m; Deadzone.Maximum = 0.80m; Deadzone.Increment = 0.01m; Deadzone.DecimalPlaces = 2; Deadzone.Width = 68;
            Rate.Minimum = 0.1m; Rate.Maximum = 5.0m; Rate.Increment = 0.1m; Rate.DecimalPlaces = 1; Rate.Width = 64;
            Repeat.Text = "Repeat"; Repeat.AutoSize = true;
            Deadzone.ValueChanged += (_, _) => Changed?.Invoke();
            Rate.ValueChanged += (_, _) => Changed?.Invoke();
            Repeat.CheckedChanged += (_, _) => Changed?.Invoke();
            Panel.Controls.Add(Deadzone, 4, 0);
            Panel.Controls.Add(Rate, 5, 0);
            Panel.Controls.Add(Repeat, 6, 0);

            var hdr = new Label { Text = "+ action          - action          neutral     DZ       rate", AutoSize = true, ForeColor = Muted };
            Panel.Controls.Add(hdr, 1, 1); Panel.SetColumnSpan(hdr, 6);

            PositiveValue.PlaceholderText = "+ shortcut value";
            NegativeValue.PlaceholderText = "- shortcut value";
            NeutralValue.PlaceholderText = "neutral shortcut";
            PositiveValue.Dock = DockStyle.Fill; NegativeValue.Dock = DockStyle.Fill; NeutralValue.Dock = DockStyle.Fill;
            PositiveValue.TextChanged += (_, _) => Changed?.Invoke();
            NegativeValue.TextChanged += (_, _) => Changed?.Invoke();
            NeutralValue.TextChanged += (_, _) => Changed?.Invoke();
            Panel.Controls.Add(PositiveValue, 1, 2);
            Panel.Controls.Add(NegativeValue, 2, 2);
            Panel.Controls.Add(NeutralValue, 3, 2);
        }

        public void Load(AxisBinding b)
        {
            SetCombo(Positive, b.PositiveAction);
            SetCombo(Negative, b.NegativeAction);
            SetCombo(Neutral, b.NeutralAction);
            PositiveValue.Text = b.PositiveValue ?? string.Empty;
            NegativeValue.Text = b.NegativeValue ?? string.Empty;
            NeutralValue.Text = b.NeutralValue ?? string.Empty;
            Deadzone.Value = (decimal)Math.Clamp(b.Deadzone, 0.02, 0.80);
            Rate.Value = (decimal)Math.Clamp(b.Rate, 0.1, 5.0);
            Repeat.Checked = b.Repeat;
        }

        public AxisBinding Read() => new()
        {
            PositiveAction = Positive.SelectedItem?.ToString() ?? "None",
            NegativeAction = Negative.SelectedItem?.ToString() ?? "None",
            NeutralAction = Neutral.SelectedItem?.ToString() ?? "None",
            PositiveValue = PositiveValue.Text.Trim(),
            NegativeValue = NegativeValue.Text.Trim(),
            NeutralValue = NeutralValue.Text.Trim(),
            Deadzone = (double)Deadzone.Value,
            Rate = (double)Rate.Value,
            Repeat = Repeat.Checked
        };
    }

    private static TableLayoutPanel NewTwoPaneLayout(int leftPercent)
    {
        leftPercent = Math.Clamp(leftPercent, 20, 80);
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Back,
            ForeColor = Fore
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, leftPercent));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - leftPercent));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return t;
    }

    private static TabPage NewPage(string text)=>new(text){BackColor=Back,ForeColor=Fore,Padding=new Padding(4),AutoScroll=true};
    private static TableLayoutPanel NewTable()
    {
        var t=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,Padding=new Padding(16),BackColor=Back,ForeColor=Fore};
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));
        return t;
    }
    private static Button MakeButton(string text,int width,EventHandler click){var b=new Button{Text=text,MinimumSize=new Size(width,30),AutoSize=true,Height=30};b.Click+=click;return b;}
    private static void AddRow(TableLayoutPanel t,string label,Control c){int r=t.RowCount++;t.RowStyles.Add(new RowStyle(SizeType.AutoSize));var l=new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(3,10,8,10)};t.Controls.Add(l,0,r);c.Dock=DockStyle.Fill;c.Margin=new Padding(3,6,3,6);t.Controls.Add(c,1,r);} 
    private static void AddWide(TableLayoutPanel t,Control c){int r=t.RowCount++;t.RowStyles.Add(new RowStyle(SizeType.AutoSize));c.Margin=new Padding(3,8,3,8);c.Dock=DockStyle.Top;t.Controls.Add(c,0,r);t.SetColumnSpan(c,2);} 
    private static void ConfigureNumeric(NumericUpDown n,decimal min,decimal max,decimal inc,int decimals){n.Minimum=min;n.Maximum=max;n.Increment=inc;n.DecimalPlaces=decimals;n.MinimumSize=new Size(110,0);n.MaximumSize=new Size(180,0);}
    private static void SetCombo(ComboBox c,string value){int i=c.FindStringExact(value);c.SelectedIndex=i>=0?i:(c.Items.Count>0?0:-1);} 
    private static void ApplyDarkTheme(Control root){root.BackColor=root is TextBox or ComboBox or ListBox or CheckedListBox or NumericUpDown?Color.FromArgb(38,41,46):root.BackColor==SystemColors.Control?Back:root.BackColor;root.ForeColor=Fore;if(root is Button)bStyle((Button)root);foreach(Control c in root.Controls)ApplyDarkTheme(c);static void bStyle(Button b){b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(46,50,56);b.ForeColor=Fore;b.FlatAppearance.BorderColor=Color.FromArgb(70,75,82);}}
}
