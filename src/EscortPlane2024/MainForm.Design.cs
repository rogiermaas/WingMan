namespace EscortPlane2024;

internal sealed partial class MainForm
{
    private readonly ToolStripStatusLabel simFooter = new("Disconnected from MSFS");
    private readonly ToolStripStatusLabel relayFooter = new("Relay: offline") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel delayFooter = new("Delay: —");
    private readonly ToolStripStatusLabel versionFooter = new("v" + ApplicationUpdates.Version) { ForeColor = Color.SlateGray };
    private readonly ToggleSwitch compactFollow = new() { AccessibleName = "Follow user", AutoCheck = false };
    private readonly Label spacingLabel = Label("Behind NM");
    private readonly Label speedLimitHint = Label();
    private readonly StableScrollPanel extraPanel = new();
    private TableLayoutPanel circleSpeedRow = null!;
    private readonly StatusStrip footer = new() { SizingGrip = true, BackColor = Color.FromArgb(229, 236, 244), Padding = new(8, 0, 12, 0) };
    private static readonly Color Accent = Color.FromArgb(22, 111, 187), SoftRed = Color.FromArgb(251, 223, 227);
    private static Label SwitchLabel(string text) => new() { Text = text, AutoSize = true, ForeColor = Color.Black, Margin = new(3, 4, 3, 2) };
    private static TableLayoutPanel AlignedRow(int labelWidth, params Control[] controls)
    {
        var row = new BufferedTableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = controls.Length, RowCount = 1, Margin = new(0), BackColor = Color.White };
        row.RowStyles.Add(new(SizeType.AutoSize)); row.MinimumSize = new(0, 28);
        for (int i = 0; i < controls.Length; i++)
        {
            var control = controls[i];
            row.ColumnStyles.Add(new(i == 0 ? SizeType.Absolute : SizeType.AutoSize, i == 0 ? labelWidth : 0));
            control.Anchor = AnchorStyles.Left; control.Margin = new(2, 1, control is ToggleSwitch ? 8 : 4, 1);
            row.Controls.Add(control, i, 0);
        }
        return row;
    }
    private static Label SectionTitle(string text) => new() { Text = text, AutoSize = true, Font = new("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(32, 64, 93), Margin = new(2, 1, 2, 5) };
    private static void StyleButton(ButtonBase button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(191, 207, 220);
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.BackColor = primary ? Accent : Color.FromArgb(239, 244, 249);
        button.ForeColor = primary ? Color.White : Color.FromArgb(34, 61, 83);
        button.Padding = new(4, 0, 4, 0); button.Cursor = Cursors.Hand;
        button.Margin = new(2, 1, 2, 1);
    }
    private void BuildFormationLayout(TableLayoutPanel layout)
    {
        layout.Controls.Clear(); layout.RowStyles.Clear(); layout.RowCount = 4; layout.Padding = new(5); layout.BackColor = Color.FromArgb(239, 244, 249);
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        ClientSize = new(650, 600); MinimumSize = new(650, 600); BackColor = layout.BackColor;
        footer.Items.AddRange([simFooter, relayFooter, delayFooter, versionFooter]); Controls.Add(footer); footer.Dock = DockStyle.Bottom; Controls.SetChildIndex(footer, Controls.Count - 1);

        var banner = new Panel { Height = 64, Dock = DockStyle.Fill, BackColor = Color.Black, Margin = new(0, 0, 0, 4) };
        using (var resource = typeof(Program).Assembly.GetManifestResourceStream("wingman-logo.png"))
        {
            if (resource != null)
            {
                using var source = Image.FromStream(resource);
                var logo = new PictureBox { Image = new Bitmap(source), SizeMode = PictureBoxSizeMode.Zoom, Location = new(4, 1), Size = new(116, 62) };
                banner.Controls.Add(logo); FormClosed += (_, _) => logo.Image?.Dispose();
            }
        }
        banner.Controls.Add(new Label { Text = "Fly together.", AutoSize = true, Font = new("Segoe UI", 17, FontStyle.Regular), ForeColor = Color.White, Location = new(128, 3) });
        banner.Controls.Add(new Label { Text = "Lead. Follow. Or both.", AutoSize = true, Font = new("Segoe UI", 10), ForeColor = Color.FromArgb(119, 199, 255), Location = new(131, 37) });
        var bannerActions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Top, Location = new(476, 8), BackColor = Color.Transparent };
        keepOnTop.ForeColor = Color.White; keepOnTop.BackColor = Color.Transparent; bannerActions.Controls.Add(keepOnTop); bannerActions.Controls.Add(collapseWindow);
        banner.Resize += (_, _) => bannerActions.Left = banner.ClientSize.Width - bannerActions.Width - 10;
        banner.Controls.Add(bannerActions); StyleButton(collapseWindow); layout.Controls.Add(banner, 0, 0);

        BufferedFlowLayoutPanel Card()
        {
            return new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new(5), Margin = new(0, 0, 0, 4), BackColor = Color.White };
        }
        var general = Card();
        pilotName.Width = 250; pilotName.PlaceholderText = "Enter your pilot name";
        StyleButton(applyName, true); applyName.AutoSize = false; applyName.Size = new(44, 29); applyName.Padding = new(0);
        general.Controls.Add(AlignedRow(100, Label("Your name"), pilotName, applyName));
        own.ForeColor = Color.SlateGray; own.MaximumSize = new(590, 0); general.Controls.Add(own); layout.Controls.Add(general, 0, 1);
        runtimeStatus.ForeColor = Color.SlateGray; runtimeStatus.Visible = false; general.Controls.Add(runtimeStatus);

        var sharing = Card();
        followersLabel.Visible = false; sharing.Controls.Add(AlignedRow(100, SwitchLabel("Followable"), followable)); sharing.Controls.Add(followersLabel); layout.Controls.Add(sharing, 0, 2);

        var following = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Color.White, Padding = new(5), Margin = new(0) };
        for (int i = 0; i < 6; i++) following.RowStyles.Add(new(i == 1 ? SizeType.Percent : SizeType.AutoSize, i == 1 ? 100 : 0));
        StyleButton(searchMsfs); searchMsfs.Click += (_, _) => SearchMsfsAircraft();
        followingLabel.ForeColor = Color.SlateGray;
        var listHeader = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new(0, 0, 0, 2) };
        listHeader.ColumnStyles.Add(new(SizeType.AutoSize)); listHeader.ColumnStyles.Add(new(SizeType.AutoSize)); listHeader.ColumnStyles.Add(new(SizeType.Percent, 100));
        var nearbyTitle = SectionTitle("Nearby aircraft"); nearbyTitle.Anchor = AnchorStyles.Left | AnchorStyles.Bottom; nearbyTitle.Margin = new(2, 0, 8, 2);
        searchMsfs.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        followingLabel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; followingLabel.TextAlign = ContentAlignment.BottomRight; followingLabel.Margin = new(2, 0, 2, 2);
        listHeader.Controls.Add(nearbyTitle, 0, 0); listHeader.Controls.Add(searchMsfs, 1, 0); listHeader.Controls.Add(followingLabel, 2, 0);
        following.Controls.Add(listHeader, 0, 0);
        pilots.BorderStyle = BorderStyle.FixedSingle; pilots.BackColor = Color.FromArgb(250, 252, 254); pilots.ForeColor = Color.FromArgb(34, 54, 75);
        pilots.Columns.Add("Pilot / ID", 156); pilots.Columns.Add("Aircraft", 152); pilots.Columns.Add("Distance", 86); pilots.Columns.Add("Source / data", 180);
        TrafficListSorting.Enable(pilots, TrafficColumn.Text, TrafficColumn.Text, TrafficColumn.Distance, TrafficColumn.Text);
        following.Controls.Add(pilots, 0, 1);
        buttonHelp.SetToolTip(pilots, "Green: WingMan aircraft. Grey: local MSFS traffic. Live means a usable position; Name only cannot be followed. Click column headings to sort.");
        buttonHelp.SetToolTip(searchMsfs, "Search MSFS aircraft and visible player labels within the available feeds. Native positions are scanned within 100 NM; labels may have no position. Scanning continues automatically and does not stop following.");
        var offsets = new BufferedTableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Margin = new(0) };
        for (int i = 0; i < 3; i++) offsets.ColumnStyles.Add(new(SizeType.Percent, 100f / 3));
        offsets.Controls.Add(AlignedRow(100, spacingLabel, behind), 0, 0);
        offsets.Controls.Add(AlignedRow(92, Label("Right NM"), lateral), 1, 0);
        offsets.Controls.Add(AlignedRow(92, Label("Above NM"), vertical), 2, 0);
        following.Controls.Add(offsets, 0, 2);
        buttonHelp.SetToolTip(behind, "Distance behind an airborne lead: 0.0 flies alongside with your Right offset. During circling this is the radius; zero uses the calculated minimum for speed and wind.");
        buttonHelp.SetToolTip(vertical, "Height relative to the lead. A grounded lead requires a positive value to circle. 0.1 NM is 608 ft. Choose clearance for surrounding terrain.");
        following.Controls.Add(AlignedRow(100, Label("Match"), speedOutput, headingOutput, altitudeOutput), 0, 3);
        var actions = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new(0) };
        var extraToggle = Button("More settings"); extraToggle.Name = "extraSettings"; StyleButton(extraToggle);
        actions.Controls.Add(AlignedRow(82, Label("IAS limits"), Label("Min kt"), minIas, Label("Max kt"), maxIas, Label("Mach cap"), maxMach));
        speedLimitHint.ForeColor = Color.SlateGray; speedLimitHint.MaximumSize = new(610, 0); actions.Controls.Add(speedLimitHint);
        circleSpeedRow = AlignedRow(100, Label("Circle IAS kt"), circleIas, Label("Set flaps as needed"));
        circleSpeedRow.Visible = false; actions.Controls.Add(circleSpeedRow);
        buttonHelp.SetToolTip(circleIas, "Speed used only while circling. May be lower than the normal following minimum. Your Max IAS and Mach caps still apply. Deploy flaps as needed; the circle can tighten as actual speed decreases.");
        buttonHelp.SetToolTip(minIas, "Your minimum IAS is used, including with flaps. The design-speed estimate only warns; it never raises this value.");
        buttonHelp.SetToolTip(maxIas, "IAS and Mach are independent caps. The lower equivalent speed applies. Their conversion changes with altitude, so editing one does not change the other.");
        buttonHelp.SetToolTip(maxMach, "Raise this maximum to allow faster automatic catch-up at altitude. The lower of the IAS and Mach limits applies. Changes keep following active; review the aircraft redline.");
        actions.Controls.Add(AlignedRow(100, SwitchLabel("Follow user"), followPilot, extraToggle, SwitchLabel("Auto-resume"), resumeTelemetry)); state.MaximumSize = new(610, 0); state.Font = new("Segoe UI", 10); actions.Controls.Add(state); following.Controls.Add(actions, 0, 4);
        buttonHelp.SetToolTip(resumeTelemetry, "Resume after losing your own simulator telemetry when fresh data returns. An established WingMan lead continues from remembered data during lead-only outages, even with this switch off. Turning Follow user off cancels both.");
        var extras = extraPanel; extras.Dock = DockStyle.Fill; extras.FlowDirection = FlowDirection.TopDown; extras.WrapContents = false;
        extras.AutoScroll = true; extras.Height = 235; extras.Width = 610; extras.Visible = false; extras.Padding = new(0, 3, 0, 0); extras.Margin = new(0);
        extras.Controls.Add(AlignedRow(142, Label("Max V/S ft/min"), maxVs, Label("Smoothing samples"), smoothing));
        extras.Controls.Add(AlignedRow(220, circleBelow, circleHeight, Label("ft AGL (100 ft steps)")));
        buttonHelp.SetToolTip(circleBelow, "When the lead is below this height above ground, circle at this height above the ground beneath the lead. Above NM is temporarily ignored. Following resumes 100 ft above the threshold. Requires WingMan height telemetry. Minimum 500 ft.");
        vsOutput.Text = "Automatic V/S height matching";
        extras.Controls.Add(AlignedRow(290, vsOutput, automaticUpdates));
        var checkUpdates = Button("Check for updates"); StyleButton(checkUpdates); checkUpdates.Click += async (_, _) => await CheckForUpdates(true);
        extras.Controls.Add(Row(checkUpdates, updateStatus));
        extras.Controls.Add(Label("Negative offsets mean left/below. 0.1 NM vertically is 608 ft.")); extras.Controls.Add(actual); extras.Controls.Add(preview);
        var locateRuntime = Button("Locate SimConnect.dll"); StyleButton(locateRuntime);
        locateRuntime.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Title = "Select SimConnect.dll from your MSFS 2024 SDK", Filter = "SimConnect runtime|SimConnect.dll", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) { UserPreferences.Put("SimConnectPath", dialog.FileName); SetText(state, "Runtime selected. Connecting to MSFS automatically."); }
        };
        var local = Button("Local traffic diagnostics"); StyleButton(local); extras.Controls.Add(Row(locateRuntime, local));
        local.Click += (_, _) => { pages.Visible = !pages.Visible; pages.Height = 280; if (pages.Visible) SearchMsfsAircraft(); };
        pages.Dock = DockStyle.Top; pages.Width = 580; pages.Height = 280; pages.Visible = false; extras.Controls.Add(pages);
        extraToggle.Click += (_, _) => { extras.Visible = !extras.Visible; extraToggle.Text = extras.Visible ? "Less" : "More settings"; ClientSize = new(ClientSize.Width, extras.Visible ? Math.Min(835, Screen.FromControl(this).WorkingArea.Height - 70) : 600); };
        following.Controls.Add(extras, 0, 5); layout.Controls.Add(following, 0, 3);
        BuildCompactLayout(layout);
    }
    private void BuildCompactLayout(TableLayoutPanel layout)
    {
        var expand = Button("Expand"); StyleButton(expand);
        compactFollow.Click += (_, _) => ToggleFollow();
        var compactPin = Check("Keep on top", keepOnTop.Checked);
        compactPin.CheckedChanged += (_, _) => keepOnTop.Checked = compactPin.Checked; keepOnTop.CheckedChanged += (_, _) => compactPin.Checked = keepOnTop.Checked;
        var content = new BufferedFlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White };
        content.Controls.Add(Row(SectionTitle("WingMan"), compactPin, expand)); compactStatus.MaximumSize = new(460, 0); content.Controls.Add(compactStatus); content.Controls.Add(compactSpacing); content.Controls.Add(AlignedRow(100, SwitchLabel("Follow user"), compactFollow));
        compactPanel.Controls.Add(content); Controls.Add(compactPanel);
        void Collapse(bool value)
        {
            if (compactMode == value) return; compactMode = value;
            if (value) { expandedSize = ClientSize; MinimumSize = new(590, 195); ClientSize = new(580, 170); layout.Visible = false; compactPanel.Visible = true; compactPanel.BringToFront(); Controls.SetChildIndex(footer, Controls.Count - 1); }
            else { compactPanel.Visible = false; MinimumSize = new(650, 600); ClientSize = expandedSize; layout.Visible = true; }
        }
        collapseWindow.Click += (_, _) => Collapse(true); expand.Click += (_, _) => Collapse(false);
    }
    private void RefreshSpeedLimitHint()
    {
        if (sim.Flight is not { Valid: true } air || sim.Own is not { } ownData)
        { SetText(speedLimitHint, "Independent caps: the lower IAS / Mach limit applies."); return; }
        var calibration = Math.Clamp(ownData.IndicatedSpeedKnots / GuidanceController.Cas(air.Mach, air.AmbientPressure), .8, 1.2);
        var machIas = GuidanceController.Cas((double)maxMach.Value, air.AmbientPressure) * calibration;
        var cap = Math.Min((double)maxIas.Value, machIas);
        SetText(speedLimitHint, $"Current cap: {cap:F0} kt / M{GuidanceController.MachFromCas(cap / calibration, air.AmbientPressure):F2} · {(machIas < (double)maxIas.Value ? "Mach" : "IAS")} limit applies");
    }
    private void RefreshVisualState()
    {
        simFooter.Text = sim.Connected ? "Connected to MSFS" : "Disconnected from MSFS";
        simFooter.ForeColor = sim.Connected ? Color.FromArgb(29, 119, 71) : Color.FromArgb(185, 38, 49);
        simFooter.ToolTipText = sim.Connected ? sim.OwnTitle : lastConnectError ?? sim.Status;
        relayFooter.Text = relay.Connected ? "Connected to WingMan Server" : "Connecting to WingMan Server";
        relayFooter.ToolTipText = relayError ?? relay.Status;
        relayFooter.ForeColor = relay.Connected ? Color.FromArgb(29, 119, 71) : Color.Black;
        delayFooter.Text = relay.Connected && relay.ClockReady ? $"Delay: {relay.RoundTripMs:F0} ms" : "Delay: —";
        buttonHelp.SetToolTip(followable, followable.Checked ? "Others can select you. Click to stop sharing your aircraft." : "Others cannot select you. Click to let them follow you.");
        bool following = sim.Follower.Active || sim.Follower.WaitingForTelemetry || followSelectedAt != null;
        followPilot.Checked = compactFollow.Checked = following;
        followPilot.Enabled = following || sim.Connected && SelectedNearbyAircraft?.CanFollow == true;
        buttonHelp.SetToolTip(followPilot, SelectedNearbyAircraft is { CanFollow: false }
            ? "This aircraft has no fresh position yet. MSFS name labels alone cannot be followed." : "Follow the selected aircraft; click again to stop.");
        compactFollow.Enabled = followPilot.Enabled;
    }
}
