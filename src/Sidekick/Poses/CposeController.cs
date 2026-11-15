using System.Globalization;
using Dalamud.Plugin.Services;
using Sidekick.Localization;

namespace Sidekick.Poses;

internal sealed class CposeController : IDisposable
{
    private readonly IFramework framework;
    private readonly ICposeGame game;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private int requested;
    private PendingPose? pending;
    private volatile bool disposed;
    private bool reportedError;

    internal CposeController(IFramework framework, ICposeGame game, IChatGui chat, IPluginLog log)
    {
        this.framework = framework;
        this.game = game;
        this.chat = chat;
        this.log = log;
        framework.Update += OnUpdate;
    }

    // A single pending request bounds work; rapid commands retain only the latest requested pose.
    internal void Request(int poseNumber)
    {
        if (!disposed)
            Interlocked.Exchange(ref requested, poseNumber);
    }

    private void OnUpdate(IFramework _) => Poll(Environment.TickCount64);

    internal void Poll(long now)
    {
        if (disposed || (Volatile.Read(ref requested) == 0 && pending is null))
            return;
        try
        {
            PollCore(now);
        }
        catch (Exception exception)
        {
            var interrupted = pending;
            pending = null;
            Interlocked.Exchange(ref requested, 0);
            ReportError(exception);
            if (interrupted is not null)
                Restore(interrupted);
            chat.PrintError(Strings.CposeFailed);
        }
    }

    private void PollCore(long now)
    {
        if (pending is { } active)
        {
            var current = game.Read();
            if (current is null || !active.Before.SameContext(current))
            {
                pending = null;
                Interlocked.Exchange(ref requested, 0);
                Restore(active);
                chat.PrintError(Strings.CposeCancelled);
                return;
            }
            if (current.CurrentIndex == active.Target && current.SelectedIndex == active.Target)
            {
                pending = null;
                game.Finish(active.Before, active.Target, false);
            }
            else if (now >= active.Deadline)
            {
                pending = null;
                Restore(active);
                if (Volatile.Read(ref requested) == 0)
                    chat.PrintError(Strings.CposeNotConfirmed);
            }
            else
                return;
        }

        var poseNumber = Interlocked.Exchange(ref requested, 0);
        if (poseNumber == 0)
            return;
        if (!game.Available)
        {
            chat.PrintError(Strings.CposeUnavailable);
            return;
        }
        var before = game.Read();
        if (before is null)
        {
            chat.PrintError(Strings.CposeBlocked);
            return;
        }
        if (CposeCommand.ToIndex(poseNumber, before.LastIndex) is not { } target)
        {
            chat.PrintError(string.Format(Strings.Culture, Strings.CposeOutOfRange,
                KindName(before.Kind), before.LastIndex + 1));
            return;
        }
        if (before.CurrentIndex == target && before.SelectedIndex == target)
        {
            return;
        }
        if (!before.CanExecute)
        {
            chat.PrintError(Strings.CposeBlocked);
            return;
        }
        // Record before crossing the native boundary so failures can clean up owned staging values.
        var next = new PendingPose(before, target, now + 1500);
        pending = next;
        if (!game.Apply(before, target))
        {
            pending = null;
            Restore(next);
            chat.PrintError(Strings.CposeBlocked);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        framework.Update -= OnUpdate;
        Interlocked.Exchange(ref requested, 0);
        var interrupted = pending;
        pending = null;
        if (interrupted is not null)
            Restore(interrupted);
    }

    private void Restore(PendingPose interrupted)
    {
        try
        {
            game.Finish(interrupted.Before, interrupted.Target, true);
        }
        catch (Exception exception)
        {
            ReportError(exception);
        }
    }

    private void ReportError(Exception exception)
    {
        if (reportedError)
            return;
        log.Error("Sidekick could not select a cpose ({ErrorType}); further errors are suppressed until reload.", exception.GetType().Name);
        reportedError = true;
    }

    private static string KindName(CposeKind kind) => kind switch
    {
        CposeKind.Idle => Strings.CposeStanding,
        CposeKind.WeaponDrawn => Strings.CposeWeaponDrawn,
        CposeKind.Sit => Strings.CposeSitting,
        CposeKind.GroundSit => Strings.CposeGroundSitting,
        CposeKind.Doze => Strings.CposeDozing,
        CposeKind.Umbrella => Strings.CposeUmbrella,
        _ => Strings.CposeAccessory,
    };

    private sealed record PendingPose(CposeSnapshot Before, byte Target, long Deadline);
}
