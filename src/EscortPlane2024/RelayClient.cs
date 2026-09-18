using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace EscortPlane2024;

internal sealed record RelayOptions(string Server = "https://wingman.rogiermaas.nl/", string Room = "", string Name = "", string Id = "", string Token = "",
    bool Share = true, bool Reconnect = false, string Lead = "", bool AutomaticUpdates = true);
internal sealed record RelayMember(string Id, string Name, bool Share, string Lead, bool Online);
internal sealed record RelayTelemetry(long Sequence, long SampleTimeMs, double SampleAgeMs, double Latitude, double Longitude,
    double AltitudeFeet, double GroundSpeedKnots, double GroundTrackDegrees, double HeadingDegrees, double VerticalSpeedFpm,
    string Model, bool Paused, bool OnGround, double SimRate,
    string AircraftType = "", string AircraftModel = "", string Registration = "", string Livery = "")
{
    public bool Valid => Sequence > 0 && SampleTimeMs > 0 && new[] { SampleAgeMs, Latitude, Longitude, AltitudeFeet,
        GroundSpeedKnots, GroundTrackDegrees, HeadingDegrees, VerticalSpeedFpm, SimRate }.All(double.IsFinite)
        && SampleAgeMs is >= 0 and <= 2000 && Math.Abs(Latitude) <= 90 && Math.Abs(Longitude) <= 180
        && AltitudeFeet is >= -2000 and <= 100000 && GroundSpeedKnots is >= 0 and <= 1500
        && GroundTrackDegrees is >= 0 and < 360 && HeadingDegrees is >= 0 and < 360
        && Math.Abs(VerticalSpeedFpm) <= 20000 && new[] { Model, AircraftType, AircraftModel, Registration, Livery }.All(s => s != null && Encoding.UTF8.GetByteCount(s) <= 160) && SimRate is > 0 and <= 128;
}
internal sealed class RelayClient : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private CancellationTokenSource? lifetime;
    private Task? runner;
    private Channel<string>? outgoing;
    private readonly ConcurrentQueue<(string Json, DateTimeOffset At)> incoming = new();
    public string Status { get; private set; } = "Offline";
    public bool Running => lifetime != null;
    public bool Connected { get; private set; }
    public string Room { get; private set; } = "";
    public double ServerOffsetMs { get; private set; }
    public double RoundTripMs { get; private set; }
    public bool ClockReady { get; private set; }
    private double bestRtt = double.PositiveInfinity;
    public static Uri ServerUri(string text)
    {
        if (!Uri.TryCreate(text.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException("Enter an HTTPS server address, such as https://wingman.rogiermaas.nl (HTTP is allowed only on localhost).");
        return uri;
    }
    public void Start(RelayOptions options, bool create)
    {
        if (Running) throw new InvalidOperationException("Disconnect before changing server or pilot.");
        var server = ServerUri(options.Server);
        lifetime = new(); var token = lifetime.Token; Room = options.Room;
        runner = RunAsync(server, options, create, token);
    }
    private async Task RunAsync(Uri server, RelayOptions options, bool create, CancellationToken token)
    {
        var delay = 1;
        while (!token.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            var channel = Channel.CreateBounded<string>(64); outgoing = channel;
            try
            {
                Status = "Going online...";
                var uri = new UriBuilder(new Uri(server, "v1/session")) { Scheme = server.Scheme == "https" ? "wss" : "ws" };
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                { timeout.CancelAfter(TimeSpan.FromSeconds(8)); await ws.ConnectAsync(uri.Uri, timeout.Token).ConfigureAwait(false); }
                await Send(ws, new { type = "join", protocol = 1, create, publicRoom = !create && Room == "PUBLIC", room = Room, id = options.Id, token = options.Token,
                    name = options.Name, share = options.Share, echo = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, token).ConfigureAwait(false);
                var write = Task.Run(async () =>
                {
                    await foreach (var json in channel.Reader.ReadAllAsync(connection.Token))
                        await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, connection.Token);
                }, connection.Token);
                var heartbeat = Task.Run(async () =>
                {
                    while (!connection.IsCancellationRequested)
                    {
                        await channel.Writer.WriteAsync(JsonSerializer.Serialize(new { type = "ping", echo = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, Json), connection.Token);
                        await Task.Delay(1000, connection.Token);
                    }
                }, connection.Token);
                try
                {
                    var buffer = new byte[65536];
                    while (!token.IsCancellationRequested)
                    {
                        int length = 0; WebSocketReceiveResult frame;
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(10000);
                        do
                        {
                            if (length == buffer.Length) throw new InvalidDataException("Relay message too large");
                            frame = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), timeout.Token);
                            if (frame.MessageType == WebSocketMessageType.Close) throw new IOException(frame.CloseStatusDescription ?? "Relay closed the connection");
                            if (frame.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Expected relay JSON");
                            length += frame.Count;
                        } while (!frame.EndOfMessage);
                        var json = Encoding.UTF8.GetString(buffer, 0, length);
                        using var parsed = JsonDocument.Parse(json);
                        if (parsed.RootElement.GetProperty("type").GetString() == "welcome")
                        { Room = parsed.RootElement.GetProperty("room").GetString()!; create = false; delay = 1; }
                        if (incoming.Count >= 256) throw new IOException("Relay receiver fell behind; reconnecting");
                        incoming.Enqueue((json, DateTimeOffset.UtcNow));
                    }
                }
                finally
                {
                    connection.Cancel(); channel.Writer.TryComplete();
                    try { await Task.WhenAll(write, heartbeat).ConfigureAwait(false); } catch (Exception) { }
                }
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
            { Status = token.IsCancellationRequested ? "Disconnected" : "Connection lost: " + ex.Message; }
            finally
            {
                connection.Cancel(); outgoing = null;
                incoming.Enqueue(("{\"type\":\"disconnected\"}", DateTimeOffset.UtcNow));
            }
            if (token.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(delay), token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            delay = Math.Min(15, delay * 2);
        }
    }
    private static Task Send(ClientWebSocket ws, object data, CancellationToken token) => ws.SendAsync(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, Json)), WebSocketMessageType.Text, true, token);
    public bool Send(object data) => outgoing?.Writer.TryWrite(JsonSerializer.Serialize(data, Json)) == true;
    public void Drain(Action<JsonElement, DateTimeOffset> accept)
    {
        while (incoming.TryDequeue(out var item))
        {
            using var doc = JsonDocument.Parse(item.Json); var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "welcome": Connected = true; ClockReady = false; bestRtt = double.PositiveInfinity; Status = "Connected"; break;
                case "disconnected": Connected = false; ClockReady = false; break;
                case "pong":
                    var rtt = item.At.ToUnixTimeMilliseconds() - root.GetProperty("echo").GetInt64();
                    if (rtt is >= 0 and < 2000)
                    {
                        RoundTripMs = rtt;
                        if (rtt <= bestRtt) { bestRtt = rtt; ServerOffsetMs = root.GetProperty("serverTimeMs").GetInt64() + rtt / 2.0 - item.At.ToUnixTimeMilliseconds(); ClockReady = true; }
                    }
                    else ClockReady = false;
                    break;
            }
            accept(root, item.At);
        }
    }
    public void Stop()
    {
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null; Connected = false; ClockReady = false; Status = "Disconnected";
    }
    public async Task StopAsync() { Stop(); if (runner != null) await runner; while (incoming.TryDequeue(out _)) { } }
    public void Dispose() => Stop();
}

