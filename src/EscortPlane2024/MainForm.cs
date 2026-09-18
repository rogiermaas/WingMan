namespace EscortPlane2024;

internal sealed partial class MainForm : Form
{
    private readonly Diagnostics log;
    private readonly SimConnectManager sim;
    private readonly CoherentBridgeSession bridge = new();
    private readonly NameplateSession nameplates = new();
    private bool readingNameplates;
    private DateTimeOffset nextNameplateRead, nextNameplateLog;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private readonly Label connection = Label(), own = Label(), target = Label(), preview = Label(), state = Label(), actual = Label();
    private readonly Label aircraftStatus = Label();
    private readonly Button connect = Button("Connect to MSFS"), feed = Button("Find nearby aircraft"), select = Button("Follow selected aircraft"), watch = Button("Find this name"), engage = Button("Start following"), stop = Button("Stop following");
    private readonly ToolTip buttonHelp = new() { AutoPopDelay = 15000 };
    private readonly TextBox name = new() { Text = TargetSearch.FocusName, Width = 160 };
    private readonly NumericUpDown radius = Number(1, 100, 20, 1), behind = Number(0.1m, 100, 1, 0.1m), lateral = Number(-20, 20, 0, 0.1m), vertical = Number(-5, 5, 0, 0.1m);
    private readonly NumericUpDown minIas = Number(40, 599, 210, 1), maxIas = Number(41, 600, 320, 1), maxMach = Number(0.2m, 0.95m, 0.78m, 0.01m), maxVs = Number(100, 6000, 1500, 100);
    private readonly NumericUpDown smoothing = Number(5, 30, 10, 1);
    private readonly NumericUpDown minimumAltitude = Number(0, 60000, 1000, 100);
    private readonly CheckBox speedOutput = Check("Speed", true), headingOutput = Check("Heading", true), altitudeOutput = Check("Altitude", true), vsOutput = Check("Automatic V/S for height matching", true);
    private readonly CheckBox keepOnTop = Check("Keep on top", false);
    private readonly StableTrafficListView contacts = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true, Dock = DockStyle.Fill };
    private readonly TrafficMapControl trafficMap = new();
    private readonly TabPage idPage = new("All IDs");
    private readonly TabPage namesPage = new("Visible names");
    private readonly StableTrafficListView namesList = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true, Dock = DockStyle.Fill };
    private readonly Label namesStatus = Label();
    private readonly Button showNames = Button("Visible names"), watchNameplate = Button("Watch selected name"), followNameplate = Button("Follow selected name");
    private IReadOnlyList<TrafficContact> contactCatalog = [];
    private readonly Dictionary<string, DateTimeOffset> lastNameFix = new(StringComparer.OrdinalIgnoreCase);
    private ContactId? selectedContact;
    private readonly BufferedTabControl pages = new() { Dock = DockStyle.Fill };
    private readonly TabPage aircraftPage = new("Traffic map"), followPage = new("Following"), optionsPage = new("Settings");
    private bool bridgeBusy, maintaining, closing, settingsLoading, wantTraffic = true;
    private DateTimeOffset nextSimConnect;
    private string? lastConnectError;
    private bool installingRuntime;
    private readonly CancellationTokenSource runtimeCancellation = new();
    private DateTimeOffset nextRuntimeAttempt;
    private DateTimeOffset nextScan, nextUi, nextBridgeRetry, nextTrafficUi;
    private string limitsAircraft = "", savedAircraft = "", savedAdapter = "";
    private int speedLimitsVersion;
    private DateTimeOffset nextPinCheck;
    private nint lastForeground;
    private DateTimeOffset? followSelectedAt;
    private string? bridgeError;
    private string? settingsError;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EscortPlane2024", "formation.json");
    private static Label Label(string text = "") => new() { Text = text, AutoSize = true, MaximumSize = new(550, 0), Margin = new(3, 2, 3, 2) };
    private static void SetText(Control control, string text) { if (control.Text != text) control.Text = text; }
    private static Button Button(string text) => new() { Text = text, AutoSize = true, Padding = new(4, 1, 4, 1) };
    private static CheckBox Check(string text, bool value) => new() { Text = text, Checked = value, AutoSize = true, Margin = new(6, 4, 6, 2) };
    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => new()
    { Minimum = min, Maximum = max, Value = value, Increment = step, DecimalPlaces = step == 0.01m ? 2 : step < 1 ? 1 : 0, Width = 76, Margin = new(3, 2, 8, 2) };
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new BufferedFlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new(0) };
        row.Controls.AddRange(controls); return row;
    }
    public MainForm(Diagnostics log, string? screenshotPath = null, bool startNamedTraffic = false)
    {
        this.log = log; sim = new(log);
        DoubleBuffered = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        Text = Program.DemoMode ? "WingMan preview - " + ApplicationUpdates.Version : "WingMan"; ClientSize = new(600, 640); MinimumSize = new(600, 580);
        Font = new("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        pages.DrawMode = TabDrawMode.OwnerDrawFixed;
        pages.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var chosen = pages.SelectedIndex == e.Index;
            using var background = new SolidBrush(chosen ? Color.Silver : Color.Gainsboro);
            e.Graphics.FillRectangle(background, e.Bounds);
            using var border = new Pen(Color.Gray);
            e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, pages.TabPages[e.Index].Text, Font, e.Bounds, Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (chosen && pages.Focused) e.DrawFocusRectangle();
        };
        var layout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(10), ColumnCount = 1, RowCount = 6 };
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        layout.Controls.Add(Row(new Label { Text = "WingMan", AutoSize = true, Font = new("Segoe UI", 16, FontStyle.Bold) }, keepOnTop, collapseWindow), 0, 0);
        layout.Controls.Add(Row(connect, feed, connection), 0, 1);
        layout.Controls.Add(own, 0, 2);
        pages.TabPages.AddRange([aircraftPage, idPage, namesPage, followPage, optionsPage]); layout.Controls.Add(pages, 0, 3);
        var aircraftLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(6), ColumnCount = 1, RowCount = 3 };
        aircraftLayout.RowStyles.Add(new(SizeType.AutoSize)); aircraftLayout.RowStyles.Add(new(SizeType.AutoSize)); aircraftLayout.RowStyles.Add(new(SizeType.Percent, 100));
        aircraftLayout.Controls.Add(Row(Label("Range NM"), radius, Label("Min ft MSL"), minimumAltitude, showNames), 0, 0);
        contacts.Columns.Add("ID / name", 180); contacts.Columns.Add("Model", 90); contacts.Columns.Add("Distance", 85);
        contacts.Columns.Add("Altitude ft", 85); contacts.Columns.Add("Position", 150);
        TrafficListSorting.Enable(contacts, TrafficColumn.Text, TrafficColumn.Text, TrafficColumn.Distance, TrafficColumn.Number, TrafficColumn.Number);
        aircraftLayout.Controls.Add(aircraftStatus, 0, 1);
        aircraftLayout.Controls.Add(trafficMap, 0, 2);
        aircraftPage.Controls.Add(aircraftLayout);
        var idLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(6), ColumnCount = 1, RowCount = 3 };
        idLayout.RowStyles.Add(new(SizeType.AutoSize)); idLayout.RowStyles.Add(new(SizeType.Percent, 100)); idLayout.RowStyles.Add(new(SizeType.AutoSize));
        idLayout.Controls.Add(Row(Label("Player name"), name, watch), 0, 0);
        idLayout.Controls.Add(contacts, 0, 1); idLayout.Controls.Add(Row(select, Label("Live position required to follow.")), 0, 2);
        idPage.Controls.Add(idLayout);
        var namesLayout = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new(6), ColumnCount = 1, RowCount = 3 };
        namesLayout.RowStyles.Add(new(SizeType.AutoSize)); namesLayout.RowStyles.Add(new(SizeType.Percent, 100)); namesLayout.RowStyles.Add(new(SizeType.AutoSize));
        namesList.Columns.Add("Player name", 165); namesList.Columns.Add("Position", 125); namesList.Columns.Add("Model", 65);
        namesList.Columns.Add("Distance", 90); namesList.Columns.Add("Altitude ft", 100);
        TrafficListSorting.Enable(namesList, TrafficColumn.Text, TrafficColumn.Text, TrafficColumn.Text, TrafficColumn.Distance, TrafficColumn.Number);
        namesLayout.Controls.Add(namesStatus, 0, 0); namesLayout.Controls.Add(namesList, 0, 1);
        namesLayout.Controls.Add(Row(watchNameplate, followNameplate), 0, 2);
        namesPage.Controls.Add(namesLayout);
        var following = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(6), AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        following.Controls.Add(target);
        following.Controls.Add(Row(Label("Behind NM"), behind, Label("Lateral NM"), lateral, Label("Vertical NM"), vertical));
        following.Controls.Add(Label("0.1 NM steps · − left/below · + right/above\n0.1 NM vertically = 608 ft"));
        following.Controls.Add(Row(Label("Update:"), speedOutput, headingOutput, altitudeOutput));
        following.Controls.Add(vsOutput); following.Controls.Add(preview); following.Controls.Add(actual); followPage.Controls.Add(following);
        var options = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(8), AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        options.Controls.Add(Row(Label("IAS min / max (kt)"), minIas, maxIas));
        options.Controls.Add(Row(Label("Max Mach"), maxMach, Label("Max V/S (ft/min)"), maxVs));
        options.Controls.Add(Row(Label("Smoothing samples (5–30)"), smoothing));
        options.Controls.Add(Label("More samples smooth multiplayer motion. Consistent turns use the latest five samples.\nBrief freezes and jumps hold MCP values; longer gaps can wait for automatic resume."));
        options.Controls.Add(Label("You control AP master, heading/speed modes and autothrottle. Automatic V/S allows altitude hold at the requested height and selects V/S for another climb or descent."));
        options.Controls.Add(Label("Stop following leaves your autopilot's last settings in place.\nReview speed limits for your aircraft. No automatic takeoff or landing."));
        optionsPage.Controls.Add(options);
        layout.Controls.Add(Row(engage, stop), 0, 4); layout.Controls.Add(state, 0, 5);
        Controls.Add(layout); stop.BackColor = Color.MistyRose; engage.BackColor = Color.Honeydew;
        InitializeFormationScreen(layout);
        buttonHelp.SetToolTip(connect, "Connect to MSFS, or disconnect and stop following.");
        buttonHelp.SetToolTip(feed, "Find nearby aircraft and keep their positions updated. Pausing updates also stops following.");
        buttonHelp.SetToolTip(watch, "Look for the exact player name entered here. Then use Start following when ready.");
        buttonHelp.SetToolTip(select, "Follow the aircraft selected in the list once its tracking data and your aircraft are ready.");
        buttonHelp.SetToolTip(engage, "Start updating your autopilot's checked speed, heading and altitude settings for the chosen aircraft.");
        buttonHelp.SetToolTip(stop, "Stop changing autopilot settings and return to the nearby aircraft list to choose another aircraft. Shortcut: Escape.");
        buttonHelp.SetToolTip(minimumAltitude, "Show contacts at or above this altitude above sea level. Zero shows all heights. IDs with no position remain in All IDs. This display filter does not change an active follow target.");
        LoadSettings();
        sim.Follower.ResumeWhenTelemetryReturns = resumeTelemetry.Checked;
        resumeTelemetry.CheckedChanged += (_, _) => { sim.Follower.ResumeWhenTelemetryReturns = resumeTelemetry.Checked; SaveSettings(); RefreshUi(); };
        WindowPinning.Apply(this, keepOnTop.Checked);
        keepOnTop.CheckedChanged += (_, _) => { WindowPinning.Apply(this, keepOnTop.Checked); SaveSettings(); };
        HandleCreated += (_, _) => WindowPinning.Apply(this, keepOnTop.Checked);
        foreach (var control in new[] { behind, lateral, vertical, minIas, maxIas, maxMach, maxVs, smoothing }) control.ValueChanged += (_, _) =>
        {
            if (!settingsLoading && (control == minIas || control == maxIas)) speedLimitsVersion = AircraftSpeedLimits.Version;
            SettingsChanged();
        };
        foreach (var control in new[] { speedOutput, headingOutput, altitudeOutput, vsOutput }) control.CheckedChanged += (_, _) => SettingsChanged();
        connect.Click += (_, _) => Run(() => { if (sim.IsOpen) sim.Disconnect(); else { sim.Connect(); } });
        feed.Click += async (_, _) =>
        {
            wantTraffic = !wantTraffic; sim.ScanPositions = wantTraffic;
            if (!wantTraffic) { followSelectedAt = null; sim.Follower.Stop("Aircraft updates paused; following stopped"); }
            if (bridge.Running != wantTraffic) await ToggleBridge();
            RefreshUi();
        };
        watch.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) WatchName(name.Text); };
        select.Click += (_, _) => SelectContact(); contacts.DoubleClick += (_, _) => SelectContact();
        contacts.SelectedIndexChanged += (_, _) =>
        {
            if (contacts.SelectedItems.Count == 1) selectedContact = (ContactId)contacts.SelectedItems[0].Tag!;
        };
        trafficMap.ContactClicked += contact => ChooseContact(contact.Id);
        trafficMap.ZoomRequested += direction => radius.Value = Math.Clamp(Math.Round(radius.Value * (direction < 0 ? 0.5m : 2m), MidpointRounding.AwayFromZero), radius.Minimum, radius.Maximum);
        radius.ValueChanged += (_, _) => { nextScan = DateTimeOffset.MinValue; RefreshUi(); };
        minimumAltitude.ValueChanged += (_, _) => { RefreshUi(); SaveSettings(); };
        showNames.Click += (_, _) => pages.SelectedTab = namesPage;
        watchNameplate.Click += (_, _) =>
        {
            if (namesList.SelectedItems.Count != 1) return;
            WatchName((string)namesList.SelectedItems[0].Tag!);
        };
        followNameplate.Click += (_, _) => FollowNamedContact();
        namesList.DoubleClick += (_, _) => FollowNamedContact();
        buttonHelp.SetToolTip(watchNameplate, "Watch this exact name for full traffic position data. This does not start following or turn a nameplate ID into an aircraft ID.");
        buttonHelp.SetToolTip(followNameplate, "Follow the selected green name once its motion and your aircraft are ready. Green means a fresh position; it does not guarantee that your autopilot modes are ready.");
        engage.Click += (_, _) => Run(() => { sim.Follower.Settings = Settings(); sim.Follower.Engage(DateTimeOffset.UtcNow); SaveSettings(); RefreshUi(); });
        stop.Click += (_, _) => StopAndScan();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { StopAndScan(); e.Handled = true; } };
        timer.Tick += async (_, _) =>
        {
            if (Program.DemoMode) { RefreshUi(); return; }
            Run(Tick);
            if (!bridgeBusy && !maintaining && bridge.Running)
            {
                maintaining = true;
                try { await bridge.MaintainAsync(); }
                catch (Exception ex) { bridgeError = ex.Message; if (relayLead.Length == 0) { followSelectedAt = null; sim.Follower.Stop("Traffic connection failed"); } bridge.Dispose(); nextBridgeRetry = DateTimeOffset.UtcNow.AddSeconds(5); log.Write("bridge_maintenance_error", new { ex.Message }); }
                finally { maintaining = false; }
            }
        };
        Shown += (_, _) =>
        {
            WindowPinning.Apply(this, keepOnTop.Checked);
            timer.Start(); if (!Program.DemoMode) { TryConnectSimulator(DateTimeOffset.UtcNow); StartSavedRelay(); }
            if (screenshotPath != null)
            {
                var capture = new System.Windows.Forms.Timer { Interval = 12000 };
                capture.Tick += (_, _) =>
                {
                    capture.Stop();
                    void Snapshot(string suffix)
                    {
                        PerformLayout();
                        using var bitmap = new Bitmap(Width, Height); DrawToBitmap(bitmap, new(0, 0, Width, Height));
                        bitmap.Save(Path.Combine(Path.GetDirectoryName(screenshotPath) ?? ".", Path.GetFileNameWithoutExtension(screenshotPath) + suffix + ".png"));
                    }
                    Snapshot("");
                    if (Controls.Find("extraSettings", true).FirstOrDefault() is Button more) { more.PerformClick(); Snapshot("-settings"); more.PerformClick(); }
                    collapseWindow.PerformClick(); Snapshot("-compact");
                    capture.Dispose(); Close();
                };
                capture.Start();
            }
        };
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            CaptureUpdateHandoff();
            sim.Follower.Stop("Application closing");
            e.Cancel = true; closing = true; timer.Stop(); runtimeCancellation.Cancel();
            while (bridgeBusy || maintaining || readingNameplates || installingRuntime) await Task.Delay(50);
            try { await bridge.StopAsync(); } catch (Exception ex) { log.Write("bridge_close_error", new { ex.Message }); }
            await relay.StopAsync(); SaveNetworkSettings(); SaveSettings(); Close();
        };
        FormClosed += (_, _) => { timer.Stop(); runtimeCancellation.Dispose(); relay.Dispose(); bridge.Dispose(); nameplates.Dispose(); sim.Dispose(); timer.Dispose(); buttonHelp.Dispose(); };
        sim.Follower.Settings = Settings(); RefreshUi();
    }
    private FormationSettings Settings() => new((double)behind.Value, (double)lateral.Value, (double)vertical.Value,
        (double)minIas.Value, (double)maxIas.Value, (double)maxMach.Value, (double)maxVs.Value,
        speedOutput.Checked, headingOutput.Checked, altitudeOutput.Checked, vsOutput.Checked, (int)smoothing.Value);
    private void SettingsChanged()
    {
        if (settingsLoading) return;
        if (sim.Follower.ApplySettings(Settings())) { settingsError = null; SaveSettings(); }
        else settingsError = "Invalid limits: keep minimum IAS below maximum. Previous settings remain in use.";
        RefreshUi();
    }
    private void SelectContact()
    {
        if (selectedContact is { } id) ChooseContact(id);
    }
    private void WatchName(string playerName)
    {
        ClearRelayLead();
        followSelectedAt = null; name.Text = playerName.Trim(); sim.SelectTarget(name.Text);
        ResolveWatchedName(DateTimeOffset.UtcNow); SaveSettings(); RefreshUi();
    }
    private IReadOnlyList<TrafficContact> CurrentContacts() => TrafficContacts.Build(sim.CoherentTraffic.Aircraft, sim.Traffic.Aircraft, sim.Own?.Position).Concat(relayTracks.Contacts(DateTimeOffset.UtcNow)).ToArray();
    private void ResolveWatchedName(DateTimeOffset now)
    {
        if (!sim.Connected || !wantTraffic || sim.Focus.MatchById || sim.Focus.TargetId != null) return;
        var fix = TrafficContacts.NamedFix(sim.Focus.Name, CurrentContacts(), now);
        if (fix?.MotionSample(now) is not { } sample) return;
        // Native positions previously reached only ID-selected targets, so watching a
        // native callsign could wait forever even while its map point was available.
        sim.SelectTarget(fix.Name, fix.Id.TrackingId, matchById: true);
        sim.Focus.Observe(sample);
    }
    private void FollowNamedContact()
    {
        if (namesList.SelectedItems.Count != 1 || !wantTraffic || !sim.Connected) return;
        var fix = TrafficContacts.NamedFix((string)namesList.SelectedItems[0].Tag!, CurrentContacts(), DateTimeOffset.UtcNow);
        if (fix == null) { RefreshUi(); return; }
        contactCatalog = CurrentContacts(); ChooseContact(fix.Id);
    }
    private void ChooseContact(ContactId id)
    {
        if (id.Relay) { var remote = relayTracks.All.FirstOrDefault(t => RelayTracks.Contact(t.Id) == id); if (remote != null) BeginRelayFollow(remote.Id); return; }
        ClearRelayLead();
        selectedContact = id;
        var contact = contactCatalog.FirstOrDefault(c => c.Id == id);
        var sample = wantTraffic ? contact?.MotionSample(DateTimeOffset.UtcNow) : null;
        if (sample == null) { RefreshUi(); return; }
        var label = contact!.Name.Length > 0 ? $"{id} · {contact.Name}" : id.ToString();
        sim.SelectTarget(label, id.TrackingId, matchById: true); sim.Focus.Observe(sample); pages.SelectedTab = followPage;
        followSelectedAt = DateTimeOffset.UtcNow; sim.Follower.Settings = Settings(); SaveSettings(); RefreshUi();
    }
    private void StopAndScan()
    {
        if (relay.Running || relayLead.Length > 0) { StopRelayFollowing(); return; }
        followSelectedAt = null; wantTraffic = true; sim.Follower.Stop("Finding nearby aircraft — choose one, then click Follow selected aircraft");
        sim.ScanPositions = true; selectedContact = null;
        nextScan = DateTimeOffset.MinValue; contacts.Focus();
        pages.SelectedTab = aircraftPage;
        if (!bridge.Running && !bridgeBusy) _ = ToggleBridge();
        RefreshUi();
    }
    private async Task ToggleBridge()
    {
        if (maintaining || bridgeBusy) return;
        bridgeBusy = true;
        try
        {
            if (bridge.Running) { followSelectedAt = null; sim.Follower.Stop("Aircraft updates paused; following stopped"); await bridge.StopAsync(); }
            else { await bridge.StartAsync(); bridgeError = null; sim.PmdgViewDetected = bridge.PmdgInstrumentDetected; sim.WorkingTitle787Detected = bridge.WorkingTitle787Detected; log.Write("coherent_bridge_started", new { bridge.ViewName, bridge.PmdgInstrumentDetected, bridge.WorkingTitle787Detected, renewableLeaseSeconds = 30 }); }
        }
        catch (Exception ex) { bridgeError = ex.Message; if (relayLead.Length == 0) { followSelectedAt = null; sim.Follower.Stop(ex.Message); } nextBridgeRetry = DateTimeOffset.UtcNow.AddSeconds(5); log.Write("bridge_error", new { ex.Message }); }
        finally { bridgeBusy = false; RefreshUi(); }
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { sim.Follower.Stop(ex.Message); log.Write("operation_error", new { ex.Message }); connection.Text = ex.Message; }
    }
    private void TryConnectSimulator(DateTimeOffset now)
    {
        if (closing || Program.DemoMode || sim.IsOpen || now < nextSimConnect) return;
        nextSimConnect = now.AddSeconds(5);
        if (SimConnectRuntime.TryLoadAvailable() == 0)
        {
            if (!installingRuntime && now >= nextRuntimeAttempt) _ = InstallRuntime();
            return;
        }
        try { sim.Connect(); lastConnectError = null; }
        catch (Exception ex)
        {
            if (lastConnectError != ex.Message) { log.Write("automatic_connection_wait", new { ex.Message }); lastConnectError = ex.Message; }
            simFooter.ToolTipText = ex.Message;
        }
    }
    private async Task InstallRuntime()
    {
        installingRuntime = true; nextRuntimeAttempt = DateTimeOffset.UtcNow.AddMinutes(5);
        runtimeStatus.Visible = true; SetText(runtimeStatus, "Downloading SimConnect...");
        try
        {
            using var client = new HttpClient();
            var path = await SimConnectRuntime.InstallAsync(client, cancellation: runtimeCancellation.Token);
            UserPreferences.Put("SimConnectPath", path);
            if (closing) return;
            SetText(runtimeStatus, "SimConnect installed.");
            log.Write("runtime_installed", new { path, sdk = "1.7.3", sha256 = SimConnectRuntime.Sha256 });
            nextSimConnect = default; lastConnectError = null;
        }
        catch (Exception ex)
        {
            if (closing) return;
            SetText(runtimeStatus, "SimConnect download failed; retrying in five minutes.");
            lastConnectError = ex.Message; log.Write("runtime_install_failed", new { ex.Message });
        }
        finally { installingRuntime = false; }
    }
    private void Tick()
    {
        DrainRelay();
        sim.Pump(); var now = DateTimeOffset.UtcNow;
        if (now >= nextPinCheck)
        {
            nextPinCheck = now.AddSeconds(1);
            var foreground = WindowPinning.GetForegroundWindow();
            if (keepOnTop.Checked && (!WindowPinning.IsTopmost(Handle) || foreground != lastForeground))
                WindowPinning.Apply(this, true);
            lastForeground = foreground;
        }
        TryConnectSimulator(now);
        TickRelay(now);
        if (sim.Connected && bridge.Running) sim.WorkingTitle787Detected = bridge.WorkingTitle787Detected;
        ResolveWatchedName(now);
        if (sim.Connected && wantTraffic && !readingNameplates && now >= nextNameplateRead) _ = ReadNameplates();
        if (!sim.Connected || !wantTraffic) nameplates.Clear();
        if (followSelectedAt is { } chosenAt)
        {
            if (sim.Follower.BlockReason(now) == null) { followSelectedAt = null; sim.Follower.Engage(now); }
            else if (now - chosenAt > TimeSpan.FromSeconds(30)) { followSelectedAt = null; sim.Follower.Stop("Could not start following — resolve the displayed condition and try again"); }
        }
        if (sim.Connected && !bridgeBusy && !maintaining && !bridge.Running && wantTraffic && now >= nextBridgeRetry)
        { nextBridgeRetry = now.AddSeconds(5); _ = ToggleBridge(); }
        sim.ScanPositions = wantTraffic;
        if (sim.Connected && wantTraffic && now >= nextScan) { sim.Scan((double)radius.Value); nextScan = now.AddSeconds(5); }
        if (now >= nextUi) { RefreshUi(); nextUi = now.AddSeconds(1); }
    }
    private async Task ReadNameplates()
    {
        readingNameplates = true; nextNameplateRead = DateTimeOffset.UtcNow.AddSeconds(1);
        try
        {
            await nameplates.PollAsync();
            if (!sim.Connected || !wantTraffic || closing) nameplates.Clear();
            else if (DateTimeOffset.UtcNow >= nextNameplateLog)
            {
                log.Write("nameplate_snapshot", new { nameplates.ReadAt, nameplates.Contacts, source = "HUD label text only; no geographic position or aircraft object ID" });
                nextNameplateLog = DateTimeOffset.UtcNow.AddSeconds(5);
            }
        }
        catch (Exception ex) { nextNameplateRead = DateTimeOffset.UtcNow.AddSeconds(5); log.Write("nameplate_error", new { ex.Message }); }
        finally { readingNameplates = false; }
    }
    private void RefreshUi()
    {
        RefreshFormationScreen();
        var now = DateTimeOffset.UtcNow; var o = sim.Own; var f = sim.Focus; var controller = sim.Follower;
        SetText(connect, sim.IsOpen ? "Disconnect" : "Connect to MSFS");
        SetText(connection, sim.Connected ? $"Connected · {controller.Adapter}" : sim.Status);
        feed.Enabled = !bridgeBusy && !maintaining && (sim.Connected || bridge.Running); SetText(feed, wantTraffic && sim.Connected ? "Pause aircraft updates" : "Find nearby aircraft");
        own.Text = o == null ? "Load a flight in MSFS 2024." : $"{sim.OwnTitle}\nYour aircraft: GS {o.GroundSpeedKnots:F0} kt · IAS {o.IndicatedSpeedKnots:F0} kt · altitude {o.Position.AltitudeFeet:F0} ft · track {o.TrueTrack:F0}°";
        var fresh = f.Latest != null && now - f.Latest.SourceTime <= TimeSpan.FromSeconds(2);
        target.Text = $"Target: {f.Name} · {f.StatusAt(now)}" + (fresh && f.GroundSpeedKnots.HasValue ? $" · GS {f.GroundSpeedKnots:F0} kt · track {f.GroundTrackDegrees:F0}° · V/S {f.VerticalSpeedFpm:F0} ft/min" : "");
        // List updates are independent of faster telemetry and control-status refreshes.
        if (now >= nextTrafficUi)
        {
            nextTrafficUi = now.AddSeconds(1);
            RefreshTrafficUi(now);
        }
        watchNameplate.Enabled = sim.Connected && wantTraffic && namesList.SelectedItems.Count == 1;
        followNameplate.Enabled = watchNameplate.Enabled && TrafficContacts.NamedFix((string)namesList.SelectedItems[0].Tag!, contactCatalog, now) is { } namedFix
            && !(namedFix.Id.TrackingId == f.TargetId && f.LastIssue != null);
        var solution = controller.Preview;
        preview.Text = solution == null ? "Guidance preview: waiting for usable target and aircraft data."
            : $"{solution.Mode} · slot error: {solution.AlongErrorNm:+0.00;-0.00;0.00} NM along / {solution.CrossErrorNm:+0.00;-0.00;0.00} NM across / {solution.VerticalErrorFeet:+0;-0;0} ft vertically\n"
            + $"Desired: IAS {solution.Ias:F0} kt / M{solution.Mach:F2} · HDG {solution.MagneticHeading:F0}°\nALT {solution.AltitudeFeet:F0} ft · V/S {solution.VerticalSpeedFpm:F0} ft/min\n{solution.Limit}";
        var block = controller.BlockReason(now);
        engage.Enabled = wantTraffic && !controller.Active && block == null && followSelectedAt == null; stop.Enabled = sim.Connected;
        engage.BackColor = engage.Enabled ? Color.Honeydew : SystemColors.Control;
        select.Enabled = sim.Connected && wantTraffic && contactCatalog.FirstOrDefault(c => c.Id == selectedContact)?.Fresh(now) == true;
        state.Text = settingsError ?? (followSelectedAt != null ? $"Preparing to follow {f.Name}: {block ?? "ready"}"
            : controller.Active ? $"{(f.GroundOrbitTarget != null ? "Circling" : "Following")} {f.Name} · {controller.Status}"
            : controller.WaitingForTelemetry ? controller.Status : block ?? controller.Status);
        state.MaximumSize = new(550, 0); state.ForeColor = controller.Active && !string.IsNullOrEmpty(controller.Preview?.Limit) ? Color.DarkOrange : controller.Active ? Color.DarkGreen : Color.DarkSlateGray;
        var ap = controller.Readback;
        actual.Text = ap == null ? "Selected-value readback unavailable." : $"Aircraft selectors: {(ap.MachMode ? $"M{ap.Mach:F2}" : $"{ap.Ias:F0} kt")} · HDG {ap.Heading:F0}° · ALT {ap.Altitude:F0} ft · V/S {ap.Vs:F0} ft/min"
            + "\n" + ap.AutothrottleStatus;
        if (sim.Connected && sim.OwnTitle.Length > 0 && limitsAircraft != sim.OwnTitle + controller.Adapter && sim.Flight is { Valid: true } air && o != null)
        {
            var changedAircraft = savedAircraft != sim.OwnTitle || savedAdapter != controller.Adapter;
            var repairLegacy = speedLimitsVersion < AircraftSpeedLimits.Version && AircraftSpeedLimits.IsLegacyCollapsedRange(Settings(), air);
            if (changedAircraft || repairLegacy)
            {
                var oldLimits = Settings(); var limits = AircraftSpeedLimits.Suggest(air, controller.IsPmdg);
                settingsLoading = true;
                minIas.Value = (decimal)limits.Min; maxIas.Value = (decimal)limits.Max;
                settingsLoading = false; controller.ApplySettings(Settings());
                log.Write("speed_limits_initialized", new { repairLegacy, oldMin = oldLimits.MinIas, oldMax = oldLimits.MaxIas, limits.Min, limits.Max, air.DesignCruiseSpeed });
                if (changedAircraft) controller.Stop("Review speed limits for this aircraft, then enable Follow user");
            }
            speedLimitsVersion = AircraftSpeedLimits.Version;
            savedAircraft = sim.OwnTitle; savedAdapter = controller.Adapter; SaveSettings();
            limitsAircraft = sim.OwnTitle + controller.Adapter;
        }
    }
    private void RefreshTrafficUi(DateTimeOffset now)
    {
        var o = sim.Own; var f = sim.Focus;
        contactCatalog = TrafficContacts.Build(sim.CoherentTraffic.Aircraft, sim.Traffic.Aircraft, o?.Position);
        var visible = contactCatalog.Where(a => TrafficContacts.AboveMinimum(a, (double)minimumAltitude.Value) &&
            (a.Position == null || o == null || o.Position.DistanceNm(a.Position) <= (double)radius.Value)).ToDictionary(a => a.Id);
        if (selectedContact is { } selectedId && !visible.ContainsKey(selectedId)) selectedContact = null;
        contacts.BeginUpdate();
        foreach (var row in contacts.Items.Cast<ListViewItem>().ToArray()) if (!visible.ContainsKey((ContactId)row.Tag!)) contacts.Items.Remove(row);
        foreach (var a in visible.Values)
        {
            var row = contacts.Items.Cast<ListViewItem>().FirstOrDefault(r => (ContactId)r.Tag! == a.Id);
            if (row == null) { row = new ListViewItem(["", "", "", "", ""]) { Tag = a.Id }; contacts.Items.Add(row); }
            StableTrafficListView.SetText(row, 0, a.Name.Length > 0 ? $"{a.Id} · {a.Name}" : a.Id.ToString());
            StableTrafficListView.SetText(row, 1, a.Model);
            row.ToolTipText = $"{row.Text}\n{a.Model}\n{a.Detail}";
            StableTrafficListView.SetText(row, 2, o == null || a.Position == null ? "—" : $"{o.Position.DistanceNm(a.Position):F2} NM");
            StableTrafficListView.SetText(row, 3, a.Position == null ? "—" : $"{a.Position.AltitudeFeet:F0}");
            StableTrafficListView.SetText(row, 4, a.PositionAt == null ? a.Detail : $"{(now-a.PositionAt.Value).TotalSeconds:F0}s ago");
            row.BackColor = wantTraffic && a.Fresh(now) ? Color.Honeydew : SystemColors.Window;
            row.ForeColor = a.Fresh(now) ? SystemColors.WindowText : Color.Gray;
        }
        contacts.SortIfNeeded();
        contacts.EndUpdate();
        // Include position-feed names even when the HUD has no corresponding label.
        var nameRows = nameplates.Contacts.Concat(contactCatalog.Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => new NameplateContact(c.Id.ToString(), c.Name, c.Model, c.Model, "", "")))
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        int fixedNames = 0;
        foreach (var key in lastNameFix.Keys.Where(key => !nameRows.ContainsKey(key)).ToArray()) lastNameFix.Remove(key);
        namesList.BeginUpdate();
        foreach (var row in namesList.Items.Cast<ListViewItem>().ToArray()) if (!nameRows.ContainsKey((string)row.Tag!)) namesList.Items.Remove(row);
        foreach (var c in nameRows.Values)
        {
            var key = c.Name.Trim();
            var row = namesList.Items.Cast<ListViewItem>().FirstOrDefault(r => string.Equals((string)r.Tag!, key, StringComparison.OrdinalIgnoreCase));
            if (row == null) { row = new ListViewItem([c.Name, "", c.Model, c.Distance, c.Altitude]) { Tag = key }; namesList.Items.Add(row); }
            var fix = wantTraffic && sim.Connected ? TrafficContacts.NamedFix(key, contactCatalog, now) : null;
            var filtered = fix != null && fix.Id.TrackingId == f.TargetId && f.LastIssue != null;
            var hasFix = fix != null && !filtered;
            if (fix != null) lastNameFix[key] = fix.PositionAt!.Value;
            var lastFix = lastNameFix.TryGetValue(key, out var fixAt) ? (DateTimeOffset?)fixAt : null;
            if (hasFix) fixedNames++;
            var ambiguous = fix == null && contactCatalog.Count(a => a.Fresh(now) && string.Equals(a.Name.Trim(), key, StringComparison.OrdinalIgnoreCase)) > 1;
            StableTrafficListView.SetText(row, 0, c.Name);
            StableTrafficListView.SetText(row, 1, !sim.Connected ? "Disconnected" : !wantTraffic ? "Paused" : filtered ? "Filtering motion" : hasFix ? "Live position" : ambiguous ? "Choose an ID" : lastFix != null ? "Updates stopped" : "No position");
            StableTrafficListView.SetText(row, 2, c.Model);
            StableTrafficListView.SetText(row, 3, fix?.Position is { } p && o != null ? $"{o.Position.DistanceNm(p):F2} NM" : c.Distance);
            StableTrafficListView.SetText(row, 4, fix?.Position is { } pos ? $"{pos.AltitudeFeet:F0}" : c.Altitude);
            row.BackColor = hasFix ? Color.PaleGreen : filtered || ambiguous ? Color.LightGoldenrodYellow : SystemColors.Window;
            row.ForeColor = hasFix ? Color.DarkGreen : SystemColors.WindowText;
            row.ToolTipText = hasFix ? $"{c.Name} · {fix!.Id}\nLive position · {(now-fix.PositionAt!.Value).TotalSeconds:F1}s old\nFollow selected name checks motion and autopilot readiness before starting."
                : filtered ? $"{c.Name}\nFiltering a multiplayer freeze or jump; waiting for consistent motion."
                : ambiguous ? $"{c.Name}\nSeveral live IDs share this name. Choose the intended aircraft in All IDs or on the map."
                : lastFix != null ? $"{c.Name}\nLast usable position {(now-lastFix.Value).TotalSeconds:F0}s ago. No fresh position is arriving from MSFS.\nThe label's distance and altitude alone cannot supply a map fix."
                : $"{c.Name}\n{c.AircraftType}\nMSFS label: {c.Distance} · {c.Altitude}\nNo fresh geographic position supplied. Watching waits for MSFS to provide it.";
        }
        namesList.SortIfNeeded();
        namesList.EndUpdate();
        SetText(namesPage, $"Visible names ({nameRows.Count})");
        SetText(showNames, $"Visible names ({nameRows.Count})");
        namesStatus.Text = !sim.Connected ? "Connect to MSFS to read player nameplates." : !wantTraffic ? "Aircraft updates paused."
            : nameplates.ReadAt == null && nameRows.Count == 0 ? nameplates.Status
            : $"{nameRows.Count} names · {fixedNames} with a live position\nGreen = position fix. Select a green name to follow.";
        var mapped = visible.Values.Count(a => a.Position != null && o != null && o.Position.DistanceNm(a.Position) <= (double)radius.Value);
        var missing = visible.Values.Count(a => a.Position == null);
        var chosen = contactCatalog.FirstOrDefault(c => c.Id == selectedContact);
        trafficMap.UpdateTraffic(o, visible.Values.ToArray(), (double)radius.Value, selectedContact);
        SetText(idPage, $"All IDs ({visible.Count})");
        var below = contactCatalog.Count(a => !TrafficContacts.AboveMinimum(a, (double)minimumAltitude.Value));
        var mapStatus = $"{mapped} on map · {below} below minimum · {missing} IDs without positions";
        var detail = chosen != null && !chosen.Fresh(now) ? $"{chosen.Id}: {chosen.Detail}; waiting for a live position."
            : bridgeError != null ? "Map traffic connection unavailable; aircraft ID scan continues."
            : mapped == 0 && nameRows.Count > 0 ? $"{nameRows.Count} player labels found — open Visible names. Positions unavailable."
            : sim.CoherentTraffic.LastQueryCount == 0 ? "MSFS map feed is empty; also checking aircraft IDs directly."
            : "Click an ID to follow · Mouse wheel zooms · North is up";
        aircraftStatus.Text = !sim.Connected ? "Connect to MSFS to find nearby aircraft."
            : !wantTraffic ? "Aircraft updates paused. Click Find nearby aircraft to resume."
            : $"{mapStatus}\n{detail}";
        buttonHelp.SetToolTip(aircraftStatus, bridgeError ?? "T IDs come from map traffic; S IDs are SimConnect aircraft objects. They are separate ID systems. Unnamed traffic can include vehicles. IDs without positions cannot be placed on the map or followed.");
    }
    private sealed record Saved(string Target, string Aircraft, FormationSettings Formation, string Adapter = "", bool KeepOnTop = false, bool TargetById = false, decimal MinimumAltitudeFeet = 1000, decimal RangeNm = 20, int SpeedLimitsVersion = 0, bool ResumeWhenTelemetryReturns = false);
    private void SaveSettings()
    {
        try { UserPreferences.Put("Formation", System.Text.Json.JsonSerializer.Serialize(new Saved(sim.Focus.Name, sim.OwnTitle, Settings().Valid ? Settings() : sim.Follower.Settings, sim.Follower.Adapter, keepOnTop.Checked, sim.Focus.MatchById, minimumAltitude.Value, radius.Value, speedLimitsVersion, resumeTelemetry.Checked))); }
        catch (IOException ex) { log.Write("settings_save_error", new { ex.Message }); }
    }
    private void LoadSettings()
    {
        try
        {
            var json = UserPreferences.Get("Formation") ?? (File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null);
            if (json == null) return;
            var saved = System.Text.Json.JsonSerializer.Deserialize<Saved>(json);
            if (saved?.Formation is not { Valid: true } s) return;
            savedAircraft = saved.Aircraft;
            savedAdapter = saved.Adapter;
            speedLimitsVersion = saved.SpeedLimitsVersion;
            keepOnTop.Checked = saved.KeepOnTop;
            resumeTelemetry.Checked = saved.ResumeWhenTelemetryReturns;
            minimumAltitude.Value = Math.Clamp(saved.MinimumAltitudeFeet, minimumAltitude.Minimum, minimumAltitude.Maximum);
            radius.Value = Math.Clamp(saved.RangeNm, radius.Minimum, radius.Maximum);
            behind.Value = (decimal)s.BehindNm; lateral.Value = (decimal)s.RightNm; vertical.Value = (decimal)s.AboveNm;
            minIas.Value = (decimal)s.MinIas; maxIas.Value = (decimal)s.MaxIas; maxMach.Value = (decimal)s.MaxMach; maxVs.Value = (decimal)s.MaxVs;
            speedOutput.Checked = s.Speed; headingOutput.Checked = s.Heading; altitudeOutput.Checked = s.Altitude; vsOutput.Checked = s.VerticalSpeed;
            smoothing.Value = s.SmoothingSamples;
            if (!saved.TargetById && !string.IsNullOrWhiteSpace(saved.Target)) { name.Text = saved.Target; sim.SelectTarget(saved.Target); }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentOutOfRangeException) { log.Write("settings_load_error", new { ex.Message }); }
    }
}
