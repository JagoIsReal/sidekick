using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Sidekick.Chat;
using Sidekick.Localization;
using Sidekick.Player;
using Sidekick.Windows;
using Sidekick.Locations;
using Sidekick.Mods;
using Sidekick.Focus;
using Sidekick.Poses;
using Sidekick.Commands;
using Sidekick.Appearance;

namespace Sidekick;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = CommandCatalog.MainCommand;
    private const string ShortCommand = CommandCatalog.ShortCommand;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly WindowSystem windows = new("Sidekick");
    private readonly ConfigurationStore configuration;
    private readonly LocalCharacterTracker character;
    private readonly ChatSubscription chat;
    private readonly SettingsWindow settings;
    private readonly ModRuleController modRules;
    private readonly EarlyHousingReader earlyHousing;
    private readonly FocusController focus;
    private readonly CposeController cposes;
    private readonly GlamourerRuleController glamourerRules;
    private readonly IChatGui chatGui;
    private readonly bool shortCommandRegistered;
    private readonly CommandInfo commandInfo;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IChatGui chatGui,
        IClientState clientState, IPlayerState playerState, IFramework framework, IPluginLog log,
        IDataManager data, ICondition condition,
        IObjectTable objects, IGameConfig gameConfig)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.chatGui = chatGui;
        configuration = new ConfigurationStore(pluginInterface, log);
        Strings.Culture = LanguagePreference.CultureFor(configuration.Value.LanguageCode);
        commandInfo = new CommandInfo(OnCommand) { HelpMessage = Strings.CommandHelp };
        character = new LocalCharacterTracker(clientState, playerState, framework);
        var highlighter = new ChatHighlighter(configuration.Value);
        focus = new FocusController(configuration, new NearbyFocusPlayers(clientState, objects, condition, framework),
            new GameChatColors(gameConfig, data), framework, log);
        var ruleContext = new GameLocationReader(clientState, playerState, condition, objects);
        earlyHousing = new EarlyHousingReader(clientState, playerState);
        modRules = new ModRuleController(framework, log, ruleContext,
            new PenumbraGateway(pluginInterface, commands), configuration.Value.ModRules, configuration.Value.RuleProfiles, earlyHousing);
        glamourerRules = new GlamourerRuleController(framework, clientState, ruleContext,
            new GlamourerGateway(pluginInterface, objects), log, configuration.Value.Glamourer, configuration.Value.RuleProfiles);
        settings = new SettingsWindow(configuration, character, highlighter, data, modRules, focus, glamourerRules,
            () => commandInfo.HelpMessage = Strings.CommandHelp);
        cposes = new CposeController(framework, new GameCpose(framework, clientState, playerState, objects, condition, data, log), chatGui, log);
        windows.AddWindow(settings);

        if (!commands.AddHandler(Command, commandInfo))
        {
            windows.RemoveAllWindows();
            character.Dispose();
            modRules.Dispose();
            earlyHousing.Dispose();
            glamourerRules.Dispose();
            focus.Dispose();
            cposes.Dispose();
            throw new InvalidOperationException("The /sidekick command is already registered.");
        }

        shortCommandRegistered = commands.AddHandler(ShortCommand, commandInfo);
        if (!shortCommandRegistered)
        {
            log.Warning("Sidekick could not register /sk; /sidekick remains available.");
            chatGui.PrintError(Strings.FocusShortCommandUnavailable);
        }
        chat = new ChatSubscription(chatGui, log, highlighter, character, focus);
        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        pluginInterface.UiBuilder.OpenMainUi += OpenSettings;
    }

    public void Dispose()
    {
        chat.Dispose();
        cposes.Dispose();
        focus.Dispose();
        modRules.Dispose();
        earlyHousing.Dispose();
        glamourerRules.Dispose();
        character.Dispose();
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        pluginInterface.UiBuilder.OpenMainUi -= OpenSettings;
        commands.RemoveHandler(Command);
        if (shortCommandRegistered)
            commands.RemoveHandler(ShortCommand);
        settings.FlushChanges();
        windows.RemoveAllWindows();
    }

    private void OnCommand(string command, string arguments)
    {
        if (CposeCommand.TryParse(arguments, out var poseNumber))
        {
            cposes.Request(poseNumber);
            return;
        }
        var action = FocusCommand.Parse(arguments);
        if (action == FocusCommandAction.OpenSettings)
            settings.Toggle();
        else if (action == FocusCommandAction.Usage)
            chatGui.PrintError(Strings.FocusCommandUsage);
        else
        {
            focus.SetEnabled(action switch
            {
                FocusCommandAction.Enable => true,
                FocusCommandAction.Disable => false,
                _ => null,
            });
        }
    }
    private void OpenSettings() => settings.IsOpen = true;
}

