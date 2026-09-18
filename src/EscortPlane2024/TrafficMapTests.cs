namespace EscortPlane2024;

internal static class TrafficMapTests
{
    internal static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var own = new Position(51, 6, 4000);
        var trafficId = new ContactId(false, 123);
        var objectId = new ContactId(true, 123);
        check(trafficId != objectId && trafficId.TrackingId != objectId.TrackingId, "Equal API IDs remain separate targets");
        check(new ContactId(true, uint.MaxValue).TrackingId > 9007199254740991UL, "Native IDs cannot collide with any accepted Coherent ID");
        check(TrafficContacts.PositionIssue(new(0, 0, 0), own) != null, "Zero placeholder cannot become a map marker");
        check(TrafficContacts.PositionIssue(own, own) != null, "FakeSim own-position echo cannot become a follow target");
        check(TrafficContacts.PositionIssue(new(0, 6, 1000), own) == null, "Equator traffic remains usable");
        check(TrafficContacts.PositionIssue(new(double.NaN, 6, 1000), own) != null, "Invalid native position rejected");
        var tracker = new TrafficTracker(); tracker.Seen(123, "", "Aircraft");
        tracker.SetPosition(123, FormationGeometry.Offset(own, 3, 5));
        tracker.Seen(456, "Truck", "Exposed object (ALL)"); tracker.SetPosition(456, FormationGeometry.Offset(own, 1, 1));
        var catalog = TrafficContacts.Build([new(123, "", "", 51.1, 6.1, 1400, 0, false, now, now)], tracker.Aircraft, own);
        check(catalog.Count == 2 && catalog.Any(c => c.Id == objectId) && catalog.Any(c => c.Id == trafficId), "Unnamed contacts retained while known non-aircraft native objects are excluded");
        var native = catalog.Single(c => c.Id == objectId);
        var motion = native.MotionSample(DateTimeOffset.UtcNow)!;
        check(Math.Abs(motion.RawAltitude / 0.3048 - native.Position!.AltitudeFeet) < 0.001, "Native feet normalized to estimator meters exactly once");
        var estimator = new TargetEstimator("S123"); estimator.LockIdentity(objectId.TrackingId, true);
        check(estimator.Observe(motion) == null, "ID-locked target accepts an empty name");
        check(estimator.Observe(motion with { Name = "Name later supplied", SourceTime = motion.SourceTime.AddSeconds(1) }) == null, "Name changes do not interrupt an ID-locked target");
        check(estimator.Observe(motion with { TrafficId = trafficId.TrackingId, SourceTime = motion.SourceTime.AddSeconds(2) })?.StartsWith("target identity changed") == true, "Another API cannot impersonate a selected native target");
        check(native.MotionSample(now.AddSeconds(10)) == null, "Stale map selection cannot start tracking");
        tracker.RejectPosition(123, "MSFS returned zeros");
        var unavailable = TrafficContacts.Build([], tracker.Aircraft, own).Single();
        check(unavailable.Position == null && unavailable.MotionSample(now) == null, "Invalid update removes old position and prevents selection for following");
        var boundary = native with { Position = new(51, 6, 1000) };
        check(TrafficContacts.AboveMinimum(boundary, 1000) && !TrafficContacts.AboveMinimum(boundary with { Position = new(51, 6, 999) }, 1000), "Altitude cutoff includes exactly 1000 ft and excludes lower contacts");
        check(TrafficContacts.AboveMinimum(boundary with { Position = new(51, 6, -100) }, 0), "Zero cutoff also restores below-sea-level contacts");
        check(TrafficContacts.AboveMinimum(unavailable, 1000) && unavailable.MotionSample(now) == null, "Unknown altitude remains listed without becoming a followable marker");
        var center = new PointF(100, 100);
        var north = TrafficMapControl.Project(own, FormationGeometry.Offset(own, 10, 0), center, 100, 20);
        var east = TrafficMapControl.Project(own, FormationGeometry.Offset(own, 0, 10), center, 100, 20);
        check(Math.Abs(north.Y - 50) < 0.1 && Math.Abs(east.X - 150) < 0.1, "Range rings project north up and east right at the correct scale");
        var dateline = TrafficMapControl.Project(new(0, 179.99, 0), new(0, -179.99, 0), center, 100, 20);
        check(dateline.X > 100 && dateline.X < 110, "Map uses short dateline crossing");

        using var map = new TrafficMapControl { Size = new(540, 245), Font = new("Segoe UI", 10) };
        var plotted = new TrafficContact[] {
            new(objectId, "", "A320", FormationGeometry.Offset(own, 8, 4), now, "Aircraft"),
            new(trafficId, "Test pilot", "B738", FormationGeometry.Offset(own, -8, -6), now, "Map traffic"),
            new(new(true, 234567890), "", "", FormationGeometry.Offset(own, 2, -12), now.AddSeconds(-8), "Stale"),
            unavailable with { Id = new(true, 999) }
        };
        map.UpdateTraffic(new(own, 200, 45, 190, 0, 2, now), plotted, 20, objectId);
        using var bitmap = new Bitmap(map.Width, map.Height);
        map.DrawToBitmap(bitmap, map.ClientRectangle);
        // Same center/scale as the rendered control; assert actual hit regions from paint, not just projection.
        var marker = TrafficMapControl.Project(own, plotted[0].Position!, new(map.Width / 2f, (map.Height - 25) / 2f), (map.Height - 65) / 2f, 20);
        ContactId? clicked = null; map.ContactClicked += c => clicked = c.Id;
        map.SelectAt(Point.Round(marker));
        check(clicked == objectId, "Clicking a rendered native marker selects its source-qualified ID");
        check(map.HitTest(new(0, 0)) == null, "Blank map area does not select a target");
        using var artifact = new Bitmap(map.Width, map.Height + 25);
        using (var graphics = Graphics.FromImage(artifact))
        {
            graphics.Clear(Color.White); graphics.DrawString("SYNTHETIC MAP TEST — not live traffic", map.Font, Brushes.DarkRed, 4, 2);
            graphics.DrawImageUnscaled(bitmap, 0, 25);
        }
        Directory.CreateDirectory("artifacts"); artifact.Save("artifacts/traffic-map-synthetic.png");
    }
}
