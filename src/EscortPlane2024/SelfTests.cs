using System.Runtime.InteropServices;

namespace EscortPlane2024;

internal static class SelfTests
{
    public static int Run(Diagnostics log)
    {
        var checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException($"Self-test failed: {name}");
            checks++; log.Write("test_passed", new { name });
        }
        // Offsets calculated from SDK 1.7.3 SimConnect.h; packet size also checked against live CameraGet responses.
        Check(Marshal.SizeOf<NativeSimConnect.CameraData>() == 84, "Camera structure is 84 bytes");
        foreach (var (name, offset) in new[] { ("PositionReferential",24), ("PositionObjectId",28), ("TargetX",32), ("Pitch",56), ("RotationReferential",68), ("RotationObjectId",72), ("Fov",76) })
            Check(Marshal.OffsetOf<NativeSimConnect.CameraData>(name).ToInt32() == offset, $"Camera ABI offset {name}");
        Check(new Position(0, 32, 1000).IsValid, "Equator latitude is valid");
        Check(!new Position(double.NaN, 32, 1000).IsValid, "NaN latitude rejected");
        Check(!new Position(91, 32, 1000).IsValid, "Out-of-range latitude rejected");
        Check(new Position(0, 179.99, 0).DistanceNm(new(0, -179.99, 0)) is > 1.19 and < 1.21, "Dateline distance takes short arc");
        Check(new Position(90, 0, 0).DistanceNm(new(90, 180, 0)) < 0.00001, "Pole longitude convergence");
        var tracker = new TrafficTracker();
        tracker.Seen(123, "A321", "Aircraft"); tracker.SetPosition(123, new(1, 2, 3000));
        tracker.Seen(123, "A321", "Exposed object (ALL)");
        Check(tracker.Aircraft.Single().Kind == "Aircraft", "Broad scan preserves specific aircraft classification");
        tracker.Expire(DateTimeOffset.UtcNow.AddSeconds(21));
        Check(!tracker.Aircraft.Any(), "Stale object expires");
        var now = DateTimeOffset.UtcNow;
        var bridge = new CoherentTrafficTracker();
        string Sample(double latitude, long? timestamp = null) => System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1, kind = "aircraft", sourceTimestampMs = timestamp ?? now.ToUnixTimeMilliseconds(),
            aircraft = new { uId = 626866296, name = "TrymTube", plane_model_icao = "A359 ULR", lat = latitude, lon = -122.3, alt = 8.6, heading = 297.3, isOnGround = true }
        });
        Check(bridge.Accept(Sample(0), now)?.Name == "TrymTube", "Coherent name and equator position retained");
        Check(bridge.Accept(Sample(1), now) == null && bridge.SampleCount == 1, "Duplicate source timestamp does not refresh a sample");
        void Reject(string json, string name)
        {
            bool rejected = false;
            try { bridge.Accept(json, now); } catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { rejected = true; }
            Check(rejected, name);
        }
        Reject(Sample(91, now.AddSeconds(1).ToUnixTimeMilliseconds()), "Coherent invalid latitude rejected");
        Reject(Sample(1, now.AddSeconds(-6).ToUnixTimeMilliseconds()), "Stale bridge payload rejected");
        Reject(Sample(1, now.AddSeconds(6).ToUnixTimeMilliseconds()), "Future bridge payload rejected");
        Reject("{", "Malformed bridge JSON rejected");
        bridge.Expire(now.AddSeconds(21));
        Check(!bridge.Aircraft.Any(), "Stale Coherent aircraft expires");
        Check(TargetSearch.Match("trymtube") == "TrymTube", "Case-insensitive focused gamertag match");
        bridge.Clear();
        Check(bridge.ListStatus(now, 0, 20).StartsWith("Waiting"), "No traffic response is distinct from an empty response");
        string Heartbeat(int count) => System.Text.Json.JsonSerializer.Serialize(new { version = 1, kind = "heartbeat", sourceTimestampMs = now.ToUnixTimeMilliseconds(), count });
        bridge.Accept(Heartbeat(0), now);
        Check(bridge.ListStatus(now, 0, 20).Contains("reporting no aircraft"), "Empty simulator list is explained before UI filtering");
        Check(bridge.ListStatus(now.AddSeconds(6), 0, 20).Contains("delayed"), "Stopped traffic query is not shown as a healthy empty list");
        bridge.Accept(Heartbeat(3), now);
        Check(bridge.ListStatus(now, 0, 20).Contains("none has a player name"), "Unnamed objects are not presented as confirmed multiplayer aircraft");
        Reject(Heartbeat(-1), "Negative traffic count rejected");
        bridge.Accept(Sample(0), now);
        Check(bridge.ListStatus(now, 0, 20).Contains("1 recent named aircraft"), "Available named traffic is distinguished from radius filtering");
        bridge.Clear();
        Check(bridge.LastQueryAt == null && bridge.LastQueryCount == null, "Disconnect clears traffic query diagnostics");
        var motion = new TargetEstimator("TrymTube");
        CoherentAircraft Moving(int second, double longitude, ulong id = 123, string name = "TrymTube") =>
            new(id, name, "A359 ULR", 0, longitude, 8.6, 200, true, now.AddSeconds(second), now.AddSeconds(second));
        Check(motion.Observe(Moving(0, 0, name: "NotTrymTube")) != null && motion.TargetId == null, "Diagnostic target requires exact name");
        for (int second = 0; second < 6; second++) motion.Observe(Moving(second, second * 10 / (3440.065 * 1852) * 180 / Math.PI));
        Check(motion.GroundSpeedKnots is > 19.43 and < 19.45 && motion.GroundTrackDegrees is > 89.99 and < 90.01, "Motion measured from positions rather than nose heading");
        Check(motion.Observe(Moving(6, 1)) == "implausible horizontal jump" && motion.AcceptedSamples == 6, "Teleport rejected without contaminating target history");
        Check(motion.Observe(Moving(6, 0, id: 124))?.StartsWith("target identity changed") == true && motion.TargetId == 123, "Same name cannot silently replace target ID");
        Check(motion.StatusAt(now.AddSeconds(8)).StartsWith("DATA HOLD") && motion.StatusAt(now.AddSeconds(11)) == "TARGET LOST", "Rejected target data ages through hold and lost states");
        motion.Observe(Moving(12, 1));
        Check(motion.GroundSpeedKnots == null && motion.StatusAt(now.AddSeconds(12)) == "ACQUIRING MOTION", "Long gap starts fresh motion acquisition");
        motion.Reset();
        for (int second = 0; second < 6; second++) motion.Observe(Moving(second, 1));
        Check(motion.GroundSpeedKnots < 0.01 && motion.GroundTrackDegrees == null, "Stationary target has no fabricated ground track");
        motion.Reset();
        for (int second = 0; second < 6; second++)
        {
            var longitude = 179.9999 + second * 10 / (3440.065 * 1852) * 180 / Math.PI;
            motion.Observe(Moving(second, longitude > 180 ? longitude - 360 : longitude));
        }
        Check(motion.GroundSpeedKnots is > 19.43 and < 19.45 && motion.GroundTrackDegrees is > 89.99 and < 90.01, "Motion estimate crosses dateline without a false reversal");
        var genericArm = new AutopilotReadback(250, .78, 280, 36000, 0, false, true, true, false, now);
        var stock787Arm = StandardAutopilot.ResolveAutothrottle(genericArm, "787-10", true, 1);
        Check(stock787Arm.AtArmed && stock787Arm.AutothrottleStatus == "Autothrottle armed.", "787 cockpit arm state overrides false generic flags");
        Check(!StandardAutopilot.ResolveAutothrottle(genericArm with { AtArmed = true }, "787-10", true, 0).AtArmed, "787 disarmed switch overrides a stale true generic flag");
        Check(!StandardAutopilot.ResolveAutothrottle(genericArm, "Cessna 172", true, 1).AtArmed
            && !StandardAutopilot.ResolveAutothrottle(genericArm, "Unknown 787 addon", false, 1).AtArmed, "Unrelated or unidentified aircraft cannot inherit the Boeing L-var");
        Check(StandardAutopilot.ResolveAutothrottle(genericArm, "787-10", false, 1).AtArmed,
            "Stock 787 A/T indication works without the optional traffic diagnostics bridge");
        Check(!StandardAutopilot.ResolveAutothrottle(genericArm, "787-10", true, double.NaN).AtArmKnown
            && !StandardAutopilot.ResolveAutothrottle(genericArm, "787-10", true, 2).AtArmKnown, "Invalid Boeing arm data is unknown, not a definite disarmed indication");
        Check(genericArm.AutothrottleStatus.Contains("does not report"), "Generic zero readback is described as reported status, not proven cockpit switch state");
        FollowerTests.Run(log, Check);
        ReacquisitionTests.Run(log, Check);
        VerticalModeTests.Run(log, Check);
        CatchUpTests.Run(log, Check);
        OrbitTests.Run(log, Check);
        ContinuationTests.Run(log, Check);
        NearbyAircraftTests.Run(Check);
        WindowPinningTests.Run(Check);
        TrafficMapTests.Run(Check);
        NameplateTests.Run(log, Check);
        NetworkTests.Run(Check);
        RuntimeTests.Run(Check);
        log.Write("self_test_summary", new { checks, passed = true });
        return 0;
    }
}
