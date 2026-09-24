using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Agex.Core.Platform;

/// <summary>
/// Keeps each secret in its own file below <c>root</c>. Subclasses decide how
/// the bytes are protected. Only key names are kept in the plain index.
/// </summary>
internal abstract class FileBackedSecureStore(string root) : ISecureStore
{
    private readonly object _lock = new();
    public abstract string Mechanism { get; }
    public abstract bool IsOsProtected { get; }
    protected abstract byte[] Protect(byte[] plain);
    protected abstract byte[]? Unprotect(byte[] cipher);

    private string FileFor(string key) => Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant() + ".bin");
    private string IndexPath => Path.Combine(root, "index.json");

    public bool Set(string key, string value)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(root);
            SecureFile.RestrictDirectory(root);
            var path = FileFor(key);
            File.WriteAllBytes(path, Protect(Encoding.UTF8.GetBytes(value)));
            SecureFile.RestrictFile(path);
            var keys = Keys().ToList();
            if (!keys.Contains(key)) { keys.Add(key); File.WriteAllText(IndexPath, JsonSerializer.Serialize(keys)); }
            return true;
        }
    }

    public string? Get(string key)
    {
        lock (_lock)
        {
            var path = FileFor(key);
            if (!File.Exists(path)) return null;
            var plain = Unprotect(File.ReadAllBytes(path));
            return plain is null ? null : Encoding.UTF8.GetString(plain);
        }
    }

    public bool Delete(string key)
    {
        lock (_lock)
        {
            var path = FileFor(key);
            var existed = File.Exists(path);
            if (existed) File.Delete(path);
            var keys = Keys().Where(item => item != key).ToList();
            if (Directory.Exists(root)) File.WriteAllText(IndexPath, JsonSerializer.Serialize(keys));
            return existed;
        }
    }

    public IReadOnlyList<string> Keys()
    {
        try { return File.Exists(IndexPath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(IndexPath)) ?? [] : []; }
        catch (JsonException) { return []; }
    }
}

/// <summary>
/// Last-resort store for Linux systems without a Secret Service. Files are
/// readable only by the current user but are NOT encrypted; the UI says so.
/// </summary>
internal sealed class PlainFileSecureStore(string root) : FileBackedSecureStore(root)
{
    public override string Mechanism => "Private file (not encrypted: no system keyring found)";
    public override bool IsOsProtected => false;
    protected override byte[] Protect(byte[] plain) => plain;
    protected override byte[]? Unprotect(byte[] cipher) => cipher;
}

/// <summary>
/// Stores secrets through a command-line keyring tool (macOS <c>security</c>,
/// Linux <c>secret-tool</c>). Secret values travel on stdin, never in arguments.
/// </summary>
internal sealed class CommandKeyringStore : ISecureStore
{
    private readonly string _indexPath;
    private readonly Func<string, string, bool> _set;
    private readonly Func<string, string?> _get;
    private readonly Func<string, bool> _delete;
    private readonly object _lock = new();

    public CommandKeyringStore(string mechanism, string indexDirectory, Func<string, string, bool> set, Func<string, string?> get, Func<string, bool> delete)
    {
        Mechanism = mechanism;
        _indexPath = Path.Combine(indexDirectory, "keyring-index.json");
        _set = set; _get = get; _delete = delete;
    }

    public string Mechanism { get; }
    public bool IsOsProtected => true;

    public bool Set(string key, string value)
    {
        lock (_lock)
        {
            if (!_set(key, value)) return false;
            var keys = Keys().ToList();
            if (!keys.Contains(key)) { keys.Add(key); SaveIndex(keys); }
            return true;
        }
    }

    public string? Get(string key) { lock (_lock) { return _get(key); } }

    public bool Delete(string key)
    {
        lock (_lock)
        {
            var ok = _delete(key);
            SaveIndex(Keys().Where(item => item != key).ToList());
            return ok;
        }
    }

    public IReadOnlyList<string> Keys()
    {
        try { return File.Exists(_indexPath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_indexPath)) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private void SaveIndex(List<string> keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        File.WriteAllText(_indexPath, JsonSerializer.Serialize(keys));
    }
}

internal static class SecureFile
{
    public static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
