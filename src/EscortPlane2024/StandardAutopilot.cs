using static EscortPlane2024.NativeSimConnect;

namespace EscortPlane2024;

internal sealed record AutopilotReadback(double Ias, double Mach, double Heading, double Altitude, double Vs,
    bool MachMode, bool VsMode, bool Available, bool AtArmed, DateTimeOffset ReceivedAt)
{
    public string AtArmSource { get; init; } = "Generic SimConnect";
    public bool AtArmKnown { get; init; } = true;
    public bool MasterEngaged { get; init; }
    public bool AltitudeHold { get; init; }
    public string AutothrottleStatus => !AtArmKnown ? "Autothrottle arm state unavailable. Check the aircraft switches."
        : AtArmed ? "Autothrottle armed."
        : AtArmSource == "Generic SimConnect" ? "Simulator does not report autothrottle armed. Check the aircraft switches."
        : "Autothrottle is not armed.";
}

internal sealed class StandardAutopilot(Diagnostics log, Func<nint> handle, Action<int, string> check)
{
    public const uint Speed = 800, Mach = 801, Heading = 802, Altitude = 803, Vs = 804, VsOn = 805;
    public AutopilotReadback? State { get; set; }
    internal static AutopilotReadback ResolveAutothrottle(AutopilotReadback standard, string title, bool workingTitle787, double boeingArm)
    {
        // Installed WT 787 BoeingAPSimVarPublisher reads this switch state, while the
        // generic arm/active SimVars can remain false even with both cockpit switches armed.
        // Normal relay operation does not run the optional traffic/instrument bridge.
        // The stock 787-10's observed exact TITLE is sufficient identification here;
        // other titles still require active WT instrument evidence. Never apply this
        // global L-var to another aircraft merely because it retained a prior value.
        var stock787 = title.Trim().Equals("787-10", StringComparison.OrdinalIgnoreCase);
        if (!stock787 && (!workingTitle787 || !title.Contains("787", StringComparison.OrdinalIgnoreCase))) return standard;
        return standard with { AtArmed = boeingArm == 1, AtArmKnown = boeingArm is 0 or 1,
            AtArmSource = "WT 787 AS01B_AUTO_THROTTLE_ARM_STATE" };
    }
    public void Initialize()
    {
        foreach (var (id, name) in new[] { (Speed, "AP_SPD_VAR_SET"), (Mach, "AP_MACH_VAR_SET"),
            (Heading, "HEADING_BUG_SET"), (Altitude, "AP_ALT_VAR_SET_ENGLISH"), (Vs, "AP_VS_VAR_SET_ENGLISH"), (VsOn, "AP_PANEL_VS_ON") })
            check(SimConnect_MapClientEventToSimEvent(handle(), id, name), $"Standard AP map {name}");
    }
    public void Send(uint id, uint value)
    {
        // Index 0 copies selections to all slots. VsOn explicitly enables V/S;
        // AP master and autothrottle are never switched here.
        check(SimConnect_TransmitClientEvent_EX1(handle(), 0, id, 1, 0x10, value, 0, 0, 0, 0), $"Follower standard event {id}={value}");
        log.Write("autopilot_command", new { id, value, adapter = "Standard SimConnect", slot = 0 });
    }
}
