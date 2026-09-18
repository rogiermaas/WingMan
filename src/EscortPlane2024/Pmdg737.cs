using System.Runtime.InteropServices;
using static EscortPlane2024.NativeSimConnect;

namespace EscortPlane2024;

internal sealed record PmdgState(double Speed, int Heading, int Altitude, int VerticalSpeed,
    bool SpeedBlank, bool VsBlank, bool AtArmed, bool DisengageBar, bool SpeedMode, bool HeadingMode,
    bool AltHold, bool VsMode, bool Cmd, bool Powered, bool PitchControl, bool RollControl,
    bool ThrottleControl, int Model, DateTimeOffset ReceivedAt);

// Offsets and event numbers from installed PMDG_NG3_SDK.h (2025, default native alignment).
// tools/inspect-pmdg.py independently derives offsets and total size from that header.
internal sealed class Pmdg737(Diagnostics log, Func<nint> handle, Action<int, string> check)
{
    public const uint DataDefinition = 700, Request = 700, DataId = 700;
    public const int DataSize = 916;
    public const uint Ias = 84134, Mach = 84135, Heading = 84136, Altitude = 84137, Vs = 84138;
    public const uint SpeedButton = 70014, HeadingButton = 70024, VsButton = 70027;
    public PmdgState? State { get; private set; }
    public void Initialize()
    {
        check(SimConnect_MapClientDataNameToID(handle(), "PMDG_NG3_Data", DataId), "PMDG map telemetry");
        check(SimConnect_AddToClientDataDefinition(handle(), DataDefinition, 0, DataSize, 0, uint.MaxValue), "PMDG define telemetry");
        check(SimConnect_RequestClientData(handle(), DataId, Request, DataDefinition, 4, 0, 0, 0, 0), "PMDG request telemetry");
        foreach (var id in new[] { Ias, Mach, Heading, Altitude, Vs, SpeedButton, HeadingButton, VsButton })
            check(SimConnect_MapClientEventToSimEvent(handle(), id, $"#{id}"), $"PMDG map event {id}");
    }
    public void Receive(nint data, uint size)
    {
        if (size < 40 + DataSize) { log.Write("pmdg_packet_rejected", new { size }); return; }
        var bytes = new byte[DataSize]; Marshal.Copy(data + 40, bytes, 0, DataSize);
        State = Decode(bytes, DateTimeOffset.UtcNow);
        if (State != null) log.Write("pmdg_telemetry", State);
    }
    internal static PmdgState? Decode(byte[] b, DateTimeOffset time)
    {
        if (b.Length < DataSize) return null;
        int U(int offset) => BitConverter.ToUInt16(b, offset);
        bool B(int offset) => b[offset] == 1;
        var speed = BitConverter.ToSingle(b, 420);
        var model = U(654);
        if (!float.IsFinite(speed) || speed is < 0 or > 999 || U(428) > 360 || U(430) > 50000 || model is < 1 or > 22) return null;
        return new(speed, U(428), U(430), BitConverter.ToInt16(b, 432), B(424), B(434), B(437), B(439),
            B(444), B(447), B(451), B(452), B(453) || B(455), B(457), B(31), B(32), B(30), model, time);
    }
    public void Send(uint id, uint value)
    {
        check(SimConnect_TransmitClientEvent(handle(), 0, id, value, 1, 0x10), $"Follower PMDG event {id}={value}");
        log.Write("autopilot_command", new { id, value, adapter = "PMDG 737 NG3 SDK" });
    }
    public void Press(uint id) => Send(id, 0x20000000);
    public void Clear() => State = null;
}
