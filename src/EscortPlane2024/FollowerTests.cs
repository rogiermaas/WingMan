namespace EscortPlane2024;

internal static class FollowerTests
{
    internal sealed class Session : IFollowerSession
    {
        public bool Connected { get; set; } = true;
        public bool Paused { get; set; }
        public bool CameraActive => false;
        public OwnTelemetry? Own { get; set; }
        public FlightTelemetry? Flight { get; set; }
        public TargetEstimator Focus { get; } = new("Test aircraft");
        public string OwnTitle { get; set; } = "Stock test aircraft";
        public bool Pmdg737Identified => OwnTitle.Contains("PMDG");
        public PmdgState? PmdgState { get; set; }
        public AutopilotReadback? StandardState { get; set; }
        public List<(string Axis, double Value)> Outputs { get; } = [];
        public int ModeRequests { get; private set; }
        public bool Accept { get; set; } = true;
        public void SendSelection(string axis, double value, bool mach, bool pmdg)
        {
            Outputs.Add((axis, value));
            if (!Accept) return;
            if (pmdg && PmdgState is { } p) PmdgState = axis switch
            {
                "speed" => p with { Speed = value }, "heading" => p with { Heading = (int)value },
                "altitude" => p with { Altitude = (int)value }, "vs" => p with { VerticalSpeed = (int)value }, _ => p
            };
            else if (StandardState is { } s) StandardState = axis switch
            {
                "speed" => mach ? s with { Mach = value } : s with { Ias = value }, "heading" => s with { Heading = value },
                "altitude" => s with { Altitude = value }, "vs" => s with { Vs = value }, _ => s
            };
        }
        public void RequestPmdgVs() { ModeRequests++; if (Accept && PmdgState is { } p) PmdgState = p with { VsMode = true, AltHold = false }; }
        public void RequestStandardVs() { ModeRequests++; if (Accept && StandardState is { } s) StandardState = s with { VsMode = true, AltitudeHold = false }; }
        public void Refresh(DateTimeOffset now, int second, bool target = true)
        {
            Own = Own! with { ReceivedAt = now }; Flight = Flight! with { ReceivedAt = now };
            StandardState = StandardState! with { ReceivedAt = now };
            if (PmdgState != null) PmdgState = PmdgState with { ReceivedAt = now };
            if (target) Focus.Observe(Sample(now, second));
        }
        public static CoherentAircraft Sample(DateTimeOffset now, int second) =>
            new(123, "Test aircraft", "Test", 0, second * 250.0 / 3600 / 60.04046, 10000 * 0.3048, 90, false, now, now);
        public Session(DateTimeOffset now)
        {
            Own = new(new(0, -2.0 / 60, 10000), 250, 90, 250, 0, 2, now);
            var mach = 250 * (1852.0 / 3600) / Math.Sqrt(1.4 * 287.05287 * 288.15);
            Flight = new(250, 90, 80, 10000, 101325, 288.15, mach, 0, 10000, 0, 0, 1, 100, now);
            StandardState = new(250, mach, 80, 10000, 0, false, true, true, true, now) { MasterEngaged = true };
            for (int second = -5; second <= 0; second++) Focus.Observe(Sample(now.AddSeconds(second), second));
        }
    }
    public static void Run(Diagnostics log, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var design = new Session(now).Flight! with { StallSpeed = 175, DesignCruiseSpeed = 400, TrueAirspeed = 55 };
        var limits = AircraftSpeedLimits.Suggest(design, false);
        check(limits == (228, 320) && limits == AircraftSpeedLimits.Suggest(design with { TrueAirspeed = 450 }, false),
            "787 design limits remain usable and independent of takeoff speed");
        check(AircraftSpeedLimits.IsLegacyCollapsedRange(new(MinIas: 227.5, MaxIas: 228.5), design)
            && !AircraftSpeedLimits.IsLegacyCollapsedRange(new(MinIas: 210, MaxIas: 320), design)
            && !AircraftSpeedLimits.IsLegacyCollapsedRange(new(MinIas: 250, MaxIas: 251), design),
            "Legacy repair matches the automatic one-knot stall-derived range, not unrelated custom limits");
        check(AircraftSpeedLimits.Suggest(design with { StallSpeed = 45, DesignCruiseSpeed = 120 }, false) == (59, 120),
            "Light aircraft use their design cruise speed instead of jet defaults");
        check(AircraftSpeedLimits.Suggest(design with { DesignCruiseSpeed = double.NaN }, false).Max == 320,
            "Unavailable design cruise telemetry has a usable stall-derived fallback");
        check(AircraftIdentification.IsPmdg737("737-800 PAX BW HD", true, false), "PMDG preset without vendor in TITLE is identified by active instrument evidence");
        check(!AircraftIdentification.IsPmdg737("Boeing 737 MAX", false, false), "A stock Boeing title alone cannot select the PMDG adapter");
        var origin = new Position(0, 0, 10000);
        var north = FormationGeometry.Slot(origin, 0, new(1, 0.2, 0.1));
        var d = FormationGeometry.Displacement(origin, north);
        check(Math.Abs(d.North + 1) < 0.001 && Math.Abs(d.East - 0.2) < 0.001, "Northbound formation: behind is south, right is east");
        var east = FormationGeometry.Slot(origin, 90, new(1, 0.2, -0.1));
        d = FormationGeometry.Displacement(origin, east);
        check(Math.Abs(d.North + 0.2) < 0.001 && Math.Abs(d.East + 1) < 0.001, "Eastbound formation rotates behind and right offsets");
        check(Math.Abs(north.AltitudeFeet - 10607.61155) < 0.01 && Math.Abs(east.AltitudeFeet - 9392.38845) < 0.01, "Signed vertical 0.1 NM converts to 607.612 feet");
        var date = FormationGeometry.Offset(new(0, 179.99, 0), 0, 2);
        check(date.Longitude < -179.9 && origin.IsValid, "Formation offset wraps the dateline");
        check(Math.Abs(GuidanceController.MachFromCas(GuidanceController.Cas(0.78, 30000), 30000) - 0.78) < 1e-9, "Compressible IAS/Mach conversion round trips at altitude");
        check(Math.Abs(GuidanceController.Cas(0.5, 30000) - GuidanceController.Cas(0.5, 101325)) > 100, "Groundspeed is not sent directly as indicated speed");
        var session = new Session(now); var guide = new GuidanceController();
        var s = new FormationSettings(MinIas: 150, MaxIas: 350);
        var solution = guide.Calculate(session.Own!, session.Flight!, session.Focus, s, now);
        check(solution.Mode == "CAPTURE" && solution.DesiredGroundSpeed > 250 && Math.Abs(solution.MagneticHeading - 80) < 0.1, "Behind target: accelerate on target track with magnetic correction");
        session.Own = session.Own! with { Position = new(0, 2.0 / 60, 10000) };
        solution = guide.Calculate(session.Own, session.Flight!, session.Focus, s, now);
        check(solution.Mode == "AHEAD HOLD" && solution.DesiredGroundSpeed < 250 && Math.Abs(FormationGeometry.Angle(solution.MagneticHeading - 80)) < 0.1, "Ahead target: slow down without a reversal");
        session.Own = session.Own with { Position = new(0, -0.9 / 60, 10000), GroundSpeedKnots = 210 };
        solution = guide.Calculate(session.Own, session.Flight!, session.Focus, s, now);
        check(solution.DesiredGroundSpeed > 248, "Overtake recovery starts accelerating before the formation slot reaches us");
        session = new Session(now);
        solution = guide.Calculate(session.Own!, session.Flight! with { IndicatedAltitude = 10200 }, session.Focus, s with { AboveNm = 0.1 }, now);
        check(Math.Abs(solution.AltitudeFeet - 10807.61155) < 0.1, "Altitude selection includes signed vertical offset and own barometric correction");
        solution = guide.Calculate(session.Own!, session.Flight!, session.Focus, s with { MaxIas = 240 }, now);
        check(solution.Ias <= 240 && solution.Limit.Length > 0, "Unreachable speed demand is bounded and reported");
        check(FollowerController.Slew(359, 350, 2) == 357 && FollowerController.Slew(200, 300, 1) == 201, "Output changes are rate bounded");
        check(SimConnectManager.EncodeSelection("vs", -1800, false, true) == 8200 && unchecked((int)SimConnectManager.EncodeSelection("vs", -1800, false, false)) == -1800,
            "PMDG and standard descent commands use their distinct signed encodings");
        check(SimConnectManager.EncodeSelection("speed", 0.78, true, true) == 78, "Mach selector encoding uses hundredths");
        var bytes = new byte[Pmdg737.DataSize];
        BitConverter.GetBytes(250f).CopyTo(bytes, 420); BitConverter.GetBytes((ushort)80).CopyTo(bytes, 428);
        BitConverter.GetBytes((ushort)10000).CopyTo(bytes, 430); BitConverter.GetBytes((short)-1800).CopyTo(bytes, 432);
        BitConverter.GetBytes((ushort)5).CopyTo(bytes, 654); bytes[437] = bytes[453] = bytes[457] = 1;
        var pmdg = Pmdg737.Decode(bytes, now);
        check(pmdg is { Speed: 250, Heading: 80, Altitude: 10000, VerticalSpeed: -1800, Model: 5, Cmd: true, AtArmed: true, Powered: true }, "PMDG packet decodes native field offsets and flags");
        check(Pmdg737.Decode(new byte[100], now) == null && Pmdg737.Decode(new byte[916], now) == null, "Incomplete/uninitialized PMDG data cannot enable outputs");
        session = new Session(now);
        var controller = new FollowerController(session, log) { Settings = s with { Speed = false, Altitude = false } };
        controller.Engage(now); controller.Tick(now);
        check(controller.Active && session.Outputs.Count == 1 && session.Outputs[0].Axis == "heading", "Independent output selection sends heading only");
        var sent = session.Outputs.Count; controller.Stop("Pilot stop"); session.Refresh(now.AddSeconds(1), 1); controller.Tick(now.AddSeconds(1));
        check(!controller.Active && session.Outputs.Count == sent, "Stop immediately prevents all further selector commands");
        controller.Engage(now.AddSeconds(1)); session.Refresh(now.AddSeconds(4), 4, false); controller.Tick(now.AddSeconds(4));
        check(controller.Active && controller.Status.StartsWith("DATA HOLD") && session.Outputs.Count == sent, "Brief target gap holds MCP values without extrapolated commands");
        session.Refresh(now.AddSeconds(7), 7, false); controller.Tick(now.AddSeconds(7));
        check(!controller.Active && controller.Status.Contains("Target data"), "Target gap beyond five seconds stops following");
        session.Refresh(now.AddSeconds(8), 8); controller.Tick(now.AddSeconds(8));
        check(!controller.Active, "Target recovery never automatically re-engages stopped outputs");
        session = new Session(now) { Accept = false };
        controller = new(session, log) { Settings = s with { Heading = false, Altitude = false } };
        controller.Engage(now);
        for (int i = 0; i < 14; i++) { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        check(!controller.Active && controller.Status.Contains("not confirmed"), "Unsupported selector writes stop on readback timeout");
        session = new Session(now) { OwnTitle = "PMDG 737-800", PmdgState = pmdg! with { VerticalSpeed = 0 } };
        session.Own = session.Own! with { Position = session.Own.Position with { AltitudeFeet = 9000 } };
        controller = new(session, log) { Settings = s with { Speed = false, Heading = false, VerticalSpeed = true } };
        controller.Engage(now); controller.Tick(now);
        check(session.ModeRequests == 1 && controller.Active, "PMDG auto height matching requests V/S when altitude correction is needed");
        session.Refresh(now.AddSeconds(1), 1); controller.Tick(now.AddSeconds(1));
        check(session.ModeRequests == 1 && session.Outputs.Any(o => o.Axis == "vs" && o.Value > 0), "V/S updates begin only after PMDG confirms the mode");
        session.PmdgState = session.PmdgState! with { Cmd = false }; session.Refresh(now.AddSeconds(2), 2); controller.Tick(now.AddSeconds(2));
        check(!controller.Active, "Pilot disconnecting PMDG CMD stops automatic height matching");
        session = new Session(now) { OwnTitle = "PMDG 737-800", PmdgState = pmdg, Accept = false };
        session.Own = session.Own! with { Position = session.Own.Position with { AltitudeFeet = 9000 } };
        controller = new(session, log) { Settings = s with { Speed = false, Heading = false, VerticalSpeed = true } };
        controller.Engage(now);
        for (int i = 0; i < 8; i++) { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        check(!controller.Active && session.ModeRequests == 1 && controller.Status.Contains("V/S mode"), "Unacknowledged PMDG mode change is not repeatedly toggled");
        session = new Session(now) { Paused = true }; controller = new(session, log) { Settings = s }; controller.Engage(now);
        check(!controller.Active && session.Outputs.Count == 0, "Paused simulator cannot engage output control");
        session.Paused = false; session.Flight = session.Flight! with { SimRate = 2 }; controller.Engage(now);
        check(!controller.Active, "Time acceleration blocks wall-clock target velocity control");
        session.Flight = session.Flight with { SimRate = 1, OnGround = 1 }; controller.Engage(now);
        check(!controller.Active, "Ground aircraft cannot engage airborne following");
        session = new Session(now);
        session.Flight = session.Flight! with { OnGround = 1, AboveGround = 14.6, TrueAirspeed = 0, Mach = 0 };
        session.Own = session.Own! with { IndicatedSpeedKnots = 0, GroundSpeedKnots = 0 };
        controller = new(session, log) { Settings = s };
        controller.Engage(now); controller.Tick(now);
        check(!controller.Active && session.Outputs.Count == 0 && controller.Status.StartsWith("Take off and climb"),
            "Stationary ground aircraft gets an actionable takeoff message without any commands");
        session.Flight = session.Flight with { OnGround = 0, AboveGround = 499 };
        controller.Engage(now);
        check(!controller.Active && controller.Status.StartsWith("Climb to at least 500 ft"),
            "Low-altitude following explains height requirement before airspeed validation");
        session = new Session(now);
        session.Flight = session.Flight! with { AmbientPressure = double.NaN };
        controller = new(session, log) { Settings = s }; controller.Engage(now);
        check(!session.Flight.Valid && !controller.Active && session.Outputs.Count == 0 && controller.Status.Contains("air pressure"),
            "Invalid airborne telemetry identifies the missing value and blocks commands");
        session = new Session(now);
        session.Flight = session.Flight! with { TrueAirspeed = 50 };
        controller = new(session, log) { Settings = s }; controller.Engage(now);
        check(!session.Flight.Valid && !controller.Active && controller.Status.Contains("above 50 kt"),
            "True airspeed threshold remains enforced with an actionable explanation");
        session = new Session(now);
        session.Own = session.Own! with { IndicatedSpeedKnots = double.NaN };
        controller = new(session, log) { Settings = s }; controller.Engage(now);
        check(!controller.Active && controller.Status.Contains("indicated airspeed"),
            "Invalid indicated airspeed identifies the offending own-aircraft field");
        session = new Session(now);
        session.Flight = session.Flight! with { AboveGround = 500 };
        controller = new(session, log) { Settings = s }; controller.Engage(now);
        check(controller.Active, "Valid airborne telemetry at 500 ft still permits following");
        var motion = new Session(now).Focus;
        var rejection = motion.Observe(Session.Sample(now.AddSeconds(1), 1) with { RawAltitude = 9000 });
        check(rejection == "implausible vertical jump", "Vertical network teleport cannot become an altitude command");
        motion = new Session(now).Focus;
        var frozen = Session.Sample(now.AddSeconds(1), 0);
        var beforeSpeed = motion.GroundSpeedKnots;
        check(motion.Observe(frozen) == "multiplayer position frozen" && motion.GroundSpeedKnots == beforeSpeed,
            "Frozen network coordinates cannot collapse the estimated speed");
        motion.Observe(Session.Sample(now.AddSeconds(2), 2));
        check(motion.LastIssue == null && motion.GroundSpeedKnots is > 249 and < 251, "Normal motion recovers from a single frozen update");
        var jump = Session.Sample(now.AddSeconds(3), 3) with { Longitude = 0.2 };
        motion.Observe(jump);
        check(motion.LastIssue != null && motion.GroundTrackDegrees is > 89 and < 91, "Forward server skip cannot reverse or spike the estimated track");
        for (int i = 4; i <= 7; i++) motion.Observe(Session.Sample(now.AddSeconds(i), i) with { Longitude = 0.2 + (i-3) * 250.0 / 3600 / 60.04046 });
        check(motion.LastIssue == null && motion.GroundSpeedKnots is > 249 and < 251,
            "Persistent server correction reacquires a consistent trajectory without differentiating across the jump");
        motion = new Session(now).Focus; motion.SampleLimit = 15;
        for (int i = 1; i <= 20; i++)
        {
            var sample = Session.Sample(now.AddSeconds(i), i);
            motion.Observe(sample with { Latitude = (i % 2 == 0 ? 1 : -1) * 0.00005 });
        }
        check(motion.GroundSpeedKnots is > 249 and < 251 && motion.GroundTrackDegrees is > 89 and < 91 && motion.SmoothedPosition != null,
            "Adjustable position regression filters multiplayer coordinate jitter");
        session = new Session(now); controller = new(session, log) { Settings = s with { Heading = false, Altitude = false } };
        controller.Engage(now); controller.Tick(now); sent = session.Outputs.Count;
        session.Refresh(now.AddSeconds(1), 1, false); session.Focus.Observe(Session.Sample(now.AddSeconds(1), 0)); controller.Tick(now.AddSeconds(1));
        check(controller.Active && session.Outputs.Count == sent, "An isolated freeze sends no new MCP commands");
        session.Refresh(now.AddSeconds(2), 2); controller.Tick(now.AddSeconds(2));
        check(controller.Active && session.Outputs.Count > sent, "Consistent motion resumes a brief hold without re-engaging the aircraft AP");
        session = new Session(now);
        session.StandardState = session.StandardState! with { Heading = 260, Altitude = 1000, Ias = 100 };
        controller = new(session, log) { Settings = s }; controller.Engage(now); controller.Tick(now);
        check(session.Outputs.Single(x => x.Axis == "heading").Value is > 78 and < 82
            && session.Outputs.Single(x => x.Axis == "altitude").Value is >= 9800 and <= 10200
            && Math.Abs(session.Outputs.Single(x => x.Axis == "speed").Value - controller.Preview!.Ias) <= 0.5,
            "First output uses guidance for speed/heading and current flight altitude instead of stale selectors");
        session = new Session(now);
        session.Own = session.Own! with { Position = new(0.03, -1.2 / 60, 10000), GroundSpeedKnots = 330, IndicatedSpeedKnots = 330 };
        session.Flight = session.Flight! with { TrueAirspeed = 330, Mach = session.Flight.Mach * 330 / 250 };
        controller = new(session, log) { Settings = s with { Altitude = false } };
        controller.Engage(now); controller.Tick(now);
        check(controller.Active && session.Outputs.Single(x => x.Axis == "speed").Value < 250,
            "Fast closure commands braking before reaching the slot, without a one-knot selector delay");
        check(Math.Abs(session.Outputs.Single(x => x.Axis == "heading").Value - 80) > 10
            && Math.Abs(FormationGeometry.Angle(session.Outputs.Single(x => x.Axis == "heading").Value - controller.Preview!.MagneticHeading)) <= 0.5,
            "Heading selector reaches filtered intercept immediately instead of moving two degrees per second");
        session.Accept = false; session.StandardState = session.StandardState! with { Heading = 80, Ias = 330 };
        sent = session.Outputs.Count;
        session.Refresh(now.AddSeconds(1), 1); controller.Tick(now.AddSeconds(1));
        check(session.Outputs.Count == sent, "Faster selector response still waits for command confirmation");
        solution = new GuidanceController().Calculate(session.Own!, session.Flight!, session.Focus, s with { MinIas = 280 }, now.AddSeconds(1));
        check(solution.Ias >= 280 && solution.Limit.Contains("may overtake"), "Minimum speed is preserved and explains why spacing cannot be maintained");
        motion = new("Turn test") { SampleLimit = 30 };
        var turnPosition = new Position(10, 10, 10000);
        for (int i = -30; i <= 30; i++)
        {
            var course = i <= 0 ? 350 : FormationGeometry.Normalize(350 + 2 * i);
            turnPosition = FormationGeometry.Offset(turnPosition, 250.0 / 3600 * Math.Cos(course * Math.PI / 180), 250.0 / 3600 * Math.Sin(course * Math.PI / 180));
            var at = now.AddSeconds(i);
            motion.Observe(new(456, "Turn test", "", turnPosition.Latitude, turnPosition.Longitude, 3048, 0, false, at, at));
        }
        check(motion.RejectedSamples == 0 && motion.GroundTrackDegrees is { } turnTrack && Math.Abs(FormationGeometry.Angle(turnTrack - 50)) < 5,
            "A sustained waypoint turn across north stays within five degrees with a 30-sample filter, without anomaly holds");
        check(motion.GroundSpeedKnots is > 248 and < 252, "Turn adaptation preserves speed instead of mistaking the curve for deceleration");
        session = new Session(now); controller = new(session, log) { Settings = s };
        controller.Engage(now); controller.Tick(now);
        var oldSlot = controller.Preview!.Slot;
        var changed = s with { BehindNm = 2, RightNm = 0.5, AboveNm = 0.1, SmoothingSamples = 20 };
        check(controller.ApplySettings(changed) && controller.Active, "Changing formation values preserves active following");
        session.Refresh(now.AddSeconds(1), 1); controller.Tick(now.AddSeconds(1));
        check(controller.Active && controller.Preview!.Slot.DistanceNm(oldSlot) > 0.8
            && Math.Abs(controller.Preview.Slot.AltitudeFeet - 10607.61155) < 0.01 && session.Focus.SampleLimit == 20,
            "Next control cycle uses new spacing, lateral/vertical offsets and smoothing without re-engagement");
        check(!controller.ApplySettings(changed with { MinIas = 400 }) && controller.Active && controller.Settings == changed,
            "An invalid edit preserves the last valid settings and active following");
        controller.ApplySettings(changed with { Speed = false });
        sent = session.Outputs.Count(x => x.Axis == "speed");
        session.Refresh(now.AddSeconds(2), 2); controller.Tick(now.AddSeconds(2));
        check(controller.Active && session.Outputs.Count(x => x.Axis == "speed") == sent,
            "Disabling speed output leaves other axes following without more speed commands");
        controller.ApplySettings(changed with { Speed = false, Heading = false, Altitude = false });
        check(!controller.Active, "Turning off every output ends control explicitly");
        session = new Session(now);
        var nativeId = new ContactId(true, 123);
        session.Focus.LockIdentity(nativeId.TrackingId, true);
        for (int second = -5; second <= 0; second++)
        {
            var observation = Session.Sample(now.AddSeconds(second), second);
            var contact = new TrafficContact(nativeId, "", "", new(observation.Latitude, observation.Longitude, observation.RawAltitude / 0.3048), observation.SourceTime, "Aircraft");
            session.Focus.Observe(contact.MotionSample(observation.SourceTime)!);
        }
        controller = new(session, log) { Settings = s }; controller.Engage(now); controller.Tick(now);
        check(controller.Active && session.Outputs.Any(x => x.Axis == "speed") && session.Outputs.Any(x => x.Axis == "heading") && session.Outputs.Any(x => x.Axis == "altitude"),
            "Unnamed native ID feeds the existing speed, heading and altitude controller through a fake transport");
        foreach (var direction in new[] { 1, -1 })
        {
            session = new Session(now); session.Focus.FastTelemetry = true;
            var startAltitude = 10000 - direction * 1000;
            session.Own = session.Own! with { Position = session.Own.Position with { AltitudeFeet = startAltitude } };
            session.Flight = session.Flight! with { IndicatedAltitude = startAltitude };
            session.StandardState = session.StandardState! with { Altitude = startAltitude };
            controller = new(session, log) { Settings = s with { VerticalSpeed = true } };
            controller.Engage(now); controller.Tick(now);
            for (var i = 1; i <= 9; i++)
            {
                var at = now.AddMilliseconds(i * 200);
                session.Refresh(at, 0, false); controller.Tick(at);
            }
            check(controller.Active && direction * session.StandardState.Vs >= 500
                && direction * (session.StandardState.Altitude - startAltitude) >= 500,
                $"5 Hz {(direction > 0 ? "climb" : "descent")} accumulates fractional steps for altitude and V/S selectors");
        }
    }
}
