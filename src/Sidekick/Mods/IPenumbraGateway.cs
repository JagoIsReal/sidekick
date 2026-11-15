namespace Sidekick.Mods;

internal sealed record ModState(bool Enabled, int Priority, IReadOnlyDictionary<string, IReadOnlyList<string>> Options);
internal readonly record struct ModRefreshRequest(Guid Collection, ModRefreshMode Mode);

// Boundary for optional IPC; the rule engine has no Dalamud/native dependencies.
internal interface IPenumbraGateway
{
    bool Available { get; }
    Guid? CurrentCollection();
    IReadOnlyDictionary<Guid, string> GetCollections();
    IReadOnlyDictionary<string, string> GetMods();
    ModState? ReadPermanent(Guid collection, string mod);
    bool HasTemporary(Guid collection, string mod);
    bool Apply(Guid collection, string mod, ModState state);
    bool IsApplied(Guid collection, string mod, ModState applied);
    // Return false only when cleanup must be retried; do not remove another owner's settings.
    bool Release(Guid collection, string mod, ModState applied);
    void Redraw(Guid collection, ModRefreshMode mode);
}
