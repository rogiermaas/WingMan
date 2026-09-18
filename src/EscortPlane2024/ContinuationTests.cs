namespace EscortPlane2024;

internal static class ContinuationTests
{
    public static void Run(Diagnostics log, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new FollowerTests.Session(now);
        session.Focus.TrustedGroundStatus = true;
        var settings = new FormationSettings(MinIas: 140, MaxIas: 200, MaxMach: .8);
        var abeam = new FormationSettings(BehindNm: 0, RightNm: .5);
        var sideSlot = FormationGeometry.Slot(new(0, 0, 1000), 90, abeam);
        check(abeam.Valid && Math.Abs(sideSlot.Longitude) < .00001 && sideSlot.Latitude < 0
            && Math.Abs(sideSlot.DistanceNm(new(0, 0, 1000)) - .5) < .00001,
            "Zero Behind NM creates a true abeam slot with the requested lateral offset");
        var flight = session.Flight! with { StallSpeed = 175 };
        var solution = new GuidanceController().Calculate(session.Own!, flight, session.Focus, settings, now);
        check(solution.Ias <= 200 && solution.Limit.Contains("design-speed reference"), "User speed caps below the design reference remain usable with an advisory warning");
        var ahead = session.Own! with { Position = FormationGeometry.Offset(session.Own.Position, 0, 5) };
        solution = new GuidanceController().Calculate(ahead, flight, session.Focus, settings with { MinIas = 180, MaxIas = 350 }, now);
        check(solution.Ias < 227 && solution.Ias >= 180 && solution.Limit.Contains("Your limits apply"), "Following may slow below the clean design estimate without silently raising the pilot minimum");
        session.Focus.ResetMotion();
        session.Focus.Observe(FollowerTests.Session.Sample(now, 0) with { IsOnGround = true });
        solution = new GuidanceController().Calculate(session.Own!, flight, session.Focus, settings with { AboveNm = .1 }, now);
        check(solution.Ias == 150 && solution.Limit.Contains("design-speed reference"), "Circling also respects the pilot's lower minimum with flaps");
        solution = new GuidanceController().Calculate(session.Own!, flight, session.Focus,
            settings with { AboveNm = .1, MinIas = 210, MaxIas = 350, CircleIas = 140 }, now);
        check(solution.Ias == 140 && solution.Limit.Contains("design-speed reference"), "Explicit circle speed can be lower than the following minimum without a hidden stall-speed override");
        var fasterCircle = solution;
        var slowerFlight = flight with { TrueAirspeed = 140, Mach = flight.Mach * 140 / flight.TrueAirspeed };
        solution = new GuidanceController().Calculate(session.Own! with { IndicatedSpeedKnots = 140, GroundSpeedKnots = 140 }, slowerFlight, session.Focus,
            settings with { AboveNm = .1, BehindNm = 0, MinIas = 210, MaxIas = 350, CircleIas = 140 }, now);
        check(solution.OrbitRadiusNm < fasterCircle.OrbitRadiusNm, "Slowing to the selected circle speed permits a smaller computed turn radius");
        solution = new GuidanceController().Calculate(session.Own!, flight, session.Focus,
            settings with { AboveNm = .1, CircleIas = 240 }, now);
        check(solution.Ias == 200 && solution.Limit.Contains("capped"), "Circle speed still respects pilot maximum IAS and explains a capped selection");
        var circleController = new FollowerController(session, log) { Settings = new(AboveNm: .2, MinIas: 210, MaxIas: 350, CircleIas: 140, VerticalSpeed: true) };
        session.Own = session.Own! with { IndicatedSpeedKnots = 140 }; session.Flight = slowerFlight;
        circleController.Engage(now); circleController.Tick(now);
        check(circleController.Active && circleController.Preview!.VerticalSpeedFpm > 0,
            "Following minimum does not block height matching at a lower selected circle speed");

        session = new(now); session.Focus.TrustedGroundStatus = true;
        var controller = new FollowerController(session, log) { Settings = new(), ResumeWhenTelemetryReturns = false };
        controller.Engage(now); controller.Tick(now);
        var liveSlot = controller.Preview!.Slot; var lastTime = session.Focus.Latest!.SourceTime;
        session.Refresh(now.AddSeconds(30), 30, false); controller.Tick(now.AddSeconds(30));
        check(controller.Active && controller.UsingRememberedData && controller.Preview!.Slot.DistanceNm(liveSlot) is > 2.07 and < 2.10,
            "Missing live lead data projects its 250 kt course for 30 seconds and keeps following");
        check(session.Focus.Latest.SourceTime == lastTime && controller.Preview!.Slot.AltitudeFeet == liveSlot.AltitudeFeet
            && controller.Status.Contains("30s old"), "Estimated guidance never refreshes real telemetry or projects an indefinite descent");
        controller.TelemetryLost("WingMan connection lost"); session.Focus.ResetMotion();
        session.Refresh(now.AddSeconds(31), 31, false); controller.Tick(now.AddSeconds(31));
        check(controller.Active && controller.UsingRememberedData, "Relay disconnect and cleared feed retain the detached lead memory");
        for (var i = 32; i <= 37; i++) { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        check(controller.Active && !controller.UsingRememberedData, "Fresh established live motion automatically replaces remembered guidance");
        controller.Stop("Pilot stopped following"); session.Refresh(now.AddSeconds(50), 50, false); controller.Tick(now.AddSeconds(50));
        check(!controller.Active && !controller.UsingRememberedData, "Explicit Stop cancels remembered guidance");

        session = new(now); session.Focus.TrustedGroundStatus = true;
        controller = new(session, log) { Settings = new(CircleBelowFeet: 1000, AboveNm: 0) };
        check(controller.BlockReason(now)?.Contains("Lead height unavailable") == true,
            "Old clients without AGL cannot silently activate altitude-triggered circles");
        var guide = new GuidanceController();
        CoherentAircraft Sample(int second, double agl) => FollowerTests.Session.Sample(now.AddSeconds(second), second) with { AboveGroundFeet = agl };
        session.Focus.Observe(Sample(1, 900)); session.Refresh(now.AddSeconds(1), 1, false);
        controller.Engage(now.AddSeconds(1)); controller.Tick(now.AddSeconds(1));
        check(controller.Active && controller.Preview?.Mode == "CIRCLE" && Math.Abs(controller.Preview.Slot.AltitudeFeet - 10100) < .1,
            "An airborne lead below 1000 AGL triggers a circle at terrain elevation plus 1000 ft, ignoring Above NM");
        solution = guide.Calculate(session.Own!, session.Flight!, session.Focus, controller.Settings, now.AddSeconds(1));
        session.Focus.Observe(Sample(2, 1050));
        solution = guide.Calculate(session.Own!, session.Flight!, session.Focus, controller.Settings, now.AddSeconds(2));
        check(solution.Mode == "CIRCLE", "100 ft hysteresis prevents circling/following flicker near the threshold");
        session.Focus.Observe(Sample(3, 1200));
        solution = guide.Calculate(session.Own!, session.Flight!, session.Focus, controller.Settings, now.AddSeconds(3));
        check(solution.Mode != "CIRCLE", "Climbing clear of the threshold automatically restores formation following");
        session.Focus.Observe(Sample(4, 800)); session.Refresh(now.AddSeconds(4), 4, false); controller.Tick(now.AddSeconds(4));
        var circleSlot = controller.Preview!.Slot;
        session.Refresh(now.AddSeconds(34), 4, false); controller.Tick(now.AddSeconds(34));
        check(controller.Active && controller.UsingRememberedData && controller.Preview?.Mode == "CIRCLE"
            && controller.Preview.Slot.DistanceNm(circleSlot) < .01 && Math.Abs(controller.Preview.Slot.AltitudeFeet - circleSlot.AltitudeFeet) < .01,
            "Loss during a low-lead circle keeps the last centre and terrain-relative height");
        check(!new FormationSettings(CircleBelowFeet: 1050).Valid && new FormationSettings(CircleBelowFeet: 1000).Valid,
            "Circling height persists in whole 100 ft steps");

        using var host = new Form { ClientSize = new(300, 180) };
        using var panel = new StableScrollPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown };
        host.Controls.Add(panel);
        for (var i = 0; i < 25; i++) panel.Controls.Add(new Label { Text = "Telemetry line " + i, AutoSize = true });
        host.Show(); panel.AutoScrollPosition = new(0, 120); var scroll = panel.AutoScrollPosition.Y;
        using (panel.PreserveScroll()) panel.Controls[20].Text = "Updated telemetry below the current view";
        check(scroll < -50 && panel.AutoScrollPosition.Y == scroll, "Live diagnostic label refresh preserves the user's scroll position");
        host.Hide();
    }
}
