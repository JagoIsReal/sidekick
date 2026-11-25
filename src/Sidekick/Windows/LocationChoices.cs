using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Sidekick.Localization;
using Sidekick.Rules;
using Sidekick.Status;

namespace Sidekick.Windows;

internal sealed class LocationChoices
{
    private readonly IDataManager data;
    private string? culture;
    internal (uint Id, string Name)[] Worlds { get; private set; } = [];
    internal (uint Id, string Name)[] Territories { get; private set; } = [];
    internal (uint Id, string Name)[] Districts { get; private set; } = [];
    internal (uint Id, string Name)[] Weathers { get; private set; } = [];
    internal (uint Id, string Name)[] OnlineStatuses { get; private set; } = [];
    internal (uint Id, string Name)[] StatusTargetsForRules { get; private set; } = [];
    internal RuleOptionGroup[] WeatherGroups { get; private set; } = [];
    internal RuleOptionGroup[] StatusGroups { get; private set; } = [];

    internal LocationChoices(IDataManager data)
    {
        this.data = data;
        RefreshLanguage();
    }

    internal void RefreshLanguage()
    {
        if (culture == Strings.Culture.Name)
            return;
        var language = Strings.Culture.TwoLetterISOLanguageName == "fr" ? ClientLanguage.French : ClientLanguage.English;
        Worlds = data.GetExcelSheet<World>(language).Where(w => w.IsPublic && !w.Name.IsEmpty)
            .Select(w => (w.RowId, w.Name.ExtractText())).OrderBy(w => w.Item2, StringComparer.OrdinalIgnoreCase).ToArray();
        var zones = data.GetExcelSheet<TerritoryType>(language).Where(t => t.RowId != 0 && !t.PlaceName.Value.Name.IsEmpty).ToArray();
        Territories = zones.Select(t => (t.RowId, t.PlaceName.Value.Name.ExtractText()))
            .OrderBy(t => t.Item2, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.RowId).ToArray();
        Districts = zones.Where(t => t.TerritoryIntendedUse.RowId == 13)
            .Select(t => (t.RowId, t.PlaceName.Value.Name.ExtractText())).ToArray();
        Weathers = data.GetExcelSheet<Weather>(language).Where(w => w.RowId is > 0 and <= byte.MaxValue && !w.Name.IsEmpty)
            .Select(w => (w.RowId, w.Name.ExtractText())).OrderBy(w => w.Item2, StringComparer.OrdinalIgnoreCase).ToArray();
        WeatherGroups = RuleOptionGroup.Create(Weathers);
        var statuses = data.GetExcelSheet<OnlineStatus>(language).Where(s => s.List && s.RowId <= byte.MaxValue && !s.Name.IsEmpty)
            .Select(s => (s.RowId, s.Name.ExtractText())).ToList();
        // Native status 0 means no special status for a loaded local actor. The sheet's
        // named Online row also represents it in social lists; present both as one choice.
        var onlineName = statuses.FirstOrDefault(s => s.RowId == StatusTargets.Online).Item2;
        if (!string.IsNullOrWhiteSpace(onlineName)) statuses.Add((0, onlineName));
        OnlineStatuses = statuses.ToArray();
        StatusGroups = RuleOptionGroup.Create(OnlineStatuses);
        StatusTargetsForRules = StatusTargets.Selectable.Select(id => statuses.FirstOrDefault(s => s.RowId == id))
            .Where(s => !string.IsNullOrWhiteSpace(s.Item2)).ToArray();
        culture = Strings.Culture.Name;
    }

    internal string World(uint id) => Worlds.FirstOrDefault(w => w.Id == id).Name ?? id.ToString();
    internal string Territory(uint id) => Territories.FirstOrDefault(t => t.Id == id).Name ?? id.ToString();
}
