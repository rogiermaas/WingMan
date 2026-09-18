namespace EscortPlane2024;

// Smart-camera indices are UI targets, not SimObject Object IDs or positions.
internal sealed record SmartCameraTarget(int Index, int Type, string Description, DateTimeOffset ObservedAt);
internal sealed class SmartCameraTracker
{
    private readonly Dictionary<int, SmartCameraTarget> targets = [];
    public int ReportedCount { get; private set; }
    public IEnumerable<SmartCameraTarget> Targets => targets.Values.OrderBy(t => t.Index);
    public void BeginSnapshot(int count)
    {
        ReportedCount = count;
        targets.Clear();
    }
    public void Seen(int index, int type, string description) => targets[index] = new(index, type, description, DateTimeOffset.UtcNow);
    public void Clear() { ReportedCount = 0; targets.Clear(); }
}
