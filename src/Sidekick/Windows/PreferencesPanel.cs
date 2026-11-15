using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sidekick.Localization;

namespace Sidekick.Windows;

internal sealed class PreferencesPanel(ConfigurationStore store, Action<string> changeLanguage)
{
    internal void Draw()
    {
        Ui.Label("Preferences.Language", Strings.Language);
        var language = store.Value.LanguageCode == "fr" ? 1 : 0;
        if (Ui.Combo("Preferences.Language", ref language, [Strings.LanguageEnglish, Strings.LanguageFrench]))
            changeLanguage(language == 1 ? "fr" : "en");

        Ui.Section("Preferences.Reset", Strings.ResetSettings);
        if (!ImGui.BeginTable("###Preferences.Reset", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;
        ImGui.TableSetupColumn("###Module", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("###Reset", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        DrawReset("Chat", Strings.ChatTab, Strings.ResetChat, Strings.ResetChatHelp, () => store.Value.Reset());
        DrawReset("Focus", Strings.FocusTab, Strings.ResetFocus, Strings.ResetFocusHelp, () => store.Value.Focus.Reset());
        ImGui.EndTable();
    }

    private void DrawReset(string id, string module, string actionLabel, string help, Action reset)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(module);
        Ui.Help($"Preferences.Reset.{id}", help);
        ImGui.TableNextColumn();
        if (Ui.IconButton($"Preferences.Reset.{id}", FontAwesomeIcon.Undo, actionLabel))
        {
            reset();
            store.Save();
        }
    }
}
