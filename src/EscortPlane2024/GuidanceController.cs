namespace EscortPlane2024;

internal sealed record FormationSettings(double BehindNm = 1, double RightNm = 0, double AboveNm = 0,
    double MinIas = 210, double MaxIas = 320, double MaxMach = 0.78, double MaxVs = 1500,
    bool Speed = true, bool Heading = true, bool Altitude = true, bool VerticalSpeed = false, int SmoothingSamples = 10)
{
    public bool Valid => new[] { BehindNm, RightNm, AboveNm, MinIas, MaxIas, MaxMach, MaxVs }.All(double.IsFinite)
        && BehindNm is >= 0.1 and <= 100 && Math.Abs(RightNm) <= 20 && Math.Abs(AboveNm) <= 5
        && MinIas >= 40 && MaxIas <= 600 && MinIas < MaxIas && MaxMach is >= 0.2 and <= 0.95 && MaxVs is >= 100 and <= 6000 && SmoothingSamples is >= 5 and <= 30;
}

internal sealed record GuidanceSolution(string Mode, double AlongErrorNm, double CrossErrorNm, double VerticalErrorFeet,
    double DesiredGroundSpeed, double Ias, double Mach, double MagneticHeading, double AltitudeFeet, double VerticalSpeedFpm,
    string Limit, Position Slot)
{
    public double? OrbitRadiusNm { get; init; }
}

internal static class FormationGeometry
{
    public const double FeetPerNm = 1852 / 0.3048;
    public static double Normalize(double angle) => (angle % 360 + 360) % 360;
    public static double Angle(double angle) => Normalize(angle + 180) - 180;
    public static (double North, double East) Displacement(Position from, Position to)
    {
        var a = from.Latitude * Math.PI / 180; var b = to.Latitude * Math.PI / 180;
        var dlon = (to.Longitude - from.Longitude) * Math.PI / 180;
        var bearing = Math.Atan2(Math.Sin(dlon) * Math.Cos(b), Math.Cos(a) * Math.Sin(b) - Math.Sin(a) * Math.Cos(b) * Math.Cos(dlon));
        var distance = from.DistanceNm(to);
        return (distance * Math.Cos(bearing), distance * Math.Sin(bearing));
    }
    public static Position Offset(Position origin, double northNm, double eastNm, double upFeet = 0)
    {
        var distance = Math.Sqrt(northNm * northNm + eastNm * eastNm) / 3440.065;
        var bearing = Math.Atan2(eastNm, northNm);
        var lat = origin.Latitude * Math.PI / 180; var lon = origin.Longitude * Math.PI / 180;
        var lat2 = Math.Asin(Math.Sin(lat) * Math.Cos(distance) + Math.Cos(lat) * Math.Sin(distance) * Math.Cos(bearing));
        var lon2 = lon + Math.Atan2(Math.Sin(bearing) * Math.Sin(distance) * Math.Cos(lat), Math.Cos(distance) - Math.Sin(lat) * Math.Sin(lat2));
        return new(lat2 * 180 / Math.PI, Angle(lon2 * 180 / Math.PI), origin.AltitudeFeet + upFeet);
    }
    public static Position Slot(Position target, double track, FormationSettings settings)
    {
        var r = track * Math.PI / 180;
        return Offset(target, -settings.BehindNm * Math.Cos(r) - settings.RightNm * Math.Sin(r),
            -settings.BehindNm * Math.Sin(r) + settings.RightNm * Math.Cos(r), settings.AboveNm * FeetPerNm);
    }
}

