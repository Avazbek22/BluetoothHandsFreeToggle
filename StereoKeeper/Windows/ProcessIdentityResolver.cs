using System.Diagnostics;
using StereoKeeper.Core;

namespace StereoKeeper.Windows;

/// <summary>
/// Turns process IDs into the apps people recognise ("Discord", "Microsoft Teams") rather than
/// helper executables. One instance serves one diagnostic run and caches what it resolves.
/// </summary>
internal sealed class ProcessIdentityResolver
{
    // WebView2 apps (for example the new Microsoft Teams) record audio in msedgewebview2.exe.
    private const string EmbeddedBrowserImage = "msedgewebview2.exe";
    private const int MaxAncestorDepth = 8;

    private readonly Dictionary<int, AppIdentity> _identities = [];
    private Dictionary<int, int>? _parents;

    public AppIdentity Resolve(int processId)
    {
        if (_identities.TryGetValue(processId, out var cached))
            return cached;

        var direct = Inspect(processId);
        var identity = IsEmbeddedBrowser(direct.Identity.ExecutablePath)
            ? AttributeToHostApp(direct)
            : direct.Identity;

        _identities[processId] = identity;
        return identity;
    }

    private AppIdentity AttributeToHostApp(InspectedProcess child)
    {
        _parents ??= NativeProcessApi.GetParentProcessIds();

        var current = child;
        for (var depth = 0; depth < MaxAncestorDepth; depth++)
        {
            if (!_parents.TryGetValue(current.Identity.ProcessId, out var parentId) ||
                parentId <= 4 ||
                parentId == current.Identity.ProcessId)
            {
                break;
            }

            var parent = Inspect(parentId);

            // A "parent" created after its child is a recycled PID, not the real parent.
            if (parent.CreationTime == 0 ||
                current.CreationTime == 0 ||
                parent.CreationTime > current.CreationTime)
            {
                break;
            }

            if (!IsEmbeddedBrowser(parent.Identity.ExecutablePath))
            {
                return child.Identity with
                {
                    Name = parent.Identity.Name,
                    ExecutablePath = parent.Identity.ExecutablePath,
                    PackageFamilyName = parent.Identity.PackageFamilyName
                };
            }

            current = parent;
        }

        return child.Identity;
    }

    private static InspectedProcess Inspect(int processId)
    {
        string? path = null;
        string? packageFamilyName = null;
        long creationTime = 0;

        using (var process = NativeProcessApi.Open(NativeProcessApi.ProcessQueryLimitedInformation, processId))
        {
            if (!process.IsInvalid)
            {
                path = NativeProcessApi.TryGetImagePath(process, out _);
                packageFamilyName = NativeProcessApi.TryGetPackageFamilyName(process);
                _ = NativeProcessApi.TryGetCreationTime(process, out creationTime);
            }
        }

        return new InspectedProcess(
            new AppIdentity(processId, GetDisplayName(processId, path), path, packageFamilyName),
            creationTime);
    }

    private static string GetDisplayName(int processId, string? executablePath)
    {
        if (executablePath is not null)
        {
            try
            {
                var version = FileVersionInfo.GetVersionInfo(executablePath);
                var described = Clean(version.FileDescription) ?? Clean(version.ProductName);
                if (described is not null)
                    return described;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Fall back to the file name below.
            }

            return Path.GetFileNameWithoutExtension(executablePath);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return $"PID {processId}";
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsEmbeddedBrowser(string? executablePath)
        => executablePath is not null &&
           string.Equals(Path.GetFileName(executablePath), EmbeddedBrowserImage, StringComparison.OrdinalIgnoreCase);

    private sealed record InspectedProcess(AppIdentity Identity, long CreationTime);
}
