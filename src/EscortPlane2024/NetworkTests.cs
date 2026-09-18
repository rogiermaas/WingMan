using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;

namespace EscortPlane2024;

internal static class NetworkTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N"); var tracks = new RelayTracks();
        var sample = new RelayTelemetry(1, now.ToUnixTimeMilliseconds(), 0, 52, 5, 10000, 250, 90, 95, 0, "Boeing 787", false, false, 1,
            "Boeing", "B78X", "PH-TEST", "Test livery");
        check(tracks.Accept(id, "Lead", sample, now, now), "Relay accepts fresh GPS telemetry with aircraft details");
        var agl = sample with { AboveGroundFeet = 800 };
        check(JsonSerializer.Deserialize<RelayTelemetry>(JsonSerializer.Serialize(agl, RelayClient.Json), RelayClient.Json)?.AboveGroundFeet == 800
            && sample.AboveGroundFeet == null && sample.Valid, "Optional AGL round trips while older telemetry remains compatible");
        check(!(sample with { AboveGroundFeet = double.NaN }).Valid && !(sample with { AboveGroundFeet = -2000 }).Valid,
            "Invalid lead height cannot enter circling guidance");
        check(!tracks.Accept(id, "Lead", sample, now, now), "Relay rejects duplicate sequences");
        check(!tracks.Accept(id, "Lead", sample with { Sequence = 2, SampleTimeMs = sample.SampleTimeMs + 1 }, now.AddSeconds(-3), now), "Delayed network positions cannot become fresh");
        check(!tracks.Accept(id, "Lead", sample with { Sequence = 2, SampleTimeMs = sample.SampleTimeMs + 1, Paused = true }, now.AddMilliseconds(1), now), "Paused leads cannot update a live track");
        check(!tracks.Accept(id, "Lead", sample with { Sequence = 2, SampleTimeMs = sample.SampleTimeMs + 1, SimRate = 2 }, now.AddMilliseconds(1), now), "Accelerated simulator data is rejected");
        check(!(sample with { Latitude = double.NaN }).Valid && !(sample with { Livery = new string('x', 161) }).Valid, "Telemetry rejects invalid numbers and oversized metadata");
        check(RelayTracks.Contact(id).TrackingId != new ContactId(false, RelayTracks.Contact(id).Value).TrackingId
            && RelayTracks.Contact(id).TrackingId != new ContactId(true, RelayTracks.Contact(id).Value).TrackingId, "Relay IDs do not overlap native traffic IDs");
        check(tracks.Contacts(now)[0].Position!.AltitudeFeet == 10000, "Relay altitude uses feet consistently");
        check(RelayClient.ServerUri("https://wingman.rogiermaas.nl").AbsoluteUri == "https://wingman.rogiermaas.nl/", "Default server URL resolves endpoint paths");
        check(Throws(() => RelayClient.ServerUri("http://example.com")) && Throws(() => RelayClient.ServerUri("https://user:pass@example.com")), "External relay URLs require HTTPS and no embedded credentials");
        var motion = new TargetEstimator("Lead") { FastTelemetry = true };
        var origin = new Position(52, 5, 10000);
        for (int i = 0; i < 10; i++)
        {
            var at = now.AddMilliseconds(i * 100); var position = FormationGeometry.Offset(origin, 0, 250 * (i * .1) / 3600, 0);
            motion.Observe(new(123, "Lead", "787", position.Latitude, position.Longitude, 10000 * .3048, 95, false, at, at));
        }
        check(motion.GroundSpeedKnots is > 249 and < 251 && motion.GroundTrackDegrees is > 89 and < 91, "Ten-Hz telemetry acquires real ground motion in under a second");
        check(motion.Observe(new(123, "Lead", "787", 55, 5, 3048, 95, false, now.AddSeconds(1), now.AddSeconds(1))) != null, "Fast telemetry still rejects teleports");
        using var rsa = RSA.Create(2048); var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        var manifest = new ReleaseManifest("1.2.0", 1, "WingMan-1.2.0-win-x64.zip", new string('a', 64), 1234, now.ToString("O"));
        SignedRelease Sign(ReleaseManifest m)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(m, RelayClient.Json);
            return new(Convert.ToBase64String(payload), Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
        }
        var valid = Sign(manifest);
        check(ApplicationUpdates.Verify(valid, publicKey).Manifest.Version == "1.2.0", "Publisher signature accepted");
        check(Throws(() => ApplicationUpdates.Verify(valid with { Payload = Convert.ToBase64String("tampered"u8.ToArray()) }, publicKey)), "Modified update metadata rejected");
        check(Throws(() => ApplicationUpdates.Verify(Sign(manifest with { File = "../../evil.zip" }), publicKey)), "Signed filename cannot traverse directories");
        using var stranger = RSA.Create(2048);
        check(Throws(() => ApplicationUpdates.Verify(valid, stranger.ExportSubjectPublicKeyInfoPem())), "Unknown publishers rejected");
        var dir = Path.Combine(Path.GetTempPath(), "WingMan-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var zip = Path.Combine(dir, "test.zip"); using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("../WingMan.exe").Open()); writer.Write("unsafe"); }
            check(Throws(() => ApplicationUpdates.ExtractExecutable(zip, Path.Combine(dir, "out.exe"))), "Update archive rejects traversal entries");
        }
        finally { Directory.Delete(dir, true); }
    }
    private static bool Throws(Action action) { try { action(); return false; } catch { return true; } }

    public static async Task<int> Integration(string server, Diagnostics log, bool publicDiscovery = false)
    {
        using var lead = new RelayClient(); using var follower = new RelayClient();
        var leadId = Guid.NewGuid().ToString("N"); var followerId = Guid.NewGuid().ToString("N"); var tracks = new RelayTracks();
        lead.Start(new(Server: server, Room: publicDiscovery ? "PUBLIC" : "", Name: "Lead test", Id: leadId, Token: new string('a', 32)), !publicDiscovery);
        var until = DateTimeOffset.UtcNow.AddSeconds(15); var joined = false; var configured = false; long sequence = 0; int received = 0; double maxAge = 0;
        var next = DateTimeOffset.MinValue; var began = DateTimeOffset.UtcNow;
        try
        {
            while (DateTimeOffset.UtcNow < until && received < 20)
            {
                lead.Drain((m, at) => { if (m.GetProperty("type").GetString() == "error") throw new Exception(m.ToString()); });
                if (lead.Connected && !joined) { follower.Start(new(Server: server, Room: lead.Room, Name: "Follower test", Id: followerId, Token: new string('b',32), Share: false), false); joined = true; }
                follower.Drain((m, at) =>
                {
                    var type = m.GetProperty("type").GetString();
                    if (type == "error") throw new Exception(m.ToString());
                    if (type == "telemetry" && follower.ClockReady)
                    {
                        var sample = m.GetProperty("telemetry").Deserialize<RelayTelemetry>(RelayClient.Json)!;
                        var time = DateTimeOffset.FromUnixTimeMilliseconds((long)(m.GetProperty("serverTimeMs").GetInt64() - follower.ServerOffsetMs));
                        if (!tracks.Accept(leadId, "Lead test", sample, time, DateTimeOffset.UtcNow)) throw new Exception("Client rejected fresh sample");
                        maxAge = Math.Max(maxAge, (DateTimeOffset.UtcNow - time).TotalMilliseconds); received++;
                    }
                });
                if (follower.Connected && !configured) { follower.Send(new { type = "configure", share = false, lead = leadId }); configured = true; }
                var now = DateTimeOffset.UtcNow;
                if (lead.Connected && follower.ClockReady && now >= next)
                {
                    next = now.AddMilliseconds(100); var p = FormationGeometry.Offset(new(52,5,10000),0,250*(now-began).TotalSeconds/3600,0);
                    lead.Send(new { type = "telemetry", telemetry = new RelayTelemetry(++sequence,now.ToUnixTimeMilliseconds(),0,p.Latitude,p.Longitude,10000,250,90,95,0,"787",false,false,1) });
                }
                await Task.Delay(10);
            }
            log.Write("relay_integration_summary", new { passed = received == 20, publicDiscovery, received, maxAge, follower.RoundTripMs });
            return received == 20 ? 0 : 2;
        }
        finally { await lead.StopAsync(); await follower.StopAsync(); }
    }
}
