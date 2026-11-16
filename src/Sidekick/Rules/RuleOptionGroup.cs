using System.Text;

namespace Sidekick.Rules;

// The translated label is presentation only. Rules persist all selected native row IDs.
internal sealed record RuleOptionGroup(string Name, uint[] Ids)
{
    internal static RuleOptionGroup[] Create(IEnumerable<(uint Id, string Name)> rows) => rows
        .Where(row => !string.IsNullOrWhiteSpace(row.Name))
        .GroupBy(row => row.Name.Trim().Normalize(NormalizationForm.FormC), StringComparer.OrdinalIgnoreCase)
        .Select(group => new RuleOptionGroup(group.Key, group.Select(row => row.Id).Distinct().Order().ToArray()))
        .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    internal void Select(List<uint> selected, bool enabled)
    {
        if (!enabled) selected.RemoveAll(Ids.Contains);
        else selected.AddRange(Ids.Where(id => !selected.Contains(id)));
    }
}
