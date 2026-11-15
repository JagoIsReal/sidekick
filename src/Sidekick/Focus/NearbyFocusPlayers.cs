using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;

namespace Sidekick.Focus;

internal sealed record NearbyFocusPlayer(FocusCharacter Character, float DistanceYalms);
internal sealed record FocusScene(FocusCharacter Self, IReadOnlyList<NearbyFocusPlayer> Players);

internal interface IFocusSceneSource
{
    FocusScene? Read();
}

// Called on the framework thread. No game-object wrappers or addresses escape into the snapshot.
internal sealed class NearbyFocusPlayers(IClientState client, IObjectTable objects, ICondition condition, IFramework framework) : IFocusSceneSource
{
    public FocusScene? Read()
    {
        if (!framework.IsInFrameworkUpdateThread || !client.IsLoggedIn || condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51] ||
            objects.LocalPlayer is not { } local || FocusCharacter.Create(local.Name.TextValue, local.HomeWorld.RowId) is not { } self)
            return null;

        var position = local.Position;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            return null;
        var players = new List<NearbyFocusPlayer>();
        foreach (var player in objects.PlayerObjects.OfType<IPlayerCharacter>())
        {
            if (FocusCharacter.Create(player.Name.TextValue, player.HomeWorld.RowId) is not { } identity || identity.Matches(self))
                continue;
            var distance = Vector3.Distance(position, player.Position);
            if (float.IsFinite(distance))
                players.Add(new NearbyFocusPlayer(identity, distance));
        }
        return new FocusScene(self, players.DistinctBy(p => p.Character)
            .OrderBy(p => p.DistanceYalms).ToArray());
    }
}
