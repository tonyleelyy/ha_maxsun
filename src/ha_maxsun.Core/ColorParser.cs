using System.Globalization;

namespace HaMaxsun.Core;

public static class ColorParser
{
    public static bool TryParseRgb(string? value, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var red) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var green) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        if (!IsByte(red) || !IsByte(green) || !IsByte(blue))
        {
            return false;
        }

        color = new RgbColor((byte)red, (byte)green, (byte)blue);
        return true;
    }

    public static RgbColor ParseRgbOrDefault(string? value, RgbColor fallback)
        => TryParseRgb(value, out var color) ? color : fallback;

    private static bool IsByte(int value) => value is >= 0 and <= 255;
}

