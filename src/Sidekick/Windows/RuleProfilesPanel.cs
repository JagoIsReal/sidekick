using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sidekick.Localization;
using Sidekick.Player;
using Sidekick.Rules;

namespace Sidekick.Windows;

// Shared by both rule tabs. Selecting an editing profile never changes the active game identity.
internal sealed class RuleProfilesPanel(ConfigurationStore store, LocationChoices choices, LocalCharacterTracker character)
{
    internal Guid? Selected { get; private set; }
    private RuleCharacterProfile? draft;
    private string worldSearch = "";
    private bool invalid;

    internal string Name(Guid? id)
    {
        if (id is null) return Strings.Account;
        var profile = store.Value.RuleProfiles.Characters.FirstOrDefault(p => p.Id == id);
        return profile is null ? Strings.MissingRuleProfile : $"{profile.CharacterName} @ {choices.World(profile.HomeWorldId)}";
    }

    internal Guid? SelectForRule(string id, Guid? selected)
    {
        Ui.Label(id, Strings.RuleProfile);
        if (!Ui.BeginCombo(id, Name(selected))) return selected;
        if (ImGui.Selectable($"{Strings.Account}###Account", selected is null)) selected = null;
        foreach (var profile in store.Value.RuleProfiles.Characters)
            if (ImGui.Selectable($"{Name(profile.Id)}###{profile.Id}", selected == profile.Id)) selected = profile.Id;
        ImGui.EndCombo();
        return selected;
    }

    internal void Draw()
    {
        Ui.Label("RuleProfiles", Strings.RuleProfile, Strings.RuleProfilesHelp);
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X - 3 * (ImGui.GetFrameHeight() + Ui.Gap)));
        if (Ui.BeginCombo("RuleProfiles.Selected", Name(Selected)))
        {
            if (ImGui.Selectable($"{Strings.Account}###Account", Selected is null)) Selected = null;
            foreach (var profile in store.Value.RuleProfiles.Characters)
                if (ImGui.Selectable($"{Name(profile.Id)}###{profile.Id}", Selected == profile.Id)) Selected = profile.Id;
            foreach (var missing in store.Value.ModRules.Rules.Select(r => r.ProfileId)
                .Concat(store.Value.Glamourer.Rules.Select(r => r.ProfileId)).Where(id => id is not null
                    && store.Value.RuleProfiles.Characters.All(p => p.Id != id)).Distinct())
                if (ImGui.Selectable($"{Strings.MissingRuleProfile}###{missing}", Selected == missing)) Selected = missing;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(store.Value.RuleProfiles.Characters.Count >= RuleProfileSettings.MaxProfiles);
        if (Ui.IconButton("RuleProfiles.Add", FontAwesomeIcon.Plus, Strings.AddRuleProfile))
            Open(new RuleCharacterProfile());
        ImGui.EndDisabled();
        ImGui.SameLine();
        var current = store.Value.RuleProfiles.Characters.FirstOrDefault(p => p.Id == Selected);
        ImGui.BeginDisabled(current is null);
        if (Ui.IconButton("RuleProfiles.Edit", FontAwesomeIcon.PencilAlt, Strings.Edit) && current is not null) Open(current.Copy());
        ImGui.EndDisabled();
        ImGui.SameLine();
        var used = Selected is not null && (store.Value.ModRules.Rules.Any(r => r.ProfileId == Selected)
            || store.Value.Glamourer.Rules.Any(r => r.ProfileId == Selected));
        ImGui.BeginDisabled(Selected is null || used);
        if (Ui.IconButton("RuleProfiles.Remove", FontAwesomeIcon.Trash, used ? Strings.RuleProfileInUse : Strings.Remove))
        {
            store.Value.RuleProfiles.Characters.RemoveAll(p => p.Id == Selected);
            Selected = null;
            Save();
        }
        ImGui.EndDisabled();
        if (current is not null && !current.Matches(character.Current)) Ui.Muted(Strings.RuleProfileInactive);
        DrawEditor();
        ImGui.Spacing();
    }

    private void Open(RuleCharacterProfile profile)
    {
        draft = profile;
        invalid = false;
        worldSearch = "";
        ImGui.OpenPopup("###RuleProfiles.Editor");
    }

    private void DrawEditor()
    {
        ImGui.SetNextWindowSize(new Vector2(560, 300) * Ui.Scale, ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal($"{Strings.EditRuleProfile}###RuleProfiles.Editor",
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse)) return;
        if (draft is not null)
        {
            var footer = ImGui.GetFrameHeightWithSpacing() + Ui.Gap * 2
                + (invalid ? ImGui.GetTextLineHeightWithSpacing() * 3 : 0);
            if (ImGui.BeginChild("###ProfileEditor.Body", new Vector2(0, -footer), false))
            {
                Ui.Section("ProfileEditor", Strings.EditRuleProfile);
                if (ImGui.BeginTable("###ProfileIdentity", 2, ImGuiTableFlags.SizingStretchSame))
                {
                    ImGui.TableNextRow(); ImGui.TableNextColumn();
                    Ui.Label("ProfileCharacter", Strings.CharacterName);
                    var name = draft.CharacterName;
                    ImGui.InputText("###ProfileCharacter", ref name, 65);
                    draft.CharacterName = name;
                    ImGui.TableNextColumn();
                    Ui.Label("ProfileWorld", Strings.HomeWorld);
                    draft.HomeWorldId = Ui.SelectId("ProfileWorld", draft.HomeWorldId == 0 ? null : draft.HomeWorldId,
                        choices.Worlds, ref worldSearch, Strings.SelectWorld, Strings.SearchWorlds, false) ?? 0;
                    ImGui.EndTable();
                }
                var enabled = draft.Enabled;
                if (ImGui.Checkbox($"{Strings.KeywordEnabled}###ProfileEnabled", ref enabled)) draft.Enabled = enabled;
                ImGui.SameLine();
                ImGui.BeginDisabled(character.Current is null);
                if (Ui.IconButton("Profile.Current", FontAwesomeIcon.Crosshairs, Strings.UseCurrentCharacter)
                    && character.Current is { } current)
                {
                    draft.CharacterName = current.DisplayName;
                    draft.HomeWorldId = current.HomeWorldId;
                }
                ImGui.EndDisabled();
            }
            ImGui.EndChild();
            ImGui.Separator();
            if (invalid) Ui.Wrapped(Strings.RuleProfileInvalid);
            if (Ui.TextButton("Profile.Save", FontAwesomeIcon.Save, Strings.SaveRuleProfile, Vector2.Zero, true))
            {
                if (!store.Value.RuleProfiles.TrySave(draft)) invalid = true;
                else
                {
                    Selected = draft.Id;
                    Save(); draft = null; ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
            if (Ui.TextButton("Profile.Cancel", FontAwesomeIcon.Times, Strings.Cancel, Vector2.Zero))
            {
                draft = null;
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.EndPopup();
    }

    private void Save()
    {
        store.Value.RuleProfiles.Validate();
        store.Value.ModRules.Changed();
        store.Save();
    }
}
