using System.Runtime.InteropServices;
using static EscortPlane2024.NativeSimConnect;

namespace EscortPlane2024;

internal sealed class SimConnectManager(Diagnostics log) : IDisposable, IFollowerSession
{
    private nint handle;
    private uint nextRequest = 100;
    private readonly Dictionary<uint, string> sentPackets = [];
    private readonly Dictionary<uint, string> scans = [];
    private readonly Dictionary<uint, int> smartRequests = [];
    private readonly HashSet<int> smartDefinitions = [];
    private readonly Queue<(uint Id, string Kind)> addedObjects = [];
    private readonly HashSet<uint> queuedObjects = [];
    private readonly Dictionary<uint, DateTimeOffset> identityProbed = [];
    private readonly Dictionary<uint, (uint ObjectId, string Field)> identityRequests = [];
    private readonly Dictionary<uint, (uint ObjectId, DateTimeOffset At)> positionRequests = [];
    private readonly Dictionary<uint, DateTimeOffset> positionPolled = [];
    private DateTimeOffset nextPositionScan;
    private double positionScanRange = 20;
    public bool ScanPositions { get; set; } = true;
    public bool ShareTelemetry { get; set; }
    private DateTimeOffset nextShareRequest;
    private static readonly string[] IdentityFields = ["ATC ID", "ATC TYPE", "ATC MODEL", "CATEGORY"];
    private DateTimeOffset nextSmartScan, nextAddedProbe;
    private DateTimeOffset connectingAt;
    public TrafficTracker Traffic { get; } = new();
    public SmartCameraTracker SmartCamera { get; } = new();
    public CoherentTrafficTracker CoherentTraffic { get; } = new();
    public TargetEstimator Focus { get; private set; } = new(TargetSearch.FocusName);
    public void SelectTarget(string name, ulong? id = null, bool matchById = false)
    {
        Follower.Stop("Target changed");
        Focus = new(name.Trim());
        Focus.SampleLimit = Follower.Settings.SmoothingSamples;
        if (id.HasValue) Focus.LockIdentity(id.Value, matchById);
        log.Write("follow_target_selected", new { name, id, matchById });
    }
    public OwnTelemetry? Own { get; private set; }
    public FlightTelemetry? Flight { get; private set; }
    public string OwnTitle { get; private set; } = "";
    public string[] OwnDetails { get; } = new string[4];
    public bool PmdgViewDetected { get; set; }
    public bool WorkingTitle787Detected { get; set; }
    public bool Pmdg737Identified => AircraftIdentification.IsPmdg737(OwnTitle, PmdgViewDetected, Pmdg.State != null);
    private Pmdg737? pmdg;
    public Pmdg737 Pmdg => pmdg ??= new(log, () => handle, Check);
    private StandardAutopilot? standard;
    public StandardAutopilot Standard => standard ??= new(log, () => handle, Check);
    private FollowerController? follower;
    public FollowerController Follower => follower ??= new(this, log);
    public bool Paused { get; private set; }
    public bool CameraActive => Camera.Active;
    public PmdgState? PmdgState => Pmdg.State;
    public AutopilotReadback? StandardState => Standard.State;
    public void RequestPmdgVs() => Pmdg.Press(Pmdg737.VsButton);
    public void RequestStandardVs() => Standard.Send(StandardAutopilot.VsOn, 0);
    public void SendSelection(string axis, double value, bool mach, bool usePmdg)
    {
        var id = (axis, usePmdg) switch
        {
            ("speed", true) => mach ? Pmdg737.Mach : Pmdg737.Ias, ("speed", false) => mach ? StandardAutopilot.Mach : StandardAutopilot.Speed,
            ("heading", true) => Pmdg737.Heading, ("heading", false) => StandardAutopilot.Heading,
            ("altitude", true) => Pmdg737.Altitude, ("altitude", false) => StandardAutopilot.Altitude,
            ("vs", true) => Pmdg737.Vs, ("vs", false) => StandardAutopilot.Vs,
            _ => throw new ArgumentException("Unknown output axis")
        };
        var payload = EncodeSelection(axis, value, mach, usePmdg);
        if (usePmdg) Pmdg.Send(id, payload); else Standard.Send(id, payload);
    }
    internal static uint EncodeSelection(string axis, double value, bool mach, bool pmdg) =>
        unchecked((uint)(int)Math.Round(axis == "speed" && mach ? value * 100 : axis == "vs" && pmdg ? value + 10000 : value));
    public uint? OwnObjectId { get; private set; }
    public bool Connected { get; private set; }
    public bool IsOpen => handle != 0;
    public string Status { get; private set; } = "Disconnected";
    public int OwnSampleCount { get; private set; }
    public int ExceptionCount { get; private set; }
    private TargetPositionProvider? camera;
    public TargetPositionProvider Camera => camera ??= new(log, () => handle, Check, RestoreCameraState);
    public event Action? Changed;
    public bool IncludeAllObjects { get; set; }
    public void Connect()
    {
        if (IsOpen) return;
        Check(SimConnect_Open(out handle, "WingMan - acquisition experiment", 0, 0, 0, uint.MaxValue), "Open");
        connectingAt = DateTimeOffset.UtcNow;
        Status = "Connecting...";
        log.Write("connection_requested", new { LoadedPath, CameraExportsAvailable });
        log.Write("target_search_started", new { names = TargetSearch.Names, matching = "OrdinalIgnoreCase; all returned objects retained" });
        try
        {
            Add(1, "PLANE LATITUDE", "degrees"); Add(1, "PLANE LONGITUDE", "degrees"); Add(1, "PLANE ALTITUDE", "feet");
            Add(1, "GROUND VELOCITY", "knots"); Add(1, "GPS GROUND TRUE TRACK", "degrees");
            Add(1, "AIRSPEED INDICATED", "knots"); Add(1, "VERTICAL SPEED", "feet per minute"); Add(1, "CAMERA STATE", "enum");
            Add(2, "TITLE", null, 9);
            var ownFields = new[] { "ATC TYPE", "ATC MODEL", "ATC ID", "LIVERY NAME" };
            for (uint i = 0; i < ownFields.Length; i++)
            {
                Add(30 + i, ownFields[i], null, 9);
                Check(SimConnect_RequestDataOnSimObject(handle, 60 + i, 30 + i, 0, 4, 0, 0, 4, 0), "Own aircraft details");
            }
            Add(3, "PLANE LATITUDE", "degrees"); Add(3, "PLANE LONGITUDE", "degrees"); Add(3, "PLANE ALTITUDE", "feet");
            Add(4, "SIM ON GROUND", "bool");
            Add(5, "CAMERA STATE", "enum");
            Add(6, "SMART CAMERA INFO:0", "number");
            foreach (var (name, unit) in new[] { ("AIRSPEED TRUE", "knots"), ("PLANE HEADING DEGREES TRUE", "degrees"),
                ("PLANE HEADING DEGREES MAGNETIC", "degrees"), ("INDICATED ALTITUDE", "feet"), ("AMBIENT PRESSURE", "pascals"),
                ("AMBIENT TEMPERATURE", "kelvin"), ("AIRSPEED MACH", "mach"), ("SIM ON GROUND", "bool"),
                ("PLANE ALT ABOVE GROUND", "feet"), ("FLAPS HANDLE INDEX", "number"), ("GEAR HANDLE POSITION", "bool"),
                ("SIMULATION RATE", "number"), ("DESIGN SPEED VS1", "knots"), ("DESIGN SPEED VC", "knots") }) Add(7, name, unit);
            Check(SimConnect_RequestDataOnSimObject(handle, 7, 7, 0, 4, 0, 0, 0, 0), "Flight telemetry");
            Check(SimConnect_RequestDataOnSimObject(handle, 8, 2, 0, 4, 0, 0, 0, 0), "Own aircraft title");
            Pmdg.Initialize();
            foreach (var (name, unit) in new[] { ("AUTOPILOT AIRSPEED HOLD VAR", "knots"), ("AUTOPILOT MACH HOLD VAR", "number"),
                ("AUTOPILOT HEADING LOCK DIR", "degrees"), ("AUTOPILOT ALTITUDE LOCK VAR", "feet"),
                ("AUTOPILOT VERTICAL HOLD VAR", "feet per minute"), ("AUTOPILOT MANAGED SPEED IN MACH", "bool"),
                ("AUTOPILOT VERTICAL HOLD", "bool"), ("AUTOPILOT AVAILABLE", "bool"), ("AUTOPILOT THROTTLE ARM", "bool") }) Add(9, name, unit);
            Add(9, "L:AS01B_AUTO_THROTTLE_ARM_STATE", "number");
            Add(9, "AUTOPILOT MASTER", "bool");
            Add(9, "AUTOPILOT ALTITUDE LOCK", "bool");
            Check(SimConnect_RequestDataOnSimObject(handle, 9, 9, 0, 4, 0, 0, 0, 0), "Standard AP readback");
            Standard.Initialize();
            Check(SimConnect_SubscribeToSystemEvent(handle, 14, "Pause_EX1"), "Pause subscription");
            for (int i = 0; i < IdentityFields.Length; i++) Add((uint)(20 + i), IdentityFields[i], null, 9);
            Check(SimConnect_RequestDataOnSimObject(handle, 1, 1, 0, 4, 0, 0, 0, 0), "Own telemetry");
            Check(SimConnect_RequestDataOnSimObject(handle, 6, 6, 0, 4, 0, 0, 0, 0), "Smart-camera target count");
            Check(SimConnect_SubscribeToSystemEvent(handle, 10, "ObjectRemoved"), "ObjectRemoved subscription");
            Check(SimConnect_SubscribeToSystemEvent(handle, 11, "SimStart"), "SimStart subscription");
            Check(SimConnect_SubscribeToSystemEvent(handle, 12, "SimStop"), "SimStop subscription");
            Check(SimConnect_SubscribeToSystemEvent(handle, 13, "ObjectAdded"), "ObjectAdded subscription");
            Check(SimConnect_SubscribeToCommBusEvent(handle, 50, "EscortPlane2024.Traffic.v1"), "Coherent traffic bridge subscription");
            Camera.Initialize();
        }
        catch { Disconnect(telemetryLost: true); throw; }
    }
    private void Add(uint id, string name, string? unit, uint type = 4) => Check(SimConnect_AddToDataDefinition(handle, id, name, unit, type, 0, uint.MaxValue), $"Define {id}: {name}");
    private void Check(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"{operation}: HRESULT 0x{result:X8}");
        if (handle != 0 && SimConnect_GetLastSentPacketID(handle, out var packet) >= 0)
        {
            sentPackets[packet] = operation;
            if (sentPackets.Count > 512) sentPackets.Remove(sentPackets.Keys.First());
        }
    }
    public void Scan(double radiusNm)
    {
        if (!Connected || OwnObjectId == null) return;
        if (!double.IsFinite(radiusNm) || radiusNm is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(radiusNm));
        positionScanRange = radiusNm;
        // TITLE-only discovery: adding unsupported position SimVars can hide FakeSim objects.
        var types = new List<(uint Type, string Kind)> { (2u, "Aircraft"), (3u, "Helicopter") };
        if (IncludeAllObjects) types.Insert(0, (1u, "Exposed object (ALL)"));
        foreach (var (type, kind) in types)
        {
            var id = nextRequest++;
            scans[id] = kind;
            Check(SimConnect_RequestDataOnSimObjectType(handle, id, 2, (uint)Math.Round(radiusNm * 1852), type), $"Scan {kind} {radiusNm} NM");
            var fallbackId = nextRequest++;
            scans[fallbackId] = kind;
            Check(SimConnect_RequestDataOnSimObjectType(handle, fallbackId, 4, (uint)Math.Round(radiusNm * 1852), type), $"Scan multiplayer-compatible SIM ON GROUND {kind} {radiusNm} NM");
        }
        while (scans.Count > 512) scans.Remove(scans.Keys.First());
        log.Write("scan_requested", new { radiusNm });
    }
    public void ProbeDirectPosition(uint objectId)
    {
        if (!Connected || objectId == OwnObjectId) return;
        var request = nextRequest++;
        positionRequests[request] = (objectId, DateTimeOffset.UtcNow);
        positionPolled[objectId] = DateTimeOffset.UtcNow;
        Check(SimConnect_RequestDataOnSimObject(handle, request, 3, objectId, 1, 0, 0, 0, 0), $"Direct position probe {objectId}");
        log.Write("direct_position_requested", new { objectId });
    }
    private void PollPositions(DateTimeOffset now)
    {
        if (Connected && now >= nextShareRequest)
        {
            nextShareRequest = now.AddMilliseconds(ShareTelemetry || Focus.FastTelemetry && Follower.Active ? 100 : 1000);
            Check(SimConnect_RequestDataOnSimObject(handle, 1, 1, 0, 1, 0, 0, 0, 0), "Shared own telemetry");
            Check(SimConnect_RequestDataOnSimObject(handle, 9, 9, 0, 1, 0, 0, 0, 0), "Formation AP readback");
        }
        foreach (var request in positionRequests.Where(p => now - p.Value.At > TimeSpan.FromSeconds(4)).Select(p => p.Key).ToArray())
        {
            var pending = positionRequests[request];
            var aircraft = Traffic.Aircraft.FirstOrDefault(a => a.ObjectId == pending.ObjectId);
            if (aircraft?.PositionAt == null || aircraft.PositionAt < pending.At) Traffic.RejectPosition(pending.ObjectId, "MSFS did not return a position");
            positionRequests.Remove(request);
        }
        if (!Connected || !ScanPositions || now < nextPositionScan) return;
        nextPositionScan = now.AddSeconds(1);
        var eligible = Traffic.Aircraft.Where(a => (a.Kind is "Aircraft" or "Helicopter") &&
            (Focus.TargetId == new ContactId(true, a.ObjectId).TrackingId || a.Position == null || Own == null || Own.Position.DistanceNm(a.Position) <= positionScanRange)).ToArray();
        var ids = eligible.Select(a => a.ObjectId).ToHashSet();
        foreach (var stale in positionPolled.Keys.Where(id => !ids.Contains(id)).ToArray()) positionPolled.Remove(stale);
        foreach (var a in eligible.OrderByDescending(a => Focus.TargetId == new ContactId(true, a.ObjectId).TrackingId)
            .ThenBy(a => positionPolled.GetValueOrDefault(a.ObjectId)).Take(64))
            if (!positionRequests.Values.Any(p => p.ObjectId == a.ObjectId)) ProbeDirectPosition(a.ObjectId);
    }
    public void Pump()
    {
        if (!IsOpen) return;
        // Bounded work on the UI thread; no concurrent SimConnect calls.
        for (var count = 0; count < 256 && IsOpen; count++)
        {
            if (SimConnect_GetNextDispatch(handle, out var data, out var size) < 0) break;
            if (size < 12) { log.Write("malformed_packet", new { size }); continue; }
            Dispatch(data, size);
        }
        Traffic.Expire(DateTimeOffset.UtcNow);
        CoherentTraffic.Expire(DateTimeOffset.UtcNow);
        PollPositions(DateTimeOffset.UtcNow);
        if (Connected && addedObjects.Count > 0 && DateTimeOffset.UtcNow >= nextAddedProbe)
        {
            var added = addedObjects.Dequeue(); queuedObjects.Remove(added.Id);
            RequestTitle(added.Id, added.Kind, "ObjectAdded TITLE");
            nextAddedProbe = DateTimeOffset.UtcNow.AddMilliseconds(100);
        }
        Camera.Tick(Own, Traffic.Aircraft.Any(a => a.ObjectId == Camera.TargetId));
        Follower.Tick(DateTimeOffset.UtcNow);
        if (!Connected && IsOpen && DateTimeOffset.UtcNow - connectingAt > TimeSpan.FromSeconds(10))
        {
            Disconnect(telemetryLost: true); Status = "Connection timed out";
        }
    }
    public void RestoreCameraState(double state)
    {
        if (!IsOpen || !double.IsFinite(state) || state is < 2 or > 9) return;
        Check(SimConnect_SetDataOnSimObject(handle, 5, 0, 0, 0, sizeof(double), ref state), $"Restore camera state {state}");
        log.Write("camera_view_restore_requested", new { state });
    }
    private static uint U32(nint p, int offset) => unchecked((uint)Marshal.ReadInt32(p, offset));
    private void ProbeIdentity(uint objectId)
    {
        var now = DateTimeOffset.UtcNow;
        if (identityProbed.TryGetValue(objectId, out var previous) && now - previous < TimeSpan.FromSeconds(15)) return;
        identityProbed[objectId] = now;
        foreach (var stale in identityProbed.Where(pair => now - pair.Value > TimeSpan.FromMinutes(1)).Select(pair => pair.Key).ToArray()) identityProbed.Remove(stale);
        for (int i = 0; i < IdentityFields.Length; i++)
        {
            var request = nextRequest++;
            identityRequests[request] = (objectId, IdentityFields[i]);
            Check(SimConnect_RequestDataOnSimObject(handle, request, (uint)(20 + i), objectId, 1, 0, 0, 0, 0), $"Identity {IdentityFields[i]} {objectId}");
        }
        while (identityRequests.Count > 512) identityRequests.Remove(identityRequests.Keys.First());
    }
    private void RequestTitle(uint objectId, string kind, string operation)
    {
        var request = nextRequest++;
        scans[request] = kind;
        while (scans.Count > 512) scans.Remove(scans.Keys.First());
        Check(SimConnect_RequestDataOnSimObject(handle, request, 2, objectId, 1, 0, 0, 0, 0), $"{operation} {objectId}");
    }
    private void ScanSmartCamera(int count)
    {
        SmartCamera.BeginSnapshot(count); smartRequests.Clear();
        var requestedCount = Math.Min(count, 64);
        log.Write("smart_camera_count", new { reportedCount = count, requestedCount });
        for (int index = 0; index < requestedCount; index++)
        {
            var definition = (uint)(1000 + index);
            if (smartDefinitions.Add(index))
            {
                Add(definition, $"SMART CAMERA LIST:{index}", "enum");
                Add(definition, $"SMART CAMERA LIST DESCRIPTION:{index}", null, 9);
            }
            var request = nextRequest++; smartRequests[request] = index;
            Check(SimConnect_RequestDataOnSimObject(handle, request, definition, 0, 1, 0, 0, 0, 0), $"Smart-camera target {index}");
        }
    }
    private static double F64(nint p, int offset) => BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p, offset));
    private static string Text(nint p, int offset, int length) => (Marshal.PtrToStringAnsi(p + offset, length) ?? "").Split('\0')[0];
    private void Dispatch(nint p, uint size)
    {
        var type = U32(p, 8);
        switch (type)
        {
            case 2 when size >= 284:
                Connected = true;
                var app = Text(p, 12, 256);
                var version = $"{U32(p, 268)}.{U32(p, 272)}.{U32(p, 276)}.{U32(p, 280)}";
                Status = $"Connected: {app} {version}";
                log.Write("connected", new { app, version, LoadedPath, CameraExportsAvailable });
                break;
            case 3: Disconnect(telemetryLost: true); Status = "Simulator closed"; break;
            case 1 when size >= 24:
                ExceptionCount++;
                var packet = U32(p, 16);
                sentPackets.TryGetValue(packet, out var operation);
                log.Write("simconnect_exception", new { code = U32(p, 12), packet, index = U32(p, 20), operation });
                Status = $"SimConnect exception {U32(p, 12)}: {operation ?? "unknown request"}";
                if (operation?.StartsWith("Camera", StringComparison.Ordinal) == true) Camera.Stop(Status);
                if (operation?.StartsWith("Follower", StringComparison.Ordinal) == true) Follower.Stop(Status);
                break;
            case 40: Camera.OnData(p, size, Own); break;
            case 16 when size >= 40 && U32(p, 12) == Pmdg737.Request: Pmdg.Receive(p, size); break;
            case 41 when size >= 20: Camera.OnStatus(U32(p, 12), U32(p, 16) != 0); break;
            case 43 when size > 32 && U32(p, 28) == 50:
                // Bridge emits one small JSON message per aircraft; reject fragmented or oversized packets.
                if (size > 65536 || U32(p, 24) != 1 || U32(p, 20) != 0)
                { log.Write("coherent_packet_rejected", new { size, reason = "oversized or fragmented" }); break; }
                try
                {
                    var bytes = new byte[size - 32]; Marshal.Copy(p + 32, bytes, 0, bytes.Length);
                    var json = System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                    var sample = CoherentTraffic.Accept(json, DateTimeOffset.UtcNow);
                    if (sample != null)
                    {
                        log.Write("coherent_traffic", sample);
                        if (Focus.Matches(sample))
                        {
                            var issue = TrafficContacts.PositionIssue(new(sample.Latitude, sample.Longitude, sample.RawAltitude / 0.3048), Own?.Position);
                            if (issue != null) { Follower.Stop(issue); break; }
                            var rejection = Focus.Observe(sample);
                            if (rejection?.StartsWith("target identity changed") == true) Follower.Stop($"Target sample rejected: {rejection}");
                            log.Write("focus_motion", new { targetName = Focus.Name, Focus.TargetId, Focus.GroundSpeedKnots,
                                Focus.GroundTrackDegrees, Focus.AcceptedSamples, Focus.RejectedSamples, rejection,
                                status = Focus.StatusAt(DateTimeOffset.UtcNow), sample.SourceTime });
                        }
                        if (TargetSearch.Match(sample.Name) is string matchedName)
                            log.Write("target_search_match", new { searchName = matchedName, trafficId = sample.TrafficId, returnedName = sample.Name, sample.Model, source = "Coherent traffic name; uId is NOT a SimObject ID" });
                    }
                    else log.Write("coherent_bridge_message", new { json });
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or InvalidDataException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException or OverflowException)
                { log.Write("coherent_packet_rejected", new { ex.Message }); }
                break;
            case 5 when size >= 28:
                if (U32(p, 16) == 10)
                {
                    var removed = U32(p, 20); Traffic.Remove(removed);
                    foreach (var r in positionRequests.Where(r => r.Value.ObjectId == removed).Select(r => r.Key).ToArray()) positionRequests.Remove(r);
                    if (Focus.TargetId == new ContactId(true, removed).TrackingId) { Follower.Stop("Selected aircraft was removed"); Focus.Reset(); }
                    log.Write("object_removed", new { objectId = removed });
                }
                else if (U32(p, 16) == 13)
                {
                    var addedId = U32(p, 20); var objectType = U32(p, 24);
                    log.Write("object_added", new { objectId = addedId, objectType });
                    if (addedId != 0 && addedId != OwnObjectId && (IncludeAllObjects || objectType is 2 or 3) && queuedObjects.Add(addedId))
                    {
                        if (addedObjects.Count >= 256)
                        {
                            var dropped = addedObjects.Dequeue(); queuedObjects.Remove(dropped.Id);
                            log.Write("object_added_queue_limit", new { droppedObjectId = dropped.Id });
                        }
                        addedObjects.Enqueue((addedId, objectType == 2 ? "Aircraft" : objectType == 3 ? "Helicopter" : $"Exposed object (event {objectType})"));
                    }
                }
                break;
            case 4 when size >= 24:
                log.Write("sim_event", new { eventId = U32(p, 16), value = U32(p, 20) });
                if (U32(p, 16) == 14) { Paused = U32(p, 20) != 0; if (Paused) Follower.Stop("Simulator paused"); }
                if (U32(p, 16) == 12) { Follower.Stop("Simulation stopped"); Camera.Stop("Simulation stopped"); Own = null; Flight = null; Traffic.Clear(); SmartCamera.Clear(); CoherentTraffic.Clear(); Focus.Reset(); smartRequests.Clear(); addedObjects.Clear(); queuedObjects.Clear(); identityProbed.Clear(); identityRequests.Clear(); positionRequests.Clear(); positionPolled.Clear(); nextPositionScan = default; }
                break;
            case 8 or 9 when size >= 40:
                var request = U32(p, 12); var objectId = U32(p, 16); var definition = U32(p, 20);
                if (definition is 2 or 4 && objectId != 0 && objectId != OwnObjectId && scans.TryGetValue(request, out var envelopeKind))
                {
                    var known = Traffic.Aircraft.Any(a => a.ObjectId == objectId);
                    if (!known || size < (definition == 2 ? 296 : 48))
                        log.Write("discovery_envelope", new { objectId, definition, request, size, declaredSize = U32(p, 0), datumCount = U32(p, 36), entry = U32(p, 28), total = U32(p, 32), kind = envelopeKind });
                    // An object identity is still useful even when FakeSim omits its SimVar payload.
                    if (!known && size < (definition == 2 ? 296 : 48)) Traffic.Seen(objectId, "", envelopeKind);
                }
                if (definition == 9 && request == 9 && size >= 136)
                {
                    var standardReadback = new AutopilotReadback(F64(p, 40), F64(p, 48), F64(p, 56), F64(p, 64), F64(p, 72),
                        F64(p, 80) != 0, F64(p, 88) != 0, F64(p, 96) != 0, F64(p, 104) != 0, DateTimeOffset.UtcNow)
                        { MasterEngaged = F64(p, 120) == 1, AltitudeHold = F64(p, 128) == 1 };
                    Standard.State = StandardAutopilot.ResolveAutothrottle(standardReadback, OwnTitle, WorkingTitle787Detected, F64(p, 112));
                    log.Write("standard_ap_telemetry", Standard.State);
                }
                else if (definition == 7 && request == 7 && size >= 152)
                {
                    Flight = new(F64(p, 40), F64(p, 48), F64(p, 56), F64(p, 64), F64(p, 72), F64(p, 80),
                        F64(p, 88), F64(p, 96), F64(p, 104), F64(p, 112), F64(p, 120), F64(p, 128), F64(p, 136), DateTimeOffset.UtcNow)
                    { DesignCruiseSpeed = F64(p, 144) };
                    log.Write("flight_telemetry", Flight);
                }
                else if (definition == 2 && request == 8 && size >= 296)
                {
                    var title = Text(p, 40, 256);
                    if (title != OwnTitle) Array.Clear(OwnDetails);
                    OwnTitle = title;
                }
                else if (definition is >= 30 and <= 33 && request == definition + 30 && size >= 296) OwnDetails[definition - 30] = Text(p, 40, 256);
                else if (definition == 1 && request == 1 && size >= 104)
                {
                    OwnObjectId = objectId;
                    Traffic.Remove(objectId);
                    var position = new Position(F64(p, 40), F64(p, 48), F64(p, 56));
                    if (!position.IsValid) { log.Write("own_sample_rejected", new { reason = "invalid coordinates" }); break; }
                    Own = new(position, F64(p, 64), F64(p, 72), F64(p, 80), F64(p, 88), F64(p, 96), DateTimeOffset.UtcNow);
                    OwnSampleCount++;
                    log.Write("own_telemetry", Own);
                }
                else if (definition == 2 && size >= 296 && objectId != OwnObjectId && objectId != 0 && scans.TryGetValue(request, out var kind))
                {
                    var title = Text(p, 40, 256);
                    Traffic.Seen(objectId, title, kind);
                    log.Write("traffic_discovered", new { objectId, title, kind, request });
                    if (kind is "Aircraft" or "Helicopter") ProbeIdentity(objectId);
                    if (TargetSearch.Match(title) is string matchedName)
                        log.Write("target_search_match", new { name = matchedName, objectId, title, source = "SimConnect TITLE; identity not yet verified" });
                }
                else if (definition == 3 && positionRequests.Remove(request, out var requested) && requested.ObjectId == objectId)
                {
                    if (size < 64) { Traffic.RejectPosition(objectId, "MSFS omitted the position"); break; }
                    var position = new Position(F64(p, 40), F64(p, 48), F64(p, 56));
                    var issue = TrafficContacts.PositionIssue(position, Own?.Position);
                    var accepted = issue == null;
                    if (accepted) Traffic.SetPosition(objectId, position);
                    else Traffic.RejectPosition(objectId, issue!);
                    var key = new ContactId(true, objectId);
                    if (Focus.MatchById && Focus.TargetId == key.TrackingId)
                    {
                        if (!accepted) Follower.Stop(issue!);
                        else if (Traffic.Aircraft.Any(a => a.ObjectId == objectId))
                        {
                            var now = DateTimeOffset.UtcNow;
                            var rejection = Focus.Observe(new(key.TrackingId, Focus.Name, "", position.Latitude, position.Longitude, position.AltitudeFeet * 0.3048, 0, false, now, now));
                            log.Write("focus_native_motion", new { objectId, rejection, Focus.GroundSpeedKnots, Focus.GroundTrackDegrees });
                        }
                    }
                    log.Write("direct_position_result", new { objectId, position, accepted, issue, source = "SimVars; not Camera API" });
                }
                else if (definition == 4 && size >= 48 && objectId != OwnObjectId && objectId != 0 && scans.TryGetValue(request, out var fallbackKind))
                {
                    var existing = Traffic.Aircraft.FirstOrDefault(a => a.ObjectId == objectId);
                    if (existing == null)
                    {
                        Traffic.Seen(objectId, "", fallbackKind);
                        RequestTitle(objectId, fallbackKind, "Fallback TITLE");
                    }
                    else Traffic.Seen(objectId, existing.Title, fallbackKind);
                    log.Write("object_discovered_by_supported_simvar", new { objectId, kind = fallbackKind, onGround = F64(p, 40) });
                }
                else if (definition == 6 && size >= 48)
                {
                    var count = F64(p, 40);
                    if (double.IsFinite(count) && count >= 0 && count <= 100000 && DateTimeOffset.UtcNow >= nextSmartScan)
                    {
                        nextSmartScan = DateTimeOffset.UtcNow.AddSeconds(5);
                        ScanSmartCamera((int)count);
                    }
                }
                else if (definition is >= 20 and <= 23 && size >= 296 && identityRequests.Remove(request, out var identity) && identity.ObjectId == objectId)
                {
                    var value = Text(p, 40, 256);
                    Traffic.SetIdentity(objectId, identity.Field, value);
                    log.Write("traffic_identity", new { objectId, field = identity.Field, value });
                    if (TargetSearch.Match(value) is string matched)
                        log.Write("target_search_match", new { name = matched, objectId, field = identity.Field, value, source = "SimConnect identity field; verify target before following" });
                }
                else if (definition >= 1000 && definition < 1064 && size >= 304 && smartRequests.Remove(request, out var smartIndex))
                {
                    var targetType = F64(p, 40); var description = Text(p, 48, 256);
                    if (double.IsFinite(targetType))
                    {
                        SmartCamera.Seen(smartIndex, (int)targetType, description);
                        log.Write("smart_camera_target", new { index = smartIndex, type = (int)targetType, description, source = "Smart camera UI list; not SimObject ID" });
                        if (TargetSearch.Match(description) is string matchedLabel)
                            log.Write("target_search_match", new { name = matchedLabel, index = smartIndex, description, source = "Smart-camera label; no SimObject ID" });
                    }
                }
                break;
        }
        Changed?.Invoke();
    }
    public void Disconnect(bool telemetryLost = false)
    {
        if (telemetryLost) Follower.TelemetryLost("MSFS connection lost"); else Follower.Stop("Disconnected");
        if (handle != 0)
        {
            try
            {
                Camera.Stop("Disconnected");
                var cleanup = System.Diagnostics.Stopwatch.StartNew();
                while (Camera.RestorePending && cleanup.ElapsedMilliseconds < 700)
                {
                    if (SimConnect_GetNextDispatch(handle, out var data, out var size) >= 0 && size >= 12)
                    {
                        // Only camera status is relevant during shutdown; never restart discovery or acquisition.
                        if (U32(data, 8) == 41 && size >= 20) Camera.OnStatus(U32(data, 12), U32(data, 16) != 0);
                    }
                    Thread.Sleep(10);
                }
                Camera.RestoreBeforeDisconnect();
            }
            finally
            {
                var result = SimConnect_Close(handle); handle = 0;
                log.Write("disconnected", new { result });
            }
        }
        Connected = false; Own = null; OwnObjectId = null; Traffic.Clear(); scans.Clear(); sentPackets.Clear();
        SmartCamera.Clear(); smartRequests.Clear(); smartDefinitions.Clear(); addedObjects.Clear(); queuedObjects.Clear();
        identityProbed.Clear(); identityRequests.Clear(); positionRequests.Clear(); positionPolled.Clear(); nextPositionScan = default;
        CoherentTraffic.Clear();
        if (Follower.WaitingForTelemetry) Focus.ResetMotion(); else Focus.Reset();
        Flight = null; OwnTitle = ""; Array.Clear(OwnDetails); Pmdg.Clear();
        Standard.State = null; Paused = false;
        PmdgViewDetected = false;
        WorkingTitle787Detected = false;
        Status = "Disconnected"; Changed?.Invoke();
    }
    public void Dispose() => Disconnect();
}
