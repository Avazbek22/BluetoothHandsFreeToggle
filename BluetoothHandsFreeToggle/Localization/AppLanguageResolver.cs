using System.Globalization;
using System.Runtime.InteropServices;

namespace BluetoothHandsFreeToggle.Localization;

public static class AppLanguageResolver
{
    public static AppLanguage ResolveStartupLanguage(ILanguagePreferenceStore preferenceStore)
    {
        ArgumentNullException.ThrowIfNull(preferenceStore);
        return preferenceStore.Load() ?? DetectSystemLanguage();
    }

    public static AppLanguage DetectSystemLanguage()
    {
        var cultures = GetUserPreferredUiCultures()
            .Concat([
                CultureInfo.CurrentUICulture,
                CultureInfo.CurrentCulture,
                CultureInfo.InstalledUICulture
            ])
            .DistinctBy(culture => culture.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return DetectFromCultures(cultures);
    }

    public static AppLanguage DetectFromCultures(params CultureInfo?[] cultures)
    {
        foreach (var culture in cultures)
        {
            if (AppLanguageCatalog.TryResolveCulture(culture, out var language))
                return language;
        }

        return AppLanguage.English;
    }

    private static List<CultureInfo> GetUserPreferredUiCultures()
    {
        const uint muiLanguageName = 0x8;
        var cultures = new List<CultureInfo>();
        uint languageCount = 0;
        uint bufferLength = 0;

        try
        {
            _ = GetUserPreferredUILanguages(
                muiLanguageName,
                out languageCount,
                null,
                ref bufferLength);
            if (bufferLength == 0)
                return cultures;

            var buffer = new char[bufferLength];
            if (!GetUserPreferredUILanguages(
                    muiLanguageName,
                    out languageCount,
                    buffer,
                    ref bufferLength))
            {
                return cultures;
            }

            foreach (var languageName in new string(buffer)
                         .Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                CultureInfo? culture = null;
                try
                {
                    culture = CultureInfo.GetCultureInfo(languageName);
                }
                catch (CultureNotFoundException)
                {
                    // Ignore malformed or unavailable Windows language entries.
                }

                if (culture is not null)
                    cultures.Add(culture);
            }
        }
        catch (DllNotFoundException)
        {
            return cultures;
        }
        catch (EntryPointNotFoundException)
        {
            return cultures;
        }

        return cultures;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserPreferredUILanguages(
        uint flags,
        out uint numberOfLanguages,
        [Out] char[]? languagesBuffer,
        ref uint languagesBufferLength);
}
