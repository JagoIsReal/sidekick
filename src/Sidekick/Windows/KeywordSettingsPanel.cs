using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sidekick.Keywords;
using Sidekick.Core;
using Sidekick.Localization;
using Sidekick.Player;

namespace Sidekick.Windows;

internal sealed class KeywordSettingsPanel
{
    private readonly ConfigurationStore store;
    private readonly LocalCharacterTracker character;
    private readonly LocationChoices choices;
    private string characterName = string.Empty;
    private uint homeWorldId;
    private string worldSearch = string.Empty;
    private string keyword = string.Empty;
    private bool account;
    private Guid? editing;
    private KeywordEditResult? error;
    private bool characterSelected;

    internal CharacterIdentity? PreviewIdentity => KeywordProfile.ForCharacter(characterName, homeWorldId) is { } profile
        ? CharacterIdentity.FromName(profile.CharacterName, profile.HomeWorldId) : character.Current;

    internal KeywordSettingsPanel(ConfigurationStore store, LocalCharacterTracker character, LocationChoices choices)
    {
        this.store = store;
        this.character = character;
        this.choices = choices;
    }

    internal void Draw()
    {
        if (!characterSelected && character.Current is { } identity)
        {
            characterName = identity.DisplayName;
            homeWorldId = identity.HomeWorldId;
            characterSelected = true;
        }
        DrawCharacterSelector();
        var characterProfile = KeywordProfile.ForCharacter(characterName, homeWorldId);

        if (ImGui.Checkbox($"{Strings.Account}###Sidekick.NewKeywordAccount", ref account))
            error = null;
        Ui.Help("Account", Strings.AccountHelp);
        var target = account ? KeywordProfile.Global : characterProfile;
        Ui.Label("Keyword", Strings.Keyword);
        var actionCount = editing.HasValue ? 2 : 1;
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - actionCount * (ImGui.GetFrameHeight() + Ui.Gap)));
        if (ImGui.InputTextWithHint("###Sidekick.KeywordInput", Strings.KeywordHint, ref keyword, KeywordSettings.MaxTextLength + 1))
            error = null;
        ImGui.SameLine();
        ImGui.BeginDisabled(target is null);
        if (Ui.IconButton("Sidekick.SaveKeyword", editing is null ? FontAwesomeIcon.Plus : FontAwesomeIcon.Save,
            editing is null ? Strings.AddKeyword : Strings.SaveKeyword, true))
        {
            var result = store.Value.Keywords.TrySave(target, keyword, editing);
            if (result == KeywordEditResult.Success)
            {
                ClearEditor();
                store.Save();
            }
            else
                error = result;
        }
        ImGui.EndDisabled();
        if (target is null)
            Ui.Tooltip(Strings.CharacterProfileHelp);
        if (editing is not null)
        {
            ImGui.SameLine();
            if (Ui.IconButton("Sidekick.CancelKeyword", FontAwesomeIcon.Times, Strings.Cancel))
                ClearEditor();
        }

        if (error is { } failure)
            Ui.Wrapped(GetError(failure));
        DrawRules(characterProfile);
    }

    private void DrawCharacterSelector()
    {
        var saved = store.Value.Keywords.Rules.Where(rule => !rule.Account)
            .Select(rule => (rule.CharacterName, rule.HomeWorldId))
            .DistinctBy(profile => (profile.HomeWorldId, profile.CharacterName.ToUpperInvariant()))
            .OrderBy(profile => profile.CharacterName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (saved.Length > 0)
        {
            Ui.Label("SavedCharacters", Strings.SavedCharacters);
            ImGui.SetNextItemWidth(Math.Min(ImGui.GetContentRegionAvail().X, 420 * Ui.Scale));
            if (Ui.BeginCombo("Sidekick.SavedCharacters", Strings.SelectCharacter))
            {
                foreach (var profile in saved)
                {
                    if (ImGui.Selectable($"{profile.CharacterName} @ {WorldName(profile.HomeWorldId)}###{profile.HomeWorldId}.{profile.CharacterName}"))
                    {
                        characterName = profile.CharacterName;
                        homeWorldId = profile.HomeWorldId;
                        characterSelected = true;
                        ClearEditor();
                    }
                }
                ImGui.EndCombo();
            }
        }

        if (ImGui.BeginTable("###Sidekick.CharacterProfile", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.35f);
            ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            Ui.Label("CharacterProfile", Strings.CharacterName, Strings.CharacterProfileHelp);
            ImGui.TableSetColumnIndex(1);
            Ui.Label("HomeWorld", Strings.HomeWorld);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            DrawNameInput();
            ImGui.TableSetColumnIndex(1);
            DrawWorldInput();
            ImGui.EndTable();
        }
    }

    private void DrawNameInput()
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("###Sidekick.ProfileName", ref characterName, 65))
        {
            characterSelected = true;
            ClearEditor();
        }
    }

    private void DrawWorldInput()
    {
        ImGui.SetNextItemWidth(-1);
        var selectedWorld = Ui.SelectId("Sidekick.HomeWorld", homeWorldId == 0 ? null : homeWorldId, choices.Worlds,
            ref worldSearch, Strings.SelectWorld, Strings.SearchWorlds, false) ?? 0;
        if (selectedWorld != homeWorldId)
        {
            homeWorldId = selectedWorld;
            characterSelected = true;
            ClearEditor();
        }
    }

    private void DrawRules(KeywordProfile? characterProfile)
    {
        var rules = store.Value.Keywords.Rules.Where(rule => rule.Account || (characterProfile?.Contains(rule) ?? false)).ToArray();
        ImGui.Spacing();
        Ui.Muted(string.Format(CultureInfo.InvariantCulture, Strings.KeywordCount, rules.Length));
        if (rules.Length == 0)
        {
            Ui.Muted(Strings.NoKeywords);
            return;
        }
        if (!ImGui.BeginTable("Sidekick.KeywordRules", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn(Strings.KeywordEnabled, ImGuiTableColumnFlags.WidthFixed,
            Math.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize(Strings.KeywordEnabled).X));
        ImGui.TableSetupColumn(Strings.Keyword, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Strings.Account, ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn(Strings.Actions, ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() * 2 + Ui.Gap);
        ImGui.TableHeadersRow();
        foreach (var rule in rules)
        {
            ImGui.PushID(rule.Id.ToString());
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            var enabled = rule.Enabled;
            if (ImGui.Checkbox("###Enabled", ref enabled) && store.Value.Keywords.SetEnabled(rule.Id, enabled))
                store.Save();
            ImGui.TableSetColumnIndex(1);
            Ui.Wrapped(rule.Text);
            ImGui.TableSetColumnIndex(2);
            var isAccount = rule.Account;
            ImGui.BeginDisabled(isAccount && characterProfile is null);
            if (ImGui.Checkbox("###Account", ref isAccount))
            {
                var result = store.Value.Keywords.TrySave(isAccount ? KeywordProfile.Global : characterProfile, rule.Text, rule.Id);
                if (result == KeywordEditResult.Success)
                {
                    if (editing == rule.Id)
                        ClearEditor();
                    store.Save();
                }
                else
                    error = result;
            }
            ImGui.EndDisabled();
            ImGui.TableSetColumnIndex(3);
            if (Ui.IconButton("Edit", FontAwesomeIcon.PencilAlt, Strings.Edit))
            {
                keyword = rule.Text;
                account = rule.Account;
                editing = rule.Id;
                error = null;
            }
            ImGui.SameLine();
            if (Ui.IconButton("Remove", FontAwesomeIcon.Trash, Strings.Remove) && store.Value.Keywords.Remove(rule.Id))
            {
                if (editing == rule.Id)
                    ClearEditor();
                store.Save();
            }
            ImGui.PopID();
        }
        ImGui.EndTable();
    }

    private string WorldName(uint id) => choices.Worlds.FirstOrDefault(world => world.Id == id).Name ?? Strings.SelectWorld;

    private void ClearEditor()
    {
        keyword = string.Empty;
        editing = null;
        error = null;
    }

    private static string GetError(KeywordEditResult result) => result switch
    {
        KeywordEditResult.Duplicate => Strings.KeywordDuplicate,
        KeywordEditResult.LimitReached => Strings.KeywordLimit,
        KeywordEditResult.ProfileUnavailable or KeywordEditResult.RuleNotFound => Strings.KeywordProfileChanged,
        _ => string.Format(CultureInfo.InvariantCulture, Strings.KeywordInvalid, KeywordSettings.MaxTextLength),
    };
}
