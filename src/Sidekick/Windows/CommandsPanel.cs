using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sidekick.Commands;
using Sidekick.Localization;

namespace Sidekick.Windows;

internal sealed class CommandsPanel
{
    private string? language;
    private CommandCategory[] categories = [];

    internal void Draw()
    {
        Ui.Intro(Strings.CommandsIntro);
        if (language != Strings.Culture.Name)
        {
            categories = CommandCatalog.Categories();
            language = Strings.Culture.Name;
        }
        foreach (var category in categories)
        {
            Ui.Section($"Commands.{category.Id}", category.Title, category.Help);
            if (!ImGui.BeginTable($"###Commands.{category.Id}", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                continue;
            var width = category.Commands.Max(command => ImGui.CalcTextSize(command.Command).X) + Ui.Gap;
            ImGui.TableSetupColumn("###Command", ImGuiTableColumnFlags.WidthFixed, width);
            ImGui.TableSetupColumn("###Description", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("###Copy", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            foreach (var command in category.Commands)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(command.Command);
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                Ui.Wrapped(command.Description);
                ImGui.TableNextColumn();
                if (Ui.IconButton($"Commands.Copy.{command.Command}", FontAwesomeIcon.Copy, Strings.CopyCommand))
                    ImGui.SetClipboardText(command.Command);
            }
            ImGui.EndTable();
        }
    }
}
