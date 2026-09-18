namespace EscortPlane2024;

internal sealed record NearbyAircraft(string Key, string Name, string Model, string Distance, string Status,
    bool WingMan, bool CanFollow, string? RelayId, ContactId? LocalId, string Detail)
{
    public Color RowColor => WingMan ? Color.FromArgb(230, 245, 234) : Color.FromArgb(239, 241, 244);

    public static IReadOnlyList<NearbyAircraft> Build(IEnumerable<RelayMember> members, RelayTracks tracks,
        IEnumerable<TrafficContact> local, IEnumerable<NameplateContact> nameplates, Position? own,
        DateTimeOffset now, string ownId, string selectedRelay, bool relayConnected, bool simConnected)
    {
        var rows = new List<NearbyAircraft>();
        string Distance(Position? position) => own != null && position != null ? $"{own.DistanceNm(position):F2} NM" : "—";
        bool InRange(Position? position) => position == null || own == null || own.DistanceNm(position) <= 100;
        foreach (var member in members.Where(m => m.Id != ownId && m.Share && m.Online))
        {
            var track = tracks.Get(member.Id);
            var position = track == null ? null : new Position(track.Telemetry.Latitude, track.Telemetry.Longitude, track.Telemetry.AltitudeFeet);
            if (member.Id != selectedRelay && (track == null || (now - track.At).TotalSeconds > 5 || !InRange(position))) continue;
            var fresh = relayConnected && track != null && (now - track.At).TotalSeconds is >= -.25 and <= 2;
            rows.Add(new("relay:" + member.Id, member.Name, track?.Telemetry.Model ?? "", Distance(position),
                fresh ? "WingMan · Live" : "WingMan · Waiting", true, fresh && simConnected, member.Id, null,
                "WingMan telemetry" + (track == null ? "" : $"\nType/model: {track.Telemetry.AircraftType} {track.Telemetry.AircraftModel}\nRegistration: {track.Telemetry.Registration}\nLivery: {track.Telemetry.Livery}")));
        }
        foreach (var contact in local.Where(c => !c.Id.Relay && InRange(c.Position)))
        {
            var fresh = simConnected && contact.Fresh(now);
            rows.Add(new("local:" + contact.Id, string.IsNullOrWhiteSpace(contact.Name) ? "ID " + contact.Id : contact.Name,
                contact.Model, Distance(contact.Position), fresh ? "MSFS · Live" : contact.Position == null ? "MSFS · No position" : "MSFS · Stale",
                false, fresh, null, contact.Id, $"MSFS ID: {contact.Id}\n{contact.Detail}\nMSFS does not reliably identify multiplayer versus AI traffic."));
        }
        // Labels supply names and displayed distances, never a followable position.
        // Suppressing a duplicate label does not merge IDs or assign a WingMan identity.
        var names = rows.Select(r => r.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var label in nameplates.Where(n => !string.IsNullOrWhiteSpace(n.Name)).OrderBy(n => n.LabelId))
        {
            if (!names.Add(label.Name.Trim())) continue;
            rows.Add(new("name:" + label.Name.Trim().ToUpperInvariant(), label.Name, label.Model,
                string.IsNullOrWhiteSpace(label.Distance) ? "—" : label.Distance, "MSFS · Name only", false, false, null, null,
                "Visible multiplayer label only. MSFS has not provided a geographic position or aircraft ID, so it cannot be followed yet."));
        }
        return rows;
    }
}
