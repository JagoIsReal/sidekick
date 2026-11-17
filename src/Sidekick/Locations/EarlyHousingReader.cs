using Dalamud.Game.ClientState;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Sidekick.Core;

namespace Sidekick.Locations;

internal interface IEarlyHousingReader
{
    LocationSnapshot? Read(bool transitioning);
    void Reset();
}

internal sealed class EarlyHousingReader : IEarlyHousingReader, IDisposable
{
    private readonly IClientState client;
    private readonly IPlayerState player;
    private readonly EarlyHousingContext context = new();
    private HousingZoneNotice? pending;
    private bool disposed;

    internal EarlyHousingReader(IClientState client, IPlayerState player)
    {
        this.client = client;
        this.player = player;
        client.ZoneInit += OnZoneInit;
    }

    // The notification copies managed values only; all native reads and IPC stay on Update.
    private void OnZoneInit(ZoneInitEventArgs args)
        => Interlocked.Exchange(ref pending, new HousingZoneNotice(args.TerritoryType.RowId, args.Weather.RowId));

    public void Reset()
    {
        Interlocked.Exchange(ref pending, null);
        context.Reset();
    }

    public unsafe LocationSnapshot? Read(bool transitioning)
    {
        if (disposed || !client.IsLoggedIn)
        {
            Reset();
            return null;
        }
        if (!player.IsLoaded) return null;

        var notice = Interlocked.Exchange(ref pending, null);
        var character = CharacterIdentity.FromName(player.CharacterName, player.HomeWorld.RowId);
        var game = GameMain.Instance();
        var housing = HousingManager.Instance();
        var unavailable = housing == null || housing->CurrentTerritory == null
            || housing->OutdoorTerritory == null || housing->CurrentTerritory != (HousingTerritory*)housing->OutdoorTerritory;
        OutdoorHousingAddress? address = null;
        if (!unavailable)
        {
            var house = housing->GetCurrentHouseId();
            if (house.WorldId == player.CurrentWorld.RowId)
                address = new OutdoorHousingAddress(house.TerritoryTypeId, house.WorldId,
                    housing->GetCurrentWard(), housing->GetCurrentDivision());
        }
        // CurrentTerritoryTypeId can be zero while the destination layout is loading.
        var territory = game == null ? 0 : transitioning && game->TerritoryLoadState == 1
            && game->NextTerritoryTypeId != 0 ? game->NextTerritoryTypeId : game->CurrentTerritoryTypeId;
        return context.Observe(transitioning, character, territory,
            address, unavailable, notice);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        client.ZoneInit -= OnZoneInit;
        Reset();
    }
}
