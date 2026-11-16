using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Sidekick.Core;
using NativeFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace Sidekick.Locations;

internal interface ILocationReader
{
    bool IsTransitioning { get; }
    LocationSnapshot? Read();
}

internal sealed class GameLocationReader(IClientState client, IPlayerState player, ICondition condition, IObjectTable objects) : ILocationReader
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
        var weather = WeatherManager.Instance();
        var weatherId = weather != null ? weather->GetCurrentWeather() : (byte)0;
        var framework = NativeFramework.Instance();
        var time = framework != null ? framework->ClientTime.EorzeaTime : 0;
        var identity = CharacterIdentity.FromName(player.CharacterName, player.HomeWorld.RowId);
        var actor = objects.LocalPlayer;
        var actorIdentity = actor is null ? null : CharacterIdentity.FromName(actor.Name.TextValue, actor.HomeWorld.RowId);
        var status = identity is not null && actorIdentity == identity ? actor?.OnlineStatus.RowId : null;
        var snapshot = new LocationSnapshot(client.TerritoryType, player.CurrentWorld.RowId, instance,
            Character: identity, OnlineStatusId: status,
            WeatherId: weatherId > 0 ? weatherId : null,
            Daytime: time > 0 ? IsDaytime(time) : null,
            Flying: condition[ConditionFlag.InFlight],
            Swimming: condition[ConditionFlag.Swimming] || condition[ConditionFlag.Diving],
            Diving: condition[ConditionFlag.Diving], Mounted: condition[ConditionFlag.Mounted] || condition[ConditionFlag.InFlight],
            AppearanceBlocked: client.IsGPosing || condition[ConditionFlag.InCombat]
                || condition[ConditionFlag.WatchingCutscene] || condition[ConditionFlag.WatchingCutscene78]);
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

    internal static bool IsDaytime(long eorzeaTime)
    {
        var hour = (eorzeaTime % 86400 + 86400) % 86400 / 3600;
        return hour is >= 6 and < 18;
    }
}
