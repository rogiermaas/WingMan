using System.Text.Json;

namespace EscortPlane2024;

internal sealed class Diagnostics : IDisposable
{
    private readonly StreamWriter writer;
    public string FilePath { get; }
    public Diagnostics()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "logs");
        try { Directory.CreateDirectory(folder); }
        catch (UnauthorizedAccessException)
        {
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WingMan", "logs");
            Directory.CreateDirectory(folder);
        }
        FilePath = Path.Combine(folder, $"WingMan-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}.jsonl");
        writer = new StreamWriter(FilePath, false) { AutoFlush = true };
    }
    private static readonly JsonSerializerOptions Options = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public void Write(string kind, object? data = null) => writer.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, kind, data }, Options));
    public void Dispose() => writer.Dispose();
}
