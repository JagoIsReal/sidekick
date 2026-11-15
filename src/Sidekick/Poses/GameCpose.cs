using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace Sidekick.Poses;

internal sealed class GameCpose : ICposeGame
{
    private readonly IFramework framework;
    private readonly IClientState client;
    private readonly IPlayerState player;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly ushort emoteId;
    private (CposeSnapshot Before, byte Target)? staged;

    internal GameCpose(IFramework framework, IClientState client, IPlayerState player, IObjectTable objects,
        ICondition condition, IDataManager data, IPluginLog log)
    {
        this.framework = framework;
        this.client = client;
        this.player = player;
        this.objects = objects;
        this.condition = condition;
        try
        {
            var rows = data.GetExcelSheet<Emote>(ClientLanguage.English)
                .Where(row => row.TextCommand.RowId != 0 && row.RowId is > 0 and <= ushort.MaxValue)
                .Where(row => IsChangePose(row.TextCommand.Value))
                .Select(row => (ushort)row.RowId).Distinct().Take(2).ToArray();
            emoteId = rows.Length == 1 ? rows[0] : (ushort)0;
            if (emoteId == 0)
                log.Warning("Sidekick could not resolve a unique /cpose emote from the game sheets.");
        }
        catch (Exception exception)
        {
            log.Warning("Sidekick could not load the cpose emote ({ErrorType}).", exception.GetType().Name);
        }
    }

    private static bool IsChangePose(TextCommand command) =>
        command.Command.ExtractText() == "/cpose" || command.ShortCommand.ExtractText() == "/cpose" ||
        command.Alias.ExtractText() == "/cpose" || command.ShortAlias.ExtractText() == "/cpose";

    public bool Available => emoteId != 0;

    public unsafe CposeSnapshot? Read()
    {
        if (!TryResolve(out var character, out var state, out var manager))
            return null;
        var kind = character->EmoteController.GetPoseKind();
        if (!Enum.IsDefined((EmoteController.PoseType)kind) || kind < 0 || kind >= state->SelectedPoses.Length)
            return null;
        var lastIndex = EmoteController.GetAvailablePoses((EmoteController.PoseType)kind);
        if (lastIndex == byte.MaxValue)
            return null;
        return new CposeSnapshot(player.ContentId, character->GameObject.EntityId, (CposeKind)kind,
            character->EmoteController.CPoseState, state->SelectedPoses[kind], lastIndex, manager->CanExecuteEmote(emoteId));
    }

    public unsafe bool Apply(CposeSnapshot expected, byte targetIndex)
    {
        if (targetIndex > expected.LastIndex || Read() is not { } current || current != expected || !current.CanExecute ||
            !TryResolve(out var character, out var state, out var manager))
            return false;

        // Prime the previous index and let one native /cpose execution reach the requested index.
        // Both writes and the execution occur within the same framework callback, before another frame is drawn.
        var predecessor = CposeCommand.Predecessor(targetIndex, expected.LastIndex);
        staged = (expected, targetIndex);
        state->SelectedPoses[(int)expected.Kind] = predecessor;
        character->EmoteController.CPoseState = predecessor;
        return manager->ExecuteEmote(emoteId);
    }

    public unsafe void Finish(CposeSnapshot original, byte targetIndex, bool restore)
    {
        if (staged is not { } owned || owned.Before != original || owned.Target != targetIndex)
            return;
        staged = null;
        if (!restore)
            return;
        if (Read() is not { } current || !original.SameContext(current) ||
            !TryResolve(out var character, out var state, out _))
            return;
        var predecessor = CposeCommand.Predecessor(targetIndex, original.LastIndex);
        if (current.CurrentIndex != predecessor || current.SelectedIndex != predecessor)
            return; // A completed native transition or a later manual change belongs to the game/user.
        state->SelectedPoses[(int)original.Kind] = original.SelectedIndex;
        character->EmoteController.CPoseState = original.CurrentIndex;
    }

    private unsafe bool TryResolve(out Character* character, out PlayerState* state, out EmoteManager* manager)
    {
        character = null;
        state = null;
        manager = null;
        if (!Available || !framework.IsInFrameworkUpdateThread || !client.IsLoggedIn || !player.IsLoaded || player.ContentId == 0 ||
            client.IsGPosing || condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51] ||
            condition[ConditionFlag.InCombat] || condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion] ||
            objects.LocalPlayer is not { } local || local.Address == nint.Zero)
            return false;
        state = PlayerState.Instance();
        manager = EmoteManager.Instance();
        if (state == null || !state->IsLoaded || state->ContentId != player.ContentId || manager == null)
            return false;
        character = (Character*)local.Address;
        return true;
    }
}
