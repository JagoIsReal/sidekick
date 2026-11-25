using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Utility;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Sidekick.Chat;
using Sidekick.Core;
using Sidekick.Localization;
using Sidekick.Player;
using Sidekick.Mods;
using Sidekick.Focus;
using Sidekick.Appearance;
using Sidekick.Status;

namespace Sidekick.Windows;

internal sealed class SettingsWindow : Window
{
    private readonly ConfigurationStore store;
    private readonly LocalCharacterTracker character;
    private readonly ChatHighlighter highlighter;
    private readonly KeywordSettingsPanel keywords;
    private readonly ModRulesPanel mods;
    private readonly FocusSettingsPanel focusPanel;
    private readonly GlamourerRulesPanel glamourerPanel;
    private readonly StatusRulesPanel statusPanel;
    private readonly LocationChoices choices;
    private readonly CommandsPanel commands = new();
    private readonly ChangelogPanel changelog = new();
    private readonly PreferencesPanel preferences;
    private readonly Action? languageChanged;
    private string? activeTab;
    private bool colorChanged;
    private string? previewText;
    private string? previewCulture;
    private bool previewEdited;

    public SettingsWindow(ConfigurationStore store, LocalCharacterTracker character, ChatHighlighter highlighter,
        IDataManager data, ModRuleController modRules, FocusController focus, GlamourerRuleController glamourer,
        StatusRuleController statusRules, Action? languageChanged = null)
        : base($"{Strings.SettingsTitle}###Sidekick.Settings")
    {
        this.store = store;
        this.character = character;
        this.highlighter = highlighter;
        this.languageChanged = languageChanged;
        choices = new LocationChoices(data);
        keywords = new KeywordSettingsPanel(store, character, choices);
        var profiles = new RuleProfilesPanel(store, choices, character);
        mods = new ModRulesPanel(store, modRules, choices, profiles);
        glamourerPanel = new GlamourerRulesPanel(store, glamourer, profiles, choices);
        statusPanel = new StatusRulesPanel(store, statusRules, profiles, choices);
        focusPanel = new FocusSettingsPanel(store, focus, choices);
        preferences = new PreferencesPanel(store, SetLanguage);
        Size = new Vector2(820, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(520, 400), MaximumSize = new Vector2(float.MaxValue) };
    }

    public override void PreDraw()
    {
        WindowName = $"{Strings.SettingsTitle}###Sidekick.Settings";
        choices.RefreshLanguage();
    }

    public override void Draw()
    {
        if (!store.CanSave)
            Ui.Wrapped(Strings.ConfigurationReadError);
        else if (store.SaveFailed)
            Ui.Wrapped(Strings.ConfigurationSaveError);

        if (!ImGui.BeginTabBar("###Sidekick.Tabs", ImGuiTabBarFlags.FittingPolicyScroll))
            return;
        DrawTab("Chat", Strings.ChatTab, DrawChat);
        DrawTab("Focus", Strings.FocusTab, focusPanel.Draw);
        DrawTab("ModRules", Strings.ModRulesTab, mods.Draw, false);
        DrawTab("Glamourer", Strings.GlamourerTab, glamourerPanel.Draw, false);
        DrawTab("Status", Strings.StatusRulesTab, statusPanel.Draw, false);
        DrawTab("Commands", Strings.CommandsTab, commands.Draw);
        DrawTab("Changelog", Strings.ChangelogTab, changelog.Draw);
        DrawTab("Preferences", Strings.PreferencesTab, preferences.Draw, icon: FontAwesomeIcon.Cog);
        ImGui.EndTabBar();
    }

    private void DrawTab(string id, string title, Action draw, bool scroll = true, FontAwesomeIcon? icon = null)
    {
        var flags = ImGuiTabItemFlags.None;
        if (icon.HasValue)
        {
            ImGui.SetNextItemWidth(ImGui.GetFrameHeight() + 2 * ImGui.GetStyle().FramePadding.X);
            flags = ImGuiTabItemFlags.Trailing | ImGuiTabItemFlags.NoReorder | ImGuiTabItemFlags.NoTooltip;
        }
        var selected = ImGui.BeginTabItem($"{(icon.HasValue ? "" : title)}###Sidekick.{id}", flags);
        if (icon is { } tabIcon)
        {
            Ui.DrawItemIcon(tabIcon, sizeScale: 0.85f);
            Ui.Tooltip(title);
        }
        if (!selected)
            return;
        if (activeTab != id)
        {
            FlushChanges();
            activeTab = id;
        }
        if (scroll)
        {
            if (ImGui.BeginChild($"###Sidekick.{id}.Content", Vector2.Zero, false))
                draw();
            ImGui.EndChild();
        }
        else
            draw();
        ImGui.EndTabItem();
    }

    private void SetLanguage(string code)
    {
        FlushChanges();
        store.Value.LanguageCode = LanguagePreference.Normalize(code);
        Strings.Culture = LanguagePreference.CultureFor(store.Value.LanguageCode);
        languageChanged?.Invoke();
        store.Save();
    }

    private void DrawChat()
    {
        Ui.Intro(Strings.ChatIntro);
        var configuration = store.Value;
        var enabled = configuration.Enabled;
        if (ImGui.Checkbox($"{Strings.EnableHighlighting}###Sidekick.Enabled", ref enabled))
        {
            configuration.Enabled = enabled;
            store.Save();
        }
        Ui.Help("Highlighting", Strings.NewMessagesOnly);
        Ui.Muted(string.Format(CultureInfo.InvariantCulture, Strings.CharacterStatus,
            character.Current?.DisplayName ?? Strings.NotLoggedIn));

        Ui.Section("Appearance", Strings.Appearance);
        Ui.Label("Color", Strings.HighlightColor);
        var color = RgbColor.FromRgb(configuration.HighlightColorRgb).ToVector3();
        if (ImGui.ColorEdit3("###Sidekick.Color", ref color, ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.DisplayHex))
        {
            configuration.HighlightColorRgb = RgbColor.FromVector3(color).PackedRgb;
            colorChanged = true;
        }
        if (colorChanged && !ImGui.IsAnyItemActive() && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId))
            FlushChanges();

        Ui.Section("Keywords", Strings.CustomKeywords,
            string.Format(CultureInfo.InvariantCulture, Strings.KeywordsHelp, Keywords.KeywordSettings.MaxRulesPerProfile));
        keywords.Draw();

        Ui.Section("Preview", Strings.Preview, Strings.PreviewHelp);
        var identity = keywords.PreviewIdentity ?? CharacterIdentity.FromName("Ann Example")!;
        if (previewText is null || (!previewEdited && previewCulture != Strings.Culture.Name))
            previewText = string.Format(Strings.Culture, Strings.PreviewMessage, identity.FirstName);
        previewCulture = Strings.Culture.Name;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("###Sidekick.PreviewInput", Strings.PreviewInput, ref previewText, 512))
            previewEdited = true;
        var preview = new SeString(new TextPayload(previewText));
        ImGuiHelpers.SeStringWrapped(highlighter.Transform(preview, identity).Encode());

    }

    public override void OnClose() => FlushChanges();

    public void FlushChanges()
    {
        focusPanel.FlushChanges();
        if (!colorChanged)
            return;
        store.Save();
        colorChanged = false;
    }
}