internal sealed class RelayTracks
{
    internal sealed record Track(string Id, string Name, RelayTelemetry Telemetry, DateTimeOffset At);
    private readonly Dictionary<string, Track> tracks = [];
    public IEnumerable<Track> All => tracks.Values;
    public void Clear() => tracks.Clear();
    public void Remove(string id) => tracks.Remove(id);
    public Track? Get(string id) => tracks.GetValueOrDefault(id);
    public static ContactId Contact(string id)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return new(false, BitConverter.ToUInt64(bytes) & 0x3fffffffffffffffUL, true);
    }
    public bool Accept(string id, string name, RelayTelemetry t, DateTimeOffset at, DateTimeOffset now)
    {
        if (!Guid.TryParseExact(id, "N", out _) || !t.Valid || t.Paused || t.SimRate != 1 || (now - at).TotalSeconds is < -0.25 or > 2) return false;
        if (tracks.TryGetValue(id, out var old) && (t.Sequence <= old.Telemetry.Sequence || t.SampleTimeMs <= old.Telemetry.SampleTimeMs || at <= old.At)) return false;
        tracks[id] = new(id, name, t, at); return true;
    }
    public IReadOnlyList<TrafficContact> Contacts(DateTimeOffset now) => tracks.Values.Where(t => now - t.At < TimeSpan.FromSeconds(20)).Select(t =>
        new TrafficContact(Contact(t.Id), t.Name, t.Telemetry.Model, new Position(t.Telemetry.Latitude, t.Telemetry.Longitude, t.Telemetry.AltitudeFeet), t.At, "Shared by pilot through formation relay")).ToArray();
}
