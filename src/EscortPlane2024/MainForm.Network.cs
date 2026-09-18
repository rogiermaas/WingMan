using System.Security.Cryptography;
using System.Text.Json;

namespace EscortPlane2024;

internal sealed partial class MainForm
{
    private readonly RelayClient relay = new();
    private readonly RelayTracks relayTracks = new();
    private readonly Dictionary<string, RelayMember> members = [];
    private RelayOptions networkOptions = new();
    private readonly string networkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EscortPlane2024", "network.json");
    private readonly TextBox relayServer = new() { Width = 315, PlaceholderText = "https://wingman.rogiermaas.nl" };
    private readonly TextBox relayRoom = new() { Width = 210, MaxLength = 20, CharacterCasing = CharacterCasing.Upper, PlaceholderText = "Paste code to join" };
    private readonly Button applyName = Button("OK");
    private readonly Label runtimeStatus = Label();
    private readonly TextBox pilotName = new() { Width = 180, MaxLength = 64 };
    private readonly ToggleSwitch followable = new() { Checked = true, AccessibleName = "Followable" }, followPilot = new() { AccessibleName = "Follow user", AutoCheck = false };
    private readonly CheckBox automaticUpdates = Check("Install updates automatically", true);
    private readonly ToggleSwitch resumeTelemetry = new() { AccessibleName = "Resume when telemetry returns" };
    private DateTimeOffset nextLeadReacquire;
    private readonly Label followersLabel = Label(), relayStatus = Label(), followingLabel = Label("Choose a nearby pilot (100 NM)."), updateStatus = Label();
    private readonly StableTrafficListView pilots = new() { View = View.Details, Dock = DockStyle.Fill, FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
    private string relayLead = "", confirmedLead = "";
    private bool confirmedShare, sendingUnavailable, relayWasActive;
    private long sequence;
    private DateTimeOffset nextPublish, lastPublished, nextUpdateCheck;
    private bool networkLoaded, updateBusy, updateLaunched;
    private string? relayError;
    private string? updateDirectory;
    private UpdateResume? resumeAfterUpdate;
    private readonly Button collapseWindow = Button("Collapse");
    private readonly Label compactStatus = Label(), compactSpacing = Label();
    private bool compactMode;
    private Size expandedSize;
    private readonly Panel compactPanel = new() { Dock = DockStyle.Fill, Visible = false, Padding = new(6) };

    private void InitializeFormationScreen(TableLayoutPanel layout)
    {
        // The old discovery pages are retained only as optional diagnostics. Normal operation is one screen.
        layout.Controls.Remove(pages); feed.Visible = false; engage.Visible = false; stop.Visible = false;
        BuildFormationLayout(layout);
        wantTraffic = false; sim.ScanPositions = false;
        try
        {
            var savedNetwork = UserPreferences.Get("Network") ?? (File.Exists(networkPath) ? File.ReadAllText(networkPath) : null);
            if (savedNetwork != null) networkOptions = JsonSerializer.Deserialize<RelayOptions>(savedNetwork, RelayClient.Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { log.Write("network_settings_error", new { ex.Message }); }
        if (!Guid.TryParseExact(networkOptions.Id, "N", out _) || networkOptions.Token.Length < 32)
            networkOptions = networkOptions with { Id = Guid.NewGuid().ToString("N"), Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) };
        relayServer.Text = networkOptions.Server; relayRoom.Text = networkOptions.Room; pilotName.Text = networkOptions.Name;
        followable.Checked = networkOptions.Share; automaticUpdates.Checked = networkOptions.AutomaticUpdates;
        relayLead = networkOptions.Lead; networkOptions = networkOptions with { Room = "PUBLIC" }; relayRoom.Text = "PUBLIC";
        pilotName.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ApplyPilotName(); } };
        applyName.Click += async (_, _) => await ApplyPilotName();
        pilotName.TextChanged += (_, _) => applyName.Enabled = !applyingPilotName && pilotName.Text.Trim() != networkOptions.Name && !string.IsNullOrWhiteSpace(pilotName.Text);
        followable.CheckedChanged += (_, _) => { if (relay.Connected) relay.Send(new { type = "configure", share = followable.Checked, lead = confirmedLead }); SaveNetworkSettings(); RefreshVisualState(); };
        automaticUpdates.CheckedChanged += (_, _) => SaveNetworkSettings();
        followPilot.Click += (_, _) => ToggleFollow();
        pilots.SelectedIndexChanged += (_, _) => RefreshVisualState();
        applyName.Enabled = false; networkLoaded = true; SaveNetworkSettings();
    }
    private bool applyingPilotName;
    private async Task ApplyPilotName()
    {
        if (closing || applyingPilotName || pilotName.Text.Trim() == networkOptions.Name) return;
        if (string.IsNullOrWhiteSpace(pilotName.Text)) { pilotName.Text = networkOptions.Name; return; }
        if (System.Text.Encoding.UTF8.GetByteCount(pilotName.Text.Trim()) > 64) { MessageBox.Show(this, "Please use a shorter pilot name (64 UTF-8 bytes maximum).", "WingMan"); return; }
        applyingPilotName = true; applyName.Enabled = false;
        try
        {
            StopRelayFollowing(); await relay.StopAsync(); networkOptions = networkOptions with { Name = pilotName.Text.Trim() }; SaveNetworkSettings();
            if (!closing && !Program.DemoMode) ConnectRelay(false);
            RefreshUi();
        }
        finally { applyingPilotName = false; applyName.Enabled = pilotName.Text.Trim() != networkOptions.Name; }
    }
    private void ToggleFollow()
    {
        if (sim.Follower.Active || sim.Follower.WaitingForTelemetry || followSelectedAt != null) StopRelayFollowing();
        else FollowSelectedNearbyAircraft();
        RefreshUi();
    }
    private void SaveNetworkSettings()
    {
        if (!networkLoaded) return;
        networkOptions = networkOptions with { Server = relayServer.Text.Trim(), Room = "PUBLIC", Share = followable.Checked, Lead = relayLead, AutomaticUpdates = automaticUpdates.Checked };
        try { UserPreferences.Put("Network", JsonSerializer.Serialize(networkOptions, RelayClient.Json)); }
        catch (IOException ex) { log.Write("network_settings_error", new { ex.Message }); }
    }
    private void ConnectRelay(bool create)
    {
        try
        {
            relayError = null; SaveNetworkSettings(); if (networkOptions.Name.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(networkOptions.Name) > 64) throw new ArgumentException("Enter a pilot name of up to 64 UTF-8 bytes.");
            relay.Start(networkOptions with { Room = "PUBLIC" }, false); networkOptions = networkOptions with { Reconnect = true }; SaveNetworkSettings(); nextUpdateCheck = default;
        }
        catch (Exception ex) { relayStatus.Text = ex.Message; MessageBox.Show(this, ex.Message, "WingMan connection"); }
    }
    private void StartSavedRelay()
    {
        resumeAfterUpdate = UpdateResume.Consume();
        if (networkOptions.Name.Length > 0 && networkOptions.Server.Length > 0) ConnectRelay(false);
    }
    private void BeginRelayFollow(string id)
    {
        if (id == networkOptions.Id || !relay.Connected || !members.TryGetValue(id, out var m) || !m.Share || !m.Online) return;
        sim.Follower.Stop("Preparing formation follow"); relayLead = id; confirmedLead = "";
        sim.SelectTarget(m.Name, RelayTracks.Contact(id).TrackingId, true); sim.Focus.FastTelemetry = true; sim.Focus.TrustedGroundStatus = true;
        sim.Follower.Settings = Settings(); followSelectedAt = DateTimeOffset.UtcNow;
        relay.Send(new { type = "configure", share = followable.Checked, lead = id }); SaveNetworkSettings(); SaveSettings();
    }
    private void ClearRelayLead()
    {
        if (relayLead.Length == 0 && confirmedLead.Length == 0) return;
        relayLead = ""; confirmedLead = "";
        if (relay.Connected) relay.Send(new { type = "configure", share = followable.Checked, lead = "" });
        SaveNetworkSettings();
    }
    private void StopRelayFollowing()
    {
        followSelectedAt = null; resumeAfterUpdate = null; sim.Follower.Stop("Following stopped"); ClearRelayLead(); pilots.Focus();
    }
    private void DrainRelay()
    {
        relay.Drain((message, received) =>
        {
            var type = message.GetProperty("type").GetString();
            switch (type)
            {
                case "welcome":
                    relayError = null; sendingUnavailable = false; relayTracks.Clear(); members.Clear(); sequence = 0; lastPublished = default; confirmedLead = ""; confirmedShare = networkOptions.Share;
                    relayRoom.Text = relay.Room; SaveNetworkSettings(); break;
                case "configured": confirmedLead = message.GetProperty("lead").GetString() ?? ""; confirmedShare = message.GetProperty("share").GetBoolean(); break;
                case "roster":
                    members.Clear(); foreach (var m in message.GetProperty("members").Deserialize<RelayMember[]>(RelayClient.Json) ?? []) members[m.Id] = m;
                    foreach (var track in relayTracks.All.ToArray()) if (!members.TryGetValue(track.Id, out var m) || !m.Online || !m.Share) relayTracks.Remove(track.Id);
                    if (relayLead.Length > 0 && (!members.TryGetValue(relayLead, out var lead) || !lead.Online || !lead.Share))
                    {
                        if (lead is { Online: true, Share: false }) sim.Follower.Stop("Lead stopped sharing");
                        else if (!sim.Follower.WaitingForTelemetry) sim.Follower.TelemetryLost("Lead disconnected");
                        followSelectedAt = null; sim.Focus.ResetMotion(); confirmedLead = "";
                    }
                    break;
                case "telemetry":
                    var id = message.GetProperty("id").GetString()!; if (id == networkOptions.Id || !relay.ClockReady) break;
                    var t = message.GetProperty("telemetry").Deserialize<RelayTelemetry>(RelayClient.Json); if (t == null) break;
                    if (t.Paused || t.SimRate != 1) { relayTracks.Remove(id); if (id == relayLead) { sim.Follower.Stop("Lead is paused or not at 1x simulation speed"); sim.Focus.Reset(); followSelectedAt = null; } break; }
                    var at = DateTimeOffset.FromUnixTimeMilliseconds((long)(message.GetProperty("serverTimeMs").GetInt64() - relay.ServerOffsetMs));
                    if (!relayTracks.Accept(id, message.GetProperty("name").GetString()!, t, at, DateTimeOffset.UtcNow)) break;
                    if (id == relayLead && confirmedLead == id)
                    {
                        if (sim.Focus.TargetId != RelayTracks.Contact(id).TrackingId) { sim.SelectTarget(members.GetValueOrDefault(id)?.Name ?? id, RelayTracks.Contact(id).TrackingId, true); sim.Focus.FastTelemetry = true; sim.Focus.TrustedGroundStatus = true; }
                        sim.Focus.Observe(new(RelayTracks.Contact(id).TrackingId, sim.Focus.Name, t.Model, t.Latitude, t.Longitude, t.AltitudeFeet * .3048, t.HeadingDegrees, t.OnGround, at, received));
                    }
                    break;
                case "unavailable":
                    var missing = message.GetProperty("id").GetString()!; relayTracks.Remove(missing);
                    if (missing == relayLead) { sim.Follower.TelemetryLost("Lead telemetry unavailable"); sim.Focus.ResetMotion(); followSelectedAt = null; }
                    break;
                case "disconnected":
                    relayTracks.Clear(); members.Clear(); confirmedLead = ""; confirmedShare = false;
                    if (relayLead.Length > 0) { sim.Follower.TelemetryLost("WingMan connection lost"); sim.Focus.ResetMotion(); followSelectedAt = null; }
                    break;
                case "error":
                    var error = message.GetProperty("message").GetString()!; log.Write("relay_error", new { error });
                    relayError = error; if (followSelectedAt != null || sim.Follower.WaitingForTelemetry) { followSelectedAt = null; sim.Follower.Stop(error); confirmedLead = ""; }
                    break;
            }
        });
    }
    private void TickRelay(DateTimeOffset now)
    {
        sim.ShareTelemetry = relay.Connected;
        if (relay.Connected && now >= nextPublish)
        {
            nextPublish = now.AddMilliseconds(100);
            if (sim.Connected && sim.Own is { } ownData && sim.Flight is { } flight && now - ownData.ReceivedAt < TimeSpan.FromSeconds(2)
                && now - flight.ReceivedAt < TimeSpan.FromSeconds(2) && !sim.Paused && flight.SimRate == 1 && ownData.Position.IsValid)
            {
                sendingUnavailable = false;
                if (ownData.ReceivedAt > lastPublished)
                {
                    var telemetry = new RelayTelemetry(++sequence, ownData.ReceivedAt.ToUnixTimeMilliseconds(), Math.Max(0, (now - ownData.ReceivedAt).TotalMilliseconds),
                        ownData.Position.Latitude, ownData.Position.Longitude, ownData.Position.AltitudeFeet, ownData.GroundSpeedKnots,
                        FormationGeometry.Normalize(ownData.TrueTrack), FormationGeometry.Normalize(flight.TrueHeading), ownData.VerticalSpeedFpm,
                        LimitUtf8(sim.OwnTitle, 160), false, flight.OnGround != 0, flight.SimRate,
                        LimitUtf8(sim.OwnDetails[0] ?? "", 160), LimitUtf8(sim.OwnDetails[1] ?? "", 160), LimitUtf8(sim.OwnDetails[2] ?? "", 160), LimitUtf8(sim.OwnDetails[3] ?? "", 160));
                    if (telemetry.Valid) { relay.Send(new { type = confirmedShare ? "telemetry" : "observer", telemetry }); lastPublished = ownData.ReceivedAt; }
                }
            }
            else if (!sendingUnavailable) { relay.Send(new { type = "unavailable" }); sendingUnavailable = true; }
        }
        if (sim.Follower.WaitingForTelemetry && relay.Connected && confirmedLead != relayLead && relayLead.Length > 0
            && members.TryGetValue(relayLead, out var returningLead) && returningLead.Online && returningLead.Share && now >= nextLeadReacquire)
        {
            nextLeadReacquire = now.AddSeconds(2);
            relay.Send(new { type = "configure", share = followable.Checked, lead = relayLead });
        }
        if (!sim.Follower.Active && !sim.Follower.WaitingForTelemetry && confirmedLead.Length > 0 && followSelectedAt == null)
        { confirmedLead = ""; relay.Send(new { type = "configure", share = followable.Checked, lead = "" }); }
        relayWasActive = sim.Follower.Active && relayLead.Length > 0;
        if (resumeAfterUpdate is { } resume && now < resume.Expires && relay.Connected && sim.Connected && sim.OwnTitle == resume.Aircraft
            && networkOptions.Server == resume.Server && relay.Room == resume.Room && members.TryGetValue(resume.Lead, out var lead) && lead.Online && lead.Share
            && relayTracks.Get(resume.Lead) is { } freshLead && now - freshLead.At < TimeSpan.FromSeconds(2)
            && (!freshLead.Telemetry.OnGround || vertical.Value > 0))
        { resumeAfterUpdate = null; BeginRelayFollow(resume.Lead); }
        if (now >= nextUpdateCheck && !updateBusy && !updateLaunched && networkOptions.Server.Length > 0)
        { nextUpdateCheck = now.AddMinutes(5); _ = CheckForUpdates(false); }
    }
    private void RefreshFormationScreen()
    {
        if (!networkLoaded) return; var now = DateTimeOffset.UtcNow;
        var leadName = members.GetValueOrDefault(relayLead)?.Name ?? sim.Focus.Name;
        var circling = sim.Focus.GroundOrbitTarget != null;
        SetText(spacingLabel, circling ? "Radius NM" : "Behind NM");
        lateral.Enabled = !circling;
        SetText(compactStatus, sim.Follower.Active ? (circling ? "Circling " : "Following ") + leadName : followSelectedAt != null ? "Preparing to follow " + leadName : sim.Follower.Status);
        SetText(compactSpacing, circling
            ? $"Radius {sim.Follower.Preview?.OrbitRadiusNm ?? (double)behind.Value:0.0} · Above {vertical.Value:0.0} NM"
            : $"Behind {behind.Value:0.0} · Right {lateral.Value:0.0} · Above {vertical.Value:0.0} NM");
        compactStatus.ForeColor = sim.Follower.Active ? Color.DarkGreen : Color.DarkSlateGray;
        if (updateLaunched && updateDirectory != null && File.Exists(Path.Combine(updateDirectory, "update-error.txt")))
        {
            updateLaunched = false; nextUpdateCheck = now.AddMinutes(5);
            SetText(updateStatus, "Update failed. WingMan keeps running; Check for updates to retry.");
            log.Write("update_helper_failed", new { error = File.ReadAllText(Path.Combine(updateDirectory, "update-error.txt")) }); updateDirectory = null;
        }
        SetText(relayStatus, relayError ?? (relay.Connected ? $"Connected · relay RTT {relay.RoundTripMs:F0} ms" : relay.Status));
        relayServer.Enabled = relayRoom.Enabled = !relay.Running;
        var followers = followable.Checked ? members.Values.Where(m => m.Online && m.Lead == networkOptions.Id).Select(m => m.Name).Order().ToArray() : [];
        followersLabel.Visible = followers.Length > 0;
        SetText(followersLabel, followers.Length > 0 ? "Following you: " + string.Join(", ", followers) : "");
        SetText(followingLabel, "Green: WingMan · Grey: MSFS");
        RefreshNearbyAircraft(now); RefreshVisualState();
    }
    private async Task CheckForUpdates(bool manual)
    {
        if (updateBusy || updateLaunched) return; updateBusy = true;
        try
        {
            SaveNetworkSettings(); var server = RelayClient.ServerUri(networkOptions.Server);
            var update = await ApplicationUpdates.CheckAsync(server);
            SetText(updateStatus, update == null ? $"Version {ApplicationUpdates.Version} is current." : $"Version {update.Manifest.Version} is available.");
            if (update != null && (manual || automaticUpdates.Checked))
            {
                SaveSettings(); SaveNetworkSettings();
                var resume = new UpdateResume(networkOptions.Server, relay.Room, relayLead, sim.OwnTitle, DateTimeOffset.UtcNow.AddMinutes(10), (sim.Follower.Active || sim.Follower.WaitingForTelemetry) && relayLead.Length > 0);
                updateDirectory = ApplicationUpdates.Launch(update, server, resume); updateLaunched = true; SetText(updateStatus, "Downloading verified update; the app will restart when ready.");
            }
        }
        catch (Exception ex) { SetText(updateStatus, "Update check: " + ex.Message); log.Write("update_error", new { ex.Message }); }
        finally { updateBusy = false; }
    }
    private void CaptureUpdateHandoff() => ApplicationUpdates.CaptureHandoff(new(networkOptions.Server, relay.Room, relayLead,
        sim.OwnTitle, DateTimeOffset.UtcNow.AddMinutes(10), (sim.Follower.Active || sim.Follower.WaitingForTelemetry) && relayLead.Length > 0));
    private static string LimitUtf8(string text, int bytes)
    {
        while (System.Text.Encoding.UTF8.GetByteCount(text) > bytes) text = text[..^1];
        return text;
    }
}
