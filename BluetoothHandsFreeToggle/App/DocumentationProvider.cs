using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.App;

public static class DocumentationProvider
{
    private static readonly Lazy<
        IReadOnlyDictionary<(DocumentationDocument Document, AppLanguage Language), string[]>>
        CachedDocuments = new(LoadDocuments);

    public static IReadOnlyList<string> GetLines(DocumentationDocument document)
        => GetLines(document, Text.CurrentLanguage);

    public static IReadOnlyList<string> GetLines(
        DocumentationDocument document,
        AppLanguage language)
    {
        try
        {
            return CachedDocuments.Value[(document, language)];
        }
        catch (Exception exception)
        {
            return
            [
                Text.Get("documentation.unavailable"),
                Text.Format("documentation.reason", exception.Message)
            ];
        }
    }

    private static Dictionary<
        (DocumentationDocument Document, AppLanguage Language),
        string[]> LoadDocuments()
        => new()
        {
            [(DocumentationDocument.Help, AppLanguage.English)] =
                LoadDocumentLines("Help", AppLanguage.English),
            [(DocumentationDocument.Help, AppLanguage.Russian)] =
                LoadDocumentLines("Help", AppLanguage.Russian),
            [(DocumentationDocument.About, AppLanguage.English)] =
                LoadDocumentLines("About", AppLanguage.English),
            [(DocumentationDocument.About, AppLanguage.Russian)] =
                LoadDocumentLines("About", AppLanguage.Russian)
        };

    private static string[] LoadDocumentLines(
        string documentName,
        AppLanguage language)
    {
        var languageCode = AppLanguageCode.ToCode(language);
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
}

public enum DocumentationDocument
{
    Help = 0,
    About = 1
}
