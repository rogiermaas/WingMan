using System.Diagnostics;
namespace EscortPlane2024;

internal static class Program
{
    internal static bool DemoMode;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update") return ApplicationUpdates.Apply(args[1]);
        ApplicationConfiguration.Initialize();
        DemoMode = args.Contains("--ui-demo") || Environment.GetEnvironmentVariable("WINGMAN_OFFLINE_TEST") == "1";
        if (DemoMode && Environment.GetEnvironmentVariable("WINGMAN_TEST_PROFILE") == null) Environment.SetEnvironmentVariable("WINGMAN_TEST_PROFILE", "ui-preview");
        NativeSimConnect.Initialize();
        using var log = new Diagnostics();
        try
        {
            if (args.Contains("--self-test")) return SelfTests.Run(log);
            if (args.Contains("--runtime-test")) return RuntimeTests.Integration(log).GetAwaiter().GetResult();
            var relayTest = Array.IndexOf(args, "--relay-test");
            if (relayTest >= 0) return NetworkTests.Integration(args[relayTest + 1], log, args.Contains("--public-discovery")).GetAwaiter().GetResult();
            var replayIndex = Array.IndexOf(args, "--replay-focus");
            if (replayIndex >= 0) return ReplayFocus(args[replayIndex + 1], log);
            if (args.Contains("--probe")) return RunProbe(args, log);
            using var instance = new Mutex(true, @"Local\WingMan.MainWindow." + (Environment.GetEnvironmentVariable("WINGMAN_TEST_PROFILE") ?? "pilot"), out var firstInstance);
            if (!firstInstance)
            {
                foreach (var process in System.Diagnostics.Process.GetProcessesByName("WingMan"))
                {
                    using (process)
                    {
                        if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                        if (IsIconic(process.MainWindowHandle)) ShowWindowAsync(process.MainWindowHandle, 9);
                        SetForegroundWindow(process.MainWindowHandle);
                        break;
                    }
                }
                log.Write("existing_instance_activated", new { });
                return 0;
            }
            var screenshotIndex = Array.IndexOf(args, "--screenshot");
            var resumeIndex = Array.IndexOf(args, "--resume-update");
            UpdateResume.StartupNonce = resumeIndex >= 0 && resumeIndex + 1 < args.Length ? args[resumeIndex + 1] : null;
            var readyIndex = Array.IndexOf(args, "--update-ready");
            using var form = new MainForm(log, screenshotIndex >= 0 && screenshotIndex + 1 < args.Length ? args[screenshotIndex + 1] : null, args.Contains("--coherent-bridge"));
            form.Shown += (_, _) => ApplicationUpdates.Ready(readyIndex >= 0 && readyIndex + 1 < args.Length ? args[readyIndex + 1] : null);
            var updateTest = Array.IndexOf(args, "--update-test");
            if (updateTest >= 0 && DemoMode)
                form.Shown += async (_, _) =>
                {
                    var server = RelayClient.ServerUri(args[updateTest + 1]);
                    if (!server.IsLoopback) throw new ArgumentException("Update tests require localhost");
                    var update = await ApplicationUpdates.CheckAsync(server) ?? throw new InvalidOperationException("No test update available");
                    // The real close handler must replace this stale intent with its current inactive state.
                    var directory = ApplicationUpdates.Launch(update, server, new(server.AbsoluteUri, "old-room", "old-lead", "old-aircraft", DateTimeOffset.UtcNow.AddMinutes(10), true));
                    File.WriteAllText(Path.Combine(UserPreferences.DataRoot, "test-update-directory.txt"), directory);
                };
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            log.Write("fatal_error", new { ex.Message, ex.StackTrace });
            if (!args.Contains("--probe") && !args.Contains("--self-test") && !args.Contains("--replay-focus")) MessageBox.Show(ex.Message, "WingMan", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    private static int RunProbe(string[] args, Diagnostics log)
    {
        using var sim = new SimConnectManager(log) { IncludeAllObjects = args.Contains("--all-objects") };
        using var bridge = new CoherentBridgeSession();
        var bridgeStarted = false;
        var seconds = 20;
        var index = Array.IndexOf(args, "--seconds");
        if (index >= 0 && index + 1 < args.Length) seconds = Math.Clamp(int.Parse(args[index + 1]), 5, 300);
        var radius = 10.0;
        var radiusIndex = Array.IndexOf(args, "--radius");
        if (radiusIndex >= 0 && radiusIndex + 1 < args.Length) radius = double.Parse(args[radiusIndex + 1], System.Globalization.CultureInfo.InvariantCulture);
        sim.Connect();
        var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
        var nextScan = DateTimeOffset.MinValue;
        var probed = new HashSet<uint>();
        var cameraStarted = false;
        var restoreIndex = Array.IndexOf(args, "--restore-camera-state");
        double? restoreState = restoreIndex >= 0 && restoreIndex + 1 < args.Length ? double.Parse(args[restoreIndex + 1], System.Globalization.CultureInfo.InvariantCulture) : null;
        var targetIndex = Array.IndexOf(args, "--camera-target");
        uint? targetId = targetIndex >= 0 && targetIndex + 1 < args.Length ? uint.Parse(args[targetIndex + 1]) : null;
        while (DateTimeOffset.UtcNow < until && sim.IsOpen)
        {
            sim.Pump();
            if (args.Contains("--coherent-bridge") && sim.Connected && !bridgeStarted)
            {
                bridge.StartAsync().GetAwaiter().GetResult(); bridgeStarted = true;
                sim.PmdgViewDetected = bridge.PmdgInstrumentDetected;
                sim.WorkingTitle787Detected = bridge.WorkingTitle787Detected;
                log.Write("coherent_bridge_started", new { bridge.ViewName, renewableLeaseSeconds = 30 });
            }
            if (bridgeStarted) bridge.MaintainAsync().GetAwaiter().GetResult();
            if (restoreState is double state && sim.Own != null) { sim.RestoreCameraState(state); restoreState = null; }
            if (sim.Connected && DateTimeOffset.UtcNow >= nextScan)
            {
                sim.Scan(radius); nextScan = DateTimeOffset.UtcNow.AddSeconds(5);
            }
            foreach (var aircraft in sim.Traffic.Aircraft.ToArray())
                if (probed.Add(aircraft.ObjectId)) sim.ProbeDirectPosition(aircraft.ObjectId);
            if (!cameraStarted && sim.Own is { } own && (args.Contains("--passive-camera") || targetId.HasValue))
            {
                var passive = args.Contains("--passive-camera");
                if (passive || targetId == 0 || sim.Traffic.Aircraft.Any(a => a.ObjectId == targetId))
                {
                    sim.Camera.Start(targetId ?? 0, own, passive); cameraStarted = true;
                }
            }
            Thread.Sleep(20);
        }
        if (bridgeStarted) bridge.StopAsync().GetAwaiter().GetResult();
        log.Write("probe_summary", new { sim.Connected, sim.OwnSampleCount, sim.ExceptionCount, discovered = probed.Count, aircraft = sim.Traffic.Aircraft.ToArray(), coherentSamples = sim.CoherentTraffic.SampleCount, coherentAircraft = sim.CoherentTraffic.Aircraft.ToArray() });
        return sim.OwnSampleCount > 0 ? 0 : 2;
    }
    private static int ReplayFocus(string path, Diagnostics log)
    {
        var estimator = new TargetEstimator(TargetSearch.FocusName);
        int inputs = 0, moving = 0, stationary = 0;
        foreach (var line in File.ReadLines(path))
        {
            using var document = System.Text.Json.JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("kind").GetString() != "coherent_traffic") continue;
            var sample = System.Text.Json.JsonSerializer.Deserialize<CoherentAircraft>(document.RootElement.GetProperty("data"));
            if (sample == null || !string.Equals(sample.Name, TargetSearch.FocusName, StringComparison.OrdinalIgnoreCase)) continue;
            inputs++;
            var rejection = estimator.Observe(sample);
            if (estimator.GroundSpeedKnots >= 3) moving++;
            else if (estimator.GroundSpeedKnots is not null) stationary++;
            log.Write("focus_replay_sample", new { sample.SourceTime, rejection, estimator.GroundSpeedKnots, estimator.GroundTrackDegrees });
        }
        log.Write("focus_replay_summary", new { inputs, moving, stationary, estimator.AcceptedSamples, estimator.RejectedSamples, estimator.TargetId });
        return inputs >= 4 && estimator.AcceptedSamples >= 4 ? 0 : 2;
    }
}
