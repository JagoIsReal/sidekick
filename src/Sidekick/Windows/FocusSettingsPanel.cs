using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Sidekick.Focus;
using Sidekick.Localization;

namespace Sidekick.Windows;

internal sealed class FocusSettingsPanel(ConfigurationStore store, FocusController controller, LocationChoices choices)
{
    private string nearbySearch = string.Empty;
    private string name = string.Empty;
    private uint worldId;
    private string worldSearch = string.Empty;
    private bool showManual;
    private bool pendingSave;
    private FocusParticipantResult? error;

    internal void Draw()
    {
        var settings = store.Value.Focus;
        Ui.Intro(Strings.FocusIntro);
        if (Ui.IconButton("Sidekick.Focus.Toggle", FontAwesomeIcon.Crosshairs, Strings.FocusToggle, controller.Enabled))
            controller.SetEnabled();
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(controller.Enabled ? Strings.FocusEnabled : Strings.FocusDisabled);
        Ui.Help("Focus.Toggle", Strings.FocusToggleHelp);
        ImGui.Spacing();

        Ui.Section("Focus.Criteria", Strings.FocusCriteria, Strings.FocusCriteriaHelp);
        var distance = settings.UseDistance;
        if (ImGui.Checkbox($"{Strings.FocusUseDistance}###Focus.UseDistance", ref distance))
        {
            settings.UseDistance = distance;
            Save();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150 * Ui.Scale);
        ImGui.BeginDisabled(!distance);
        var radius = settings.DistanceYalms;
        if (ImGui.DragFloat("###Focus.Distance", ref radius, 0.5f, 1, FocusSettings.MaxDistance, "%.6g y", ImGuiSliderFlags.AlwaysClamp))
        {
            settings.DistanceYalms = radius;
            pendingSave = true;
        }
        SaveAfterEdit();
        ImGui.EndDisabled();
        Ui.Help("Focus.Distance", Strings.FocusDistanceHelp);

        var participants = settings.UseParticipants;
        if (ImGui.Checkbox($"{Strings.FocusUseParticipants}###Focus.UseParticipants", ref participants))
        {
            settings.UseParticipants = participants;
            Save();
        }
        Ui.Help("Focus.Participants", Strings.FocusParticipantsHelp);
        if (!distance && !participants)
            Ui.Muted(Strings.FocusNoCriteria);

        ImGui.BeginDisabled(!distance || !participants);
        Ui.Label("Focus.Combination", Strings.FocusCombination, Strings.FocusCombinationHelp);
        var combination = (int)settings.Combination;
        if (Ui.Combo("Focus.Combination", ref combination, [Strings.FocusAny, Strings.FocusAll]))
        {
            settings.Combination = (FocusCombination)combination;
            Save();
        }
        ImGui.EndDisabled();

        Ui.Section("Focus.Appearance", Strings.Appearance);
        Ui.Label("Focus.Opacity", Strings.FocusOpacity, Strings.FocusOpacityHelp);
        var opacity = settings.OtherOpacityPercent;
        if (ImGui.SliderInt("###Focus.Opacity", ref opacity, 1, 100, "%d %%", ImGuiSliderFlags.AlwaysClamp))
        {
            settings.OtherOpacityPercent = opacity;
            pendingSave = true;
        }
        SaveAfterEdit();
        Ui.Muted(Strings.FocusGameColors);
        if (controller.UnresolvedColors > 0)
            Ui.Wrapped(string.Format(Strings.Culture, Strings.FocusUnresolvedColors, controller.UnresolvedColors));

        DrawChannels(settings);
        if (Ui.Section("Focus.People", Strings.FocusParticipants, Strings.FocusParticipantsHelp,
            FontAwesomeIcon.Plus, Strings.FocusAddManual))
        {
            showManual = !showManual;
            error = null;
        }
        DrawNearby(settings);
        if (showManual)
            DrawManual(settings);
        if (error is { } result)
            Ui.Wrapped(result switch
            {
                FocusParticipantResult.Duplicate => Strings.FocusParticipantDuplicate,
                FocusParticipantResult.LimitReached => Strings.FocusParticipantLimit,
                _ => Strings.FocusParticipantInvalid,
            });
        DrawParticipants(settings);
    }

