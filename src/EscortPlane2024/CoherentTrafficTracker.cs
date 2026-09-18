using System.Text.Json;

namespace EscortPlane2024;

// Coherent uId is an opaque traffic ID, never a SimConnect Object ID.
internal sealed record CoherentAircraft(ulong TrafficId, string Name, string Model, double Latitude, double Longitude,
    double RawAltitude, double Heading, bool IsOnGround, DateTimeOffset SourceTime, DateTimeOffset ReceivedAt);

internal sealed class CoherentTrafficTracker
{
    private readonly Dictionary<ulong, CoherentAircraft> aircraft = [];
    public IEnumerable<CoherentAircraft> Aircraft => aircraft.Values;
    public DateTimeOffset? LastMessageAt { get; private set; }
    public DateTimeOffset? LastQueryAt { get; private set; }
    public int? LastQueryCount { get; private set; }
    public string Status { get; private set; } = "Waiting for experimental Coherent bridge";
    public int SampleCount { get; private set; }
    public CoherentAircraft? Accept(string json, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported bridge version");
        var sourceTime = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("sourceTimestampMs").GetInt64());
        if ((now - sourceTime).TotalSeconds is < -5 or > 5) throw new InvalidDataException("Stale or future bridge sample");
        var kind = root.GetProperty("kind").GetString();
        if (kind == "heartbeat")
        {
            var count = root.GetProperty("count").GetInt32();
            if (count is < 0 or > 512) throw new InvalidDataException("Invalid traffic count");
            LastMessageAt = now; LastQueryAt = now; LastQueryCount = count;
            Status = $"Coherent bridge active; latest query returned {count} contacts";
            return null;
        }
        if (kind != "aircraft") throw new InvalidDataException("Unknown bridge message");
        var value = root.GetProperty("aircraft");
        var id = value.GetProperty("uId").GetUInt64();
        if (id > 9007199254740991) throw new InvalidDataException("Traffic ID exceeds exact JavaScript integer range");
        var name = value.GetProperty("name").GetString() ?? "";
        var model = value.TryGetProperty("plane_model_icao", out var modelValue) ? modelValue.GetString() ?? "" : "";
        var lat = value.GetProperty("lat").GetDouble(); var lon = value.GetProperty("lon").GetDouble();
        var alt = value.GetProperty("alt").GetDouble(); var heading = value.GetProperty("heading").GetDouble();
        if (name.Length > 256 || model.Length > 128 || !double.IsFinite(lat) || Math.Abs(lat) > 90
            || !double.IsFinite(lon) || Math.Abs(lon) > 180 || !double.IsFinite(alt) || !double.IsFinite(heading))
            throw new InvalidDataException("Invalid traffic payload");
        if (aircraft.TryGetValue(id, out var previous) && sourceTime <= previous.SourceTime) return null;
        var sample = new CoherentAircraft(id, name, model, lat, lon, alt, heading,
            value.GetProperty("isOnGround").GetBoolean(), sourceTime, now);
        aircraft[id] = sample; LastMessageAt = now; SampleCount++;
        return sample;
    }
    public void Expire(DateTimeOffset now)
    {
        foreach (var id in aircraft.Where(pair => now - pair.Value.ReceivedAt > TimeSpan.FromSeconds(20)).Select(pair => pair.Key).ToArray()) aircraft.Remove(id);
        if (LastMessageAt is { } last && now - last > TimeSpan.FromSeconds(5)) Status = "Coherent bridge data delayed / unavailable";
    }
    public string ListStatus(DateTimeOffset now, int visibleCount, double radiusNm)
    {
        if (LastQueryAt == null) return "Waiting for MSFS to send its aircraft list…";
        if (now - LastQueryAt > TimeSpan.FromSeconds(5)) return "MSFS aircraft updates are delayed. Listed positions may be out of date.";
        if (LastQueryCount == 0) return "MSFS is reporting no aircraft to the app.\nVisible player nameplates can still appear in the simulator.";
        var named = aircraft.Values.Count(a => !string.IsNullOrWhiteSpace(a.Name) && now - a.ReceivedAt <= TimeSpan.FromSeconds(5));
        if (named == 0) return $"MSFS reports {LastQueryCount} objects, but none has a player name.\nUnnamed objects can include ground vehicles.";
        return $"{visibleCount} listed · {named} recent named aircraft · radius {radiusNm:0} NM";
    }
    public void Clear() { aircraft.Clear(); LastMessageAt = null; LastQueryAt = null; LastQueryCount = null; Status = "Waiting for experimental Coherent bridge"; }
}
