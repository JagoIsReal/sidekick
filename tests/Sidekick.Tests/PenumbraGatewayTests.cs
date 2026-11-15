using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Moq;
using Sidekick.Mods;
using CurrentSettings = System.ValueTuple<int, (bool, int, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>, bool)?>;
using TemporarySettings = System.ValueTuple<int, (bool, bool, int, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>)?, string>;
using SettingsInput = System.ValueTuple<bool, bool, int, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<string>>>;

namespace Sidekick.Tests;

public sealed class PenumbraGatewayTests
{
    private static readonly Guid Collection = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void IpcPreservesInheritedOptionsAndUsesOneNonBlockingIdentificationKey()
    {
        var ipc = new IpcFixture();
        ipc.Current.Setup(x => x.InvokeFunc(Collection, "mod", "", false)).Returns(
            (0, (true, 42, new Dictionary<string, List<string>> { ["Style"] = ["Blue"] }, true)));
        var gateway = new PenumbraGateway(ipc.Plugin.Object, ipc.Commands.Object);
        var state = gateway.ReadPermanent(Collection, "mod")!;
        Assert.True(state.Enabled);
        Assert.Equal(42, state.Priority);
        Assert.Equal("Blue", Assert.Single(state.Options["Style"]));
        Assert.False(gateway.HasTemporary(Collection, "mod"));
        Assert.True(gateway.Apply(Collection, "mod", state with { Enabled = false }));
        Assert.True(ipc.LastKey < 0);
        Assert.Equal("Sidekick location rules", ipc.Snapshot.Item3);
        Assert.False(ipc.Snapshot.Item2!.Value.Item1); // Do not force inheritance.
        Assert.False(ipc.Snapshot.Item2.Value.Item2);
        Assert.Equal(42, ipc.Snapshot.Item2.Value.Item3);
        Assert.True(gateway.Release(Collection, "mod", state with { Enabled = false }));
        Assert.Null(ipc.Snapshot.Item2);
        ipc.Remove.Verify(x => x.InvokeFunc(Collection, "mod", "", ipc.LastKey), Times.Once);
    }

    [Fact]
    public void ExpandedDefaultOptionsCanBeReleasedButSubsequentManualEditsCannot()
    {
        var ipc = new IpcFixture { ExpandDefaults = true };
        var gateway = new PenumbraGateway(ipc.Plugin.Object, ipc.Commands.Object);
        var state = new ModState(true, 0, new Dictionary<string, IReadOnlyList<string>>());
        Assert.True(gateway.Apply(Collection, "mod", state));
        Assert.True(gateway.IsApplied(Collection, "mod", state));
        Assert.True(gateway.Release(Collection, "mod", state));
        Assert.Null(ipc.Snapshot.Item2);
        Assert.True(gateway.Apply(Collection, "mod", state));
        ipc.Snapshot = (0, (false, true, 99, new Dictionary<string, List<string>> { ["Style"] = ["Changed"] }), "Sidekick location rules");
        Assert.False(gateway.IsApplied(Collection, "mod", state));
        Assert.True(gateway.Release(Collection, "mod", state));
        Assert.Equal(99, ipc.Snapshot.Item2!.Value.Item3);
        ipc.Remove.Verify(x => x.InvokeFunc(Collection, "mod", "", ipc.LastKey), Times.Once);
    }

