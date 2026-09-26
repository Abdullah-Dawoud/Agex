using System.Security.Cryptography;
using System.Text;

namespace AutodeskAIBridge.Core;

/// <summary>Per-user settings shared by Host and Autodesk plug-ins.</summary>
public sealed record BridgeRuntimeSettings(string PipeName, string SharedSecret)
{
    public const string ProductFolder = "AutodeskAIBridge";
    public static string RootDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolder);
    public static string SecretPath => Path.Combine(RootDirectory, "config", "ipc-secret");
    public static string PipePath => Path.Combine(RootDirectory, "config", "ipc-pipe");
    public static string LogDirectory => Path.Combine(RootDirectory, "Logs");

    public static BridgeRuntimeSettings Ensure()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretPath)!);
        var secret = File.Exists(SecretPath) ? File.ReadAllText(SecretPath).Trim() : string.Empty;
        if (secret.Length < 32)
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            secret = ToHex(bytes);
            WritePrivateFile(SecretPath, secret + Environment.NewLine);
        }

        var pipe = File.Exists(PipePath) ? File.ReadAllText(PipePath).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(pipe))
        {
            pipe = "AutodeskAIBridge-" + StableUserSuffix();
            WritePrivateFile(PipePath, pipe + Environment.NewLine);
        }
        return new BridgeRuntimeSettings(pipe, secret);
    }

    public static BridgeRuntimeSettings? TryLoad()
    {
        try
        {
            if (!File.Exists(SecretPath) || !File.Exists(PipePath)) return null;
            var secret = File.ReadAllText(SecretPath).Trim();
            var pipe = File.ReadAllText(PipePath).Trim();
            return secret.Length >= 32 && !string.IsNullOrWhiteSpace(pipe) ? new BridgeRuntimeSettings(pipe, secret) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void WritePrivateFile(string path, string contents)
    {
        var temp = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, contents, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    private static string StableUserSuffix()
    {
        var identity = Environment.UserDomainName + "\\" + Environment.UserName + "@" + Environment.MachineName;
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Substring(0, 16).ToLowerInvariant();
    }

    private static string ToHex(byte[] bytes)
        => BitConverter.ToString(bytes).Replace("-", string.Empty);
}
