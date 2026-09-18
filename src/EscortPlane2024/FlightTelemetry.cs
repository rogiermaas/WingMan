namespace EscortPlane2024;

internal sealed record FlightTelemetry(double TrueAirspeed, double TrueHeading, double MagneticHeading,
    double IndicatedAltitude, double AmbientPressure, double AmbientTemperature, double Mach,
    double OnGround, double AboveGround, double Flaps, double Gear, double SimRate,
    double StallSpeed, DateTimeOffset ReceivedAt)
{
    public double DesignCruiseSpeed { get; init; }
    public bool Valid => ValidationIssue == null;
    public string? ValidationIssue
    {
        get
        {
            foreach (var (value, name) in new (double, string)[] {
                (TrueAirspeed, "true airspeed"), (TrueHeading, "true heading"), (MagneticHeading, "magnetic heading"),
                (IndicatedAltitude, "indicated altitude"), (AmbientPressure, "air pressure"),
                (AmbientTemperature, "air temperature"), (Mach, "Mach speed"), (OnGround, "on-ground status"),
                (AboveGround, "height above ground"), (Flaps, "flap position"), (Gear, "gear position"),
                (SimRate, "simulation rate"), (StallSpeed, "stall speed") })
                if (!double.IsFinite(value)) return $"Waiting for valid {name} from MSFS";
            if (TrueAirspeed <= 50) return $"Following requires true airspeed above 50 kt (currently {TrueAirspeed:F0} kt)";
            if (AmbientPressure <= 1000) return "MSFS air pressure is outside the usable range";
            if (AmbientTemperature <= 150) return "MSFS air temperature is outside the usable range";
            if (Mach is <= 0 or >= 1.5) return "MSFS Mach speed is outside the usable range";
            return null;
        }
    }
}
