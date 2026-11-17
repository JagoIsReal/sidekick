using Dalamud.Bindings.ImGui;
using Sidekick.Changelog;
using Sidekick.Localization;

namespace Sidekick.Windows;

internal sealed class ChangelogPanel
{
    private string? language;
    private ChangelogRelease[] releases = [];

    internal void Draw()
    {
        Ui.Intro(Strings.ChangelogIntro);
        if (language != Strings.Culture.Name)
        {
            releases = ChangelogCatalog.Releases(typeof(Configuration).Assembly.GetName().Version!);
            language = Strings.Culture.Name;
        }
        for (var i = 0; i < releases.Length; i++)
        {
            var release = releases[i];
            var version = release.Version.ToString();
            var title = i == 0 ? $"{version} · {Strings.ChangelogCurrentVersion}" : version;
            Ui.Section($"Changelog.{version}", title);
            foreach (var section in release.Sections.Where(s => s.Items.Length > 0))
            {
                Ui.Muted(ChangelogCatalog.SectionTitle(section.Kind));
                foreach (var item in section.Items)
                {
                    ImGui.Bullet();
                    ImGui.SameLine();
                    Ui.Wrapped(item);
                }
                ImGui.Spacing();
            }
        }
    }
}
