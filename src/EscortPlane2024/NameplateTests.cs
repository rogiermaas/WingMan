using System.Text.Json;

namespace EscortPlane2024;

internal static class NameplateTests
{
    public static void Run(Diagnostics log, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        using (var list = new StableTrafficListView { View = View.Details })
        {
            list.Columns.Add("Name");
            _ = list.Handle; // Exercise native ListView sorting, as in the visible app.
            var first = new ListViewItem("Alpha") { Tag = "retained identity" };
            var second = new ListViewItem("Beta");
            list.Items.AddRange([first, second]);
            list.ListViewItemSorter = new TrafficListSorting(list, TrafficColumn.Text);
            list.Sort();
            check(!list.SortIfNeeded(), "Unchanged list ordering does not trigger a native re-sort");
            StableTrafficListView.SetText(first, 0, "Zulu");
            check(list.SortIfNeeded() && ReferenceEquals(list.Items[1], first) && (string)first.Tag == "retained identity",
                "Changing a sort key reorders existing row objects without clearing or recreating them");
            StableTrafficListView.SetText(first, 0, "Zulu");
            check(!list.SortIfNeeded() && ReferenceEquals(list.Items[0], second), "Repeated display refresh preserves unchanged row identity and order");
        }
        check(TrafficListSorting.CompareValues("Alpha", "beta", TrafficColumn.Text, false) < 0
            && TrafficListSorting.CompareValues("Alpha", "beta", TrafficColumn.Text, true) > 0, "Column text sorting reverses direction and ignores case");
        check(TrafficListSorting.CompareValues("9", "100", TrafficColumn.Number, false) < 0, "Altitude columns sort numerically rather than alphabetically");
        check(TrafficListSorting.CompareValues("2 km", "1.5 NM", TrafficColumn.Distance, false) < 0, "Mixed kilometre and nautical-mile distances sort in common units");
        check(Math.Abs(TrafficListSorting.NumericValue("24,42 km", TrafficColumn.Distance)!.Value - 24.42 / 1.852) < 0.001
            && TrafficListSorting.NumericValue("3,564.15 ft", TrafficColumn.Number) == 3564.15
            && TrafficListSorting.NumericValue("3.564,15 ft", TrafficColumn.Number) == 3564.15,
            "Localized decimal and thousands separators retain numeric sorting");
        check(TrafficListSorting.CompareValues("—", "100", TrafficColumn.Number, false) > 0
            && TrafficListSorting.CompareValues("—", "100", TrafficColumn.Number, true) > 0, "Unknown numeric values sort last in both directions");
        var fix = new TrafficContact(new(false, 321), "TrymTube", "A321", new(51, 2, 10000), now, "Map traffic");
        foreach (var onGround in new[] { true, false })
        {
            var raw = new CoherentAircraft(321, "Departure test", "B738", 51, 2, 30, 90, onGround, now, now);
            var catalog = TrafficContacts.Build([raw], [], null);
            check(TrafficContacts.NamedFix("Departure test", catalog, now) != null,
                $"Ground flag {onGround} cannot remove a fresh name fix at takeoff");
        }
        check(TrafficContacts.NamedFix("trymtube", [fix], now) == fix, "HUD names resolve to a fresh position case-insensitively");
        check(TrafficContacts.NamedFix(" TrymTube ", [fix], now) == fix, "Name matching trims display whitespace");
        check(TrafficContacts.NamedFix("TrymTube", [fix], now.AddSeconds(3)) == null, "Green name fixes expire after two seconds without updates");
        check(TrafficContacts.NamedFix("TrymTube", [fix with { Position = null }], now) == null, "A name without coordinates never gets a green position fix");
        check(TrafficContacts.NamedFix("TrymTube", [fix, fix with { Id = new(true, 654) }], now) == null, "Ambiguous live names require choosing an aircraft ID");
        check(TrafficContacts.NamedFix("TrymTube", [fix, fix with { Id = new(true, 654), PositionAt = now.AddSeconds(-3) }], now) == fix,
            "An expired ID does not hide a unique live named fix");
        var nativeFix = fix with { Id = new(true, 321) };
        var watched = new TargetEstimator("TrymTube");
        watched.LockIdentity(TrafficContacts.NamedFix("TrymTube", [nativeFix], now)!.Id.TrackingId, true);
        watched.Observe(nativeFix.MotionSample(now)!);
        check(watched.Latest != null && watched.TargetId == nativeFix.Id.TrackingId && watched.MatchById,
            "Watching a resolved native callsign locks its separate SimConnect ID and accepts position data");
        object Label(string id, string name) => new { labelId = id, name, model = "A321", aircraftType = "Airbus A321", distance = "24,42 km", altitude = "3,564.15 ft" };
        string Packet(object[] labels, long? at = null, int version = 1) => JsonSerializer.Serialize(new { version, readAt = at ?? now.ToUnixTimeMilliseconds(), contacts = labels });
        var parsed = NameplateSession.Parse(Packet([Label("UI-1", "Daniël33#3652")]), now);
        check(parsed.Single().Name == "Daniël33#3652" && parsed.Single().Distance == "24,42 km" && parsed.Single().Altitude == "3,564.15 ft", "Nameplate Unicode and localized display units retained without inventing a position");
        check(NameplateSession.Parse(Packet([Label("UI-1", "Old"), Label("UI-1", "Updated")]), now).Single().Name == "Updated", "Duplicate HUD label IDs do not duplicate rows");
        check(NameplateSession.Parse(Packet([Label("", "Pilot"), Label("UI-1", "")]), now).Count == 0, "Missing HUD identity or name is not offered as a player");
        void Reject(string json, string description)
        {
            var rejected = false;
            try { NameplateSession.Parse(json, now); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
            check(rejected, description);
        }
        Reject(Packet([Label("UI-1", "Pilot")], now.AddSeconds(-6).ToUnixTimeMilliseconds()), "Stale nameplate snapshot rejected");
        Reject(Packet([Label("UI-1", "Pilot")], version: 2), "Unknown nameplate format rejected");
        Reject(Packet([Label("UI-1", "Pilot\nInjected row")]), "Control characters cannot create misleading nameplate rows");
        Reject(Packet(Enumerable.Range(0, 513).Select(i => Label("UI-" + i, "Pilot")).ToArray()), "Nameplate response is bounded to 512 contacts");
        var session = new FollowerTests.Session(now);
        session.Focus.Reset(); // A nameplate alone supplies no motion history.
        var controller = new FollowerController(session, log);
        controller.Engage(now); controller.Tick(now);
        check(!controller.Active && session.Outputs.Count == 0, "A nameplate without positions cannot start autopilot outputs");
    }
}
