namespace EscortPlane2024;

// Separates flight guidance from the native transport; the same controller runs against a deterministic test session.
internal interface IFollowerSession
{
    bool Connected { get; }
    bool Paused { get; }
    bool CameraActive { get; }
    OwnTelemetry? Own { get; }
    FlightTelemetry? Flight { get; }
    TargetEstimator Focus { get; }
    string OwnTitle { get; }
    bool Pmdg737Identified { get; }
    PmdgState? PmdgState { get; }
    AutopilotReadback? StandardState { get; }
    void SendSelection(string axis, double value, bool mach, bool pmdg);
    void RequestPmdgVs();
    void RequestStandardVs();
}
