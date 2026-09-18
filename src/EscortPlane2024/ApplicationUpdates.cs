using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EscortPlane2024;

internal sealed record ReleaseManifest(string Version, int Protocol, string File, string Sha256, long Size, string PublishedAt);
internal sealed record SignedRelease(string Payload, string Signature);
internal sealed record VerifiedRelease(ReleaseManifest Manifest, SignedRelease Envelope);
internal sealed record UpdateResume(string Server, string Room, string Lead, string Aircraft, DateTimeOffset Expires, bool WasFollowing)
{
    internal static string? StartupNonce;
    public static UpdateResume? Consume()
    {
        if (StartupNonce == null) return null;
        var value = UserPreferences.Get("UpdateResume"); UserPreferences.Remove("UpdateResume");
        if (value == null) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<ResumeTicket>(value, RelayClient.Json);
            return saved?.Nonce == StartupNonce && saved.Resume.WasFollowing && saved.Resume.Expires > DateTimeOffset.UtcNow ? saved.Resume : null;
        }
        catch (JsonException) { return null; }
    }
    internal sealed record ResumeTicket(string Nonce, UpdateResume Resume);
}
internal static class ApplicationUpdates
{
    public static string Version => typeof(Program).Assembly.GetName().Version!.ToString(3);
    private static readonly string Root = Path.Combine(UserPreferences.DataRoot, "updates");
    internal sealed record Job(int ProcessId, long ProcessStartTicks, string Executable, string Server, SignedRelease Release, UpdateResume Resume);
    public static VerifiedRelease Verify(SignedRelease envelope, string? publicKey = null)
    {
        if (envelope.Payload.Length > 16000 || envelope.Signature.Length > 2048) throw new InvalidDataException("Update metadata too large");
        var payload = Convert.FromBase64String(envelope.Payload); var signature = Convert.FromBase64String(envelope.Signature);
        if (publicKey == null)
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("update-public.pem") ?? throw new InvalidOperationException("Publisher key is missing");
            using var reader = new StreamReader(stream); publicKey = reader.ReadToEnd();
        }
        using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidDataException("Update signature does not match the publisher");
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(payload, RelayClient.Json) ?? throw new InvalidDataException("Empty update manifest");
        if (!System.Version.TryParse(manifest.Version, out var version) || version.Build < 0 || manifest.Protocol != 1
            || !Regex.IsMatch(manifest.File, @"^WingMan-\d+\.\d+\.\d+(?:\.\d+)?-win-x64\.zip$")
            || manifest.File != $"WingMan-{manifest.Version}-win-x64.zip" || !Regex.IsMatch(manifest.Sha256, "^[a-fA-F0-9]{64}$")
            || manifest.Size is <= 0 or > 300 * 1024 * 1024) throw new InvalidDataException("Unsupported update manifest");
        return new(manifest, envelope);
    }
    public static async Task<VerifiedRelease?> CheckAsync(Uri server)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await http.GetAsync(new Uri(server, "v1/update?version=" + Version));
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode(); if (response.Content.Headers.ContentLength > 20000) throw new InvalidDataException("Update response too large");
        var bytes = await response.Content.ReadAsByteArrayAsync(); if (bytes.Length > 20000) throw new InvalidDataException("Update response too large");
        var signed = JsonSerializer.Deserialize<SignedRelease>(bytes, RelayClient.Json) ?? throw new InvalidDataException("No update metadata");
        var update = Verify(signed);
        return System.Version.Parse(update.Manifest.Version) > System.Version.Parse(Version) ? update : null;
    }
    public static string Launch(VerifiedRelease update, Uri server, UpdateResume resume)
    {
        var dir = Path.Combine(Root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var executable = Environment.ProcessPath!;
        var helper = Path.Combine(dir, "WingMan.exe"); File.Copy(executable, helper);
        using var process = Process.GetCurrentProcess();
        var jobPath = Path.Combine(dir, "job.json");
        File.WriteAllText(jobPath, JsonSerializer.Serialize(new Job(process.Id, process.StartTime.ToUniversalTime().Ticks, executable, server.AbsoluteUri, update.Envelope, resume), RelayClient.Json));
        // Arguments come from local generated paths only, never from the relay manifest.
        var safeForCmd = !(helper + jobPath).Any(c => "%!&|<>^\"\r\n".Contains(c));
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (safeForCmd) { start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"); start.Arguments = $"/d /s /c \"\"{helper}\" --apply-update \"{jobPath}\"\""; }
        else { start.FileName = helper; start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(jobPath); }
        _ = Process.Start(start) ?? throw new IOException("Could not start update helper");
        return dir;
    }
    internal static void ExtractExecutable(string archive, string destination)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count != 1 || zip.Entries[0].FullName != "WingMan.exe" || zip.Entries[0].Length is <= 0 or > 300 * 1024 * 1024)
            throw new InvalidDataException("Update archive must contain only WingMan.exe");
        using var input = zip.Entries[0].Open(); using var output = File.Create(destination); var buffer = new byte[81920]; long total = 0;
        int count; while ((count = input.Read(buffer)) != 0) { total += count; if (total > 300L * 1024 * 1024) throw new InvalidDataException("Update too large"); output.Write(buffer, 0, count); }
    }
    public static int Apply(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!directory.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 2;
        try { ApplyAsync(path, directory).GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "update-error.txt"), ex.ToString()); return 1; }
    }
    private static async Task ApplyAsync(string path, string directory)
    {
        using var updateLock = new FileStream(Path.Combine(Root, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(path), RelayClient.Json) ?? throw new InvalidDataException("Invalid update job");
        var update = Verify(job.Release); if (System.Version.Parse(update.Manifest.Version) <= System.Version.Parse(Version)) throw new InvalidDataException("Refusing an older update");
        var server = RelayClient.ServerUri(job.Server); var zipPath = Path.Combine(directory, "update.zip");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
        using (var response = await http.GetAsync(new Uri(server, "releases/" + update.Manifest.File), HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode(); using var source = await response.Content.ReadAsStreamAsync(); using var dest = File.Create(zipPath);
            var buffer = new byte[81920]; long count = 0; int read;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0) { count += read; if (count > update.Manifest.Size) throw new InvalidDataException("Download exceeds signed size"); await dest.WriteAsync(buffer.AsMemory(0, read), timeout.Token); }
            if (count != update.Manifest.Size) throw new InvalidDataException("Incomplete update download");
        }
        using (var file = File.OpenRead(zipPath)) if (!Convert.ToHexString(SHA256.HashData(file)).Equals(update.Manifest.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update hash mismatch");
        var staged = Path.Combine(directory, "new.exe"); ExtractExecutable(zipPath, staged);
        var actual = FileVersionInfo.GetVersionInfo(staged);
        if (actual.FileMajorPart != System.Version.Parse(update.Manifest.Version).Major || actual.FileMinorPart != System.Version.Parse(update.Manifest.Version).Minor || actual.FileBuildPart != System.Version.Parse(update.Manifest.Version).Build)
            throw new InvalidDataException("Executable version differs from signed release");
        using var old = Process.GetProcessById(job.ProcessId);
        if (old.StartTime.ToUniversalTime().Ticks != job.ProcessStartTicks || !string.Equals(old.MainModule?.FileName, job.Executable, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Original process changed");
        var target = Path.GetFullPath(job.Executable); var backup = target + ".previous"; var stagedBeside = target + ".new";
        File.Copy(staged, stagedBeside, true); // Check write permission while the original app is still running.
        var handoffId = Guid.NewGuid().ToString("N");
        UserPreferences.Remove("UpdateHandoff");
        UserPreferences.Put("UpdateCloseRequest", JsonSerializer.Serialize(new CloseRequest(handoffId, job.ProcessId, DateTimeOffset.UtcNow.AddSeconds(40)), RelayClient.Json));
        if (!old.CloseMainWindow()) { UserPreferences.Remove("UpdateCloseRequest"); throw new IOException("Could not request the old app to close"); }
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30))) await old.WaitForExitAsync(timeout.Token);
        var nonce = Guid.NewGuid().ToString("N"); var ready = Path.Combine(directory, "ready.txt");
        var handoffText = UserPreferences.Get("UpdateHandoff"); UserPreferences.Remove("UpdateHandoff"); UserPreferences.Remove("UpdateCloseRequest");
        var handoff = handoffText == null ? null : JsonSerializer.Deserialize<UpdateResume.ResumeTicket>(handoffText, RelayClient.Json);
        if (handoff?.Nonce == handoffId)
            UserPreferences.Put("UpdateResume", JsonSerializer.Serialize(new UpdateResume.ResumeTicket(nonce, handoff.Resume), RelayClient.Json));
        var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
        start.ArgumentList.Add("--resume-update"); start.ArgumentList.Add(nonce); start.ArgumentList.Add("--update-ready"); start.ArgumentList.Add(ready);
        Process? replacement = null;
        bool replaced = false;
        try
        {
            File.Replace(stagedBeside, target, backup, true); replaced = true;
            replacement = Process.Start(start) ?? throw new IOException("New app did not start");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!File.Exists(ready) && !replacement.HasExited && DateTimeOffset.UtcNow < deadline) await Task.Delay(200);
            if (!File.Exists(ready)) throw new IOException("New app did not confirm startup");
            UserPreferences.Put("InstalledVersion", update.Manifest.Version);
            File.WriteAllText(Path.Combine(directory, "result.txt"), "Update complete: " + update.Manifest.Version);
        }
        catch
        {
            if (replacement != null && !replacement.HasExited) { replacement.Kill(); await replacement.WaitForExitAsync(); }
            UserPreferences.Remove("UpdateResume"); if (replaced) File.Copy(backup, target, true);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = false }); throw;
        }
        finally { replacement?.Dispose(); }
    }
    internal sealed record CloseRequest(string Nonce, int ProcessId, DateTimeOffset Expires);
    internal static void CaptureHandoff(UpdateResume resume)
    {
        var text = UserPreferences.Get("UpdateCloseRequest"); if (text == null) return;
        try
        {
            var request = JsonSerializer.Deserialize<CloseRequest>(text, RelayClient.Json);
            if (request?.ProcessId == Environment.ProcessId && request.Expires > DateTimeOffset.UtcNow)
                UserPreferences.Put("UpdateHandoff", JsonSerializer.Serialize(new UpdateResume.ResumeTicket(request.Nonce, resume), RelayClient.Json));
        }
        catch (JsonException) { }
        UserPreferences.Remove("UpdateCloseRequest");
    }
    public static void Ready(string? path)
    {
        if (path == null) return;
        var full = Path.GetFullPath(path);
        if (full.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full) == "ready.txt") File.WriteAllText(full, Version);
    }
}
