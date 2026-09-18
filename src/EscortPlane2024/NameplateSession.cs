using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;

namespace EscortPlane2024;

// Display-only fallback. Reading a HUD label does not provide a navigable position or a SimObject ID.
internal sealed record NameplateContact(string LabelId, string Name, string Model, string AircraftType, string Distance, string Altitude);
internal sealed class NameplateSession : IDisposable
{
    private ClientWebSocket? socket;
    private int request;
    public IReadOnlyList<NameplateContact> Contacts { get; private set; } = [];
    public DateTimeOffset? ReadAt { get; private set; }
    public string Status { get; private set; } = "Waiting for MSFS nameplates";
    public async Task PollAsync()
    {
        try
        {
            if (socket?.State != WebSocketState.Open)
            {
                socket?.Dispose(); socket = new();
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var pages = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:19999/pagelist.json"));
                var atlas = pages.RootElement.EnumerateArray().FirstOrDefault(p => p.GetProperty("url").GetString()?.EndsWith("/Global/atlas.html", StringComparison.OrdinalIgnoreCase) == true);
                if (atlas.ValueKind == JsonValueKind.Undefined) throw new IOException("MSFS nameplate view was not found");
                using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await socket.ConnectAsync(new($"ws://127.0.0.1:19999/devtools/page/{atlas.GetProperty("id").GetInt32()}"), connectTimeout.Token);
            }
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("coherent-nameplates-read.js")!;
            using var reader = new StreamReader(stream);
            var script = await reader.ReadToEndAsync();
            var id = ++request;
            var packet = JsonSerializer.SerializeToUtf8Bytes(new { id, method = "Runtime.evaluate", @params = new { expression = script, returnByValue = true, objectGroup = "escort-nameplates" } });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Text, true, timeout.Token);
            var buffer = new byte[8192];
            while (true)
            {
                using var message = new MemoryStream(); WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                    if (received.MessageType == WebSocketMessageType.Close) throw new IOException("MSFS closed the nameplate view");
                    message.Write(buffer, 0, received.Count);
                    if (message.Length > 1024 * 1024) throw new InvalidDataException("Oversized nameplate response");
                } while (!received.EndOfMessage);
                using var response = JsonDocument.Parse(message.ToArray());
                var root = response.RootElement;
                if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
                if (root.TryGetProperty("error", out _)) throw new IOException("Nameplate inspector request failed");
                var result = root.GetProperty("result");
                if (result.TryGetProperty("wasThrown", out var thrown) && thrown.GetBoolean()) throw new IOException("MSFS nameplate data is unavailable in this view");
                Contacts = Parse(result.GetProperty("result").GetProperty("value").GetString()!, DateTimeOffset.UtcNow);
                ReadAt = DateTimeOffset.UtcNow; Status = $"{Contacts.Count} nameplates read from MSFS";
                return;
            }
        }
        catch (Exception ex)
        {
            Dispose(); Contacts = []; ReadAt = null; Status = "Nameplates unavailable: " + ex.Message;
            throw;
        }
    }
    internal static IReadOnlyList<NameplateContact> Parse(string json, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 6 });
        var root = document.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unknown nameplate format");
        var time = DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("readAt").GetInt64());
        if (Math.Abs((now - time).TotalSeconds) > 5) throw new InvalidDataException("Nameplate read is out of date");
        var array = root.GetProperty("contacts");
        if (array.GetArrayLength() > 512) throw new InvalidDataException("Too many nameplates");
        var contacts = new Dictionary<string, NameplateContact>();
        foreach (var item in array.EnumerateArray())
        {
            string Field(string name)
            {
                var value = item.GetProperty(name).GetString() ?? "";
                if (value.Length > 512 || value.Any(char.IsControl)) throw new InvalidDataException("Invalid nameplate field");
                return value;
            }
            var contact = new NameplateContact(Field("labelId"), Field("name"), Field("model"), Field("aircraftType"), Field("distance"), Field("altitude"));
            if (string.IsNullOrWhiteSpace(contact.LabelId) || string.IsNullOrWhiteSpace(contact.Name)) continue;
            contacts[contact.LabelId] = contact;
        }
        return contacts.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public void Clear() { Contacts = []; ReadAt = null; }
    public void Dispose() { socket?.Dispose(); socket = null; }
}
