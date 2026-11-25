using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sidekick.Localization;
using Sidekick.Status;

namespace Sidekick.Windows;

internal sealed class StatusRulesPanel(ConfigurationStore store, StatusRuleController controller,
    RuleProfilesPanel profiles, LocationChoices choices)
{
    private readonly RuleConditionsPanel conditions = new(choices);
    private StatusRule? draft;
    private bool editing;
    private bool invalid;

    internal void Draw()
    {
        Ui.Intro(Strings.StatusRulesIntro);
        if (draft is not null)
        {
            DrawEditor(draft);
            return;
        }

        profiles.Draw();
        var settings = store.Value.StatusRules;
        var enabled = settings.Enabled;
        if (ImGui.Checkbox($"{Strings.EnableStatusRules}###Status.Enabled", ref enabled))
        {
            settings.Enabled = enabled;
            store.Save();
        }
        Ui.Help("Status.Policy", Strings.StatusRulePolicy);
        if (controller.Failed) Ui.Wrapped(Strings.StatusUpdateFailed);
        if (controller.Current is { } context) Ui.Muted(conditions.Describe(context));

        Ui.Section("Status.Rules", Strings.SavedRules, Strings.RuleProfileOrderHelp);
        ImGui.BeginDisabled(settings.Rules.Count >= StatusRuleSettings.MaxRules);
        if (Ui.IconButton("Status.Add", FontAwesomeIcon.Plus, Strings.AddStatusRule, true))
            Open(new StatusRule { ProfileId = profiles.Selected, AtLogin = true }, editing: false);
        ImGui.EndDisabled();

        var visible = settings.Rules.Where(r => r.ProfileId == profiles.Selected).ToArray();
        if (visible.Length == 0)
        {
            Ui.Muted(Strings.NoStatusRules);
            return;
        }

        if (ImGui.BeginChild("###Status.Rules.List", Vector2.Zero, false))
            DrawList(settings, visible);
        ImGui.EndChild();
    }

    private void DrawList(StatusRuleSettings settings, StatusRule[] visible)
    {
        if (!ImGui.BeginTable("###Status.Rules.Table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn(Strings.KeywordEnabled, ImGuiTableColumnFlags.WidthFixed,
            Math.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize(Strings.KeywordEnabled).X));
        ImGui.TableSetupColumn(Strings.RuleName, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(Strings.Actions, ImGuiTableColumnFlags.WidthFixed, 4 * ImGui.GetFrameHeight() + 3 * Ui.Gap);
        ImGui.TableHeadersRow();
        for (var i = 0; i < visible.Length; i++)
        {
            var rule = visible[i];
            ImGui.PushID(rule.Id.ToString());
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var enabled = rule.Enabled;
            if (ImGui.Checkbox("###Enabled", ref enabled))
            {
                rule.Enabled = enabled;
                store.Save();
            }
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(rule.Name);
            Ui.Muted(string.Format(Strings.Culture, Strings.StatusRuleSummary, TriggerName(rule.AtLogin), TargetName(rule.TargetStatusId)));
            ImGui.TableNextColumn();
            if (Ui.IconButton("Edit", FontAwesomeIcon.PencilAlt, Strings.Edit))
                Open(rule.Copy(), editing: true);
            ImGui.SameLine();
            ImGui.BeginDisabled(i == 0);
            var up = Ui.IconButton("Up", FontAwesomeIcon.ArrowUp, Strings.MoveUp);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(i == visible.Length - 1);
            var down = Ui.IconButton("Down", FontAwesomeIcon.ArrowDown, Strings.MoveDown);
            ImGui.EndDisabled();
            ImGui.SameLine();
            var remove = Ui.CtrlIconButton("Remove", FontAwesomeIcon.Trash, Strings.Remove);
            ImGui.PopID();
            if (remove)
                settings.Rules.Remove(rule);
            else if (up || down)
            {
                var first = settings.Rules.IndexOf(rule);
                var second = settings.Rules.IndexOf(visible[i + (up ? -1 : 1)]);
                (settings.Rules[first], settings.Rules[second]) = (settings.Rules[second], settings.Rules[first]);
            }
            else
                continue;
            store.Save();
            break;
        }
        ImGui.EndTable();
    }

    private void Open(StatusRule rule, bool editing)
    {
        draft = rule;
        this.editing = editing;
        invalid = false;
    }

    private void DrawEditor(StatusRule rule)
    {
        Ui.Section("Status.Editor", editing ? Strings.EditRule : Strings.NewRule, Strings.RuleDraftHelp);
        var footer = ImGui.GetFrameHeightWithSpacing() + Ui.Gap * 2
            + (invalid ? ImGui.GetTextLineHeightWithSpacing() * 3 : 0);
        if (ImGui.BeginChild("###Status.Editor.Body", new Vector2(0, -footer), false))
        {
            rule.ProfileId = profiles.SelectForRule("Status.Profile", rule.ProfileId);
            Ui.Label("Status.Name", Strings.RuleName);
            var name = rule.Name;
            ImGui.InputText("###Status.Name", ref name, 81);
            rule.Name = name;
            Ui.Label("Status.Trigger", Strings.StatusRuleTrigger, Strings.StatusTriggerHelp);
            var trigger = rule.AtLogin ? 0 : 1;
            if (Ui.Combo("Status.Trigger", ref trigger, [Strings.StatusAtLogin, Strings.StatusWhenMatched]))
                rule.AtLogin = trigger == 0;
            Ui.Label("Status.Target", Strings.StatusRuleTarget);
            if (Ui.BeginCombo("Status.Target", TargetName(rule.TargetStatusId)))
            {
                foreach (var target in choices.StatusTargetsForRules)
                    if (ImGui.Selectable($"{target.Name}###{target.Id}", rule.TargetStatusId == target.Id))
                        rule.TargetStatusId = target.Id;
                ImGui.EndCombo();
            }
            rule.Conditions = conditions.Draw(rule.Conditions, controller.Current);
        }
        ImGui.EndChild();
        ImGui.Separator();
        if (invalid) Ui.Wrapped(Strings.StatusRuleInvalid);
        if (Ui.TextButton("Status.Save", FontAwesomeIcon.Save, Strings.SaveModRule, Vector2.Zero, true))
        {
            rule.Name = rule.Name.Trim();
            var rules = store.Value.StatusRules.Rules;
            var index = rules.FindIndex(r => r.Id == rule.Id);
            if (!rule.IsValid || (index < 0 && rules.Count >= StatusRuleSettings.MaxRules))
                invalid = true;
            else
            {
                if (index >= 0) rules[index] = rule;
                else rules.Add(rule);
                store.Save();
                draft = null;
            }
        }
        ImGui.SameLine();
        if (Ui.TextButton("Status.Cancel", FontAwesomeIcon.Times, Strings.Cancel, Vector2.Zero))
            draft = null;
    }

    private string TargetName(uint? id) =>
        choices.StatusTargetsForRules.FirstOrDefault(target => target.Id == id).Name ?? Strings.SelectStatusTarget;

    private static string TriggerName(bool atLogin) => atLogin ? Strings.StatusAtLogin : Strings.StatusWhenMatched;
}
