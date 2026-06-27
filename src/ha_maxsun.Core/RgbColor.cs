namespace HaMaxsun.Core;

public readonly record struct RgbColor(byte Red, byte Green, byte Blue)
{
    public static readonly RgbColor Black = new(0, 0, 0);

    public int[] ToArray() => [Red, Green, Blue];

    public override string ToString() => $"{Red},{Green},{Blue}";
}

