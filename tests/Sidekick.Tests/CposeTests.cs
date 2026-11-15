using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Moq;
using Sidekick.Localization;
using Sidekick.Poses;

namespace Sidekick.Tests;

public sealed class CposeTests
{
    [Theory]
    [InlineData("cpose 1", 1)]
    [InlineData(" cpose 5 ", 5)]
    [InlineData("CPOSE\t7", 7)]
    [InlineData("cpose 01", 1)]
    public void CommandsAcceptPositiveOneBasedIndices(string text, int expected)
    {
        Assert.True(CposeCommand.TryParse(text, out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cpose")]
    [InlineData("cpose 0")]
    [InlineData("cpose -1")]
    [InlineData("cpose +1")]
    [InlineData("cpose 1.5")]
    [InlineData("cpose 9999999999999999999")]
    [InlineData("cpose 256")]
    [InlineData("cpose 1 5")]
    [InlineData("focus 1")]
    public void InvalidCommandsNeverProduceAValidRequest(string text) => Assert.False(CposeCommand.TryParse(text, out _));

    [Theory]
    [InlineData(1, 6, 0, 6)]
    [InlineData(5, 6, 4, 3)]
    [InlineData(7, 6, 6, 5)]
    [InlineData(1, 0, 0, 0)]
    public void IndicesAndPredecessorsRespectEachPosturesNativeRange(int number, byte last, byte target, byte previous)
    {
        Assert.Equal(target, CposeCommand.ToIndex(number, last));
        Assert.Equal(previous, CposeCommand.Predecessor(target, last));
        Assert.Null(CposeCommand.ToIndex(0, last));
        Assert.Null(CposeCommand.ToIndex(last + 2, last));
    }

    [Fact]
    public void PoseKindsMatchTheTargetedPublicNativeEnum()
    {
        foreach (var kind in Enum.GetValues<CposeKind>())
        {
            Assert.True(Enum.TryParse<EmoteController.PoseType>(kind.ToString(), out var native));
            Assert.Equal((byte)kind, (byte)native);
        }
    }

    [Fact]
    public void SelectingPoseFiveExecutesOnceAndNeverPrintsConfirmation()
    {
        using var fixture = new Fixture();
        fixture.Controller.Request(5);
        Assert.Empty(fixture.Game.Calls);
        fixture.Controller.Poll(0);
        Assert.Equal((byte)4, Assert.Single(fixture.Game.Calls));
        Assert.Empty(fixture.Printed);
        fixture.Game.Confirm();
        fixture.Controller.Poll(10);
        Assert.Single(fixture.Game.Calls);
        Assert.Empty(fixture.Printed);
        Assert.False(fixture.Game.HasStaging);
        fixture.Controller.Poll(2000);
        Assert.Single(fixture.Game.Calls);
    }

    [Fact]
    public void NoCommandsAreSentWhenAlreadyOnTheRequestedPose()
    {
        using var fixture = new Fixture();
        fixture.Controller.Request(1);
        fixture.Controller.Poll(0);
        Assert.Empty(fixture.Game.Calls);
        Assert.Empty(fixture.Printed);
    }

    [Fact]
    public void RangeComesFromTheCurrentPostureAndInvalidNumbersDoNotMutateState()
    {
        using var fixture = new Fixture();
        fixture.Game.Current = fixture.Game.Current! with { Kind = CposeKind.GroundSit, LastIndex = 2 };
        var before = fixture.Game.Current;
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        Assert.Empty(fixture.Game.Calls);
        Assert.Same(before, fixture.Game.Current);
        Assert.Equal(string.Format(Strings.Culture, Strings.CposeOutOfRange, Strings.CposeGroundSitting, 3), Assert.Single(fixture.Errors));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void MissingGameCharacterOrPermissionBlocksExecution(bool available, bool loaded, bool permitted)
    {
        using var fixture = new Fixture();
        fixture.Game.Available = available;
        fixture.Game.Current = loaded ? fixture.Game.Current! with { CanExecute = permitted } : null;
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        Assert.Empty(fixture.Game.Calls);
        Assert.Single(fixture.Errors);
    }

    [Fact]
    public void FailureRestoresOnlyStagingOwnedByThePlugin()
    {
        using var fixture = new Fixture();
        var before = fixture.Game.Current;
        fixture.Game.Reject = true;
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        Assert.Single(fixture.Game.Calls);
        Assert.Equal(before, fixture.Game.Current);
        Assert.False(fixture.Game.HasStaging);
        Assert.Single(fixture.Errors);
    }

    [Fact]
    public void TimeoutCleansUpWithoutCyclingThroughMoreEmotes()
    {
        using var fixture = new Fixture();
        var before = fixture.Game.Current;
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        fixture.Controller.Poll(1499);
        Assert.Empty(fixture.Errors);
        fixture.Controller.Poll(1500);
        Assert.Single(fixture.Game.Calls);
        Assert.Equal(before, fixture.Game.Current);
        Assert.Equal(Strings.CposeNotConfirmed, Assert.Single(fixture.Errors));
    }

    [Fact]
    public void AManualChangeAfterApplicationIsPreserved()
    {
        using var fixture = new Fixture();
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        var manual = fixture.Game.Current! with { CurrentIndex = 6, SelectedIndex = 6 };
        fixture.Game.Current = manual;
        fixture.Controller.Poll(1500);
        Assert.Same(manual, fixture.Game.Current);
        Assert.Single(fixture.Game.Calls);
    }

    [Fact]
    public void RapidRequestsKeepTheLatestWithoutCreatingAnUnboundedQueue()
    {
        using var fixture = new Fixture();
        fixture.Controller.Request(2);
        fixture.Controller.Request(3);
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        Assert.Equal((byte)4, Assert.Single(fixture.Game.Calls));
        fixture.Controller.Request(6);
        fixture.Controller.Request(7);
        fixture.Controller.Poll(10);
        Assert.Single(fixture.Game.Calls);
        fixture.Game.Confirm();
        fixture.Controller.Poll(20);
        Assert.Equal(new byte[] { 4, 6 }, fixture.Game.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LogoutOrCharacterChangeCancelsThePendingRequest(bool logout)
    {
        using var fixture = new Fixture();
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        fixture.Game.Current = logout ? null : fixture.Game.Current! with { CharacterId = 456 };
        var changed = fixture.Game.Current;
        fixture.Controller.Poll(10);
        Assert.Same(changed, fixture.Game.Current);
        Assert.Equal(Strings.CposeCancelled, Assert.Single(fixture.Errors));
        fixture.Controller.Poll(2000);
        Assert.Single(fixture.Game.Calls);
    }

    [Fact]
    public void PostureChangesCancelRatherThanApplyingAnIndexToAnotherPoseFamily()
    {
        using var fixture = new Fixture();
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        var changed = fixture.Game.Current! with { Kind = CposeKind.Sit, LastIndex = 3 };
        fixture.Game.Current = changed;
        fixture.Controller.Poll(10);
        Assert.Same(changed, fixture.Game.Current);
        Assert.Equal(Strings.CposeCancelled, Assert.Single(fixture.Errors));
    }

    [Fact]
    public void DisposeUnsubscribesCancelsQueuedWorkAndCleansOwnedStaging()
    {
        var fixture = new Fixture();
        var before = fixture.Game.Current;
        fixture.Controller.Request(5);
        fixture.Framework.Raise(x => x.Update += null, fixture.Framework.Object);
        Assert.Single(fixture.Game.Calls);
        fixture.Controller.Request(7);
        fixture.Dispose();
        Assert.Equal(before, fixture.Game.Current);
        fixture.Framework.Raise(x => x.Update += null, fixture.Framework.Object);
        fixture.Controller.Request(2);
        fixture.Controller.Poll(999999);
        Assert.Single(fixture.Game.Calls);
    }

    [Fact]
    public void ExceptionsAreIsolatedAndOwnedStagingIsCleaned()
    {
        using var fixture = new Fixture();
        fixture.Game.ThrowAfterStaging = true;
        var before = fixture.Game.Current;
        fixture.Controller.Request(5);
        fixture.Controller.Poll(0);
        Assert.Equal(before, fixture.Game.Current);
        Assert.Equal(Strings.CposeFailed, Assert.Single(fixture.Errors));
        fixture.Controller.Poll(2000);
        Assert.Single(fixture.Game.Calls);
    }

    [Fact]
    public void NoRequestDoesNotReadTheGameOnEachFrame()
    {
        using var fixture = new Fixture();
        fixture.Framework.Raise(x => x.Update += null, fixture.Framework.Object);
        fixture.Controller.Poll(1000);
        Assert.Equal(0, fixture.Game.Reads);
        Assert.Empty(fixture.Game.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Mock<IFramework> Framework = new();
        internal readonly FakeCpose Game = new();
        internal readonly List<string> Printed = [];
        internal readonly List<string> Errors = [];
        internal readonly CposeController Controller;

        internal Fixture()
        {
            var chat = new Mock<IChatGui>();
            chat.Setup(x => x.Print(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<ushort?>()))
                .Callback<string, string?, ushort?>((message, _, _) => Printed.Add(message));
            chat.Setup(x => x.PrintError(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<ushort?>()))
                .Callback<string, string?, ushort?>((message, _, _) => Errors.Add(message));
            Controller = new CposeController(Framework.Object, Game, chat.Object, Mock.Of<IPluginLog>());
        }

        public void Dispose() => Controller.Dispose();
    }

    private sealed class FakeCpose : ICposeGame
    {
        public bool Available { get; set; } = true;
        internal CposeSnapshot? Current = new(123, 42, CposeKind.Idle, 0, 0, 6, true);
        internal readonly List<byte> Calls = [];
        internal bool Reject;
        internal bool ThrowAfterStaging;
        internal int Reads;
        private (CposeSnapshot Before, byte Target)? staged;
        internal bool HasStaging => staged is not null;
        public CposeSnapshot? Read()
        {
            Reads++;
            return Current;
        }

        public bool Apply(CposeSnapshot expected, byte targetIndex)
        {
            Calls.Add(targetIndex);
            staged = (expected, targetIndex);
            var previous = CposeCommand.Predecessor(targetIndex, expected.LastIndex);
            Current = expected with { CurrentIndex = previous, SelectedIndex = previous };
            if (ThrowAfterStaging)
                throw new InvalidOperationException("Simulated native failure");
            return !Reject;
        }

        internal void Confirm()
        {
            if (staged is { } active)
                Current = Current! with { CurrentIndex = active.Target, SelectedIndex = active.Target };
        }

        public void Finish(CposeSnapshot original, byte targetIndex, bool restore)
        {
            if (staged is not { } owned || owned.Before != original || owned.Target != targetIndex)
                return;
            staged = null;
            var previous = CposeCommand.Predecessor(targetIndex, original.LastIndex);
            if (restore && Current is { } current && original.SameContext(current) &&
                current.CurrentIndex == previous && current.SelectedIndex == previous)
                Current = original;
        }
    }
}