internal sealed partial class GuidanceController
{
    private string mode = "CAPTURE";
    private DateTimeOffset? settledSince;
    public void Reset() { mode = "CAPTURE"; settledSince = null; }
    public GuidanceSolution Calculate(OwnTelemetry own, FlightTelemetry flight, TargetEstimator target, FormationSettings s, DateTimeOffset now)
    {
        if (target.GroundOrbitTarget is { } ground)
        {
            Reset();
            return CalculateOrbit(own, flight, ground, s, now);
        }
        if (!s.Valid || !flight.Valid || target.Latest == null || target.GroundTrackDegrees == null || target.GroundSpeedKnots == null)
            throw new InvalidOperationException("Guidance inputs unavailable");
        var t = target.Latest;
        var track = target.GroundTrackDegrees.Value; var speed = target.GroundSpeedKnots.Value;
        var tr = track * Math.PI / 180;
        var targetAge = Math.Clamp((now - t.SourceTime).TotalSeconds, 0, 2);
        var targetPosition = FormationGeometry.Offset(target.SmoothedPosition ?? new(t.Latitude, t.Longitude, t.RawAltitude / 0.3048),
            speed * targetAge / 3600 * Math.Cos(tr), speed * targetAge / 3600 * Math.Sin(tr), (target.VerticalSpeedFpm ?? 0) * targetAge / 60);
        var slot = FormationGeometry.Slot(targetPosition, track, s);
        var ownAge = Math.Clamp((now - own.ReceivedAt).TotalSeconds, 0, 2); var ownTrack = own.TrueTrack * Math.PI / 180;
        var ownPosition = FormationGeometry.Offset(own.Position, own.GroundSpeedKnots * ownAge / 3600 * Math.Cos(ownTrack),
            own.GroundSpeedKnots * ownAge / 3600 * Math.Sin(ownTrack), own.VerticalSpeedFpm * ownAge / 60);
        var (north, east) = FormationGeometry.Displacement(ownPosition, slot);
        var along = north * Math.Cos(tr) + east * Math.Sin(tr);
        var cross = -north * Math.Sin(tr) + east * Math.Cos(tr);
        var vertical = slot.AltitudeFeet - ownPosition.AltitudeFeet;
        if (along < -0.2) { mode = "AHEAD HOLD"; settledSince = null; }
        else if (mode == "AHEAD HOLD" && along > -0.05) mode = "CAPTURE";
        if (mode != "AHEAD HOLD")
        {
            if (Math.Abs(along) < 0.08 && Math.Abs(cross) < 0.05 && Math.Abs(vertical) < 150)
            {
                settledSince ??= now;
                if (now - settledSince >= TimeSpan.FromSeconds(5)) mode = "FOLLOW";
            }
            else { settledSince = null; if (Math.Abs(along) > 0.15 || Math.Abs(cross) > 0.1 || Math.Abs(vertical) > 250) mode = "CAPTURE"; }
        }
        // Heading always follows the target's forward direction: a slot behind cannot command a U-turn.
        var maxIntercept = mode == "AHEAD HOLD" ? 10 : 25;
        var intercept = Math.Clamp(Math.Atan2(cross, Math.Max(0.7, speed * 30 / 3600)) * 180 / Math.PI, -maxIntercept, maxIntercept);
        var course = FormationGeometry.Normalize(track + intercept); var cr = course * Math.PI / 180;
        // Use the remaining distance to the formation slot, not distance to the lead.
        var ownAlongSpeed = own.GroundSpeedKnots * Math.Cos((own.TrueTrack - track) * Math.PI / 180);
        var gs = Math.Max(40, speed + CatchUpCorrection(along, ownAlongSpeed - speed));
        var hr = flight.TrueHeading * Math.PI / 180;
        var windNorth = own.GroundSpeedKnots * Math.Cos(ownTrack) - flight.TrueAirspeed * Math.Cos(hr);
        var windEast = own.GroundSpeedKnots * Math.Sin(ownTrack) - flight.TrueAirspeed * Math.Sin(hr);
        var airNorth = gs * Math.Cos(cr) - windNorth; var airEast = gs * Math.Sin(cr) - windEast;
        var tas = Math.Sqrt(airNorth * airNorth + airEast * airEast);
        var magVariation = FormationGeometry.Angle(flight.TrueHeading - flight.MagneticHeading);
        var soundKnots = Math.Sqrt(1.4 * 287.05287 * flight.AmbientTemperature) / (1852.0 / 3600);
        var mach = tas / soundKnots;
        var ias = Cas(mach, flight.AmbientPressure);
        // Calibrate ideal CAS to the aircraft's reported IAS at the current flight condition.
        var calibration = Math.Clamp(own.IndicatedSpeedKnots / Cas(flight.Mach, flight.AmbientPressure), 0.8, 1.2);
        ias *= calibration;
        var lower = Math.Max(s.MinIas, flight.StallSpeed > 0 ? flight.StallSpeed * 1.3 : s.MinIas);
        var machLimitIas = Cas(s.MaxMach, flight.AmbientPressure) * calibration;
        var upper = Math.Min(s.MaxIas, machLimitIas);
        if (lower >= upper) throw new InvalidOperationException("No usable speed range at this altitude; revise limits");
        var limitedIas = Math.Clamp(ias, lower, upper);
        var limitedMach = MachFromCas(limitedIas / calibration, flight.AmbientPressure);
        // Far-away catch-up can request more speed than the aircraft's configured
        // limits allow. Correct crosswind using the airspeed we actually command,
        // rather than the larger, unreachable catch-up demand.
        var crosswind = -windNorth * Math.Sin(cr) + windEast * Math.Cos(cr);
        var headingTrue = FormationGeometry.Normalize(course + Math.Asin(Math.Clamp(-crosswind / (limitedMach * soundKnots), -1, 1)) * 180 / Math.PI);
        var selectedAltitude = slot.AltitudeFeet + flight.IndicatedAltitude - own.Position.AltitudeFeet;
        var vs = Math.Clamp((target.VerticalSpeedFpm ?? 0) + vertical * 1.5, -s.MaxVs, s.MaxVs);
        return new(mode, along, cross, vertical, gs, limitedIas, limitedMach,
            FormationGeometry.Normalize(headingTrue - magVariation), Math.Clamp(selectedAltitude, 0, 45000), vs,
            ias < lower - 0.1 ? $"Minimum IAS {lower:F0} kt prevents the requested slowdown; you may overtake."
                : ias > upper + 0.1 ? (machLimitIas < s.MaxIas
                    ? $"Max Mach {s.MaxMach:F2} limits IAS to {upper:F0} kt; raise Max Mach to allow faster catch-up."
                    : $"Max IAS {s.MaxIas:F0} kt limits catch-up speed; raise Max IAS to allow faster catch-up.") : "", slot);
    }
    internal static double CatchUpCorrection(double slotErrorNm, double closingKnots)
    {
        // Account for 20 seconds of speed response before sizing the correction.
        // Far away, a braking-distance envelope replaces the old fixed +40 kt cap.
        // Assume 0.5 kt/s deceleration for this guidance model (not an aircraft
        // performance guarantee). Near the slot, proportional correction takes
        // over smoothly and the speed difference tends to zero.
        var remaining = slotErrorNm - closingKnots * 20 / 3600;
        if (remaining <= 0) return Math.Max(-40, remaining * 25);
        var brakingEnvelope = Math.Sqrt(2 * 0.5 * 3600 * remaining);
        return Math.Min(remaining * 25, brakingEnvelope);
    }
    internal static double Cas(double mach, double pressure)
    {
        var impact = pressure * (Math.Pow(1 + 0.2 * mach * mach, 3.5) - 1);
        return 340.294 / (1852.0 / 3600) * Math.Sqrt(5 * (Math.Pow(impact / 101325 + 1, 2.0 / 7) - 1));
    }
    internal static double MachFromCas(double cas, double pressure)
    {
        var impact = 101325 * (Math.Pow(1 + 0.2 * Math.Pow(cas * (1852.0 / 3600) / 340.294, 2), 3.5) - 1);
        return Math.Sqrt(5 * (Math.Pow(impact / pressure + 1, 2.0 / 7) - 1));
    }
}
