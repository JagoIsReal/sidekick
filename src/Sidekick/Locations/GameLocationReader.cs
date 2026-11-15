using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Sidekick.Locations;

internal interface ILocationReader
{
    bool IsTransitioning { get; }
    LocationSnapshot? Read();
}

internal sealed class GameLocationReader(IClientState client, IPlayerState player, ICondition condition) : ILocationReader
{
    public bool IsTransitioning => client.IsLoggedIn && (condition[ConditionFlag.BetweenAreas]
        || condition[ConditionFlag.BetweenAreas51]);

    // Called only from IFramework.Update. No pointers survive this call.
    public unsafe LocationSnapshot? Read()
    {
        if (!client.IsLoggedIn || !player.IsLoaded || IsTransitioning
            || client.TerritoryType == 0 || player.CurrentWorld.RowId == 0)
            return null;

        var ui = UIState.Instance();
        var instance = ui != null && ui->PublicInstance.TerritoryTypeId == client.TerritoryType
            ? ui->PublicInstance.InstanceId : 0;
        var snapshot = new LocationSnapshot(client.TerritoryType, player.CurrentWorld.RowId, instance);
        var housing = HousingManager.Instance();
        if (housing == null || housing->CurrentTerritory == null)
            return snapshot;

        var ward = housing->GetCurrentWard();
        if (ward < 0)
            return snapshot;

        var house = housing->GetCurrentHouseId();
        return snapshot.WithHousing(house.TerritoryTypeId, ward, housing->GetCurrentPlot(),
            housing->GetCurrentRoom(), housing->GetCurrentDivision(), housing->IsInside() || housing->IsInWorkshop());
    }
}
