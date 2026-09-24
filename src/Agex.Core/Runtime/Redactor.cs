using System.Text.RegularExpressions;

namespace Agex.Core.Runtime;

/// <summary>
/// Removes secrets from text before it is logged, displayed in Diagnostics or
/// copied. Applied to every log line and every diagnostic export.
/// </summary>
public static partial class Redactor
{
    [GeneratedRegex(@"(?ix)
        (sk-(?:proj-|ant-)?[A-Za-z0-9_\-]{16,})            # OpenAI / Anthropic keys
      | (gh[pousr]_[A-Za-z0-9]{20,})                        # GitHub tokens
      | (github_pat_[A-Za-z0-9_]{20,})
      | (AIza[0-9A-Za-z_\-]{30,})                           # Google API keys
      | (xox[baprs]-[A-Za-z0-9\-]{10,})                      # Slack tokens
      | (AKIA[0-9A-Z]{16})                                   # AWS access key id
      | (-----BEGIN[ A-Z]*PRIVATE\ KEY-----[\s\S]*?-----END[ A-Z]*PRIVATE\ KEY-----)
      | ((?<=\bBearer\s)[A-Za-z0-9._~+/=\-]{12,})
      | ((?<=(?:api[_-]?key|token|secret|password|passwd|authorization)[""']?\s*[:=]\s*[""']?)[^\s""',;]{6,})
    ")]
    private static partial Regex SecretPattern();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return SecretPattern().Replace(text, "[REDACTED]");
    }

    /// <summary>Replaces the user's home folder with "~" so shared diagnostics do not reveal the account name.</summary>
    public static string RedactPaths(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home) || string.IsNullOrEmpty(text)) return text;
        return text.Replace(home, "~", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    public static string ForSharing(string? text) => RedactPaths(Redact(text));
}
