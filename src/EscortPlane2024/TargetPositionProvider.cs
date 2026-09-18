using System.Diagnostics;
using System.Runtime.InteropServices;
using static EscortPlane2024.NativeSimConnect;

namespace EscortPlane2024;

// Experimental camera acquisition only. These observations are never AP inputs.
internal sealed class TargetPositionProvider(Diagnostics log, Func<nint> connection, Action<int, string> check, Action<double> restoreCameraState)
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double nextRequestAt, pendingAt, startedAt;
    private uint? pendingReferential;
    private bool acquireRequested, referenceVerified, waitingForAvailability, bindingSent;
    private bool unverifiedWorldRequested;
    private double boundAt;
    private int worldSinceReference;
    private double baselineCameraState;
    private bool restorePending;
    private double releasedAt;
    public uint? TargetId { get; private set; }
    public uint Availability { get; private set; }
    public bool GameControlled { get; private set; }
    public bool Active => TargetId.HasValue;
    public bool RestorePending => restorePending;
    public string Status { get; private set; } = "Camera idle";
    public int SampleCount { get; private set; }
    public Position? RawPosition { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public bool ViewStateChanged { get; private set; }
    public bool Passive { get; private set; }
    public void Initialize()
    {
        if (!CameraExportsAvailable) { Status = "Current Camera API unavailable in loaded DLL"; return; }
        check(SimConnect_SubscribeToCameraStatusUpdate(connection()), "Camera status subscription");
        check(SimConnect_CameraGetStatus(connection()), "Camera initial status");
    }
    public void Start(uint targetId, OwnTelemetry own, bool passive = false)
    {
        if (!CameraExportsAvailable) throw new InvalidOperationException("The loaded SimConnect DLL has no Camera API.");
        if (Active || restorePending) throw new InvalidOperationException("Stop the current camera experiment and wait for view restoration before starting another.");
        Stop("Target change");
        TargetId = targetId; Passive = passive; RawPosition = null; ReceivedAt = null; SampleCount = 0;
        referenceVerified = false; bindingSent = false; pendingReferential = null; worldSinceReference = 0;
        unverifiedWorldRequested = false;
        baselineCameraState = own.CameraState; ViewStateChanged = false;
        startedAt = clock.Elapsed.TotalSeconds;
        Status = passive ? "Passive camera read (current view, not target position)" : "Checking camera availability";
        waitingForAvailability = !passive;
        log.Write("camera_experiment_started", new { targetId, passive, baselineCameraState });
        check(SimConnect_CameraGetStatus(connection()), "Camera pre-acquisition status");
    }
    public void OnStatus(uint state, bool gameControlled)
    {
        Availability = state; GameControlled = gameControlled;
        log.Write("camera_status", new { state, gameControlled, targetId = TargetId });
        if (restorePending && state == 0) RestoreView();
        if (!Active || Passive) return;
        if (waitingForAvailability)
        {
            waitingForAvailability = false;
            if (state is 2 or 3 || gameControlled) { Stop("Camera unavailable or owned by another add-on"); return; }
            acquireRequested = true;
            check(SimConnect_CameraAcquire(connection(), "EscortPlane2024.AcquisitionExperiment"), "Camera acquire");
            Status = "Waiting for camera acquisition";
        }
        else if (acquireRequested && state == 1 && !gameControlled && !bindingSent)
        {
            var camera = new CameraData { PositionReferential = 1, PositionObjectId = TargetId!.Value,
                RotationReferential = 1, RotationObjectId = TargetId.Value, Fov = 0.8 };
            check(SimConnect_CameraSet(connection(), camera, 1 | 2 | 8), $"Camera bind {TargetId}");
            bindingSent = true; boundAt = clock.Elapsed.TotalSeconds;
            Status = "Verifying camera SimObject reference";
        }
        else if (acquireRequested && (state != 1 || gameControlled)) Stop("Camera ownership revoked or simulator took control", restoreView: false);
    }
    private void Request(uint referential)
    {
        if (pendingReferential != null) return;
        pendingReferential = referential; pendingAt = clock.Elapsed.TotalSeconds;
        check(SimConnect_CameraGet(connection(), referential), $"Camera get reference {referential}");
    }
    public void Tick(OwnTelemetry? own, bool targetStillPresent)
    {
        if (restorePending && clock.Elapsed.TotalSeconds - releasedAt > 0.3) RestoreView();
        if (!Active) return;
        var now = clock.Elapsed.TotalSeconds;
        if (!Passive && TargetId != 0 && !targetStillPresent) { Stop("Target no longer exposed by MSFS"); return; }
        if (own != null && own.CameraState != baselineCameraState && !ViewStateChanged)
        {
            ViewStateChanged = true;
            log.Write("camera_view_state_changed", new { baselineCameraState, currentState = own.CameraState, targetId = TargetId });
        }
        if (pendingReferential != null && now - pendingAt > 2) { Stop("Camera response timed out"); return; }
        if (!Passive && !referenceVerified && now - startedAt > 5) { Stop("Camera reference acquisition timed out"); return; }
        if (!Passive && bindingSent && !referenceVerified && pendingReferential == null && now - boundAt > 0.3 && now >= nextRequestAt)
        {
            Request(1); nextRequestAt = now + 0.1;
        }
        if (pendingReferential == null && now >= nextRequestAt && (Passive || referenceVerified))
        {
            Request(!Passive && worldSinceReference >= 10 ? 1u : 2u);
            nextRequestAt = now + (Passive ? 1 : 0.1);
        }
    }
    public void OnData(nint p, uint size, OwnTelemetry? own)
    {
        if (size < 96)
        {
            var bytes = new byte[size]; Marshal.Copy(p, bytes, 0, bytes.Length);
            log.Write("camera_abi_mismatch", new { size, declaredSize = Marshal.ReadInt32(p), bytes = Convert.ToHexString(bytes), expectedSize = 96 });
            Stop("Camera ABI mismatch: target binding unavailable for this runtime");
            return;
        }
        var data = Marshal.PtrToStructure<CameraData>(p + 12);
        log.Write("camera_raw", new { targetId = TargetId, data.X, data.Y, data.Z, data.PositionReferential, data.PositionObjectId,
            data.Pitch, data.Bank, data.Heading, data.RotationReferential, data.RotationObjectId, data.Fov, passive = Passive });
        if (!Active || pendingReferential == null) return;
        var expected = pendingReferential;
        if (data.PositionReferential != expected) { Stop("Unexpected camera referential; sample rejected"); return; }
        pendingReferential = null;
        if (expected == 1)
        {
            if (data.PositionObjectId != TargetId || Math.Abs(data.X) > 0.1 || Math.Abs(data.Y) > 0.1 || Math.Abs(data.Z) > 0.1)
            {
                // CameraSet applies asynchronously. Never use transition samples as target positions.
                if (referenceVerified) Stop("Camera reference changed; target position no longer verified");
                else if (clock.Elapsed.TotalSeconds - boundAt > 2)
                {
                    // Runtime may report a transform relative to USER (ID 0), even after CameraSet targets another object.
                    // Capture WORLD once for independent comparison; do not promote it to a verified target sample.
                    unverifiedWorldRequested = true;
                    Request(2);
                }
                else log.Write("camera_reference_settling", new { data.PositionObjectId, data.X, data.Y, data.Z });
                return;
            }
            referenceVerified = true; worldSinceReference = 0;
            Status = "Sampling WORLD coordinates (experimental; view/altitude validation required)";
            return;
        }
        // SDK WORLD axes are lat/lon/alt; Z is retained in meters in the log.
        // Altitude datum is NOT assumed to match PLANE ALTITUDE (MSL).
        var position = new Position(data.X, data.Y, data.Z / 0.3048);
        if (unverifiedWorldRequested)
        {
            log.Write("camera_unverified_world", new { targetId = TargetId, latitude = data.X, longitude = data.Y,
                rawAltitudeMeters = data.Z, own = own?.Position, reason = "Requested object not echoed in relative CameraGet; independent target comparison required" });
            Stop("Captured unverified WORLD position; object-reference proof unresolved");
            return;
        }
        if (!position.IsValid || (data.X == 0 && data.Y == 0 && data.Z == 0))
        { log.Write("camera_sample_rejected", new { reason = "invalid WORLD position" }); return; }
        if (!Passive && TargetId != 0 && own != null && own.Position.DistanceNm(position) < 0.005)
        { log.Write("camera_sample_rejected", new { reason = "possible own-aircraft fallback", targetId = TargetId }); return; }
        if (RawPosition != null && ReceivedAt != null)
        {
            var dt = (DateTimeOffset.UtcNow - ReceivedAt.Value).TotalSeconds;
            if (dt > 0 && (RawPosition.DistanceNm(position) * 3600 / dt > 1500 || Math.Abs(position.AltitudeFeet - RawPosition.AltitudeFeet) / dt > 500))
            { log.Write("camera_sample_rejected", new { reason = "implausible jump", targetId = TargetId }); return; }
        }
        RawPosition = position; ReceivedAt = DateTimeOffset.UtcNow; SampleCount++; worldSinceReference++;
        log.Write("camera_observation", new { targetId = TargetId, latitude = data.X, longitude = data.Y, rawAltitudeMeters = data.Z,
            altitudeDatum = "unverified", SampleCount, Passive, ViewStateChanged, own = own?.Position });
    }
    public void Stop(string reason, bool restoreView = true)
    {
        var release = acquireRequested;
        acquireRequested = false; waitingForAvailability = false; referenceVerified = false; bindingSent = false; pendingReferential = null;
        if (Active) log.Write("camera_experiment_stopped", new { reason, targetId = TargetId, SampleCount, ViewStateChanged });
        TargetId = null; Status = reason;
        if (release && connection() != 0)
        {
            restorePending = restoreView; releasedAt = clock.Elapsed.TotalSeconds;
            check(SimConnect_CameraRelease(connection(), ""), "Camera release to previous view");
        }
    }
    private void RestoreView()
    {
        restorePending = false;
        restoreCameraState(baselineCameraState);
    }
    public void RestoreBeforeDisconnect()
    {
        if (restorePending) RestoreView();
    }
}
