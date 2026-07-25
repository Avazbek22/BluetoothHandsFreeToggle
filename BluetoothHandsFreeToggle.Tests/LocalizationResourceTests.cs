using System.Text.Json;
using System.Text.RegularExpressions;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Localization;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed partial class LocalizationResourceTests
{
    [Fact]
    public void EnglishAndRussianResourcesHaveIdenticalUniqueKeys()
    {
        var english = LoadResource("en.json");
        var russian = LoadResource("ru.json");

        Assert.Equal(
            english.Keys.OrderBy(key => key, StringComparer.Ordinal),
            russian.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void TranslationsUseTheSameFormatPlaceholders()
    {
        var english = LoadResource("en.json");
        var russian = LoadResource("ru.json");

        foreach (var (key, englishValue) in english)
        {
            var englishPlaceholders = ReadPlaceholders(englishValue);
            var russianPlaceholders = ReadPlaceholders(russian[key]);

            Assert.True(
                englishPlaceholders.SequenceEqual(russianPlaceholders),
                $"Placeholder mismatch for '{key}': " +
                $"EN=[{string.Join(",", englishPlaceholders)}], " +
                $"RU=[{string.Join(",", russianPlaceholders)}]");
        }
    }

    [Theory]
    [InlineData(DocumentationDocument.Help, AppLanguage.English, "Modes:")]
    [InlineData(DocumentationDocument.Help, AppLanguage.Russian, "Режимы:")]
    [InlineData(DocumentationDocument.About, AppLanguage.English, "License:")]
    [InlineData(DocumentationDocument.About, AppLanguage.Russian, "Лицензия:")]
    public void EmbeddedDocumentationLoadsForEveryLanguage(
        DocumentationDocument document,
        AppLanguage language,
        string expectedLine)
    {
        var lines = DocumentationProvider.GetLines(document, language);

        Assert.Contains(lines, line => line.Contains(expectedLine, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains(
            "Documentation is unavailable",
            StringComparison.Ordinal));
    }

    private static Dictionary<string, string> LoadResource(string fileName)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(
                $".Localization.Resources.{fileName}",
                StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream);

        var properties = document.RootElement.EnumerateObject().ToList();
        var duplicate = properties
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        Assert.Null(duplicate);

        return properties.ToDictionary(
            property => property.Name,
            property => property.Value.GetString() ?? string.Empty,
            StringComparer.Ordinal);
    }

    private static string[] ReadPlaceholders(string value)
        => PlaceholderRegex()
            .Matches(value)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

    [GeneratedRegex(@"\{(\d+)(?:[^}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
