namespace EscortPlane2024;

internal sealed record Position(double Latitude, double Longitude, double AltitudeFeet)
{
    public bool IsValid => double.IsFinite(Latitude) && double.IsFinite(Longitude) && double.IsFinite(AltitudeFeet)
        && Math.Abs(Latitude) <= 90 && Math.Abs(Longitude) <= 180 && AltitudeFeet is > -2000 and < 100000;
    public double DistanceNm(Position other)
    {
        const double radians = Math.PI / 180;
        double dlat = (other.Latitude - Latitude) * radians, dlon = (other.Longitude - Longitude) * radians;
        var a = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(Latitude * radians) * Math.Cos(other.Latitude * radians) * Math.Pow(Math.Sin(dlon / 2), 2);
        return 3440.065 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
internal sealed record OwnTelemetry(Position Position, double GroundSpeedKnots, double TrueTrack, double IndicatedSpeedKnots, double VerticalSpeedFpm, double CameraState, DateTimeOffset ReceivedAt);
internal sealed record TrafficAircraft(uint ObjectId, string Title, string Kind, DateTimeOffset LastSeen, Position? Position = null, DateTimeOffset? PositionAt = null,
    string Callsign = "", string AircraftType = "", string Model = "", string Category = "", string? PositionIssue = "Waiting for position");
internal sealed class TrafficTracker
{
    private readonly Dictionary<uint, TrafficAircraft> aircraft = [];
    public IEnumerable<TrafficAircraft> Aircraft => aircraft.Values;
    public void Seen(uint id, string title, string kind)
    {
        aircraft.TryGetValue(id, out var old);
        aircraft[id] = new(id, string.IsNullOrWhiteSpace(title) ? "(TITLE unavailable)" : title,
            kind == "Exposed object (ALL)" && old != null ? old.Kind : kind, DateTimeOffset.UtcNow, old?.Position, old?.PositionAt,
            old?.Callsign ?? "", old?.AircraftType ?? "", old?.Model ?? "", old?.Category ?? "", old?.PositionIssue ?? (old?.Position == null ? "Waiting for position" : null));
    }
    public void SetIdentity(uint id, string field, string value)
    {
        if (!aircraft.TryGetValue(id, out var item)) return;
        aircraft[id] = field switch
        {
            "ATC ID" => item with { Callsign = value }, "ATC TYPE" => item with { AircraftType = value },
            "ATC MODEL" => item with { Model = value }, "CATEGORY" => item with { Category = value }, _ => item
        };
    }
    public void SetPosition(uint id, Position position)
    {
        if (aircraft.TryGetValue(id, out var item)) aircraft[id] = item with { Position = position, PositionAt = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow, PositionIssue = null };
    }
    public void RejectPosition(uint id, string reason)
    {
        if (aircraft.TryGetValue(id, out var item)) aircraft[id] = item with { Position = null, PositionAt = null, PositionIssue = reason };
    }
    public void Remove(uint id) => aircraft.Remove(id);
    public void Clear() => aircraft.Clear();
    public void Expire(DateTimeOffset now)
    {
        foreach (var id in aircraft.Where(pair => now - pair.Value.LastSeen > TimeSpan.FromSeconds(20)).Select(pair => pair.Key).ToArray()) aircraft.Remove(id);
    }
}
