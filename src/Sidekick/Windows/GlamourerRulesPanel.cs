using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sidekick.Appearance;
using Sidekick.Localization;

namespace Sidekick.Windows;

internal sealed class GlamourerRulesPanel(ConfigurationStore store, GlamourerRuleController controller,
    RuleProfilesPanel profiles, LocationChoices choices)
{
    private readonly RuleConditionsPanel conditions = new(choices);
    private GlamourerRule? draft;
    private Guid? editing;
    private string designSearch = "";
    private bool invalid;

    internal void Draw()
    {
        Ui.Intro(Strings.GlamourerIntro);
        if (draft is not null)
        {
            DrawEditor();
            return;
        }
        profiles.Draw();
        var settings = store.Value.Glamourer;
        var enabled = settings.Enabled;
        if (ImGui.Checkbox($"{Strings.EnableGlamourerRules}###Glamourer.Enabled", ref enabled))
        {
            settings.Enabled = enabled;
            Save();
        }
        Ui.Help("Glamourer.Policy", Strings.GlamourerRestoreHelp);
        if (!controller.Available) Ui.Muted(Strings.GlamourerUnavailable);
        if (controller.Available && controller.ActiveRuleName is { } activeRule)
            Ui.Muted(string.Format(Strings.Culture, Strings.GlamourerActiveRule, activeRule));
        if (controller.Failed) Ui.Wrapped(Strings.GlamourerFailed);
        if (controller.Skipped) Ui.Muted(Strings.GlamourerSkipped);
        if (controller.DeferredCount > 0)
            Ui.Muted(string.Format(Strings.Culture, Strings.GlamourerDeferred, controller.DeferredCount));
        if (controller.Current is { } context)
        {
            Ui.Muted(conditions.Describe(context));
            if (context.AppearanceBlocked) Ui.Muted(Strings.GlamourerPaused);
        }
        Ui.Section("Glamourer.Rules", Strings.SavedRules, Strings.RuleProfileOrderHelp);
        ImGui.BeginDisabled(settings.Rules.Count >= GlamourerSettings.MaxRules);
        if (Ui.IconButton("Glamourer.Add", FontAwesomeIcon.Plus, Strings.AddModRule, true))
        {
            draft = new GlamourerRule { ProfileId = profiles.Selected };
            editing = null; invalid = false;
        }
        ImGui.EndDisabled(); ImGui.SameLine();
        if (Ui.IconButton("Glamourer.Refresh", FontAwesomeIcon.Sync, Strings.RefreshListsHelp)) controller.Refresh();
        ImGui.Spacing();
        var visible = settings.Rules.Where(r => r.ProfileId == profiles.Selected).ToArray();
        if (visible.Length == 0) Ui.Muted(Strings.NoModRules);
        if (!ImGui.BeginChild("###Glamourer.Rules.List", Vector2.Zero, false))
        {
            ImGui.EndChild(); return;
        }
        if (ImGui.BeginTable("###Glamourer.Rules.Table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Strings.KeywordEnabled, ImGuiTableColumnFlags.WidthFixed,
                Math.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize(Strings.KeywordEnabled).X));
            ImGui.TableSetupColumn(Strings.RuleName, ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Strings.Actions, ImGuiTableColumnFlags.WidthFixed, 4 * ImGui.GetFrameHeight() + 3 * Ui.Gap);
            ImGui.TableHeadersRow();
            for (var i = 0; i < visible.Length; i++)
            {
                var rule = visible[i];
                ImGui.PushID(rule.Id.ToString());
                ImGui.TableNextRow(); ImGui.TableNextColumn();
                var active = rule.Enabled;
                if (ImGui.Checkbox("###Enabled", ref active)) { rule.Enabled = active; Save(); }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(rule.Name);
                Ui.Muted(DesignName(rule.DesignId));
                ImGui.TableNextColumn();
                if (Ui.IconButton("Edit", FontAwesomeIcon.PencilAlt, Strings.Edit))
                {
                    draft = rule.Copy(); editing = rule.Id; invalid = false;
                }
                ImGui.SameLine(); ImGui.BeginDisabled(i == 0);
                var up = Ui.IconButton("Up", FontAwesomeIcon.ArrowUp, Strings.MoveUp);
                ImGui.EndDisabled(); ImGui.SameLine(); ImGui.BeginDisabled(i == visible.Length - 1);
                var down = Ui.IconButton("Down", FontAwesomeIcon.ArrowDown, Strings.MoveDown);
                ImGui.EndDisabled(); ImGui.SameLine();
                var remove = Ui.CtrlIconButton("Remove", FontAwesomeIcon.Trash, Strings.Remove);
                ImGui.PopID();
                if (remove) { settings.Rules.Remove(rule); Save(); break; }
                if (!up && !down) continue;
                var first = settings.Rules.IndexOf(rule);
                var second = settings.Rules.IndexOf(visible[i + (up ? -1 : 1)]);
                (settings.Rules[first], settings.Rules[second]) = (settings.Rules[second], settings.Rules[first]);
                Save(); break;
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private string DesignName(Guid id) => controller.Designs.GetValueOrDefault(id) ?? Strings.GlamourerMissingDesign;

    private void DrawEditor()
    {
        var rule = draft!;
        Ui.Section("Glamourer.Editor", editing.HasValue ? Strings.EditRule : Strings.NewRule, Strings.RuleDraftHelp);
        rule.ProfileId = profiles.SelectForRule("Glamourer.Profile", rule.ProfileId);
        Ui.Label("Glamourer.Name", Strings.RuleName);
        var name = rule.Name;
        ImGui.InputText("###Glamourer.Name", ref name, 81);
        rule.Name = name;
        var footer = ImGui.GetFrameHeightWithSpacing() + Ui.Gap * 2 + (invalid ? ImGui.GetTextLineHeightWithSpacing() * 3 : 0);
        if (ImGui.BeginChild("###Glamourer.Editor.Body", new Vector2(0, -footer), false))
        {
            Ui.Section("Glamourer.Action", Strings.GlamourerDesign, Strings.GlamourerRestoreHelp);
            ImGui.SetNextItemWidth(-1);
            if (Ui.BeginCombo("Glamourer.Design", DesignName(rule.DesignId)))
            {
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("###DesignSearch", Strings.GlamourerSearchDesigns, ref designSearch, 128);
                foreach (var design in controller.Designs.Where(d => d.Key != Guid.Empty
                    && d.Value.Contains(designSearch, StringComparison.OrdinalIgnoreCase)).OrderBy(d => d.Value, StringComparer.OrdinalIgnoreCase))
                    if (ImGui.Selectable($"{design.Value}###{design.Key}", rule.DesignId == design.Key)) rule.DesignId = design.Key;
                ImGui.EndCombo();
            }
            rule.Conditions = conditions.Draw(rule.Conditions, controller.Current);
        }
        ImGui.EndChild(); ImGui.Separator();
        if (invalid) Ui.Wrapped(Strings.GlamourerRuleInvalid);
        if (Ui.TextButton("Glamourer.Save", FontAwesomeIcon.Save, Strings.SaveModRule, new Vector2(150 * Ui.Scale, 0), true))
        {
            rule.Name = rule.Name.Trim();
            if (!rule.IsValid || (editing is null && store.Value.Glamourer.Rules.Count >= GlamourerSettings.MaxRules)) invalid = true;
            else
            {
                var index = store.Value.Glamourer.Rules.FindIndex(r => r.Id == editing);
                if (index >= 0) store.Value.Glamourer.Rules[index] = rule.Copy();
                else store.Value.Glamourer.Rules.Add(rule.Copy());
                Save(); draft = null;
            }
        }
        ImGui.SameLine();
        if (Ui.TextButton("Glamourer.Cancel", FontAwesomeIcon.Times, Strings.Cancel, new Vector2(110 * Ui.Scale, 0))) draft = null;
    }

    private void Save()
    {
        store.Save(); controller.Refresh();
    }
}
