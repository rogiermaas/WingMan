namespace EscortPlane2024;

internal static class NearbyAircraftTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        const string lead = "11111111111111111111111111111111", self = "22222222222222222222222222222222";
        var members = new[] { new RelayMember(lead, "Same pilot", true, "", true), new RelayMember(self, "You", true, "", true) };
        var tracks = new RelayTracks();
        var t = new RelayTelemetry(1, now.ToUnixTimeMilliseconds(), 0, 52, 4, 10000, 250, 90, 90, 0, "787-10", false, false, 1);
        tracks.Accept(lead, "Same pilot", t, now, now); tracks.Accept(self, "You", t, now, now);
        var own = FormationGeometry.Offset(new(52, 4, 10000), 4, 0);
        var native = new TrafficContact(new(true, 7), "Same pilot", "A321", new(52, 4, 10000), now, "Aircraft");
        var map = native with { Id = new(false, 7), Name = "" };
        var labels = new[] { new NameplateContact("hud-1", "SAME PILOT", "A321", "", "4 NM", "10000 ft"),
            new NameplateContact("hud-2", "Label only", "A320", "", "2 km", "1000 ft") };
        var rows = NearbyAircraft.Build(members, tracks, [native, map], labels, own, now, self, lead, true, true);
        check(rows.Count == 4 && rows.Count(r => r.WingMan) == 1 && !rows.Any(r => r.RelayId == self),
            "Combined nearby list includes relay, native, map and unmatched label while excluding own relay identity");
        check(rows.Select(r => r.Key).Distinct().Count() == rows.Count && rows.Count(r => r.Name == "Same pilot") == 2,
            "Matching names and numeric IDs across sources cannot merge aircraft identities");
        check(rows.Single(r => r.RelayId == lead).RowColor == Color.FromArgb(230, 245, 234)
            && rows.Where(r => !r.WingMan).All(r => r.RowColor == Color.FromArgb(239, 241, 244)),
            "WingMan aircraft use pale green and simulator-only entries use light grey");
        check(rows.Single(r => r.LocalId == map.Id).Name == "ID T7" && rows.Single(r => r.LocalId == native.Id).CanFollow,
            "Unnamed MSFS positions appear by stable source ID and fresh native positions are followable");
        var label = rows.Single(r => r.Name == "Label only");
        check(!label.CanFollow && label.LocalId == null && label.Status.Contains("Name only") && label.Distance == "2 km",
            "HUD labels remain display-only even when they contain a distance or altitude");
        var stale = NearbyAircraft.Build(members, tracks, [native], labels, own, now.AddSeconds(3), self, lead, true, true);
        check(stale.Where(r => r.RelayId != null || r.LocalId != null).All(r => !r.CanFollow)
            && stale.Single(r => r.RelayId == lead).WingMan,
            "Stale positions disable following without changing source classification");
        var noFix = native with { Position = null, PositionAt = null };
        var before = NearbyAircraft.Build([], tracks, [noFix], [], own, now, self, "", false, true).Single();
        var after = NearbyAircraft.Build([], tracks, [native], [], own, now, self, "", false, true).Single();
        check(before.Key == after.Key && !before.CanFollow && after.CanFollow,
            "A future MSFS position update makes the same listed aircraft followable without changing its row identity");
        var away = native with { Id = new(true, 8), Position = FormationGeometry.Offset(own, 101, 0) };
        var low = native with { Id = new(true, 9), Position = native.Position! with { AltitudeFeet = 0 } };
        var ranged = NearbyAircraft.Build([], tracks, [away, low, noFix], [], own, now, self, "", false, true);
        check(!ranged.Any(r => r.LocalId == away.Id) && ranged.Any(r => r.LocalId == low.Id) && ranged.Any(r => r.LocalId == noFix.Id),
            "Nearby local list uses 100 NM, keeps missing positions inspectable, and has no hidden minimum-altitude filter");
        var disconnected = NearbyAircraft.Build(members, tracks, [native], [], own, now, self, lead, false, true);
        check(disconnected.Single(r => r.LocalId == native.Id).CanFollow && !disconnected.Single(r => r.RelayId == lead).CanFollow,
            "MSFS following does not require relay connectivity; disconnected WingMan telemetry cannot start following");
        check(NearbyAircraft.Build(members, tracks, [native], [], own, now, self, lead, true, false).All(r => !r.CanFollow),
            "Own simulator must be connected before any listed aircraft can start following");
    }
}
