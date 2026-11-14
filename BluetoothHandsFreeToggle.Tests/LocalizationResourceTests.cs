using System.Text.Json;
using System.Text.RegularExpressions;
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
    public void EveryLocalizationKeyUsedInCodeExistsInEnglish()
    {
        var english = LoadResource("en.json");
        var prefixes = english.Keys
            .Select(key => key[..(key.IndexOf('.', StringComparison.Ordinal) + 1)])
            .ToHashSet(StringComparer.Ordinal);
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "BluetoothHandsFreeToggle");

        var missing = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => KeyLiteralRegex().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Where(key => prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(key => !key.EndsWith(".json", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(key => !english.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, $"Keys used in code but missing from en.json: {string.Join(", ", missing)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BluetoothHandsFreeToggle.sln")))
            directory = directory.Parent;

        return directory?.FullName
               ?? throw new InvalidOperationException("Repository root was not found.");
    }

    [GeneratedRegex(@"""([a-z]+\.[A-Za-z.]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex KeyLiteralRegex();

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
