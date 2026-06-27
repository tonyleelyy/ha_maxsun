namespace HaMaxsun.Core;

public sealed record LightState(bool On, RgbColor Color, int Brightness)
{
    public static readonly LightState Default = new(false, new RgbColor(255, 255, 255), 255);

    public RgbColor EffectiveColor => ColorMath.EffectiveColor(On, Color, Brightness);
}

