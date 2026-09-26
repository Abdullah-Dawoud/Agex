using System.Text;

namespace AutodeskAIBridge.Core;

/// <summary>Small rotating file logger with bounded entries and no payload logging.</summary>
public sealed class BridgeLogger
{
    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public BridgeLogger(string? directory = null, long maxBytes = 5 * 1024 * 1024)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutodeskAIBridge", "Logs");
        _maxBytes = maxBytes;
    }

    public void Write(string severity, string message, string? correlationId = null, string? requestId = null, string? application = null, string? document = null, string? tool = null, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var line = string.Join(" | ",
            DateTimeOffset.UtcNow.ToString("O"), severity, Safe(correlationId), Safe(requestId), Safe(application), Safe(document), Safe(tool), duration?.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) ?? "", message.Replace('\r', ' ').Replace('\n', ' '));
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "bridge.log");
            if (File.Exists(path) && new FileInfo(path).Length > _maxBytes)
            {
                var rotated = path + ".1";
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(path, rotated);
            }
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value!.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
}
