using System.Reflection;
using System.Text.Json;

namespace QraConfig.Models;

public sealed record SettingsIconDefinition(
    string Id,
    string TitleRu,
    string MaterialSymbol,
    string ComposeImageVector,
    string SvgPath);

public static class SettingsIconContract
{
    private sealed record Document(int SchemaVersion, string ViewBox, IReadOnlyList<SettingsIconDefinition> Icons);

    public static IReadOnlyDictionary<string, SettingsIconDefinition> Icons { get; } = Load();

    public static SettingsIconDefinition Get(string id) =>
        Icons.TryGetValue(id, out var icon)
            ? icon
            : throw new InvalidOperationException($"Unknown settings icon id: {id}");

    private static IReadOnlyDictionary<string, SettingsIconDefinition> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QraConfig.IconContract.json")
            ?? throw new InvalidOperationException("Embedded settings icon contract is missing.");
        var document = JsonSerializer.Deserialize<Document>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Settings icon contract is invalid.");
        if (document.SchemaVersion != 1 || document.ViewBox != "0 0 24 24")
            throw new InvalidOperationException("Unsupported settings icon contract.");
        return document.Icons.ToDictionary(icon => icon.Id, StringComparer.Ordinal);
    }
}
