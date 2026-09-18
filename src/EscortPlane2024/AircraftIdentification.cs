namespace EscortPlane2024;

internal static class AircraftIdentification
{
    // The installed PMDG preset reports TITLE="737-800 PAX BW HD", without the vendor name.
    // Require separate vendor evidence from active PMDG instruments or its validated SDK data.
    public static bool IsPmdg737(string title, bool pmdgInstrument, bool sdkData) =>
        title.Contains("737", StringComparison.OrdinalIgnoreCase)
        && (title.Contains("PMDG", StringComparison.OrdinalIgnoreCase) || pmdgInstrument || sdkData);
}
