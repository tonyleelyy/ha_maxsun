using System.Globalization;

namespace HaMaxsun.Core;

public sealed class EntityIds
{
    public string Power { get; set; } = "input_boolean.maxsun_motherboard_rgb_power";
    public string Brightness { get; set; } = "input_number.maxsun_motherboard_rgb_brightness";
    public string Color { get; set; } = "input_text.maxsun_motherboard_rgb_color";
    public string Available { get; set; } = "input_boolean.maxsun_motherboard_rgb_available";

    public bool IsControlEntity(string entityId)
        => string.Equals(entityId, Power, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(entityId, Brightness, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(entityId, Color, StringComparison.OrdinalIgnoreCase);
}

public sealed record EntityState(string EntityId, string? State);

public sealed class LightStateAccumulator
{
    private readonly EntityIds _entities;
    private bool _on = LightState.Default.On;
    private RgbColor _color = LightState.Default.Color;
    private int _brightness = LightState.Default.Brightness;

    public LightStateAccumulator(EntityIds entities)
    {
        _entities = entities;
    }

    public LightState Current => new(_on, _color, _brightness);

    public bool Update(EntityState state)
    {
        if (string.Equals(state.EntityId, _entities.Power, StringComparison.OrdinalIgnoreCase))
        {
            _on = string.Equals(state.State, "on", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        if (string.Equals(state.EntityId, _entities.Brightness, StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseBrightness(state.State, out var brightness))
            {
                _brightness = ColorMath.ClampByte(brightness);
                return true;
            }

            return false;
        }

        if (string.Equals(state.EntityId, _entities.Color, StringComparison.OrdinalIgnoreCase))
        {
            if (ColorParser.TryParseRgb(state.State, out var color))
            {
                _color = color;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryParseBrightness(string? state, out int brightness)
    {
        brightness = 0;
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        if (int.TryParse(state, NumberStyles.Integer, CultureInfo.InvariantCulture, out brightness))
        {
            return true;
        }

        if (double.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            brightness = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            return true;
        }

        return false;
    }
}

