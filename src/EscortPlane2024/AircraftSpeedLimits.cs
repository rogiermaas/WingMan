namespace EscortPlane2024;

internal static class AircraftSpeedLimits
{
    public const int Version = 1;
    // Use aircraft design data, never a sample taken during the takeoff roll.
    public static (double Min, double Max) Suggest(FlightTelemetry flight, bool pmdg)
    {
        if (pmdg) return (210, 320);
        var stall = double.IsFinite(flight.StallSpeed) && flight.StallSpeed > 0 ? flight.StallSpeed : 50;
        var min = Math.Clamp(Math.Ceiling(stall * 1.3), 40, 280);
        var cruise = flight.DesignCruiseSpeed;
        var max = double.IsFinite(cruise) && cruise >= min + 20 && cruise <= 600
            ? Math.Min(320, Math.Floor(cruise)) : Math.Min(320, Math.Max(min + 40, Math.Floor(stall * 2)));
        return (min, Math.Max(min + 20, max));
    }
    public static bool IsLegacyCollapsedRange(FormationSettings settings, FlightTelemetry flight) =>
        double.IsFinite(flight.StallSpeed) && flight.StallSpeed > 0
        && Math.Abs(settings.MaxIas - settings.MinIas - 1) < 0.0001
        && Math.Abs(settings.MinIas - Math.Clamp(flight.StallSpeed * 1.3, 40, 598)) < 0.0001;
}
