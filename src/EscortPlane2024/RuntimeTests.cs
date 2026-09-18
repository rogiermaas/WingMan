using System.Net;
using System.Runtime.InteropServices;

namespace EscortPlane2024;

internal static class RuntimeTests
{
    private sealed class Response(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    internal static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "WingMan-runtime-test-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "SimConnect.dll");
        foreach (var size in new[] { 1, SimConnectRuntime.ByteCount })
        {
            using var client = new HttpClient(new Response(new byte[size]));
            bool rejected = false;
            try { SimConnectRuntime.InstallAsync(client, [destination]).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected && !Directory.Exists(root), size == 1 ? "Wrong-sized runtime never reaches disk" : "Tampered runtime never reaches disk");
        }
    }
    internal static async Task<int> Integration(Diagnostics log)
    {
        var root = Path.Combine(Path.GetTempPath(), "WingMan-runtime-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var existing = Path.Combine(root, "existing", "SimConnect.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "Preserve this existing SDK file");
        var target = Path.Combine(root, "fallback", "SimConnect SDK", "lib", "SimConnect.dll");
        nint handle = 0;
        try
        {
            using var client = new HttpClient();
            var installed = await SimConnectRuntime.InstallAsync(client, [existing, target]);
            if (installed != target || await File.ReadAllTextAsync(existing) != "Preserve this existing SDK file") throw new Exception("Existing SDK file was not preserved");
            SimConnectRuntime.Validate(await File.ReadAllBytesAsync(target));
            handle = NativeLibrary.Load(target);
            foreach (var export in new[] { "SimConnect_Open", "SimConnect_SubscribeToCommBusEvent", "SimConnect_CameraGet" })
                if (!NativeLibrary.TryGetExport(handle, export, out _)) throw new Exception("Missing runtime export " + export);
            log.Write("runtime_integration_summary", new { passed = true, download = SimConnectRuntime.DownloadUrl, existingSdkPreserved = true, fallbackInstalled = true, nativeLoad = true });
            return 0;
        }
        finally
        {
            if (handle != 0) NativeLibrary.Free(handle);
            // Only files created by this test, under its unique temporary directory.
            var resolved = Path.GetFullPath(root);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("WingMan-runtime-install-", StringComparison.Ordinal)) throw new IOException("Unexpected test cleanup path");
            Directory.Delete(resolved, true);
        }
    }
}
