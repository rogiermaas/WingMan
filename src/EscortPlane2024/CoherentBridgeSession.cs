using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace EscortPlane2024;

// Development-only bridge using the SDK's existing local Coherent inspector.
// It runs documented traffic/CommBus calls in an active aircraft logic view.
internal sealed class CoherentBridgeSession : IDisposable
{
    private ClientWebSocket? socket;
    private int request;
    private readonly string owner = Guid.NewGuid().ToString("N");
    private DateTimeOffset nextRenew;
    private int? failedView;
    private int? activeView;
    public bool Running => socket?.State == WebSocketState.Open;
    public string ViewName { get; private set; } = "";
    public bool PmdgInstrumentDetected { get; private set; }
    public bool WorkingTitle787Detected { get; private set; }
    private static string Script(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing bridge resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public async Task StartAsync()
    {
        if (Running) return;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var document = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:19999/pagelist.json").ConfigureAwait(false));
        var views = document.RootElement.EnumerateArray().ToArray();
        PmdgInstrumentDetected = views.Any(v => v.GetProperty("title").GetString()?.Contains("PMDG", StringComparison.OrdinalIgnoreCase) == true);
        WorkingTitle787Detected = views.Any(v => v.GetProperty("title").GetString()?.Contains("WTB78x_", StringComparison.OrdinalIgnoreCase) == true);
        var candidates = views.Where(v => v.GetProperty("url").GetString()?.Contains("/VCockpit/", StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(v => v.GetProperty("id").GetInt32() == failedView)
            .ThenByDescending(v => v.GetProperty("url").GetString()?.Contains("VCockpitLogic.html", StringComparison.OrdinalIgnoreCase) == true)
            .ThenBy(v => v.GetProperty("title").GetString()?.Contains("WasmInstrument", StringComparison.OrdinalIgnoreCase) == true)
            .Take(4).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("No aircraft logic or instrument view is exposed by the SDK debugger. Enable the SDK Coherent debugger and load an aircraft with an HTML instrument.");
        Exception? lastError = null;
        foreach (var view in candidates)
        {
            ViewName = view.GetProperty("title").GetString() ?? "Aircraft logic";
            var id = view.GetProperty("id").GetInt32();
            socket = new ClientWebSocket();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/page/{id}"), timeout.Token).ConfigureAwait(false);
                await EvaluateAsync(Script("coherent-register-maps.js")).ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);
                await EvaluateAsync(Script("coherent-bridge-start.js").Replace("__ESCORT_OWNER__", owner)).ConfigureAwait(false);
                activeView = id;
                nextRenew = DateTimeOffset.UtcNow.AddSeconds(5);
                return;
            }
            catch (Exception ex) { lastError = ex; failedView = id; Dispose(); }
        }
        throw new IOException("Could not connect to an aircraft data view. " + lastError?.Message, lastError);
    }
    public async Task StopAsync()
    {
        try { if (Running) await EvaluateAsync($"if(window.__escortTrafficBridge && window.__escortTrafficBridge.owner === '{owner}') window.__escortTrafficBridge.stop();").ConfigureAwait(false); }
        finally { Dispose(); }
    }
    public async Task MaintainAsync()
    {
        if (!Running || DateTimeOffset.UtcNow < nextRenew) return;
        nextRenew = DateTimeOffset.UtcNow.AddSeconds(5);
        try
        {
            await EvaluateAsync($"(function() {{ var b=window.__escortTrafficBridge; if(!b || b.owner !== '{owner}' || b.stopped) throw new Error('Traffic bridge stopped or another app owns it'); if(Date.now()-(b.receivedAt || b.startedAt)>8000) {{ b.stop(); throw new Error('Aircraft data view stopped responding'); }} b.leaseUntil=Date.now()+30000; }})()").ConfigureAwait(false);
        }
        catch { failedView = activeView; throw; }
    }
    private async Task EvaluateAsync(string expression)
    {
        if (socket == null) throw new InvalidOperationException("Debugger is disconnected");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var id = ++request;
        var packet = JsonSerializer.SerializeToUtf8Bytes(new { id, method = "Runtime.evaluate", @params = new { expression, returnByValue = true, objectGroup = "escort-acquisition" } });
        await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
        var buffer = new byte[8192];
        while (!timeout.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close) throw new IOException("Coherent debugger closed the view");
                message.Write(buffer, 0, received.Count);
                if (message.Length > 1024 * 1024) throw new InvalidDataException("Oversized debugger response");
            } while (!received.EndOfMessage);
            using var response = JsonDocument.Parse(message.ToArray());
            var root = response.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
            if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
            var result = root.GetProperty("result");
            if (result.TryGetProperty("wasThrown", out var thrown) && thrown.GetBoolean()) throw new InvalidOperationException(result.ToString());
            return;
        }
        throw new TimeoutException("Coherent debugger response timed out");
    }
    public void Dispose() { socket?.Dispose(); socket = null; }
}