    private void DrawChannels(FocusSettings settings)
    {
        Ui.Section("Focus.Channels", Strings.FocusChannels, Strings.FocusChannelsHelp);
        if (settings.Channels.Count == 0)
            Ui.Muted(Strings.FocusNoChannels);
        if (ImGui.BeginChild("###Focus.Channels.List", new System.Numerics.Vector2(0, 175 * Ui.Scale), true))
        {
            var columns = ImGui.GetContentRegionAvail().X >= 650 * Ui.Scale ? 3 : 2;
            if (ImGui.BeginTable("###Focus.Channels.Table", columns, ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var channel in FocusChannels.All)
                {
                    ImGui.TableNextColumn();
                    var selected = settings.Channels.Contains(channel);
                    if (ImGui.Checkbox($"{ChannelName(channel)}###Focus.Channel.{(int)channel}", ref selected))
                    {
                        if (selected)
                            settings.Channels.Add(channel);
                        else
                            settings.Channels.Remove(channel);
                        Save();
                    }
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private void DrawNearby(FocusSettings settings)
    {
        ImGui.SetNextItemWidth(-1);
        if (!Ui.BeginCombo("Focus.Nearby", Strings.FocusNearby))
            return;
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("###Focus.Nearby.Search", Strings.FocusNearbyHint, ref nearbySearch, 128);
        var players = controller.Nearby()?.Players ?? [];
        if (players.Count == 0)
            Ui.Muted(controller.NearbyUnavailable ? Strings.FocusNearbyUnavailable : Strings.FocusNearbyEmpty);
        foreach (var player in players)
        {
            var identity = player.Character;
            var label = string.Format(Strings.Culture, Strings.FocusNearbyEntry,
                identity.Name, choices.World(identity.HomeWorldId), player.DistanceYalms);
            if (!label.Contains(nearbySearch, StringComparison.OrdinalIgnoreCase))
                continue;
            var existing = settings.Participants.FirstOrDefault(p => new FocusCharacter(p.Name, p.HomeWorldId).Matches(identity));
            if (ImGui.Selectable($"{label}###Focus.Nearby.{identity.Name}.{identity.HomeWorldId}", existing?.Enabled == true,
                ImGuiSelectableFlags.DontClosePopups))
            {
                if (existing is null)
                    Add(settings, identity.Name, identity.HomeWorldId);
                else
                {
                    settings.SetParticipantEnabled(existing.Id, !existing.Enabled);
                    Save();
                }
            }
            Ui.Tooltip(label);
        }
        ImGui.EndCombo();
    }

    private void DrawManual(FocusSettings settings)
    {
        ImGui.Spacing();
        if (ImGui.BeginTable("###Focus.Manual.Fields", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            Ui.Label("Focus.Name", Strings.CharacterName);
            ImGui.InputTextWithHint("###Focus.Name", Strings.CharacterName, ref name, 65);
            ImGui.TableNextColumn();
            Ui.Label("Focus.World", Strings.HomeWorld);
            worldId = Ui.SelectId("Focus.World", worldId == 0 ? null : worldId, choices.Worlds,
                ref worldSearch, Strings.SelectWorld, Strings.SearchWorlds, false) ?? 0;
            ImGui.EndTable();
        }
        if (Ui.IconButton("Focus.Manual.Add", FontAwesomeIcon.Plus, Strings.FocusAddManual, true) &&
            Add(settings, name, worldId))
        {
            name = string.Empty;
            showManual = false;
        }
        ImGui.SameLine();
        if (Ui.IconButton("Focus.Manual.Cancel", FontAwesomeIcon.Times, Strings.Cancel))
        {
            showManual = false;
            error = null;
        }
    }

    private void DrawParticipants(FocusSettings settings)
    {
        if (settings.Participants.Count == 0)
        {
            Ui.Muted(Strings.FocusNoParticipants);
            return;
        }
        ImGui.Spacing();
        if (!ImGui.BeginTable("###Focus.People.Table", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            return;
        ImGui.TableSetupColumn("###Focus.Person", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("###Focus.Remove", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        Guid? remove = null;
        foreach (var participant in settings.Participants)
        {
            ImGui.TableNextColumn();
            var selected = participant.Enabled;
            var label = string.Format(Strings.Culture, Strings.FocusParticipantEntry,
                participant.Name, choices.World(participant.HomeWorldId));
            if (ImGui.Checkbox($"{label}###Focus.Person.{participant.Id}", ref selected))
            {
                settings.SetParticipantEnabled(participant.Id, selected);
                Save();
            }
            Ui.Tooltip(label);
            ImGui.TableNextColumn();
            if (Ui.IconButton($"Focus.Person.Remove.{participant.Id}", FontAwesomeIcon.Trash, Strings.Remove))
                remove = participant.Id;
        }
        ImGui.EndTable();
        if (remove is { } id)
        {
            settings.Remove(id);
            Save();
        }
    }

    private bool Add(FocusSettings settings, string characterName, uint homeWorldId)
    {
        var result = settings.Add(characterName, homeWorldId);
        error = result == FocusParticipantResult.Success ? null : result;
        if (error is not null)
            return false;
        Save();
        return true;
    }

    private void SaveAfterEdit()
    {
        if (ImGui.IsItemDeactivatedAfterEdit())
            FlushChanges();
    }

    private void Save()
    {
        store.Save();
        pendingSave = false;
    }

    internal void FlushChanges()
    {
        if (pendingSave)
            Save();
    }

    internal static string ChannelName(XivChatType channel) => channel switch
    {
        XivChatType.Say => Strings.FocusChannelSay,
        XivChatType.CustomEmote => Strings.FocusChannelCustomEmote,
        XivChatType.StandardEmote => Strings.FocusChannelStandardEmote,
        XivChatType.Yell => Strings.FocusChannelYell,
        XivChatType.Shout => Strings.FocusChannelShout,
        XivChatType.TellIncoming => Strings.FocusChannelTellIncoming,
        XivChatType.TellOutgoing => Strings.FocusChannelTellOutgoing,
        XivChatType.Party => Strings.FocusChannelParty,
        XivChatType.CrossParty => Strings.FocusChannelCrossParty,
        XivChatType.Alliance => Strings.FocusChannelAlliance,
        XivChatType.FreeCompany => Strings.FocusChannelFreeCompany,
        XivChatType.PvPTeam => Strings.FocusChannelPvPTeam,
        XivChatType.NoviceNetwork => Strings.FocusChannelNoviceNetwork,
        >= XivChatType.Ls1 and <= XivChatType.Ls8 => string.Format(Strings.Culture,
            Strings.FocusChannelLinkshell, (int)channel - (int)XivChatType.Ls1 + 1),
        XivChatType.CrossLinkShell1 => string.Format(Strings.Culture, Strings.FocusChannelCrossLinkshell, 1),
        >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8 => string.Format(Strings.Culture,
            Strings.FocusChannelCrossLinkshell, (int)channel - (int)XivChatType.CrossLinkShell2 + 2),
        _ => channel.ToString(),
    };
}
