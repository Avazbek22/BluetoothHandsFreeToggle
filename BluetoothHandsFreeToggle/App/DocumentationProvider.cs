using System.Collections.Concurrent;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.App;

public static class DocumentationProvider
{
    private static readonly ConcurrentDictionary<
        (DocumentationDocument Document, AppLanguage Language),
        string[]> CachedDocuments = new();

    public static IReadOnlyList<string> GetLines(DocumentationDocument document)
        => GetLines(document, Text.CurrentLanguage);

    public static IReadOnlyList<string> GetLines(
        DocumentationDocument document,
        AppLanguage language)
    {
        try
        {
            return CachedDocuments.GetOrAdd(
                (document, language),
                key => LoadDocumentLines(key.Document, key.Language));
        }
        catch (Exception exception) when (language is not AppLanguage.English)
        {
            try
            {
                return CachedDocuments.GetOrAdd(
                    (document, AppLanguage.English),
                    key => LoadDocumentLines(key.Document, key.Language));
            }
            catch
            {
                return BuildUnavailableDocument(exception);
            }
        }
        catch (Exception exception)
        {
            return BuildUnavailableDocument(exception);
        }
    }

    private static string[] LoadDocumentLines(
        DocumentationDocument document,
        AppLanguage language)
    {
        var documentName = document.ToString();
        var languageCode = AppLanguageCatalog.Get(language).ResourceCode;
        var assembly = typeof(DocumentationProvider).Assembly;
        var suffix = $".Docs.{documentName}.{languageCode}.txt";
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(
                suffix,
                StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(resourceName))
            throw new InvalidOperationException($"Embedded document '{suffix}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException(
                               $"Embedded document '{suffix}' could not be opened.");
        using var reader = new StreamReader(stream);

        return reader
            .ReadToEnd()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }

    private static string[] BuildUnavailableDocument(Exception exception)
        =>
        [
            Text.Get("documentation.unavailable"),
            Text.Format("documentation.reason", exception.Message)
        ];
}

public enum DocumentationDocument
{
    Help = 0,
    About = 1
}
