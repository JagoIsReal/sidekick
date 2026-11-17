using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sidekick.Localization;
using Sidekick.Locations;
using Sidekick.Mods;

namespace Sidekick.Windows;

internal sealed class ModRulesPanel
{
    private readonly ConfigurationStore store;
    private readonly ModRuleController controller;
    private readonly LocationChoices choices;
    private readonly RuleConditionsPanel conditions;
    private readonly RuleProfilesPanel profiles;
    private ModLocationRule? draft;
    private Guid? editing;
    private string search = "";
    private string collectionSearch = "";
    private bool invalid;

    internal ModRulesPanel(ConfigurationStore store, ModRuleController controller, LocationChoices choices, RuleProfilesPanel profiles)
    {
        this.store = store;
        this.controller = controller;
        this.choices = choices;
        this.profiles = profiles;
        conditions = new RuleConditionsPanel(choices);
    }

    internal void Draw()
    {
        Ui.Intro(Strings.ModRulesIntro);
        if (draft is not null)
        {
            DrawEditor();
            return;
        }
        var settings = store.Value.ModRules;
        profiles.Draw();
        var enabled = settings.Enabled;
        if (ImGui.Checkbox($"{Strings.EnableModRules}###Sidekick.ModRules.Enabled", ref enabled))
        {
            settings.Enabled = enabled;
            Save();
        }
        Ui.Help("ModRules", Strings.ModRulesHelp);
        ImGui.Spacing();
        ImGui.TextUnformatted(controller.Available ? Strings.PenumbraReadyShort : Strings.PenumbraUnavailableShort);
        Ui.Tooltip(controller.Available ? Strings.PenumbraReady : Strings.PenumbraUnavailable);
        ImGui.SameLine();
        Ui.Muted(string.Format(CultureInfo.InvariantCulture, Strings.ModRulesStatus, controller.ActiveCount, controller.SkippedCount));
        if (controller.Failed)
            Ui.Wrapped(Strings.ModIntegrationError);
        if (settings.Rules.Any(r => r.EarlyApplication))
            Ui.Muted(string.Format(CultureInfo.InvariantCulture, Strings.EarlyApplicationResult, controller.LastEarlyApplications));

        ImGui.Spacing();
        if (ImGui.BeginTable("###CurrentLocation", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed,
                ImGui.CalcTextSize(Strings.YourLocation).X + ImGui.GetFrameHeight() + Ui.Gap);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Strings.YourLocation);
            Ui.Help("CurrentLocation", Strings.LocationFiltersHelp);
            ImGui.TableSetColumnIndex(1);
            ImGui.AlignTextToFramePadding();
            Ui.Wrapped(controller.Current is { } current ? Describe(current) : Strings.LocationUnavailable);
            ImGui.EndTable();
        }

        Ui.Section("Rules", Strings.SavedRules, Strings.RuleProfileOrderHelp);
        ImGui.BeginDisabled(settings.Rules.Count >= ModRuleSettings.MaxRules);
        if (Ui.IconButton("Sidekick.ModRules.Add", FontAwesomeIcon.Plus, Strings.AddModRule, true))
        {
            draft = new ModLocationRule { ProfileId = profiles.Selected };
            editing = null;
            invalid = false;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (Ui.IconButton("Sidekick.ModRules.Refresh", FontAwesomeIcon.Sync, Strings.RefreshListsHelp))
            controller.Refresh();
        ImGui.Spacing();
        if (ImGui.BeginChild("###Sidekick.ModRules.List", Vector2.Zero, false))
            DrawRules();
        ImGui.EndChild();
    }

