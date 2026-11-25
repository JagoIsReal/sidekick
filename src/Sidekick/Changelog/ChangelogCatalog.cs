using Sidekick.Localization;

namespace Sidekick.Changelog;

internal enum ChangeKind { Added, Changed, Removed }
internal sealed record ChangelogSection(ChangeKind Kind, string[] Items);
internal sealed record ChangelogRelease(Version Version, ChangelogSection[] Sections);

internal static class ChangelogCatalog
{
    // The current version comes from the binary, including a version selected by the release workflow.
    // Historical entries correspond to published releases; do not list intermediate local builds.
    internal static ChangelogRelease[] Releases(Version currentVersion) =>
    [
        new(currentVersion,
        [
            new(ChangeKind.Added, [Strings.ChangelogStatusRules]),
            new(ChangeKind.Changed, [Strings.ChangelogSettingsGear]),
        ]),
        new(new Version(0, 11, 2, 0),
        [
            new(ChangeKind.Added,
            [
                Strings.ChangelogEarlyApplication,
                Strings.ChangelogHistory,
            ]),
            new(ChangeKind.Changed, [Strings.ChangelogEarlyHelp]),
        ]),
        new(new Version(0, 11, 1, 0),
        [
            new(ChangeKind.Added,
            [
                Strings.ChangelogGlamourer,
                Strings.ChangelogProfiles,
                Strings.ChangelogConditions,
            ]),
            new(ChangeKind.Changed,
            [
                Strings.ChangelogMultipleSelections,
                Strings.ChangelogRuleEditors,
                Strings.ChangelogCtrlClick,
            ]),
        ]),
        new(new Version(0, 10, 2, 0),
        [
            new(ChangeKind.Added,
            [
                Strings.ChangelogChat,
                Strings.ChangelogFocus,
                Strings.ChangelogPoses,
                Strings.ChangelogPenumbra,
                Strings.ChangelogLanguages,
            ]),
            new(ChangeKind.Changed, [Strings.ChangelogOwnMentions]),
        ]),
    ];

    internal static string SectionTitle(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => Strings.ChangelogAdded,
        ChangeKind.Changed => Strings.ChangelogChanged,
        ChangeKind.Removed => Strings.ChangelogRemoved,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
