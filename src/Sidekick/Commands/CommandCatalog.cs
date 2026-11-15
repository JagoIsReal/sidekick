using Sidekick.Localization;

namespace Sidekick.Commands;

internal sealed record CommandHelp(string Command, string Description);
internal sealed record CommandCategory(string Id, string Title, CommandHelp[] Commands, string? Help = null);

internal static class CommandCatalog
{
    internal const string MainCommand = "/sidekick";
    internal const string ShortCommand = "/sk";

    internal static CommandCategory[] Categories() =>
    [
        new("General", Strings.CommandsGeneral,
        [
            new(MainCommand, Strings.CommandOpen),
            new(ShortCommand, Strings.CommandOpen),
        ], Strings.CommandsAliasesHelp),
        new("Emotes", Strings.CommandsEmotes,
            [new($"{ShortCommand} cpose <N>", Strings.CommandCposeGeneric),
            .. Enumerable.Range(1, 7).Select(index => new CommandHelp($"{ShortCommand} cpose {index}",
                string.Format(Strings.Culture, Strings.CommandCpose, index)))], Strings.CommandsEmotesHelp),
        new("Focus", Strings.FocusTab,
        [
            new($"{ShortCommand} focus", Strings.CommandFocusToggle),
            new($"{ShortCommand} focus on", Strings.CommandFocusOn),
            new($"{ShortCommand} focus off", Strings.CommandFocusOff),
        ]),
    ];
}
