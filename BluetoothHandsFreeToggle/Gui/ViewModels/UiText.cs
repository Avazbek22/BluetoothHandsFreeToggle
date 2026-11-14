using BluetoothHandsFreeToggle.App;
using BluetoothHandsFreeToggle.Localization;

namespace BluetoothHandsFreeToggle.Gui.ViewModels;

/// <summary>
/// The fixed labels of the panel in the current language. A new instance replaces the old one on
/// a language change, which refreshes every label binding at once.
/// </summary>
public sealed class UiText
{
    public string AppTitle { get; } = "HandsFree Toggle";
    public string Settings { get; } = Text.Get("gui.settings");
    public string Back { get; } = Text.Get("gui.back");
    public string AdminRequired { get; } = Text.Get("gui.adminRequired");

    public string HardTitle { get; } = Text.Get("gui.hardCard.title");

    public string StepsTitle { get; } = Text.Get("gui.steps.title");
    public string MicTitle { get; } = Text.Get("gui.mic.title");
    public string MicRecording { get; } = Text.Get("gui.mic.recording");
    public string MicDefault { get; } = Text.Get("gui.mic.default");
    public string Change { get; } = Text.Get("gui.action.change");
    public string CopyReport { get; } = Text.Get("gui.copyReport");

    public string Version { get; } = Text.Format("gui.version", AppInfo.AppDisplayVersion);
    public string VersionNumber { get; } = AppInfo.AppDisplayVersion;
    public string Help { get; } = Text.Get("gui.help");
    public string Support { get; } = Text.Get("gui.support");

    public string General { get; } = Text.Get("gui.settings.general");
    public string Autostart { get; } = Text.Get("gui.settings.autostart");
    public string AutostartDescription { get; } = Text.Get("gui.settings.autostartDesc");
    public string Language { get; } = Text.Get("gui.settings.language");
    public string LanguageDescription { get; } = Text.Format("gui.settings.languageDesc", AppLanguageCatalog.All.Count);
    public string Notifications { get; } = Text.Get("gui.settings.notifications");
    public string NotifyCall { get; } = Text.Get("gui.settings.notifyCall");
    public string NotifyCallDescription { get; } = Text.Get("gui.settings.notifyCallDesc");
    public string NotifyMic { get; } = Text.Get("gui.settings.notifyMic");
    public string NotifyMicDescription { get; } = Text.Get("gui.settings.notifyMicDesc");
    public string About { get; } = Text.Get("gui.settings.about");
    public string VersionLabel { get; } = Text.Get("gui.settings.version");
    public string SourceCode { get; } = Text.Get("gui.settings.source");
    public string SupportProject { get; } = Text.Get("gui.settings.support");
    public string Quit { get; } = Text.Get("gui.settings.quit");

    public string HelpTitle { get; } = Text.Get("gui.help.title");
    public IReadOnlyList<HelpSection> HelpSections { get; } =
    [
        new(Text.Get("gui.help.why.title"), Text.Get("gui.help.why.body")),
        new(Text.Get("gui.help.fix.title"), Text.Get("gui.help.fix.body")),
        new(Text.Get("gui.help.hard.title"), Text.Get("gui.help.hard.body")),
        new(Text.Get("gui.help.mic.title"), Text.Get("gui.help.mic.body")),
        new(Text.Get("gui.help.undo.title"), Text.Get("gui.help.undo.body")),
        new(Text.Get("gui.help.safety.title"), Text.Get("gui.help.safety.body")),
        new(Text.Get("gui.help.details.title"), Text.Get("gui.help.details.body"), IsAside: true)
    ];
    public string HelpConsole { get; } = Text.Get("gui.help.console");
}

/// <summary>A help card; an aside (technical details) is drawn quieter than the main answers.</summary>
public sealed record HelpSection(string Title, string Body, bool IsAside = false);
