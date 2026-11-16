using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Moq;
using Newtonsoft.Json.Linq;
using Sidekick.Appearance;
using Sidekick.Core;

namespace Sidekick.Tests;

public sealed class GlamourerGatewayTests
{
    [Fact]
    public void PublicContractsApplyEquipmentOnceWithoutLockingAndUseTheLiveObjectIndex()
    {
        var fixture = new Fixture();
        var gateway = new GlamourerGateway(fixture.Plugin.Object, fixture.Objects.Object);
        Assert.True(gateway.Available);
        Assert.Equal(fixture.Character, gateway.Character);
        var before = gateway.Capture(fixture.Character)!;
        Assert.Equal(JObject.Parse(before).ToString(), fixture.State.ToString());
        Assert.True(gateway.Apply(fixture.Character, fixture.Design));
        fixture.Apply.Verify(x => x.InvokeFunc(fixture.Design, 17, 0, 3UL), Times.Once);
        Assert.True(gateway.Restore(fixture.Character, before));
        fixture.Restore.Verify(x => x.InvokeFunc(It.Is<object>(o => o is JObject), 17, 0, 3UL), Times.Once);
        Assert.Equal("Test outfit", gateway.GetDesigns()[fixture.Design]);
    }

    [Fact]
    public void WrongHomeWorldMissingActorAndForeignLocksCannotBeOverridden()
    {
        var fixture = new Fixture();
        var gateway = new GlamourerGateway(fixture.Plugin.Object, fixture.Objects.Object);
        var otherWorld = CharacterIdentity.FromName(fixture.Character.DisplayName, 22)!;
        Assert.Null(gateway.Capture(otherWorld));
        Assert.False(gateway.Apply(otherWorld, fixture.Design));
        Assert.False(gateway.Restore(otherWorld, "{}"));
        fixture.Read.Setup(x => x.InvokeFunc(17, 0)).Returns((6, null));
        Assert.Null(gateway.Capture(fixture.Character));
        fixture.Apply.Setup(x => x.InvokeFunc(fixture.Design, 17, 0, 3UL)).Returns(6);
        Assert.False(gateway.Apply(fixture.Character, fixture.Design));
        fixture.Objects.SetupGet(x => x.LocalPlayer).Returns((IPlayerCharacter?)null);
        Assert.Null(gateway.Character);
        Assert.False(gateway.Restore(fixture.Character, "{}"));
        fixture.Restore.Verify(x => x.InvokeFunc(It.IsAny<object>(), It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<ulong>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void UnsupportedMajorVersionsAreUnavailable(int major)
    {
        var fixture = new Fixture();
        fixture.Version.Setup(x => x.InvokeFunc()).Returns((major, 8));
        Assert.False(new GlamourerGateway(fixture.Plugin.Object, fixture.Objects.Object).Available);
    }

    private sealed class Fixture
    {
        internal readonly Mock<IDalamudPluginInterface> Plugin = new();
        internal readonly Mock<IObjectTable> Objects = new();
        internal readonly Mock<ICallGateSubscriber<(int, int)>> Version = new();
        internal readonly Mock<ICallGateSubscriber<int, uint, (int, JObject?)>> Read = new();
        internal readonly Mock<ICallGateSubscriber<Guid, int, uint, ulong, int>> Apply = new();
        internal readonly Mock<ICallGateSubscriber<object, int, uint, ulong, int>> Restore = new();
        internal readonly CharacterIdentity Character = CharacterIdentity.FromName("Ann Example", 21)!;
        internal readonly Guid Design = Guid.NewGuid();
        internal readonly JObject State = JObject.Parse("{\"Equipment\":{\"Body\":1}}");

        internal Fixture()
        {
            var actor = new Mock<IPlayerCharacter>();
            actor.SetupGet(x => x.Name).Returns(new SeString(new TextPayload(Character.DisplayName)));
            actor.SetupGet(x => x.HomeWorld).Returns(new RowRef<World>(null!, 21, Lumina.Data.Language.English));
            actor.SetupGet(x => x.ObjectIndex).Returns((ushort)17);
            Objects.SetupGet(x => x.LocalPlayer).Returns(actor.Object);
            Version.SetupGet(x => x.HasFunction).Returns(true);
            Version.Setup(x => x.InvokeFunc()).Returns((1, 8));
            var designs = new Mock<ICallGateSubscriber<Dictionary<Guid, string>>>();
            designs.SetupGet(x => x.HasFunction).Returns(true);
            designs.Setup(x => x.InvokeFunc()).Returns(new Dictionary<Guid, string> { [Design] = "Test outfit" });
            Read.SetupGet(x => x.HasFunction).Returns(true);
            Read.Setup(x => x.InvokeFunc(17, 0)).Returns((0, State));
            Apply.SetupGet(x => x.HasFunction).Returns(true);
            Restore.SetupGet(x => x.HasFunction).Returns(true);
            Plugin.Setup(x => x.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersion.V2")).Returns(Version.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList.V2")).Returns(designs.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState")).Returns(Read.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<Guid, int, uint, ulong, int>("Glamourer.ApplyDesign")).Returns(Apply.Object);
            Plugin.Setup(x => x.GetIpcSubscriber<object, int, uint, ulong, int>("Glamourer.ApplyState")).Returns(Restore.Object);
        }
    }
}
