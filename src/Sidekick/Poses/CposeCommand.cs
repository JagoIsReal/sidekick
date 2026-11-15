using System.Globalization;

namespace Sidekick.Poses;

internal static class CposeCommand
{
    internal static bool TryParse(string arguments, out int poseNumber)
    {
        poseNumber = 0;
        var parts = arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].Equals("cpose", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out poseNumber) &&
            poseNumber is >= 1 and <= byte.MaxValue;
    }

    internal static byte? ToIndex(int poseNumber, byte lastIndex) =>
        poseNumber >= 1 && poseNumber <= lastIndex + 1 ? (byte)(poseNumber - 1) : null;

    internal static byte Predecessor(byte targetIndex, byte lastIndex) => targetIndex == 0 ? lastIndex : (byte)(targetIndex - 1);
}

internal enum CposeKind : byte { Idle, WeaponDrawn, Sit, GroundSit, Doze, Umbrella, Accessory }

internal sealed record CposeSnapshot(ulong CharacterId, uint EntityId, CposeKind Kind, byte CurrentIndex,
    byte SelectedIndex, byte LastIndex, bool CanExecute)
{
    internal bool SameContext(CposeSnapshot other) => CharacterId == other.CharacterId && EntityId == other.EntityId &&
        Kind == other.Kind && LastIndex == other.LastIndex;
}

internal interface ICposeGame
{
    bool Available { get; }
    CposeSnapshot? Read();
    bool Apply(CposeSnapshot expected, byte targetIndex);
    void Finish(CposeSnapshot original, byte targetIndex, bool restore);
}
