using Dalamud.Plugin.Services;
using Sidekick.Core;

namespace Sidekick.Player;

internal sealed class LocalCharacterTracker : IDisposable
{
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IFramework framework;
    private string? lastName;

    public LocalCharacterTracker(IClientState clientState, IPlayerState playerState, IFramework framework)
    {
        this.clientState = clientState;
        this.playerState = playerState;
        this.framework = framework;
        clientState.Login += Refresh;
        clientState.Logout += OnLogout;
        framework.Update += OnUpdate;
        Refresh();
    }

    public CharacterIdentity? Current { get; private set; }

    public void Refresh()
    {
        var loaded = clientState.IsLoggedIn && playerState.IsLoaded;
        var name = loaded ? playerState.CharacterName : null;
        var homeWorldId = loaded ? playerState.HomeWorld.RowId : 0;
        if (string.Equals(lastName, name, StringComparison.Ordinal) &&
            (Current?.HomeWorldId ?? 0) == homeWorldId)
            return;

        lastName = name;
        Current = CharacterIdentity.FromName(name, homeWorldId);
    }

    public void Dispose()
    {
        clientState.Login -= Refresh;
        clientState.Logout -= OnLogout;
        framework.Update -= OnUpdate;
        OnLogout(0, 0);
    }

    private void OnLogout(int type, int code)
    {
        lastName = null;
        Current = null;
    }

    private void OnUpdate(IFramework _) // Login data can become available after the Login event.
    {
        if ((Current is null || Current.HomeWorldId == 0) &&
            clientState.IsLoggedIn && playerState.IsLoaded)
            Refresh();
    }

}

