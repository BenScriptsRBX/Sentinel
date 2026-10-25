using System.Reflection;
using System.Text.Json;

namespace Sentinel.Core;

public static class EmbeddedData
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static Assembly Asm => typeof(EmbeddedData).Assembly;

    /// <summary>Deserialises every embedded resource whose name starts with <paramref name="prefix"/>.</summary>
    public static IEnumerable<(string Name, T Value)> LoadAll<T>(string prefix)
    {
        foreach (var name in Asm.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order())
        {
            using var stream = Asm.GetManifestResourceStream(name)!;
            T? value;
            try { value = JsonSerializer.Deserialize<T>(stream, Json); }
            catch (JsonException ex) { throw new InvalidDataException($"Bad embedded data file {name}: {ex.Message}", ex); }
            if (value is not null) yield return (name, value);
        }
    }

    public static T Load<T>(string resource) =>
        LoadAll<T>(resource).Select(x => x.Value).FirstOrDefault()
        ?? throw new FileNotFoundException($"Embedded resource {resource} missing");
}
