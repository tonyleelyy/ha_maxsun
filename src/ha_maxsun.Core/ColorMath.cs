namespace HaMaxsun.Core;

public static class ColorMath
{
    public static RgbColor ApplyBrightness(RgbColor color, int brightness)
    {
        var clamped = ClampByte(brightness);
        return new RgbColor(
            Scale(color.Red, clamped),
            Scale(color.Green, clamped),
            Scale(color.Blue, clamped));
    }

    public static RgbColor EffectiveColor(bool on, RgbColor color, int brightness)
        => on ? ApplyBrightness(color, brightness) : RgbColor.Black;

    public static byte ClampByte(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 255)
        {
            return 255;
        }

        return (byte)value;
    }

    private static byte Scale(byte value, byte brightness)
        => (byte)((value * brightness) / 255);
}

