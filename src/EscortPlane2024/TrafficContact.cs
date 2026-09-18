namespace EscortPlane2024;

// The two APIs use unrelated IDs. Keep their namespaces distinct, including in the estimator.
internal readonly record struct ContactId(bool SimObject, ulong Value, bool Relay = false)
{
    public ulong TrackingId => Relay ? (1UL << 62) | Value : SimObject ? (1UL << 63) | Value : Value;
    public override string ToString() => Relay ? $"R{Value:X8}" : $"{(SimObject ? "S" : "T")}{Value}";
}

internal sealed record TrafficContact(ContactId Id, string Name, string Model, Position? Position,
    DateTimeOffset? PositionAt, string Detail)
{
    public bool Fresh(DateTimeOffset now) => Position is { IsValid: true } && PositionAt is { } at && (now - at).TotalSeconds is >= 0 and <= 2;
    public CoherentAircraft? MotionSample(DateTimeOffset now) => !Fresh(now) ? null :
        new(Id.TrackingId, Name, Model, Position!.Latitude, Position.Longitude, Position.AltitudeFeet * 0.3048,
            0, false, PositionAt!.Value, now);
}

internal static class TrafficContacts
{
    // A HUD label is only a name. Resolve it to one fresh aircraft ID, never to a label ID.
    // Conflicting live IDs deliberately require the pilot to choose an ID on the map/list.
    public static TrafficContact? NamedFix(string name, IEnumerable<TrafficContact> contacts, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var matches = contacts.Where(c => c.Fresh(now) && string.Equals(c.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    // Missing coordinates remain inspectable in All IDs, but can never be plotted or followed.
    public static bool AboveMinimum(TrafficContact contact, double minimumFeet) => minimumFeet <= 0 || contact.Position == null || contact.Position.AltitudeFeet >= minimumFeet;
    public static string? PositionIssue(Position p, Position? own)
    {
        if (!p.IsValid) return "Invalid position";
        if (p.Latitude == 0 && p.Longitude == 0 && p.AltitudeFeet == 0) return "MSFS returned zeros";
        // FakeSim may return the user's position for a different object ID. Never follow that echo.
        if (own != null && own.DistanceNm(p) < 0.002 && Math.Abs(own.AltitudeFeet - p.AltitudeFeet) < 5)
            return "MSFS returned your aircraft's position";
        return null;
    }
    public static IReadOnlyList<TrafficContact> Build(IEnumerable<CoherentAircraft> traffic, IEnumerable<TrafficAircraft> objects, Position? own)
    {
        var result = new List<TrafficContact>();
        foreach (var a in traffic)
        {
            var p = new Position(a.Latitude, a.Longitude, a.RawAltitude / 0.3048);
            var issue = PositionIssue(p, own);
            result.Add(new(new(false, a.TrafficId), a.Name, a.Model, issue == null ? p : null,
                issue == null ? a.SourceTime : null, issue ?? (string.IsNullOrWhiteSpace(a.Name) ? "Unnamed traffic object; type unverified" : "Map traffic")));
        }
        foreach (var a in objects.Where(a => a.Kind is "Aircraft" or "Helicopter"))
        {
            var issue = a.Position == null ? a.PositionIssue : PositionIssue(a.Position, own);
            result.Add(new(new(true, a.ObjectId), a.Callsign,
                a.Model.Length > 0 && !a.Model.StartsWith("$$:") ? a.Model : a.Title == "(TITLE unavailable)" ? "" : a.Title,
                issue == null ? a.Position : null, issue == null ? a.PositionAt : null,
                issue ?? $"{a.Kind} · SimConnect"));
        }
        return result;
    }
}
