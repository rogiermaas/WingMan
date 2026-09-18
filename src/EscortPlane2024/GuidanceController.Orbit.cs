namespace EscortPlane2024;

internal sealed partial class GuidanceController
{
    private static GuidanceSolution CalculateOrbit(OwnTelemetry own, FlightTelemetry flight,
        CoherentAircraft ground, FormationSettings s, DateTimeOffset now)
    {
        if (!s.Valid || !flight.Valid) throw new InvalidOperationException("Guidance inputs unavailable");
        if (s.AboveNm <= 0) throw new InvalidOperationException("Lead is on the ground: set Above NM to a positive value to circle");
        var calibration = Math.Clamp(own.IndicatedSpeedKnots / Cas(flight.Mach, flight.AmbientPressure), .8, 1.2);
        var lower = Math.Max(s.MinIas, flight.StallSpeed > 0 ? flight.StallSpeed * 1.3 : s.MinIas);
        var upper = Math.Min(s.MaxIas, Cas(s.MaxMach, flight.AmbientPressure) * calibration);
        if (lower >= upper) throw new InvalidOperationException("No usable speed range at this altitude; revise limits");
        // A parked lead has no airspeed to match. Keep a small margin above the
        // minimum so ordinary speed fluctuation does not inhibit height matching.
        var orbitIas = Math.Min(lower + 10, upper);
        var mach = MachFromCas(orbitIas / calibration, flight.AmbientPressure);
        var soundKnots = Math.Sqrt(1.4 * 287.05287 * flight.AmbientTemperature) / (1852.0 / 3600);
        var tas = mach * soundKnots;
        var ownTrack = own.TrueTrack * Math.PI / 180;
        var heading = flight.TrueHeading * Math.PI / 180;
        var windNorth = own.GroundSpeedKnots * Math.Cos(ownTrack) - flight.TrueAirspeed * Math.Cos(heading);
        var windEast = own.GroundSpeedKnots * Math.Sin(ownTrack) - flight.TrueAirspeed * Math.Sin(heading);
        var wind = Math.Sqrt(windNorth * windNorth + windEast * windEast);
        if (wind >= tas * .9) throw new InvalidOperationException("Wind is too strong to circle at this speed; raise Minimum IAS");

        // Size for a nominal 20-degree turn, using current speed while slowing.
        // This is guidance sizing, not a guarantee of a particular aircraft's AP
        // bank limit. Wind is included conservatively for the downwind leg.
        var sizingSpeed = (Math.Max(tas, flight.TrueAirspeed) + wind) * 1852 / 3600;
        var minimumRadius = sizingSpeed * sizingSpeed / (9.80665 * Math.Tan(20 * Math.PI / 180)) / 1852;
        var radius = Math.Max(s.BehindNm, minimumRadius);
        var center = new Position(ground.Latitude, ground.Longitude, ground.RawAltitude / .3048);
        var ownAge = Math.Clamp((now - own.ReceivedAt).TotalSeconds, 0, 2);
        var ownPosition = FormationGeometry.Offset(own.Position,
            own.GroundSpeedKnots * ownAge / 3600 * Math.Cos(ownTrack),
            own.GroundSpeedKnots * ownAge / 3600 * Math.Sin(ownTrack), own.VerticalSpeedFpm * ownAge / 60);
        var (north, east) = FormationGeometry.Displacement(center, ownPosition);
        var distance = Math.Sqrt(north * north + east * east);
        var radial = distance > .001 ? Math.Atan2(east, north) : ownTrack - Math.PI / 2;
        var radialError = distance - radius;
        // Clockwise tangent with smooth inward/outward correction. A small lead
        // angle offsets the heading-mode response delay around the circle.
        var anticipation = Math.Min(10, own.GroundSpeedKnots * 5 / 3600 / radius * 180 / Math.PI);
        var course = radial * 180 / Math.PI + 90 + Math.Atan2(2 * radialError, radius) * 180 / Math.PI + anticipation;
        var cr = course * Math.PI / 180;
        var crosswind = -windNorth * Math.Sin(cr) + windEast * Math.Cos(cr);
        var trueHeading = course + Math.Asin(Math.Clamp(-crosswind / tas, -1, 1)) * 180 / Math.PI;
        var gs = Math.Sqrt(Math.Max(0, tas * tas - crosswind * crosswind)) + windNorth * Math.Cos(cr) + windEast * Math.Sin(cr);
        var slot = FormationGeometry.Offset(center, radius * Math.Cos(radial), radius * Math.Sin(radial), s.AboveNm * FormationGeometry.FeetPerNm);
        var vertical = slot.AltitudeFeet - ownPosition.AltitudeFeet;
        var selectedAltitude = slot.AltitudeFeet + flight.IndicatedAltitude - own.Position.AltitudeFeet;
        var limit = radius > s.BehindNm + .01
            ? $"Circling clockwise at {radius:F1} NM radius (requested {s.BehindNm:F1}); widened for speed/wind."
            : $"Circling clockwise at {radius:F1} NM radius.";
        if (s.RightNm != 0) limit += " Right offset resumes after takeoff.";
        return new("CIRCLE", radialError, 0, vertical, gs, orbitIas, mach,
            FormationGeometry.Normalize(trueHeading - FormationGeometry.Angle(flight.TrueHeading - flight.MagneticHeading)),
            Math.Clamp(selectedAltitude, 0, 45000), Math.Clamp(vertical * 1.5, -s.MaxVs, s.MaxVs), limit, slot)
            { OrbitRadiusNm = radius };
    }
}
