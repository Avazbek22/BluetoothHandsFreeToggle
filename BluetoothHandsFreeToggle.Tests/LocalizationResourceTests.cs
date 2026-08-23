using System.Text.Json;
using System.Text.RegularExpressions;
using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Localization;
using Xunit;

namespace BluetoothHandsFreeToggle.Tests;

public sealed partial class LocalizationResourceTests
{
    private static readonly string[] TechnicalTerms =
    [
        "BluetoothHandsFreeToggle", "BTAGService", "BthHFSrv", "Boosty",
        "A2DP", "HFP", "Win32", "[OK]", "[WARN]", "[ERROR]"
    ];

    [Fact]
    public void AllLanguageResourcesHaveIdenticalUniqueKeys()
    {
        var english = LoadResource("en.json");

        foreach (var definition in AppLanguageCatalog.All)
        {
            var translation = LoadResource($"{definition.ResourceCode}.json");
            Assert.Equal(
                english.Keys.OrderBy(key => key, StringComparer.Ordinal),
                translation.Keys.OrderBy(key => key, StringComparer.Ordinal));
            Assert.DoesNotContain(translation, item => string.IsNullOrWhiteSpace(item.Value));
            Assert.DoesNotContain(
                translation,
                item => item.Value.Contains("BHFT", StringComparison.Ordinal) ||
                        item.Value.Contains('\uFFFD'));
        }
    }

    [Fact]
    public void TranslationsUseTheSameFormatPlaceholders()
    {
        var english = LoadResource("en.json");

        foreach (var definition in AppLanguageCatalog.All)
        {
            var translation = LoadResource($"{definition.ResourceCode}.json");
            foreach (var (key, englishValue) in english)
            {
                var englishPlaceholders = ReadPlaceholders(englishValue);
                var translatedPlaceholders = ReadPlaceholders(translation[key]);

                Assert.True(
                    englishPlaceholders.SequenceEqual(translatedPlaceholders),
                    $"Placeholder mismatch for '{key}' in {definition.Code}: " +
                    $"EN=[{string.Join(",", englishPlaceholders)}], " +
                    $"translation=[{string.Join(",", translatedPlaceholders)}]");

                foreach (var term in TechnicalTerms.Where(term => englishValue.Contains(
                             term,
                             StringComparison.Ordinal)))
                {
                    Assert.Contains(term, translation[key], StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void EmbeddedDocumentationLoadsForEveryLanguage()
    {
        var resourceNames = typeof(DocumentationProvider).Assembly.GetManifestResourceNames();
        foreach (var definition in AppLanguageCatalog.All)
        {
            foreach (var document in Enum.GetValues<DocumentationDocument>())
            {
                var suffix = $".Docs.{document}.{definition.ResourceCode}.txt";
                Assert.Single(
                    resourceNames,
                    name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                var lines = DocumentationProvider.GetLines(document, definition.Language);
                Assert.True(lines.Count >= 3, $"{document}.{definition.Code} is incomplete.");
                Assert.DoesNotContain(lines, line => line.Contains(
                    "Documentation is unavailable",
                    StringComparison.Ordinal));
                Assert.DoesNotContain(
                    lines,
                    line => line.Contains("BHFT", StringComparison.Ordinal) ||
                            line.Contains('\uFFFD'));
                Assert.Contains(
                    lines,
                    line => line.Contains(
                        "https://github.com/Avazbek22/BluetoothHandsFreeToggle",
                        StringComparison.Ordinal));
            }
        }
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
