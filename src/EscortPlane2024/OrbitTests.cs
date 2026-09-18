namespace EscortPlane2024;

internal static class OrbitTests
{
    public static void Run(Diagnostics log, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = new FormationSettings(BehindNm: 4, AboveNm: .1, MinIas: 150, MaxIas: 350);
        CoherentAircraft Ground(DateTimeOffset at) => FollowerTests.Session.Sample(at, 0) with { IsOnGround = true };
        FollowerTests.Session GroundSession()
        {
            var s = new FollowerTests.Session(now);
            s.Focus.ResetMotion(); s.Focus.FastTelemetry = true; s.Focus.TrustedGroundStatus = true;
            for (int i = -5; i <= 0; i++) s.Focus.Observe(Ground(now.AddSeconds(i)));
            s.Own = s.Own! with { Position = FormationGeometry.Offset(new(0, 0, 10000), 4, 0, FormationGeometry.FeetPerNm * .1) };
            s.Flight = s.Flight! with { IndicatedAltitude = s.Own.Position.AltitudeFeet };
            return s;
        }
        var session = GroundSession();
        var controller = new FollowerController(session, log) { Settings = settings };
        check(session.Focus.GroundSpeedKnots == 0 && session.Focus.GroundTrackDegrees == null && controller.BlockReason(now) == null,
            "A stationary relay lead needs fresh position, not a fabricated ground track");
        controller.Engage(now); controller.Tick(now);
        check(controller.Active && controller.Preview?.Mode == "CIRCLE" && session.Outputs.Any(x => x.Axis == "heading"),
            "Grounded lead engages circle guidance and sends autopilot selections");
        var circle = controller.Preview!;
        check(circle.OrbitRadiusNm == 4 && circle.MagneticHeading is > 80 and < 90 && Math.Abs(circle.VerticalErrorFeet) < .01,
            "At the north edge of the circle, guidance turns clockwise at the positive height offset");
        check(circle.Ias == 160 && circle.Mach <= settings.MaxMach && Math.Abs(circle.AltitudeFeet - 10607.61155) < .01,
            "Circle speed keeps ten knots above the minimum and selects lead elevation plus Above NM");
        var guide = new GuidanceController();
        var lateral = guide.Calculate(session.Own!, session.Flight!, session.Focus, settings with { RightNm = 5 }, now);
        check(lateral.Slot == circle.Slot && lateral.MagneticHeading == circle.MagneticHeading && lateral.Limit.Contains("Right offset"),
            "Ground circle ignores lateral spacing and explains that it resumes after takeoff");
        var outside = guide.Calculate(session.Own! with { Position = FormationGeometry.Offset(new(0, 0, 10607.61155), 8, 0) }, session.Flight!, session.Focus, settings, now);
        var inside = guide.Calculate(session.Own! with { Position = FormationGeometry.Offset(new(0, 0, 10607.61155), 2, 0) }, session.Flight!, session.Focus, settings, now);
        check(outside.MagneticHeading > 130 && inside.MagneticHeading < 50,
            "Circle capture points inward from outside and outward from inside the requested radius");
        var center = guide.Calculate(session.Own! with { Position = new(0, 0, 10607.61155) }, session.Flight!, session.Focus, settings, now);
        check(double.IsFinite(center.MagneticHeading) && double.IsFinite(center.Ias), "Flying over the circle center produces finite escape guidance");
        var tight = guide.Calculate(session.Own!, session.Flight!, session.Focus, settings with { BehindNm = .1 }, now);
        check(tight.OrbitRadiusNm > 2 && tight.Limit.Contains("widened") && tight.Ias >= settings.MinIas,
            "An impossible 0.1 NM jet circle widens for current speed without commanding stall speed");
        var baro = guide.Calculate(session.Own!, session.Flight! with { IndicatedAltitude = session.Flight.IndicatedAltitude + 200 }, session.Focus, settings, now);
        check(Math.Abs(baro.AltitudeFeet - circle.AltitudeFeet - 200) < .01,
            "Orbit altitude preserves the own-aircraft barometric correction");
        foreach (var above in new[] { 0.0, -.1 })
        {
            var blocked = new FollowerController(session, log) { Settings = settings with { AboveNm = above } };
            blocked.Engage(now);
            check(!blocked.Active && blocked.Status.Contains("positive"), "Zero or negative ground-orbit height blocks engagement");
        }
        controller.ApplySettings(settings with { AboveNm = 0 });
        session.Refresh(now.AddSeconds(1), 0, false); session.Focus.Observe(Ground(now.AddSeconds(1)));
        var sent = session.Outputs.Count; controller.Tick(now.AddSeconds(1));
        check(!controller.Active && session.Outputs.Count == sent && controller.Status.Contains("positive"),
            "Removing the positive height during a circle stops outputs before commanding the ground elevation");

        session = GroundSession(); controller = new(session, log) { Settings = settings };
        controller.Engage(now); controller.Tick(now);
        var changed = settings with { BehindNm = 6, AboveNm = .2 };
        controller.ApplySettings(changed); session.Refresh(now.AddSeconds(1), 0, false); session.Focus.Observe(Ground(now.AddSeconds(1))); controller.Tick(now.AddSeconds(1));
        check(controller.Active && controller.Preview?.OrbitRadiusNm == 6 && Math.Abs(controller.Preview.Slot.AltitudeFeet - 11215.2231) < .01,
            "Circle radius and height changes apply without stopping follow");
        session.Refresh(now.AddSeconds(2), 0, false);
        session.Focus.Observe(Ground(now.AddSeconds(2)) with { Longitude = 15.0 / 3600 / 60.04046 }); controller.Tick(now.AddSeconds(2));
        check(controller.Active && session.Focus.GroundOrbitTarget!.Longitude > 0
            && Math.Abs(controller.Preview!.Slot.DistanceNm(new(0, session.Focus.GroundOrbitTarget.Longitude, 0)) - 6) < .001 && session.Focus.LastIssue == null,
            "Taxiing updates move the circle center without triggering a frozen-flight rejection");

        session = GroundSession(); controller = new(session, log) { Settings = settings };
        controller.Engage(now); controller.Tick(now);
        for (int i = 1; i <= 5; i++)
        {
            var at = now.AddSeconds(i); session.Refresh(at, i, false);
            session.Focus.Observe(FollowerTests.Session.Sample(at, i)); controller.Tick(at);
            if (i == 1) check(controller.Active && controller.Preview?.Mode == "CIRCLE",
                "Takeoff keeps circling while the first airborne samples establish a trajectory");
        }
        check(controller.Active && controller.Preview?.Mode != "CIRCLE" && session.Focus.GroundOrbitTarget == null,
            "Takeoff automatically resumes airborne formation following once motion is established");
        var landed = FollowerTests.Session.Sample(now.AddSeconds(6), 5) with { IsOnGround = true };
        session.Refresh(now.AddSeconds(6), 0, false); session.Focus.Observe(landed); controller.Tick(now.AddSeconds(6));
        check(controller.Active && controller.Preview?.Mode == "CIRCLE" && session.Focus.LastIssue == null,
            "A valid landing transitions directly from follow to circle instead of rejecting a stopped lead");
        var teleport = landed with { SourceTime = now.AddSeconds(7), Longitude = 30 };
        check(session.Focus.Observe(teleport) == "implausible horizontal jump" && session.Focus.Latest == landed,
            "Ground status does not exempt landing telemetry from jump rejection");

        session = GroundSession(); controller = new(session, log) { Settings = settings, ResumeWhenTelemetryReturns = true };
        controller.Engage(now); controller.Tick(now); sent = session.Outputs.Count;
        session.Refresh(now.AddSeconds(3), 0, false); controller.Tick(now.AddSeconds(3));
        check(controller.Active && controller.Status.StartsWith("DATA HOLD") && session.Outputs.Count == sent,
            "Stale ground telemetry freezes circle outputs just like airborne following");
        session.Refresh(now.AddSeconds(6), 0, false); controller.Tick(now.AddSeconds(6));
        check(!controller.Active && controller.WaitingForTelemetry, "Extended ground telemetry loss waits only when automatic resume is enabled");
        session.Focus.ResetMotion(); session.Refresh(now.AddSeconds(7), 0, false); session.Focus.Observe(Ground(now.AddSeconds(7))); controller.Tick(now.AddSeconds(7));
        check(controller.Active && controller.Preview?.Mode == "CIRCLE", "Fresh stationary ground telemetry can automatically resume the same circle");
        session = GroundSession(); session.Flight = session.Flight! with { OnGround = 1 }; controller = new(session, log) { Settings = settings }; controller.Engage(now);
        check(!controller.Active && controller.Status.StartsWith("Take off"), "Ground lead support does not permit own-aircraft taxi or automatic takeoff");
        session = new(now);
        session.Focus.Observe(FollowerTests.Session.Sample(now.AddSeconds(1), 1) with { IsOnGround = true });
        check(session.Focus.GroundOrbitTarget == null && session.Focus.GroundSpeedKnots > 40,
            "Untrusted map on-ground flags cannot switch an airborne target into circling");

        // Closed-loop exercise with a five-second heading response and a physical
        // 25-degree turn-rate ceiling. Test both capture directions in crosswind.
        foreach (var startRadius in new[] { 1.0, 8.0 })
        {
            session = GroundSession(); var position = FormationGeometry.Offset(new(0, 0, 10607.61155), startRadius, 0);
            var trueHeading = 90.0; var maxFinalError = 0.0;
            const double tas = 160, windEast = 20, dt = .5;
            for (int i = 0; i < 7200; i++)
            {
                var at = now.AddSeconds(i * dt);
                var n = tas * Math.Cos(trueHeading * Math.PI / 180); var e = tas * Math.Sin(trueHeading * Math.PI / 180) + windEast;
                var gs = Math.Sqrt(n * n + e * e); var track = FormationGeometry.Normalize(Math.Atan2(e, n) * 180 / Math.PI);
                var own = session.Own! with { Position = position, GroundSpeedKnots = gs, TrueTrack = track, IndicatedSpeedKnots = tas, ReceivedAt = at };
                var flight = session.Flight! with { TrueAirspeed = tas, TrueHeading = trueHeading, MagneticHeading = FormationGeometry.Normalize(trueHeading - 10),
                    Mach = tas * 1852 / 3600 / Math.Sqrt(1.4 * 287.05287 * 288.15), ReceivedAt = at };
                var demand = guide.Calculate(own, flight, session.Focus, settings, at);
                var turnLimit = 9.80665 * Math.Tan(25 * Math.PI / 180) / (tas * 1852 / 3600) * 180 / Math.PI;
                trueHeading = FormationGeometry.Normalize(trueHeading + Math.Clamp(FormationGeometry.Angle(demand.MagneticHeading + 10 - trueHeading) / 5, -turnLimit, turnLimit) * dt);
                position = FormationGeometry.Offset(position, n * dt / 3600, e * dt / 3600);
                if (i > 6000) maxFinalError = Math.Max(maxFinalError, Math.Abs(position.DistanceNm(new(0, 0, 0)) - settings.BehindNm));
            }
            check(maxFinalError < .2, $"Circle capture from {startRadius} NM settles within 0.2 NM in crosswind with limited turn rate (error {maxFinalError:F3})");
        }
    }
}
