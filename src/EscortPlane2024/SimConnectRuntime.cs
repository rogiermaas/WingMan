using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace EscortPlane2024;

internal static class SimConnectRuntime
{
    internal const string Sha256 = "B10DE7ADF4C62E5F66C89DD6D01B64091BBBCEC83411CFE191D6B85FBEE61D15";
    internal const int ByteCount = 84480;
    internal static readonly Uri DownloadUrl = new("https://wingman.rogiermaas.nl/runtime/msfs2024-1.7.3/SimConnect.dll");
    private static nint loaded;
    internal static string LoadedPath { get; private set; } = "Not loaded";
    private static readonly string[] RequiredExports = ["SimConnect_Open", "SimConnect_Close", "SimConnect_GetNextDispatch", "SimConnect_SubscribeToCommBusEvent", "SimConnect_CameraGet", "SimConnect_CameraSet", "SimConnect_CameraAcquire"];
    internal static string DefaultSdkRoot => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "MSFS 2024 SDK");
    private static string InSdk(string root) => Path.Combine(root, "SimConnect SDK", "lib", "SimConnect.dll");
    internal static string UserSdkRoot => Path.Combine(UserPreferences.DataRoot, "MSFS 2024 SDK");

    internal static IEnumerable<string> Candidates()
    {
        if (UserPreferences.Get("SimConnectPath") is { Length: > 0 } saved) yield return saved;
        yield return Path.Combine(AppContext.BaseDirectory, "SimConnect.dll");
        if (Environment.GetEnvironmentVariable("MSFS2024_SDK") is { Length: > 0 } root) yield return InSdk(root);
        yield return InSdk(DefaultSdkRoot);
        yield return InSdk(UserSdkRoot);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            yield return InSdk(Path.Combine(drive.RootDirectory.FullName, "MSFS 2024 SDK"));
        yield return InSdk(@"G:\MSFS 2024 SDK Extracted\MSFS 2024 SDK");
    }
    internal static nint TryLoadAvailable()
    {
        if (loaded != 0) return loaded;
        foreach (var path in Candidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            if (TryLoadCompatible(path, out var handle)) { LoadedPath = path; loaded = handle; return loaded; }
        }
        return 0;
    }
    private static bool TryLoadCompatible(string path, out nint handle)
    {
        handle = 0;
        try
        {
            if (!NativeLibrary.TryLoad(path, out var candidate)) return false;
            if (RequiredExports.All(name => NativeLibrary.TryGetExport(candidate, name, out _))) { handle = candidate; return true; }
            NativeLibrary.Free(candidate);
        }
        catch (Exception ex) when (ex is BadImageFormatException or DllNotFoundException or IOException or UnauthorizedAccessException) { }
        return false;
    }
    internal static void Validate(byte[] bytes)
    {
        if (bytes.Length != ByteCount || Convert.ToHexString(SHA256.HashData(bytes)) != Sha256)
            throw new InvalidDataException("SimConnect download failed verification. Nothing was installed.");
    }
    internal static async Task<string> InstallAsync(HttpClient client, IEnumerable<string>? destinations = null, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } size && size != ByteCount) throw new InvalidDataException("Unexpected SimConnect download size.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        var bytes = new byte[ByteCount];
        await input.ReadExactlyAsync(bytes, timeout.Token);
        if (await input.ReadAsync(new byte[1], timeout.Token) != 0) throw new InvalidDataException("SimConnect download is too large.");
        Validate(bytes);
        var sdkRoot = Environment.GetEnvironmentVariable("MSFS2024_SDK");
        destinations ??= new[] { InSdk(string.IsNullOrWhiteSpace(sdkRoot) ? DefaultSdkRoot : sdkRoot), InSdk(UserSdkRoot) };
        Exception? lastError = null;
        foreach (var destination in destinations.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? temporary = null;
            try
            {
                if (File.Exists(destination))
                {
                    if (TryLoadCompatible(destination, out var existing)) { NativeLibrary.Free(existing); return destination; }
                    // Preserve existing SDK files; use the private fallback when a different runtime is present.
                    lastError = new IOException("An incompatible SimConnect runtime already exists at " + destination); continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
                await File.WriteAllBytesAsync(temporary, bytes, timeout.Token);
                File.Move(temporary, destination, false); temporary = null;
                return destination;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { lastError = ex; }
            finally { if (temporary != null) try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        throw new IOException("Could not install SimConnect in an accessible SDK folder.", lastError);
    }
}
