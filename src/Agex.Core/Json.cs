using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agex.Core;

/// <summary>JSON settings for every file AGEX stores (snake_case, enums as strings).</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = Create(indented: true);
    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        WriteIndented = indented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Reading untrusted files: bound nesting.
        MaxDepth = 64,
    };

    /// <summary>Writes JSON atomically (temp file then replace), so a crash never leaves a half-written file.</summary>
    public static void WriteFile<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options), new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    public static T? ReadFile<T>(string path) where T : class =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
}
