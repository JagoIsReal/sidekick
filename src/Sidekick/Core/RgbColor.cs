using System.Numerics;

namespace Sidekick.Core;

public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    public const uint DefaultRgb = 0xFFD54F;

    public uint PackedRgb => (uint)(Red << 16 | Green << 8 | Blue);
    public uint ToRgba(byte alpha) => Red | (uint)Green << 8 | (uint)Blue << 16 | (uint)alpha << 24;
    public Vector3 ToVector3() => new(Red / 255f, Green / 255f, Blue / 255f);

    public static RgbColor FromRgb(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static RgbColor FromVector3(Vector3 color) => new(ToByte(color.X), ToByte(color.Y), ToByte(color.Z));

    private static byte ToByte(float value) => float.IsFinite(value)
        ? (byte)MathF.Round(Math.Clamp(value, 0, 1) * 255)
        : (byte)0;
}