    [Theory]
    [InlineData(0, "Other plugin")]
    [InlineData(19, "")]
    [InlineData(2, "")]
    [InlineData(3, "")]
    public void CleanupNeverRemovesForeignOrMissingSettings(int error, string source)
    {
        var ipc = new IpcFixture { Snapshot = (error, (false, true, 0, []), source) };
        var gateway = new PenumbraGateway(ipc.Plugin.Object, ipc.Commands.Object);
        var state = new ModState(true, 0, new Dictionary<string, IReadOnlyList<string>>());
        Assert.True(gateway.HasTemporary(Collection, "mod"));
        Assert.True(gateway.Release(Collection, "mod", state));
        ipc.Remove.Verify(x => x.InvokeFunc(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void UnknownCleanupFailureIsRetriedAndMissingPermanentSettingsUseDefaults()
    {
        var ipc = new IpcFixture();
        var gateway = new PenumbraGateway(ipc.Plugin.Object, ipc.Commands.Object);
        ipc.Current.Setup(x => x.InvokeFunc(Collection, "mod", "", false)).Returns((0, null));
        var defaults = gateway.ReadPermanent(Collection, "mod")!;
        Assert.False(defaults.Enabled);
        Assert.Empty(defaults.Options);
        ipc.Snapshot = (255, null, "");
        Assert.False(gateway.Release(Collection, "mod", defaults));
        ipc.Current.Setup(x => x.InvokeFunc(Collection, "mod", "", false)).Returns((3, null));
        Assert.Null(gateway.ReadPermanent(Collection, "mod"));
    }

    [Theory]
    [InlineData(ModRefreshMode.CollectionMembers)]
    [InlineData(ModRefreshMode.All)]
    [InlineData(ModRefreshMode.Self)]
    [InlineData(ModRefreshMode.Furniture)]
    public void RefreshUsesOnlyTheSelectedPublicEndpointOrCommand(ModRefreshMode mode)
    {
        var ipc = new IpcFixture();
        ipc.Commands.Setup(x => x.ProcessCommand("/penumbra redraw furniture")).Returns(true);
        var gateway = new PenumbraGateway(ipc.Plugin.Object, ipc.Commands.Object);
        gateway.Redraw(Collection, mode);
        ipc.RedrawCollection.Verify(x => x.InvokeAction(Collection, 0), mode == ModRefreshMode.CollectionMembers ? Times.Once() : Times.Never());
        ipc.RedrawAll.Verify(x => x.InvokeAction(0), mode == ModRefreshMode.All ? Times.Once() : Times.Never());
        ipc.RedrawSelf.Verify(x => x.InvokeAction(0, 0), mode == ModRefreshMode.Self ? Times.Once() : Times.Never());
        ipc.Commands.Verify(x => x.ProcessCommand("/penumbra redraw furniture"), mode == ModRefreshMode.Furniture ? Times.Once() : Times.Never());
    }

    [Fact]
    public void UnsupportedFurnitureCommandReportsFailureForRetry()
    {
        var ipc = new IpcFixture();
        ipc.Commands.Setup(x => x.ProcessCommand("/penumbra redraw furniture")).Returns(false);
        var gateway = new PenumbraGateway(ipc.Plugin.Object, ipc.Commands.Object);
        Assert.Throws<InvalidOperationException>(() => gateway.Redraw(Collection, ModRefreshMode.Furniture));
        ipc.RedrawAll.Verify(x => x.InvokeAction(It.IsAny<int>()), Times.Never());
    }

    [Fact]
    public void CollectionsAreListedByTheirRealNamesAndKeepTheirStableIdsOnRename()
    {
        var ipc = new IpcFixture();
        var id = Guid.NewGuid();
        ipc.Collections.Setup(x => x.InvokeFunc()).Returns(new Dictionary<Guid, string> { [id] = "Default" });
        var gateway = new PenumbraGateway(ipc.Plugin.Object, ipc.Commands.Object);
        Assert.Equal("Default", gateway.GetCollections()[id]);
        ipc.Collections.Setup(x => x.InvokeFunc()).Returns(new Dictionary<Guid, string> { [id] = "Housing assets" });
        Assert.Equal("Housing assets", gateway.GetCollections()[id]);
    }

    [Fact]
    public void RequiredPublicContractsAreCheckedBeforeEnablingIntegration()
    {
        var ipc = new IpcFixture();
        var gateway = new PenumbraGateway(ipc.Plugin.Object, ipc.Commands.Object);
        Assert.True(gateway.Available);
        ipc.Version.Setup(x => x.InvokeFunc()).Returns((6, 0));
        Assert.False(gateway.Available);
        ipc.Version.Setup(x => x.InvokeFunc()).Returns((5, 0));
        ipc.Collections.SetupGet(x => x.HasFunction).Returns(false);
        Assert.False(gateway.Available);
    }

    private sealed class IpcFixture
    {
        internal readonly Mock<IDalamudPluginInterface> Plugin = new();
        internal readonly Mock<ICommandManager> Commands = new();
        internal readonly Mock<ICallGateSubscriber<(int, int)>> Version = new();
        internal readonly Mock<ICallGateSubscriber<Dictionary<string, string>>> Mods = new();
        internal readonly Mock<ICallGateSubscriber<int, (bool, bool, (Guid, string))>> CollectionForObject = new();
        internal readonly Mock<ICallGateSubscriber<Dictionary<Guid, string>>> Collections = new();
        internal readonly Mock<ICallGateSubscriber<Guid, int, object>> RedrawCollection = new();
        internal readonly Mock<ICallGateSubscriber<int, object>> RedrawAll = new();
        internal readonly Mock<ICallGateSubscriber<int, int, object>> RedrawSelf = new();
        internal readonly Mock<ICallGateSubscriber<Guid, string, string, bool, CurrentSettings>> Current = new();
        internal readonly Mock<ICallGateSubscriber<Guid, string, string, int, TemporarySettings>> Query = new();
        internal readonly Mock<ICallGateSubscriber<Guid, string, string, SettingsInput, string, int, int>> Set = new();
        internal readonly Mock<ICallGateSubscriber<Guid, string, string, int, int>> Remove = new();
        internal TemporarySettings Snapshot = (0, null, "");
        internal int LastKey;
        internal bool ExpandDefaults;

        internal IpcFixture()
        {
            Plugin.Setup(x => x.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5")).Returns(Version.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList")).Returns(Mods.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<int, (bool, bool, (Guid, string))>("Penumbra.GetCollectionForObject.V5")).Returns(CollectionForObject.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections.V5")).Returns(Collections.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Guid, int, object>("Penumbra.RedrawCollectionMembers")).Returns(RedrawCollection.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<int, object>("Penumbra.RedrawAll.V5")).Returns(RedrawAll.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<int, int, object>("Penumbra.RedrawObject.V5")).Returns(RedrawSelf.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Guid, string, string, bool, CurrentSettings>("Penumbra.GetCurrentModSettings.V5")).Returns(Current.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Guid, string, string, int, TemporarySettings>("Penumbra.QueryTemporaryModSettings.V5")).Returns(Query.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Guid, string, string, SettingsInput, string, int, int>("Penumbra.SetTemporaryModSettings.V5")).Returns(Set.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Guid, string, string, int, int>("Penumbra.RemoveTemporaryModSettings.V5")).Returns(Remove.Object);
            Version.SetupGet(x => x.HasFunction).Returns(true);
            Version.Setup(x => x.InvokeFunc()).Returns((5, 0));
            Mods.SetupGet(x => x.HasFunction).Returns(true);
            CollectionForObject.SetupGet(x => x.HasFunction).Returns(true);
            Collections.SetupGet(x => x.HasFunction).Returns(true);
            Current.SetupGet(x => x.HasFunction).Returns(true);
            Query.SetupGet(x => x.HasFunction).Returns(true);
            Set.SetupGet(x => x.HasFunction).Returns(true);
            Remove.SetupGet(x => x.HasFunction).Returns(true);
            RedrawCollection.SetupGet(x => x.HasAction).Returns(true);
            RedrawAll.SetupGet(x => x.HasAction).Returns(true);
            RedrawSelf.SetupGet(x => x.HasAction).Returns(true);
            Query.Setup(x => x.InvokeFunc(It.IsAny<Guid>(), It.IsAny<string>(), "", It.IsAny<int>())).Returns(() => Snapshot);
            Set.Setup(x => x.InvokeFunc(It.IsAny<Guid>(), It.IsAny<string>(), "", It.IsAny<SettingsInput>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<Guid, string, string, SettingsInput, string, int>((_, _, _, input, source, key) =>
                {
                    LastKey = key;
                    var options = input.Item4.ToDictionary(p => p.Key, p => p.Value.ToList());
                    if (ExpandDefaults && options.Count == 0)
                        options["Style"] = ["Default"];
                    Snapshot = (0, (input.Item1, input.Item2, input.Item3, options), source);
                }).Returns(0);
            Remove.Setup(x => x.InvokeFunc(It.IsAny<Guid>(), It.IsAny<string>(), "", It.IsAny<int>()))
                .Callback(() => Snapshot = (0, null, "")).Returns(0);
        }
    }
}
