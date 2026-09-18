namespace EscortPlane2024;

internal static class CatchUpTests
{
    public static void Run(Diagnostics log, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new FollowerTests.Session(now);
        var settings = new FormationSettings(BehindNm: 4, MinIas: 150, MaxIas: 350);
        GuidanceSolution AtDistance(double separation)
        {
            session.Own = session.Own! with { Position = new(0, -separation / 60.04046, 10000) };
            return new GuidanceController().Calculate(session.Own, session.Flight!, session.Focus, settings, now);
        }
        var far = AtDistance(100);
        check(Math.Abs(far.AlongErrorNm - 96) < .05 && Math.Abs(far.Ias - settings.MaxIas) < .01
            && far.DesiredGroundSpeed > 500,
            "100 NM separation with a 4 NM slot uses the available maximum, not a lead plus 40 kt cap");
        var middle = AtDistance(6); var near = AtDistance(4.5); var settled = AtDistance(4);
        check(middle.Ias > near.Ias && near.Ias > settled.Ias && Math.Abs(settled.Ias - 250) < .1,
            "Catch-up speed progressively reduces to lead speed at the requested 4 NM spacing");
        check(GuidanceController.CatchUpCorrection(2, 100) < GuidanceController.CatchUpCorrection(2, 0)
            && GuidanceController.CatchUpCorrection(.2, 100) < 0,
            "High closing speed starts braking before crossing the formation slot");
        check(GuidanceController.CatchUpCorrection(-.1, -40) > 0,
            "An approaching slot starts acceleration early when recovering from being ahead");
        check(GuidanceController.CatchUpCorrection(-20, 0) == -40,
            "Being far ahead retains bounded slowdown rather than commanding a reversal");

        // Validate IAS/Mach clipping with pressure, calibration and a significant crosswind.
        session = new(now);
        session.Own = session.Own! with { Position = new(0, -100.0 / 60.04046, 10000), GroundSpeedKnots = 250, IndicatedSpeedKnots = 260 };
        session.Flight = session.Flight! with { AmbientPressure = 23800, AmbientTemperature = 230, Mach = .77, TrueAirspeed = 450, TrueHeading = 80, MagneticHeading = 70 };
        var limited = new GuidanceController().Calculate(session.Own, session.Flight, session.Focus, settings with { MaxMach = .78 }, now);
        check(limited.Mach <= .7800001 && limited.Ias <= settings.MaxIas && limited.Limit.Contains("Max Mach 0.78"),
            "Long-range catch-up still obeys the pilot's Mach ceiling at altitude");
        var sound = Math.Sqrt(1.4 * 287.05287 * session.Flight.AmbientTemperature) / (1852.0 / 3600);
        var windNorth = -450 * Math.Cos(80 * Math.PI / 180);
        var commandedHeading = (limited.MagneticHeading + 10) * Math.PI / 180;
        check(Math.Abs(limited.Mach * sound * Math.Cos(commandedHeading) + windNorth) < .05,
            "Speed-limited catch-up corrects crosswind using the commanded airspeed");

        // Response model: 20-second speed lag, acceleration/deceleration limited
        // to 0.5 kt/s, and no speedbrake assistance. Exercise the evolving gap,
        // rather than simply comparing the controller against its own formula.
        foreach (var scenario in new[] { (Gap: 20.0, Own: 250.0), (Gap: 2.0, Own: 300.0) })
        {
            var error = scenario.Gap; var own = scenario.Own; var minError = error; var peak = own;
            for (var second = 0; second < 3600; second++)
            {
                var command = Math.Clamp(250 + GuidanceController.CatchUpCorrection(error, own - 250), 150, 350);
                own += Math.Clamp((command - own) / 20, -.5, .5);
                error += (250 - own) / 3600;
                minError = Math.Min(minError, error); peak = Math.Max(peak, own);
            }
            check(Math.Abs(error) < .03 && Math.Abs(own - 250) < .2 && minError > -.1,
                $"Catch-up from {scenario.Gap} NM slot error settles with less than 0.1 NM overshoot in the lagged response model");
            if (scenario.Gap == 20) check(peak > 345, "Long-range response model uses the available speed headroom");
        }
    }
}
