namespace EscortPlane2024;

// Updates selections and optionally V/S mode. AP master and autothrottle arm remain with the pilot.
internal sealed class FollowerController(IFollowerSession sim, Diagnostics log)
{
    private readonly GuidanceController guidance = new();
    private readonly Dictionary<string, (double Value, DateTimeOffset Sent)> pending = [];
    private readonly Dictionary<string, double> initial = [];
    private DateTimeOffset lastTick;
    private string engagedAircraft = "";
    private bool engagedMach;
    private ulong? engagedTarget;
    private string engagedTargetName = "";
    private bool resumeWhenTelemetryReturns;
    public bool WaitingForTelemetry { get; private set; }
    public bool ResumeWhenTelemetryReturns
    {
        get => resumeWhenTelemetryReturns;
        set
        {
            resumeWhenTelemetryReturns = value;
            if (!value && WaitingForTelemetry) Stop("Automatic resume disabled; enable Follow user to follow again");
        }
    }
    private DateTimeOffset? verticalModeRequested;
    private TargetEstimator? rememberedTarget;
    private bool rememberedCircle;
    public bool UsingRememberedData { get; private set; }
    public double RememberedAgeSeconds(DateTimeOffset now) => rememberedTarget?.Latest is { } t ? Math.Max(0, (now - t.SourceTime).TotalSeconds) : 0;
    private TargetEstimator GuidanceTarget(DateTimeOffset now)
    {
        var live = sim.Focus;
        var usable = live.Latest is { } t && (now - t.SourceTime).TotalSeconds is >= -1 and <= 2 && live.LastIssue == null
            && (live.GroundOrbitTarget != null || live.GroundSpeedKnots is > 40 && live.GroundTrackDegrees != null
                || Settings.CircleBelowFeet > 0 && t.AboveGroundFeet <= Settings.CircleBelowFeet);
        UsingRememberedData = Active && !usable && rememberedTarget != null
            && sim.OwnTitle == engagedAircraft && live.TargetId == engagedTarget && live.Name == engagedTargetName;
        return UsingRememberedData ? rememberedTarget!.ContinueAt(now, rememberedCircle) : live;
    }
    public FormationSettings Settings { get; set; } = new();
    public bool ApplySettings(FormationSettings settings)
    {
        if (!settings.Valid) return false;
        if (Active)
        {
            // Disabled axes must not leave readback timeouts behind. Still-enabled
            // axes keep their outstanding confirmations before issuing the new target.
            foreach (var axis in new[] { "speed", "heading", "altitude", "vs" })
            {
                var enabled = axis switch { "speed" => settings.Speed, "heading" => settings.Heading,
                    "altitude" => settings.Altitude, _ => settings.Altitude && settings.VerticalSpeed };
                if (!enabled) { pending.Remove(axis); initial.Remove(axis); }
            }
            if (!Settings.Altitude && settings.Altitude) initial["altitude"] = sim.Flight!.IndicatedAltitude;
            if ((!Settings.Altitude || !Settings.VerticalSpeed) && settings.Altitude && settings.VerticalSpeed)
                initial["vs"] = sim.Own!.VerticalSpeedFpm;
            if (!settings.Altitude || !settings.VerticalSpeed) verticalModeRequested = null;
        }
        Settings = settings;
        sim.Focus.SampleLimit = settings.SmoothingSamples;
        if (!(settings.Speed || settings.Heading || settings.Altitude)) Stop("All autopilot outputs are off");
        log.Write("formation_settings_applied", new { Settings, Active });
        return true;
    }
    public GuidanceSolution? Preview { get; private set; }
    public bool Active { get; private set; }
    public string Status { get; private set; } = "Preview only — choose target and outputs, then ENGAGE";
    public bool IsPmdg => sim.Pmdg737Identified;
    public string Adapter => IsPmdg ? "PMDG 737 SDK" : "Standard SimConnect";
    public AutopilotReadback? Readback => IsPmdg ? sim.PmdgState is { } p
        ? new(p.Speed >= 10 ? p.Speed : 0, p.Speed < 10 ? p.Speed : 0, p.Heading, p.Altitude, p.VerticalSpeed,
            p.Speed < 10, p.VsMode, p.Powered, p.AtArmed, p.ReceivedAt) { AtArmSource = "PMDG 737 SDK" } : null : sim.StandardState;
    public string? BlockReason(DateTimeOffset now)
    {
        var data = DataBlockReason(now);
        if (data != null) return data;
        if (sim.CameraActive) return "Stop the camera experiment before engaging";
        if (string.IsNullOrWhiteSpace(sim.OwnTitle)) return "Waiting for aircraft identification";
        if (IsPmdg && sim.PmdgState == null) return "PMDG SDK data unavailable — reload the aircraft after enabling data broadcast";
        var read = Readback;
        if (read == null || now - read.ReceivedAt > TimeSpan.FromSeconds(3) || !read.Available) return "Autopilot readback unavailable";
        if (IsPmdg && Settings.Speed && sim.PmdgState!.SpeedBlank) return "PMDG speed window is blank — select a speed mode first";
        if (Settings.VerticalSpeed && Settings.Altitude && IsPmdg && !sim.PmdgState!.Cmd) return "Automatic PMDG height matching requires CMD A or CMD B engaged";
        if (Settings.VerticalSpeed && Settings.Altitude && !IsPmdg && !read.MasterEngaged) return "Automatic height matching requires the aircraft autopilot engaged";
        if (Settings.VerticalSpeed && Settings.Altitude && !IsPmdg && !read.VsMode && !read.AltitudeHold && verticalModeRequested == null)
            return "Select V/S or altitude hold in the aircraft, or uncheck automatic V/S";
        if (Settings.Speed && ((read.MachMode && read.Mach is < 0.1 or > 1) || (!read.MachMode && read.Ias is < 30 or > 650))) return "Invalid speed selector readback";
        if (!new[] { read.Ias, read.Mach, read.Heading, read.Altitude, read.Vs }.All(double.IsFinite)) return "Invalid autopilot selector data";
        return null;
    }
    public string? DataBlockReason(DateTimeOffset now)
    {
        var focus = GuidanceTarget(now);
        if (!sim.Connected || sim.Own == null || sim.Flight == null) return "Waiting for own-aircraft telemetry";
        if (sim.Paused || sim.Flight.SimRate != 1) return "Requires unpaused flight at 1× simulation rate";
        if (!Settings.Valid || !(Settings.Speed || Settings.Heading || Settings.Altitude)) return "Choose outputs and valid limits";
        if (now - sim.Own.ReceivedAt > TimeSpan.FromSeconds(2) || now - sim.Flight.ReceivedAt > TimeSpan.FromSeconds(2)) return "Own telemetry delayed";
        // A stationary aircraft is normal on the ground. Explain the flight requirement
        // before applying airspeed checks that are only meaningful once airborne.
        if (!double.IsFinite(sim.Flight.OnGround)) return "Waiting for valid on-ground status from MSFS";
        if (sim.Flight.OnGround != 0) return "Take off and climb to at least 500 ft above ground before following";
        if (!double.IsFinite(sim.Flight.AboveGround)) return "Waiting for valid height above ground from MSFS";
        if (sim.Flight.AboveGround < 500) return $"Climb to at least 500 ft above ground before following (currently {sim.Flight.AboveGround:F0} ft)";
        if (sim.Flight.ValidationIssue is { } flightIssue) return flightIssue;
        if (!sim.Own.Position.IsValid) return "Waiting for valid own-aircraft position from MSFS";
        foreach (var (value, name) in new (double, string)[] {
            (sim.Own.GroundSpeedKnots, "ground speed"), (sim.Own.TrueTrack, "ground track"),
            (sim.Own.IndicatedSpeedKnots, "indicated airspeed"), (sim.Own.VerticalSpeedFpm, "vertical speed") })
            if (!double.IsFinite(value)) return $"Waiting for valid {name} from MSFS";
        if (sim.Own.IndicatedSpeedKnots < 20) return $"Following requires indicated airspeed of at least 20 kt (currently {sim.Own.IndicatedSpeedKnots:F0} kt)";
        if (focus.Latest == null || (now - focus.Latest.SourceTime).TotalSeconds is < -1 or > 2) return "Target data delayed or unavailable";
        if (focus.LastIssue != null) return "Filtering multiplayer position anomaly";
        if (Settings.CircleBelowFeet > 0 && !focus.TrustedGroundStatus)
            return "Altitude circling requires height data from a WingMan lead";
        if (Settings.CircleBelowFeet > 0 && !focus.Latest.IsOnGround && focus.Latest.AboveGroundFeet == null)
            return "Lead height unavailable: update the lead's WingMan or disable altitude circling";
        if (focus.GroundOrbitTarget != null || Settings.CircleBelowFeet > 0 && focus.Latest.AboveGroundFeet <= Settings.CircleBelowFeet + 100)
        {
            if (Settings.CircleBelowFeet == 0 && Settings.AboveNm <= 0) return "Lead is on the ground: set Above NM to a positive value to circle";
        }
        else if (focus.GroundSpeedKnots is not > 40 || focus.GroundTrackDegrees == null) return "Acquiring target flight motion";
        if (sim.Own.Position.DistanceNm(new(focus.Latest.Latitude, focus.Latest.Longitude, 0)) > 100) return "Target beyond 100 NM";
        return null;
    }
    public void Engage(DateTimeOffset now)
    {
        var reason = BlockReason(now);
        if (reason != null) { Status = reason; log.Write("follower_engage_blocked", new { reason }); return; }
        guidance.Reset(); pending.Clear(); verticalModeRequested = null; lastTick = now.AddSeconds(-1);
        rememberedTarget = null; UsingRememberedData = false;
        engagedAircraft = sim.OwnTitle; engagedMach = Readback!.MachMode;
        engagedTarget = sim.Focus.TargetId; engagedTargetName = sim.Focus.Name; WaitingForTelemetry = false;
        initial.Clear();
        initial["altitude"] = sim.Flight!.IndicatedAltitude;
        initial["vs"] = sim.Own!.VerticalSpeedFpm;
        Active = true; Status = "ENGAGED — updating selected outputs";
        log.Write("follower_engaged", new { target = sim.Focus.Name, sim.Focus.TargetId, Adapter, Settings });
    }
    public void Stop(string reason)
    {
        if (Active) log.Write("follower_stopped", new { reason, policy = "No further commands; last selected values and pilot modes retained" });
        Active = false; WaitingForTelemetry = false; pending.Clear(); initial.Clear(); verticalModeRequested = null; Status = reason;
        rememberedTarget = null; UsingRememberedData = false;
    }
    public void TelemetryLost(string reason)
    {
        if (Active && rememberedTarget != null && (reason.StartsWith("Lead ") || reason.StartsWith("WingMan ")))
        { UsingRememberedData = true; Status = "Continuing with last lead data: " + reason; return; }
        var resume = ResumeWhenTelemetryReturns && (Active || WaitingForTelemetry);
        Stop(reason);
        if (!resume) return;
        WaitingForTelemetry = true; Preview = null;
        Status = "Waiting to resume: " + reason;
        log.Write("follower_waiting_for_telemetry", new { reason, target = engagedTarget, name = engagedTargetName });
    }
    private bool TelemetryUnavailable(DateTimeOffset now) => !sim.Connected || sim.Own == null || sim.Flight == null
        || now - sim.Own.ReceivedAt > TimeSpan.FromSeconds(2) || now - sim.Flight.ReceivedAt > TimeSpan.FromSeconds(2)
        || sim.Focus.Latest == null || (now - sim.Focus.Latest.SourceTime).TotalSeconds is < -1 or > 2
        || sim.Focus.LastIssue != null || (sim.Focus.GroundOrbitTarget == null && !(Settings.CircleBelowFeet > 0 && sim.Focus.Latest.AboveGroundFeet <= Settings.CircleBelowFeet)
            && (sim.Focus.GroundSpeedKnots == null || sim.Focus.GroundTrackDegrees == null));
    public void Tick(DateTimeOffset now)
    {
        if (now - lastTick < TimeSpan.FromSeconds(sim.Focus.FastTelemetry ? 0.2 : 1)) return;
        var dt = Math.Clamp((now - lastTick).TotalSeconds, 0.1, 2); lastTick = now;
        sim.Focus.SampleLimit = Settings.SmoothingSamples;
        if (WaitingForTelemetry)
        {
            // Identity/mode changes and explicit stops must never become an automatic restart.
            if ((sim.OwnTitle.Length > 0 && sim.OwnTitle != engagedAircraft)
                || sim.Focus.TargetId != engagedTarget || sim.Focus.Name != engagedTargetName
                || (Readback is { } readback && now - readback.ReceivedAt < TimeSpan.FromSeconds(3) && readback.MachMode != engagedMach))
            { Stop("Aircraft, target or speed mode changed — enable Follow user again"); return; }
            var blocked = BlockReason(now);
            if (blocked != null) { Preview = null; Status = "Waiting to resume: " + blocked; return; }
            Engage(now);
            log.Write("follower_resumed_after_telemetry", new { target = engagedTarget, name = engagedTargetName });
        }
        // Brief server anomalies freeze outputs. No extrapolated commands, no abrupt slowdown from frozen coordinates.
        var guidanceTarget = GuidanceTarget(now);
        var targetAge = sim.Focus.Latest == null ? double.PositiveInfinity : (now - sim.Focus.Latest.SourceTime).TotalSeconds;
        if (!UsingRememberedData && Active && sim.Connected && !sim.Paused && sim.Own != null && sim.Flight is { SimRate: 1 }
            && now - sim.Own.ReceivedAt < TimeSpan.FromSeconds(2) && now - sim.Flight.ReceivedAt < TimeSpan.FromSeconds(2)
            && targetAge <= 5 && (targetAge > 2 || sim.Focus.LastIssue != null))
        {
            Preview = null; Status = "DATA HOLD — MCP values frozen while target motion recovers";
            // Preserve outstanding readback checks. A traffic anomaly must not hide an unsupported selector write.
            return;
        }
        var reason = DataBlockReason(now);
        if (reason != null) { Preview = null; if (Active) { if (TelemetryUnavailable(now)) TelemetryLost(reason); else Stop(reason); } return; }
        try { Preview = guidance.Calculate(sim.Own!, sim.Flight!, guidanceTarget, Settings, now); }
        catch (InvalidOperationException ex) { Preview = null; if (Active) Stop(ex.Message); else Status = ex.Message; return; }
        if (Active && !UsingRememberedData && sim.Focus.TrustedGroundStatus)
        { rememberedTarget = sim.Focus.Snapshot(); rememberedCircle = Preview.Mode == "CIRCLE"; }
        if (UsingRememberedData)
        {
            var memory = $"LAST KNOWN DATA · {RememberedAgeSeconds(now):F0}s old · " + (rememberedCircle ? "holding circle centre" : "projecting last course/speed; altitude held");
            Preview = Preview with { Limit = string.Join(" · ", new[] { memory, Preview.Limit }.Where(x => x.Length > 0)) };
        }
        if (IsPmdg && Preview.AltitudeFeet > 41000)
        {
            var error = 41000 - sim.Flight!.IndicatedAltitude;
            Preview = Preview with { AltitudeFeet = 41000, VerticalErrorFeet = error,
                VerticalSpeedFpm = Math.Clamp(error * 1.5, -Settings.MaxVs, Settings.MaxVs),
                Limit = "PMDG 737 altitude capped at 41,000 ft; requested height is unreachable" };
        }
        if (Settings.VerticalSpeed && sim.Own is { } air && sim.Flight is { } flight)
        {
            var climbFloor = Preview.Mode == "CIRCLE" && Settings.CircleIas > 0 ? Math.Min(Settings.MinIas + 5, Preview.Ias - 5) : Settings.MinIas + 5;
            if ((air.IndicatedSpeedKnots < climbFloor && Preview.VerticalSpeedFpm > 0)
                || ((air.IndicatedSpeedKnots > Settings.MaxIas - 5 || flight.Mach > Settings.MaxMach) && Preview.VerticalSpeedFpm < 0))
            {
                var protection = Preview.VerticalSpeedFpm > 0
                    ? $"Climb paused: IAS {air.IndicatedSpeedKnots:F0} kt is below {climbFloor:F0} kt; waiting for speed"
                    : "Descent paused to protect the configured maximum speed";
                Preview = Preview with { VerticalSpeedFpm = 0, Limit = string.Join(" · ", new[] { Preview.Limit, protection }.Where(x => x.Length > 0)) };
            }
        }
        if (!Active) return;
        reason = BlockReason(now);
        if (reason != null)
        {
            if (Readback == null || now - Readback.ReceivedAt > TimeSpan.FromSeconds(3)) TelemetryLost(reason);
            else Stop(reason);
            return;
        }
        if (sim.OwnTitle != engagedAircraft || Readback!.MachMode != engagedMach) { Stop("Aircraft or speed mode changed — review and re-engage"); return; }
        var read = Readback!; var solution = Preview;
        if (Settings.Altitude && Settings.VerticalSpeed)
        {
            if (read.VsMode) verticalModeRequested = null;
            else if (verticalModeRequested.HasValue && now - verticalModeRequested > TimeSpan.FromSeconds(5))
            { Stop($"{Adapter} did not confirm V/S mode"); return; }
            // ALT HOLD/capture may legitimately replace V/S near the slot. Re-enter V/S only when a height correction is needed.
            if (IsPmdg && !read.VsMode && verticalModeRequested == null && Math.Abs(solution.VerticalErrorFeet) > 150)
            {
                sim.RequestPmdgVs(); verticalModeRequested = now;
                log.Write("vertical_mode_requested", new { mode = "PMDG V/S", solution.VerticalErrorFeet });
            }
        }
        var current = new Dictionary<string, double> { ["speed"] = read.MachMode ? read.Mach : read.Ias,
            ["heading"] = read.Heading, ["altitude"] = read.Altitude, ["vs"] = read.Vs };
        foreach (var (axis, expected) in pending.ToArray())
        {
            if (read.ReceivedAt <= expected.Sent) continue;
            var error = axis == "heading" ? Math.Abs(FormationGeometry.Angle(current[axis] - expected.Value)) : Math.Abs(current[axis] - expected.Value);
            var tolerance = axis == "speed" && read.MachMode ? 0.004 : axis is "altitude" or "vs" ? 1 : 0.25;
            if (error <= tolerance) pending.Remove(axis);
            else if (now - expected.Sent > TimeSpan.FromSeconds(5)) { Stop($"{axis} command not confirmed — aircraft may require a custom interface or pilot changed the selector"); return; }
        }
        if (Settings.Speed && !pending.ContainsKey("speed"))
        {
            // These are selected targets, not throttle or bank commands. The aircraft AP
            // governs its physical response; slewing the selectors adds avoidable pursuit lag.
            var value = read.MachMode ? Math.Round(solution.Mach, 2) : Math.Round(solution.Ias);
            Command("speed", value, now, () => sim.SendSelection("speed", value, read.MachMode, IsPmdg));
        }
        if (Settings.Heading && !pending.ContainsKey("heading"))
        {
            var value = FormationGeometry.Normalize(Math.Round(solution.MagneticHeading));
            Command("heading", value, now, () => sim.SendSelection("heading", value, false, IsPmdg));
        }
        if (Settings.Altitude && !pending.ContainsKey("altitude"))
        {
            var value = RampSelection("altitude", read.Altitude, solution.AltitudeFeet, 200 * dt);
            Command("altitude", value, now, () => sim.SendSelection("altitude", value, false, IsPmdg));
        }
        if (Settings.Altitude && Settings.VerticalSpeed && read.VsMode && !pending.ContainsKey("vs"))
        {
            var value = RampSelection("vs", read.Vs, solution.VerticalSpeedFpm, 200 * dt);
            Command("vs", value, now, () => sim.SendSelection("vs", value, false, IsPmdg));
        }
        if (!IsPmdg && Settings.Altitude && Settings.VerticalSpeed && !read.VsMode && verticalModeRequested == null
            && Math.Abs(solution.VerticalErrorFeet) > 150 && Math.Abs(solution.VerticalSpeedFpm) >= 100
            && (read.Altitude - sim.Flight!.IndicatedAltitude) * Math.Sign(solution.VerticalSpeedFpm) >= 100)
        {
            // Let normal ALT capture/hold settle. Once a new height is needed,
            // first move the altitude selector away from the aircraft, then enter V/S.
            // Re-seed the ramp from actual flight motion instead of the old climb rate.
            initial["vs"] = sim.Own!.VerticalSpeedFpm; pending.Remove("vs");
            sim.RequestStandardVs(); verticalModeRequested = now;
            log.Write("vertical_mode_requested", new { mode = "Standard V/S", solution.VerticalErrorFeet });
        }
        Status = $"ENGAGED · {solution.Mode} · {Adapter}" + (verticalModeRequested != null ? " · waiting for V/S" : "")
            + (solution.Limit.Length > 0 ? " · " + solution.Limit : "");
        log.Write("guidance_output", new { solution, pending = pending.Keys.ToArray() });
    }
    private void Command(string axis, double value, DateTimeOffset now, Action send)
    { send(); if (axis is not ("altitude" or "vs")) initial.Remove(axis); pending[axis] = (value, now); }
    private double RampSelection(string axis, double current, double desired, double step)
    {
        // Preserve sub-100-unit movement between 5 Hz updates. Restarting at the
        // rounded readback each time can round every 40-unit step back to zero.
        var continuous = Slew(initial.GetValueOrDefault(axis, current), desired, step);
        initial[axis] = continuous;
        return Math.Round(continuous / 100) * 100;
    }
    internal static double Slew(double current, double desired, double step) => current + Math.Clamp(desired - current, -step, step);
}
