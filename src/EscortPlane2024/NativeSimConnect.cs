using System.Reflection;
using System.Runtime.InteropServices;

namespace EscortPlane2024;

// ABI verified against MSFS 2024 SDK 1.7.3 SimConnect.h (pack 1).
internal static class NativeSimConnect
{
    private const string Dll = "SimConnect.dll";
    public static string LoadedPath { get; private set; } = "Not loaded";
    public static bool CameraExportsAvailable { get; private set; }
    public static void Initialize() => NativeLibrary.SetDllImportResolver(typeof(NativeSimConnect).Assembly, Resolve);
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? search)
    {
        if (name != Dll) return 0;
        var handle = SimConnectRuntime.TryLoadAvailable();
        if (handle == 0) throw new DllNotFoundException("SimConnect is missing. WingMan will download it automatically when online.");
        LoadedPath = SimConnectRuntime.LoadedPath;
        CameraExportsAvailable = true;
        return handle;

    }
    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)]
    internal static extern int SimConnect_Open(out nint handle, string name, nint window, uint message, nint eventHandle, uint config);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_Close(nint handle);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_GetNextDispatch(nint handle, out nint data, out uint size);
    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)]
    internal static extern int SimConnect_AddToDataDefinition(nint handle, uint definition, string name, string? units, uint type, float epsilon, uint datum);
    [DllImport(Dll, ExactSpelling = true)]
    internal static extern int SimConnect_RequestDataOnSimObject(nint handle, uint request, uint definition, uint objectId, uint period, uint flags, uint origin, uint interval, uint limit);
    [DllImport(Dll, ExactSpelling = true)]
    internal static extern int SimConnect_RequestDataOnSimObjectType(nint handle, uint request, uint definition, uint radiusMeters, uint type);
    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)]
    internal static extern int SimConnect_SubscribeToSystemEvent(nint handle, uint eventId, string name);
    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)]
    internal static extern int SimConnect_SubscribeToCommBusEvent(nint handle, uint eventId, string name);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_GetLastSentPacketID(nint handle, out uint id);
    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)] internal static extern int SimConnect_MapClientEventToSimEvent(nint handle, uint eventId, string name);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_TransmitClientEvent(nint handle, uint objectId, uint eventId, uint data, uint priority, uint flags);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_TransmitClientEvent_EX1(nint handle, uint objectId, uint eventId, uint priority, uint flags, uint data0, uint data1, uint data2, uint data3, uint data4);
    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)] internal static extern int SimConnect_MapClientDataNameToID(nint handle, string name, uint id);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_AddToClientDataDefinition(nint handle, uint definition, uint offset, uint size, float epsilon, uint datum);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_RequestClientData(nint handle, uint id, uint request, uint definition, uint period, uint flags, uint origin, uint interval, uint limit);
    [DllImport(Dll, ExactSpelling = true)]
    internal static extern int SimConnect_SetDataOnSimObject(nint handle, uint definition, uint objectId, uint flags, uint arrayCount, uint unitSize, ref double data);
    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)] internal static extern int SimConnect_CameraAcquire(nint handle, string clientId);
    [DllImport(Dll, CharSet = CharSet.Ansi, ExactSpelling = true)] internal static extern int SimConnect_CameraRelease(nint handle, string cameraDefinition);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_CameraGetStatus(nint handle);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_CameraGet(nint handle, uint referential);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_CameraSet(nint handle, CameraData data, uint mask);
    [DllImport(Dll, ExactSpelling = true)] internal static extern int SimConnect_SubscribeToCameraStatusUpdate(nint handle);
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct CameraData
    {
        public double X, Y, Z;
        public uint PositionReferential, PositionObjectId;
        public double TargetX, TargetY, TargetZ;
        public float Pitch, Bank, Heading;
        public uint RotationReferential, RotationObjectId;
        public double Fov;
    }
}
