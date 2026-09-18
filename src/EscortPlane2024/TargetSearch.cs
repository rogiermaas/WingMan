namespace EscortPlane2024;

// Search matches never engage control. Diagnostic focus requires an exact name match.
internal static class TargetSearch
{
    public const string FocusName = "TrymTube";
    public static readonly string[] Names = [FocusName];
    public static string? Match(string text) => Names.FirstOrDefault(name =>
        text.Contains(name, StringComparison.OrdinalIgnoreCase));
    public static string Description => string.Join(", ", Names);
}
