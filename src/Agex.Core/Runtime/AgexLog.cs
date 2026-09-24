using System.Text.Json;

namespace Agex.Core.Runtime;

/// <summary>
/// Structured local log: one JSON object per line, one file per day, secrets
/// redacted, old files deleted. Logs never leave the machine.
/// </summary>
public sealed class AgexLog
{
    private readonly string _directory;
    private readonly int _keepFiles;
    private readonly object _lock = new();
    private bool _pruned;

    public AgexLog(string directory, int keepFiles = 30)
    {
        _directory = directory;
        _keepFiles = keepFiles;
    }

    public string CurrentFile => Path.Combine(_directory, $"agex-{DateTime.UtcNow:yyyyMMdd}.log");
    public string Directory => _directory;

    public void Write(string evt, object? data = null)
    {
        try
        {
            var line = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["at"] = DateTime.UtcNow.ToString("o"),
                ["event"] = evt,
                ["data"] = data,
            });
            line = Redactor.Redact(line);
            lock (_lock)
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.AppendAllText(CurrentFile, line + "\n");
                if (!_pruned) { _pruned = true; Prune(); }
            }
        }
        catch (Exception)
        {
            // Logging must never break a request (full disk, read-only folder).
        }
    }

    public void Error(string evt, Exception ex, object? data = null) =>
        Write(evt, new { error = ex.GetType().Name, message = ex.Message, detail = data });

    private void Prune()
    {
        var files = new DirectoryInfo(_directory).GetFiles("agex-*.log").OrderByDescending(file => file.Name).Skip(_keepFiles);
        foreach (var file in files) { try { file.Delete(); } catch (IOException) { } }
    }
}
