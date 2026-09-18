namespace EscortPlane2024;

internal static class VerticalModeTests
{
    public static void Run(Diagnostics log, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var s = new FormationSettings(MinIas: 150, MaxIas: 350, VerticalSpeed: true);
        var session = new FollowerTests.Session(now);
        session.StandardState = session.StandardState! with { VsMode = false, AltitudeHold = true };
        var controller = new FollowerController(session, log) { Settings = s };
        controller.Engage(now); controller.Tick(now);
        check(controller.Active && session.ModeRequests == 0 && session.Outputs.All(o => o.Axis != "vs"),
            "Stock altitude hold remains active at the requested height without reselecting V/S");
        controller.ApplySettings(s with { AboveNm = 0.2 });
        session.Refresh(now.AddSeconds(1), 1); controller.Tick(now.AddSeconds(1));
        check(session.ModeRequests == 0 && session.StandardState.Altitude > 10000,
            "Stock climb first sets a new altitude before requesting V/S");
        session.Refresh(now.AddSeconds(2), 2); controller.Tick(now.AddSeconds(2));
        check(controller.Active && session.ModeRequests == 1,
            "Stock climb reselects V/S after the new altitude selector is observed");
        session.Refresh(now.AddSeconds(3), 3); controller.Tick(now.AddSeconds(3));
        check(session.Outputs.Any(o => o.Axis == "vs" && o.Value > 0) && session.ModeRequests == 1,
            "Stock V/S commands begin after mode confirmation and do not toggle repeatedly");
        controller.ApplySettings(s); session.StandardState = session.StandardState! with { VsMode = false, AltitudeHold = true };
        session.Refresh(now.AddSeconds(4), 4); controller.Tick(now.AddSeconds(4));
        check(controller.Active && session.ModeRequests == 1, "Returning to altitude hold does not stop following");
        controller.ApplySettings(s with { AboveNm = -0.2 });
        for (var i = 5; i <= 12; i++) { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        check(controller.Active && session.ModeRequests == 2 && session.Outputs.Any(o => o.Axis == "vs" && o.Value < 0),
            "A later descent can reselect V/S after another normal altitude capture");
        session.StandardState = session.StandardState! with { MasterEngaged = false };
        session.Refresh(now.AddSeconds(13), 13); controller.Tick(now.AddSeconds(13));
        check(!controller.Active && !controller.WaitingForTelemetry && session.ModeRequests == 2,
            "Disconnecting stock AP master stops automatic height matching without reengaging it");

        session = new(now) { Accept = false };
        session.StandardState = session.StandardState! with { VsMode = false, AltitudeHold = true, Altitude = 11200 };
        controller = new(session, log) { Settings = s with { AboveNm = 0.2 }, ResumeWhenTelemetryReturns = true };
        controller.Engage(now);
        for (var i = 0; i <= 8; i++) { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        check(!controller.Active && !controller.WaitingForTelemetry && session.ModeRequests == 1 && controller.Status.Contains("V/S mode"),
            "An unsupported stock V/S event times out once and cannot trigger automatic restart");

        session = new(now); session.StandardState = session.StandardState! with { VsMode = false, AltitudeHold = true };
        controller = new(session, log) { Settings = s with { AboveNm = 0.2, VerticalSpeed = false } };
        controller.Engage(now);
        for (var i = 0; i <= 3; i++) { session.Refresh(now.AddSeconds(i), i); controller.Tick(now.AddSeconds(i)); }
        check(controller.Active && session.ModeRequests == 0 && session.Outputs.All(o => o.Axis != "vs"),
            "Disabling Automatic V/S leaves vertical modes with the pilot");

        session = new(now);
        session.Flight = session.Flight! with { AmbientPressure = 23800, Mach = 0.77, TrueAirspeed = 450, AmbientTemperature = 230 };
        session.Own = session.Own! with { Position = new(0, -20.0 / 60, 10000), GroundSpeedKnots = 250, IndicatedSpeedKnots = 260 };
        var guide = new GuidanceController();
        var lower = guide.Calculate(session.Own, session.Flight, session.Focus, s with { MaxMach = 0.78 }, now);
        var higher = guide.Calculate(session.Own, session.Flight, session.Focus, s with { MaxMach = 0.84 }, now);
        check(lower.Limit.Contains("Max Mach 0.78") && higher.Ias > lower.Ias + 10,
            "Raising Max Mach allows a higher IAS ceiling at altitude while retaining spacing guidance");
    }
}
