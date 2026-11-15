using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;

namespace Sidekick.Windows;

// Shared presentation primitives; they never own or save configuration.
internal static class Ui
{
    internal static float Scale => ImGui.GetFontSize() / 17f;
    internal static float Gap => ImGui.GetStyle().ItemSpacing.X;

    internal static void Intro(string text)
    {
        Muted(text);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    internal static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    internal static void Muted(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        Wrapped(text);
        ImGui.PopStyleColor();
    }

    internal static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ImGui.IsItemFocused())
            return;
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    internal static void Help(string id, string text, bool inline = true)
    {
        if (inline)
            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        var size = ImGui.GetFrameHeight();
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImGui.Button($"###Help.{id}", new Vector2(size));
        ImGui.PopStyleColor(3);
        var minimum = ImGui.GetItemRectMin();
        var maximum = ImGui.GetItemRectMax();
        var center = (minimum + maximum) * 0.5f;
        var color = ImGui.GetColorU32(ImGui.IsItemHovered() || ImGui.IsItemFocused() ? ImGuiCol.Text : ImGuiCol.TextDisabled);
        var draw = ImGui.GetWindowDrawList();
        draw.AddCircle(center, size * 0.32f, color, 24, Math.Max(1, Scale));
        draw.AddText(center - ImGui.CalcTextSize("?") * 0.5f, color, "?");
        Tooltip(text);
    }

    internal static bool Section(string id, string title, string? help = null,
        FontAwesomeIcon? action = null, string? actionHelp = null, bool actionEnabled = true)
    {
        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(title);
        if (help is not null)
            Help(id, help);
        var clicked = false;
        if (action.HasValue)
        {
            var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeight();
            ImGui.SameLine();
            ImGui.SetCursorPosX(right);
            ImGui.BeginDisabled(!actionEnabled);
            clicked = IconButton($"Section.{id}.Action", action.Value, actionHelp ?? title);
            ImGui.EndDisabled();
        }
        ImGui.Separator();
        ImGui.Spacing();
        return clicked;
    }

    internal static void Label(string id, string label, string? help = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        if (help is not null)
            Help(id, help);
        ImGui.SetNextItemWidth(-1);
    }

    internal static unsafe bool IconButton(string id, FontAwesomeIcon icon, string help, bool primary = false)
    {
        var colors = ImGui.GetStyle().Colors;
        if (primary)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, colors[(int)ImGuiCol.HeaderActive]);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colors[(int)ImGuiCol.HeaderHovered]);
        }
        var clicked = ImGui.Button($"###{id}", new Vector2(ImGui.GetFrameHeight()));
        if (primary)
            ImGui.PopStyleColor(2);
        var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
        ImGui.PushFont(UiBuilder.IconFont);
        try
        {
            var font = ImGui.GetFont();
            var glyph = new ImFontGlyphPtr(font.FindGlyph((ushort)icon));
            var scale = ImGui.GetFontSize() / Math.Max(1, font.FontSize);
            // Center the visible glyph, rather than its advance width or font line box.
            var offset = glyph.IsNull ? ImGui.CalcTextSize(icon.ToIconString()) * 0.5f
                : new Vector2(glyph.X0 + glyph.X1, glyph.Y0 + glyph.Y1) * (0.5f * scale);
            ImGui.GetWindowDrawList().AddText(center - offset, ImGui.GetColorU32(ImGuiCol.Text), icon.ToIconString());
        }
        finally
        {
            ImGui.PopFont();
        }
        Tooltip(help);
        return clicked;
    }

    internal static bool TextButton(string id, FontAwesomeIcon icon, string text, Vector2 size, bool primary = false)
    {
        var colors = ImGui.GetStyle().Colors;
        return ImGuiComponents.IconButtonWithText(icon, $"{text}###{id}",
            primary ? colors[(int)ImGuiCol.HeaderActive] : null,
            null, primary ? colors[(int)ImGuiCol.HeaderHovered] : null, size / ImGuiHelpers.GlobalScale);
    }

    internal static bool BeginCombo(string id, string preview)
    {
        var width = Math.Max(1, ImGui.CalcItemWidth());
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, 320 * Scale));
        return ImGui.BeginCombo($"###{id}", preview);
    }

    internal static bool Combo(string id, ref int selected, string[] options)
    {
        var changed = false;
        if (!BeginCombo(id, options[Math.Clamp(selected, 0, options.Length - 1)]))
            return false;
        for (var i = 0; i < options.Length; i++)
        {
            if (ImGui.Selectable($"{options[i]}###{i}", selected == i))
            {
                selected = i;
                changed = true;
            }
            if (selected == i)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
        return changed;
    }

    internal static uint? SelectId(string id, uint? selected, (uint Id, string Name)[] choices,
        ref string search, string emptyLabel, string searchLabel, bool allowAny = true)
    {
        var result = selected;
        var preview = selected.HasValue ? choices.FirstOrDefault(c => c.Id == selected).Name ?? selected.ToString()! : emptyLabel;
        if (!BeginCombo(id, preview))
            return result;
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint("###Search", searchLabel, ref search, 128);
        if (allowAny && ImGui.Selectable($"{emptyLabel}###Any", !result.HasValue))
            result = null;
        var query = search;
        var ambiguousNames = choices.GroupBy(c => c.Name, StringComparer.Ordinal)
            .Where(g => g.Skip(1).Any()).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var choice in choices.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal)))
        {
            var label = ambiguousNames.Contains(choice.Name) ? $"{choice.Name} ({choice.Id})" : choice.Name;
            if (ImGui.Selectable($"{label}###{choice.Id}", result == choice.Id))
                result = choice.Id;
            Tooltip(label);
        }
        ImGui.EndCombo();
        return result;
    }
}
