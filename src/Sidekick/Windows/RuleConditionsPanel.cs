using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sidekick.Localization;
using Sidekick.Locations;
using Sidekick.Rules;

namespace Sidekick.Windows;

internal sealed class RuleConditionsPanel(LocationChoices choices)
{
    private const uint PartialSelection = 1;
    private const uint FullSelection = 3;
    private readonly Dictionary<string, string> selectorSearch = new(StringComparer.Ordinal);
    internal LocationCondition Draw(LocationCondition filter, LocationSnapshot? context)
    {
        if (filter.WeatherId.HasValue) filter.NormalizeSelections();
        DrawEnvironment(filter, context);
        if (Ui.Section("Location", Strings.LocationSection, Strings.LocationFiltersHelp,
            FontAwesomeIcon.Crosshairs, Strings.UseCurrentLocation, context is not null)
            && context is { } current)
            filter = LocationCondition.Capture(current) with
            {
                WeatherIds = [.. filter.WeatherIds],
                OnlineStatusIds = [.. filter.OnlineStatusIds],
                Daytime = filter.Daytime,
                Flying = filter.Flying,
                Swimming = filter.Swimming,
                Diving = filter.Diving,
                Mounted = filter.Mounted,
            };
        filter.WorldId = SelectId(Strings.CurrentWorld, "World", filter.WorldId, choices.Worlds);
        filter.TerritoryId = SelectId(Strings.Territory, "Territory", filter.TerritoryId, choices.Territories);
        filter.PublicInstanceId = (uint?)Number(Strings.PublicInstance, "Instance", (int?)filter.PublicInstanceId, 9);

        Ui.Section("Housing", Strings.HousingSection, Strings.HousingHelp);
        filter.HousingDistrictId = SelectId(Strings.HousingDistrict, "District", filter.HousingDistrictId, choices.Districts);
        if (ImGui.BeginTable("###AddressNumbers", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Ward", ImGuiTableColumnFlags.WidthFixed,
                Math.Max(64 * Ui.Scale, ImGui.CalcTextSize(Strings.Ward).X));
            ImGui.TableSetupColumn("Plot", ImGuiTableColumnFlags.WidthFixed,
                Math.Max(64 * Ui.Scale, ImGui.CalcTextSize(Strings.Plot).X));
            ImGui.TableSetupColumn("Division", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            filter.Ward = Number(Strings.Ward, "Ward", filter.Ward, 30, true);
            ImGui.TableSetColumnIndex(1);
            filter.Plot = Number(Strings.Plot, "Plot", filter.Plot, 60, true);
            ImGui.TableSetColumnIndex(2);
            Ui.Label("Division", Strings.HousingDivision);
            var division = filter.Division ?? 0;
            if (Ui.Combo("Division", ref division, new[] { Strings.AnyLocation, Strings.MainDivision, Strings.Subdivision }))
                filter.Division = division == 0 ? null : division;
            ImGui.EndTable();
        }
        if (ImGui.BeginTable("###RoomAndArea", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            filter.Room = Number(Strings.Room, "Room", filter.Room, 1023);
            ImGui.TableSetColumnIndex(1);
            Ui.Label("Area", Strings.HousingArea);
            var indoors = filter.Indoors.HasValue ? filter.Indoors.Value ? 2 : 1 : 0;
            if (Ui.Combo("Indoors", ref indoors, new[] { Strings.AnyLocation, Strings.Outdoors, Strings.Indoors }))
                filter.Indoors = indoors == 0 ? null : indoors == 2;
            ImGui.EndTable();
        }
        return filter;
    }

    private uint? SelectId(string label, string id, uint? selected, (uint Id, string Name)[] entries)
    {
        Ui.Label(id, label);
        var query = selectorSearch.GetValueOrDefault(id) ?? "";
        var result = Ui.SelectId(id, selected, entries, ref query, Strings.AnyLocation, Strings.SearchLocations);
        selectorSearch[id] = query;
        return result;
    }

    private static int? Number(string label, string id, int? selected, int maximum, bool compact = false)
    {
        Ui.Label(id, label);
        var value = selected ?? 0;
        return ImGui.InputInt($"###{id}", ref value, compact ? 0 : 1, compact ? 0 : 100)
            ? value == 0 ? null : Math.Clamp(value, 1, maximum) : selected;
    }

    private void DrawEnvironment(LocationCondition filter, LocationSnapshot? context)
    {
        Ui.Section("Environment", Strings.RuleEnvironment, Strings.RuleConditionsHelp);
        if (context is not null) Ui.Muted(Describe(context));
        if (ImGui.BeginTable("###Environment", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            SelectMany(Strings.RuleWeather, "Weather", filter.WeatherIds, choices.WeatherGroups, Strings.AnyWeather);
            ImGui.TableNextColumn();
            filter.Daytime = SelectBool("Daytime", Strings.RuleDaytime, filter.Daytime, Strings.RuleDay, Strings.RuleNight);
            ImGui.EndTable();
        }
        SelectMany(Strings.RuleOnlineStatus, "OnlineStatus", filter.OnlineStatusIds, choices.StatusGroups, Strings.AnyOnlineStatus);
        if (ImGui.BeginTable("###Activity", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            filter.Flying = SelectBool("Flying", Strings.RuleFlying, filter.Flying);
            ImGui.TableNextColumn();
            filter.Mounted = SelectBool("Mounted", Strings.RuleMounted, filter.Mounted);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            filter.Swimming = SelectBool("Swimming", Strings.RuleSwimming, filter.Swimming);
            ImGui.TableNextColumn();
            filter.Diving = SelectBool("Diving", Strings.RuleDiving, filter.Diving);
            ImGui.EndTable();
        }
    }

    private void SelectMany(string label, string id, List<uint> selected, RuleOptionGroup[] groups, string anyLabel)
    {
        Ui.Label(id, label, Strings.RuleMultiSelectionHelp);
        var names = groups.Where(group => group.Ids.Any(selected.Contains)).Select(group => group.Name).ToArray();
        var preview = selected.Count == 0 ? anyLabel : names.Length > 0 ? string.Join(", ", names)
            : string.Format(Strings.Culture, Strings.RuleSelectionCount, selected.Count);
        if (!Ui.BeginCombo(id, preview)) return;
        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        var search = selectorSearch.GetValueOrDefault(id) ?? "";
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint($"###{id}.Search", Strings.SearchLocations, ref search, 128);
        selectorSearch[id] = search;
        if (ImGui.Selectable($"{anyLabel}###Any", selected.Count == 0, ImGuiSelectableFlags.DontClosePopups)) selected.Clear();
        ImGui.Separator();
        foreach (var group in groups.Where(group => group.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            var count = group.Ids.Count(selected.Contains);
            var flags = count == group.Ids.Length ? FullSelection : count > 0 ? PartialSelection : 0;
            if (ImGui.CheckboxFlags($"{group.Name}###{group.Ids[0]}", ref flags, FullSelection))
                group.Select(selected, flags == FullSelection);
        }
        ImGui.EndCombo();
    }

    private static bool? SelectBool(string id, string label, bool? value, string? yes = null, string? no = null)
    {
        Ui.Label(id, label);
        var index = value is null ? 0 : value.Value ? 1 : 2;
        if (Ui.Combo(id, ref index, [Strings.AnyLocation, yes ?? Strings.RuleYes, no ?? Strings.RuleNo]))
            return index == 0 ? null : index == 1;
        return value;
    }

    internal string Describe(LocationSnapshot context)
    {
        var weather = context.WeatherId is { } id ? choices.Weathers.FirstOrDefault(w => w.Id == id).Name ?? id.ToString(CultureInfo.InvariantCulture) : Strings.RuleUnknown;
        var day = context.Daytime is null ? Strings.RuleUnknown : context.Daytime.Value ? Strings.RuleDay : Strings.RuleNight;
        var activity = context.Flying == true ? Strings.RuleFlying : context.Diving == true ? Strings.RuleDiving
            : context.Swimming == true ? Strings.RuleSwimming : context.Mounted == true ? Strings.RuleMounted : Strings.RuleOnFoot;
        var status = context.OnlineStatusId is { } online ? choices.OnlineStatuses.FirstOrDefault(s => s.Id == online).Name : null;
        return string.Format(Strings.Culture, Strings.RuleContextSummary, weather, day, activity)
            + (string.IsNullOrEmpty(status) ? "" : " · " + status);
    }
}
