namespace EscortPlane2024;

// Horizontal motion diagnostics only. Raw traffic altitude is not a validated MSL input.
internal sealed class TargetEstimator(string targetName)
{
    public string Name => targetName;
    private readonly List<CoherentAircraft> history = [];
    private readonly List<CoherentAircraft> recovery = [];
    public int SampleLimit { get; set; } = 10;
    public bool FastTelemetry { get; set; }
    // Only the lead's own SimConnect telemetry is authoritative. The legacy
    // multiplayer map has reported on-ground=true for aircraft in flight.
    public bool TrustedGroundStatus { get; set; }
    public CoherentAircraft? GroundOrbitTarget { get; private set; }
    public string? LastIssue { get; private set; }
    public DateTimeOffset? LastIssueAt { get; private set; }
    public Position? SmoothedPosition { get; private set; }
    public ulong? TargetId { get; private set; }
    public bool MatchById { get; private set; }
    public CoherentAircraft? Latest { get; private set; }
    public int AcceptedSamples { get; private set; }
    public int RejectedSamples { get; private set; }
    public double? GroundSpeedKnots { get; private set; }
    public double? GroundTrackDegrees { get; private set; }
    public double? VerticalSpeedFpm { get; private set; }
    public void LockIdentity(ulong id, bool matchById = false) { Reset(); TargetId = id; MatchById = matchById; }
    public bool Matches(CoherentAircraft sample) => MatchById ? sample.TrafficId == TargetId : string.Equals(sample.Name, targetName, StringComparison.OrdinalIgnoreCase);
    public string? Observe(CoherentAircraft sample)
    {
        if (!MatchById && !string.Equals(sample.Name, targetName, StringComparison.OrdinalIgnoreCase)) return "different name";
        if (TargetId.HasValue && sample.TrafficId != TargetId) return Reject("target identity changed; reconnect to reacquire");
        if (!double.IsFinite(sample.Latitude) || Math.Abs(sample.Latitude) > 90 || !double.IsFinite(sample.Longitude) || Math.Abs(sample.Longitude) > 180
            || !double.IsFinite(sample.RawAltitude) || sample.RawAltitude is < -600 or > 30000)
            return Reject("invalid position");
        if (Latest is { } previous)
        {
            var dt = (sample.SourceTime - previous.SourceTime).TotalSeconds;
            if (dt <= 0) return Reject("non-increasing timestamp");
            if (dt > 5)
            {
                history.Clear(); GroundSpeedKnots = null; GroundTrackDegrees = null; VerticalSpeedFpm = null; SmoothedPosition = null;
            }
            else if (new Position(previous.Latitude, previous.Longitude, 0).DistanceNm(new(sample.Latitude, sample.Longitude, 0)) * 3600 / dt > 1500)
                return RejectMotion("implausible horizontal jump", sample);
            else if (Math.Abs(sample.RawAltitude - previous.RawAltitude) / 0.3048 * 60 / dt > 10000)
                return RejectMotion("implausible vertical jump", sample);
            else if (!(TrustedGroundStatus && (sample.IsOnGround || previous.IsOnGround))
                && GroundSpeedKnots is > 40 && GroundTrackDegrees is double course)
            {
                var movement = new Position(previous.Latitude, previous.Longitude, 0).DistanceNm(new(sample.Latitude, sample.Longitude, 0));
                if (movement < 0.002 && dt >= 0.5) return RejectMotion("multiplayer position frozen", sample);
                var angle = course * Math.PI / 180;
                var expected = FormationGeometry.Offset(SmoothedPosition ?? new(previous.Latitude, previous.Longitude, previous.RawAltitude / 0.3048),
                    GroundSpeedKnots.Value * dt / 3600 * Math.Cos(angle), GroundSpeedKnots.Value * dt / 3600 * Math.Sin(angle), (VerticalSpeedFpm ?? 0) * dt / 60);
                if (expected.DistanceNm(new(sample.Latitude, sample.Longitude, 0)) > Math.Max(0.03, GroundSpeedKnots.Value * dt / 3600 * 0.65)
                    || Math.Abs(expected.AltitudeFeet - sample.RawAltitude / 0.3048) > Math.Max(150, Math.Abs(VerticalSpeedFpm ?? 0) * dt / 60 + 80))
                    return RejectMotion("multiplayer position correction", sample);
            }
        }
        if (TrustedGroundStatus && Latest is { } last && last.IsOnGround != sample.IsOnGround)
        {
            // Never mix a landing rollout or takeoff with the previous phase's
            // regression. Absolute jump checks above still apply across phases.
            history.Clear(); GroundSpeedKnots = null; GroundTrackDegrees = null;
            VerticalSpeedFpm = null; SmoothedPosition = null;
        }
        TargetId ??= sample.TrafficId;
        recovery.Clear(); LastIssue = null; LastIssueAt = null;
        Latest = sample; AcceptedSamples++;
        history.Add(sample);
        history.RemoveAll(p => sample.SourceTime - p.SourceTime > TimeSpan.FromSeconds(35));
        while (history.Count > Math.Clamp(SampleLimit, 5, 30)) history.RemoveAt(0);
        ShortenWindowDuringTurn();
        Estimate();
        UpdateGroundOrbitTarget(sample);
        return null;
    }
    private void UpdateGroundOrbitTarget(CoherentAircraft sample)
    {
        if (TrustedGroundStatus && sample.IsOnGround) GroundOrbitTarget = sample;
        else if (!TrustedGroundStatus || (GroundSpeedKnots is > 40 && GroundTrackDegrees != null)) GroundOrbitTarget = null;
    }
    private void ShortenWindowDuringTurn()
    {
        if (history.Count <= 5) return;
        // Three consecutive course changes in the same direction indicate a turn.
        // Discard the old straight leg so a long jitter filter cannot delay a waypoint
        // turn for half its window. Alternating coordinate jitter does not qualify.
        var recent = history.TakeLast(5).ToArray();
        var courses = new List<double>();
        for (var i = 1; i < recent.Length; i++)
        {
            var a = recent[i-1]; var b = recent[i];
            var (n, e) = Displacement(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            if (Math.Sqrt(n*n + e*e) < 4) return;
            courses.Add(Math.Atan2(e, n) * 180 / Math.PI);
        }
        var turns = courses.Zip(courses.Skip(1), (a, b) => FormationGeometry.Angle(b-a)).ToArray();
        if (turns.All(t => t is > 0.4 and < 10) || turns.All(t => t is < -0.4 and > -10))
            history.RemoveRange(0, history.Count - 5);
    }
    private string Reject(string reason) { RejectedSamples++; return reason; }
    private string? RejectMotion(string reason, CoherentAircraft sample)
    {
        RejectedSamples++; LastIssue = reason; LastIssueAt = sample.SourceTime;
        recovery.Add(sample);
        while (recovery.Count > 4) recovery.RemoveAt(0);
        if (recovery.Count == 4 && (recovery[^1].SourceTime - recovery[0].SourceTime).TotalSeconds >= (FastTelemetry ? 0.25 : 3))
        {
            var courses = new List<double>(); var speeds = new List<double>();
            for (int i = 1; i < recovery.Count; i++)
            {
                var a = recovery[i-1]; var b = recovery[i]; var elapsed = (b.SourceTime-a.SourceTime).TotalSeconds;
                if (elapsed is <= 0 or > 2) return reason;
                var (n, e) = Displacement(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                speeds.Add(Math.Sqrt(n*n+e*e) * 3600 / 1852 / elapsed);
                courses.Add(Math.Atan2(e,n) * 180 / Math.PI);
                if (Math.Abs(b.RawAltitude-a.RawAltitude) / 0.3048 * 60 / elapsed > 10000) return reason;
            }
            if (speeds.All(v => v is > 40 and < 1500) && speeds.Max() / speeds.Min() < 1.5
                && courses.All(c => Math.Abs(FormationGeometry.Angle(c-courses[0])) < 15))
            {
                // A persistent server correction needs a fresh local trajectory, never a velocity across the jump.
                history.Clear(); history.AddRange(recovery); recovery.Clear();
                Latest = sample; AcceptedSamples++; Estimate(); UpdateGroundOrbitTarget(sample); LastIssue = null; LastIssueAt = null;
                return null;
            }
        }
        return reason;
    }
    private void Estimate()
    {
        if (history.Count < 4 || (history[^1].SourceTime - history[0].SourceTime).TotalSeconds < (FastTelemetry ? 0.25 : 3))
        { GroundSpeedKnots = null; GroundTrackDegrees = null; VerticalSpeedFpm = null; SmoothedPosition = null; return; }
        var origin = history[0];
        var values = history.Select(p =>
        {
            var (north, east) = Displacement(origin.Latitude, origin.Longitude, p.Latitude, p.Longitude);
            return (Time: (p.SourceTime - origin.SourceTime).TotalSeconds, North: north, East: east);
        }).ToArray();
        var meanT = values.Average(p => p.Time); var meanN = values.Average(p => p.North); var meanE = values.Average(p => p.East);
        var denominator = values.Sum(p => Math.Pow(p.Time - meanT, 2));
        var northMps = values.Sum(p => (p.Time - meanT) * (p.North - meanN)) / denominator;
        var eastMps = values.Sum(p => (p.Time - meanT) * (p.East - meanE)) / denominator;
        var meanAlt = history.Average(p => p.RawAltitude);
        VerticalSpeedFpm = history.Sum(p => ((p.SourceTime - origin.SourceTime).TotalSeconds - meanT) * (p.RawAltitude - meanAlt)) / denominator * 60 / 0.3048;
        var newestTime = values[^1].Time;
        SmoothedPosition = FormationGeometry.Offset(new(origin.Latitude, origin.Longitude, 0),
            (meanN + northMps * (newestTime - meanT)) / 1852, (meanE + eastMps * (newestTime - meanT)) / 1852,
            meanAlt / 0.3048 + VerticalSpeedFpm.Value * (newestTime-meanT) / 60);
        GroundSpeedKnots = Math.Sqrt(northMps * northMps + eastMps * eastMps) * 3600 / 1852;
        // Course is not meaningful at walking pace; never substitute the aircraft nose heading.
        GroundTrackDegrees = GroundSpeedKnots >= 3 ? (Math.Atan2(eastMps, northMps) * 180 / Math.PI + 360) % 360 : null;
    }
    private static (double North, double East) Displacement(double latitude, double longitude, double targetLatitude, double targetLongitude)
    {
        var lat1 = latitude * Math.PI / 180; var lat2 = targetLatitude * Math.PI / 180;
        var dlon = (targetLongitude - longitude) * Math.PI / 180;
        var bearing = Math.Atan2(Math.Sin(dlon) * Math.Cos(lat2), Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dlon));
        var meters = new Position(latitude, longitude, 0).DistanceNm(new(targetLatitude, targetLongitude, 0)) * 1852;
        return (meters * Math.Cos(bearing), meters * Math.Sin(bearing));
    }
    public string StatusAt(DateTimeOffset now)
    {
        if (Latest == null) return $"Searching for {targetName}";
        var age = (now - Latest.SourceTime).TotalSeconds;
        if (LastIssue != null && age <= 5) return "DATA HOLD — filtering multiplayer anomaly";
        if (age > 5) return "TARGET LOST";
        if (age > 2) return "TARGET DATA DELAYED";
        if (!GroundSpeedKnots.HasValue) return "ACQUIRING MOTION";
        // MSFS returned isOnGround=true during TrymTube's confirmed departure/climb.
        // Preserve that flag in raw logs, but never use it to classify flight phase.
        return "MOTION TRACKING";
    }
    public void Reset()
    {
        history.Clear(); TargetId = null; MatchById = false; Latest = null; GroundSpeedKnots = null; GroundTrackDegrees = null;
        VerticalSpeedFpm = null; SmoothedPosition = null; GroundOrbitTarget = null; recovery.Clear(); LastIssue = null; LastIssueAt = null;
        AcceptedSamples = 0; RejectedSamples = 0;
    }
    public void ResetMotion()
    {
        var id = TargetId; var byId = MatchById;
        Reset(); TargetId = id; MatchById = byId;
    }
}