    private void DrawRules()
    {
        var settings = store.Value.ModRules;
        var visible = settings.Rules.Where(r => r.ProfileId == profiles.Selected).ToArray();
        if (visible.Length == 0)
        {
            Ui.Muted(Strings.NoModRules);
            return;
        }
        if (!ImGui.BeginTable("###Sidekick.ModRules.Table", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn(Strings.KeywordEnabled, ImGuiTableColumnFlags.WidthFixed,
            Math.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize(Strings.KeywordEnabled).X));
        ImGui.TableSetupColumn(Strings.RuleName, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Strings.Mods, ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(Strings.Mods).X + Ui.Gap);
        var actionsWidth = ImGui.GetFrameHeight() * 4 + Ui.Gap * 3;
        ImGui.TableSetupColumn(Strings.Actions, ImGuiTableColumnFlags.WidthFixed, actionsWidth);
        ImGui.TableHeadersRow();
        for (var i = 0; i < visible.Length; i++)
        {
            var rule = visible[i];
            ImGui.PushID(rule.Id.ToString());
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            var enabled = rule.Enabled;
            if (ImGui.Checkbox("###Enabled", ref enabled))
            {
                rule.Enabled = enabled;
                Save();
            }
            Ui.Tooltip(Strings.RuleEnabledHelp);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(rule.Name);
            Ui.Muted(string.Format(CultureInfo.InvariantCulture, Strings.RuleSummary,
                ActionName(rule.Action), CollectionName(rule.CollectionId), RefreshName(rule.RefreshMode)));
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(rule.Mods.Count.ToString(CultureInfo.InvariantCulture));
            ImGui.TableSetColumnIndex(3);
            if (Ui.IconButton("Edit", FontAwesomeIcon.PencilAlt, Strings.Edit))
            {
                draft = rule.Copy();
                editing = rule.Id;
                invalid = false;
            }
            ImGui.SameLine();
            var reordered = false;
            ImGui.BeginDisabled(i == 0);
            if (Ui.IconButton("Up", FontAwesomeIcon.ArrowUp, Strings.MoveUp))
            {
                Move(rule, visible[i - 1]);
                Save();
                reordered = true;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(i == visible.Length - 1);
            if (Ui.IconButton("Down", FontAwesomeIcon.ArrowDown, Strings.MoveDown))
            {
                Move(rule, visible[i + 1]);
                Save();
                reordered = true;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            var remove = Ui.CtrlIconButton("Remove", FontAwesomeIcon.Trash, Strings.Remove);
            ImGui.PopID();
            if (reordered && !remove)
                break;
            if (!remove)
                continue;
            settings.Rules.Remove(rule);
            if (editing == rule.Id)
                draft = null;
            Save();
            break;
        }
        ImGui.EndTable();
    }

    private void DrawEditor()
    {
        var rule = draft!;
        Ui.Section("RuleEditor", editing.HasValue ? Strings.EditRule : Strings.NewRule, Strings.RuleDraftHelp);
        rule.ProfileId = profiles.SelectForRule("ModRule.Profile", rule.ProfileId);
        Ui.Label("RuleName", Strings.RuleName);
        var name = rule.Name;
        ImGui.InputText("###Sidekick.ModRules.Name", ref name, 81);
        rule.Name = name;
        ImGui.Spacing();
        var footer = ImGui.GetFrameHeightWithSpacing() + Ui.Gap * 2
            + (invalid ? ImGui.GetTextLineHeightWithSpacing() * 4 : 0);
        if (ImGui.BeginChild("###Sidekick.ModRules.EditorBody", new Vector2(0, -footer), false))
        {
            ImGui.PushID(rule.Id.ToString());
            if (ImGui.GetContentRegionAvail().X >= 680 * Ui.Scale
                && ImGui.BeginTable("###EditorColumns", 2, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                DrawLocation(rule);
                ImGui.TableSetColumnIndex(1);
                DrawModSelection(rule);
                ImGui.EndTable();
            }
            else
            {
                DrawLocation(rule);
                DrawModSelection(rule);
            }
            ImGui.PopID();
        }
        ImGui.EndChild();
        ImGui.Separator();
        if (invalid)
            Ui.Wrapped(Strings.ModRuleInvalid);
        if (Ui.TextButton("Sidekick.ModRules.Save", FontAwesomeIcon.Save, Strings.SaveModRule, new Vector2(150 * Ui.Scale, 0), true))
        {
            rule.Name = rule.Name.Trim();
            if (!rule.IsValid || (editing is null && store.Value.ModRules.Rules.Count >= ModRuleSettings.MaxRules))
                invalid = true;
            else
            {
                var index = store.Value.ModRules.Rules.FindIndex(r => r.Id == editing);
                if (index >= 0)
                    store.Value.ModRules.Rules[index] = rule.Copy();
                else
                    store.Value.ModRules.Rules.Add(rule.Copy());
                Save();
                draft = null;
            }
        }
        ImGui.SameLine();
        if (Ui.TextButton("Sidekick.ModRules.Cancel", FontAwesomeIcon.Times, Strings.Cancel, new Vector2(110 * Ui.Scale, 0)))
            draft = null;
    }

    private void DrawLocation(ModLocationRule rule) => rule.Location = conditions.Draw(rule.Location, controller.Current);

    private void DrawModSelection(ModLocationRule rule)
    {
        Ui.Section("Execution", Strings.ExecutionSection, Strings.ModRulesHelp);
        Ui.Label("Action", Strings.ModAction);
        var action = (int)rule.Action;
        if (Ui.Combo("Action", ref action, new[] { Strings.EnableMod, Strings.DisableMod, Strings.ToggleMod }))
            rule.Action = (ModAction)action;
        Ui.Label("Collection", Strings.TargetCollection, Strings.TargetCollectionHelp);
        if (Ui.BeginCombo("Collection", CollectionName(rule.CollectionId)))
        {
            if (ImGui.IsWindowAppearing())
            {
                controller.RefreshLists();
                ImGui.SetKeyboardFocusHere();
            }
            ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
            ImGui.InputTextWithHint("###CollectionSearch", Strings.SearchCollections, ref collectionSearch, 128);
            if (ImGui.Selectable($"{Strings.CurrentCharacterCollection}###CurrentCharacter", rule.CollectionId is null))
                rule.CollectionId = null;
            ImGui.Separator();
            foreach (var collection in controller.Collections.Where(c => c.Key != Guid.Empty
                && c.Value.Contains(collectionSearch, StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.Value, StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable($"{collection.Value}###{collection.Key}", rule.CollectionId == collection.Key))
                    rule.CollectionId = collection.Key;
                Ui.Tooltip(collection.Value);
            }
            ImGui.EndCombo();
        }
        Ui.Label("Refresh", Strings.ModRefresh, Strings.ModRefreshHelp);
        var refresh = (int)rule.RefreshMode;
        if (Ui.Combo("Refresh", ref refresh,
            new[] { Strings.RefreshCollection, Strings.RefreshAll, Strings.RefreshSelf, Strings.RefreshFurniture }))
            rule.RefreshMode = (ModRefreshMode)refresh;
        ImGui.Spacing();
        ImGui.BeginDisabled(!rule.SupportsEarlyApplication);
        var early = rule.EarlyApplication;
        if (ImGui.Checkbox($"{Strings.EarlyApplication}###EarlyApplication", ref early))
            rule.EarlyApplication = early;
        ImGui.EndDisabled();
        Ui.Help("EarlyApplicationHelp", Strings.EarlyApplicationHelp);
        Ui.Section("SelectedMods", string.Format(CultureInfo.InvariantCulture, Strings.SelectedMods, rule.Mods.Count));
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("###SearchMods", Strings.SearchMods, ref search, 128);
        if (ImGui.BeginChild("###ModList", new Vector2(0, 260 * Ui.Scale), true))
        {
            var mods = controller.Mods.Keys.Concat(rule.Mods).Distinct(StringComparer.Ordinal)
                .Select(id => (Id: id, Name: controller.Mods.GetValueOrDefault(id) ?? id))
                .Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || m.Id.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (mods.Length == 0)
                Ui.Muted(controller.Available ? Strings.NoModsFound : Strings.PenumbraUnavailableShort);
            foreach (var mod in mods)
            {
                ImGui.PushID(mod.Id);
                var selected = rule.Mods.Contains(mod.Id, StringComparer.Ordinal);
                ImGui.BeginDisabled(!selected && rule.Mods.Count >= 64);
                if (ImGui.Checkbox("###Selected", ref selected))
                {
                    if (selected)
                        rule.Mods.Add(mod.Id);
                    else
                        rule.Mods.Remove(mod.Id);
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextUnformatted(mod.Name);
                Ui.Tooltip(mod.Id);
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    private string CollectionName(Guid? id) => id.HasValue
        ? controller.Collections.GetValueOrDefault(id.Value) ?? Strings.MissingCollection : Strings.CurrentCharacterCollection;

    private static string ActionName(ModAction action) => action switch
    {
        ModAction.Enable => Strings.EnableMod,
        ModAction.Disable => Strings.DisableMod,
        _ => Strings.ToggleMod,
    };

    private static string RefreshName(ModRefreshMode mode) => mode switch
    {
        ModRefreshMode.All => Strings.RefreshAll,
        ModRefreshMode.Self => Strings.RefreshSelf,
        ModRefreshMode.Furniture => Strings.RefreshFurniture,
        _ => Strings.RefreshCollection,
    };

    private string Describe(LocationSnapshot location)
    {
        var territory = choices.Territory(location.TerritoryId);
        if (location.HousingDistrictId is { } district && district != location.TerritoryId)
            territory = string.Format(CultureInfo.InvariantCulture, Strings.ResidentialLocation, choices.Territory(district), territory);
        var heading = string.Format(CultureInfo.InvariantCulture, Strings.LocationHeading, choices.World(location.WorldId), territory);
        var details = new List<string>();
        if (location.PublicInstanceId > 0)
            details.Add(string.Format(CultureInfo.InvariantCulture, Strings.InstanceSummary, location.PublicInstanceId));
        if (location.Ward.HasValue)
            details.Add(string.Format(CultureInfo.InvariantCulture, Strings.WardSummary, location.Ward));
        if (location.Plot.HasValue)
            details.Add(string.Format(CultureInfo.InvariantCulture, Strings.PlotSummary, location.Plot));
        if (location.Room.HasValue)
            details.Add(string.Format(CultureInfo.InvariantCulture, Strings.RoomSummary, location.Room));
        if (location.Indoors.HasValue)
            details.Add(location.Indoors.Value ? Strings.Indoors : Strings.Outdoors);
        return details.Count == 0 ? heading : heading + " · " + string.Join(" · ", details);
    }

    private void Save()
    {
        store.Value.ModRules.Changed();
        store.Save();
        controller.Refresh();
    }

    private void Move(ModLocationRule rule, ModLocationRule other)
    {
        var rules = store.Value.ModRules.Rules;
        var first = rules.IndexOf(rule);
        var second = rules.IndexOf(other);
        (rules[first], rules[second]) = (rules[second], rules[first]);
    }
}
