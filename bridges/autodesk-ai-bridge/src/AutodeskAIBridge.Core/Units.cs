using System.Globalization;

namespace AutodeskAIBridge.Core;

public enum BridgeUnit
{
    Millimetres,
    Centimetres,
    Metres,
    Inches,
    Feet,
    Degrees,
    Radians
}

/// <summary>Single unit conversion boundary. Linear values use metres; angles use radians.</summary>
public static class BridgeUnits
{
    public static bool TryParse(string? unit, out BridgeUnit value)
    {
        value = unit?.Trim().ToLowerInvariant() switch
        {
            "mm" or "millimetre" or "millimetres" => BridgeUnit.Millimetres,
            "cm" or "centimetre" or "centimetres" => BridgeUnit.Centimetres,
            "m" or "metre" or "metres" => BridgeUnit.Metres,
            "in" or "inch" or "inches" => BridgeUnit.Inches,
            "ft" or "foot" or "feet" => BridgeUnit.Feet,
            "deg" or "degree" or "degrees" => BridgeUnit.Degrees,
            "rad" or "radian" or "radians" => BridgeUnit.Radians,
            _ => default
        };
        return unit?.Trim().ToLowerInvariant() is "mm" or "millimetre" or "millimetres" or
            "cm" or "centimetre" or "centimetres" or "m" or "metre" or "metres" or
            "in" or "inch" or "inches" or "ft" or "foot" or "feet" or
            "deg" or "degree" or "degrees" or "rad" or "radian" or "radians";
    }

    public static double ToMetres(double value, string unit)
    {
        if (!TryParse(unit, out var parsed) || !Finite(value) || parsed is BridgeUnit.Degrees or BridgeUnit.Radians)
            throw new ArgumentException("Linear unit and finite value required.", nameof(unit));
        return parsed switch
        {
            BridgeUnit.Millimetres => value / 1000d,
            BridgeUnit.Centimetres => value / 100d,
            BridgeUnit.Metres => value,
            BridgeUnit.Inches => value * 0.0254d,
            BridgeUnit.Feet => value * 0.3048d,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }

    public static double FromMetres(double value, string unit)
    {
        if (!TryParse(unit, out var parsed) || !Finite(value) || parsed is BridgeUnit.Degrees or BridgeUnit.Radians)
            throw new ArgumentException("Linear unit and finite value required.", nameof(unit));
        return parsed switch
        {
            BridgeUnit.Millimetres => value * 1000d,
            BridgeUnit.Centimetres => value * 100d,
            BridgeUnit.Metres => value,
            BridgeUnit.Inches => value / 0.0254d,
            BridgeUnit.Feet => value / 0.3048d,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }

    public static double ToRadians(double value, string unit)
    {
        if (!TryParse(unit, out var parsed) || !Finite(value) || parsed is not (BridgeUnit.Degrees or BridgeUnit.Radians))
            throw new ArgumentException("Angle unit and finite value required.", nameof(unit));
        return parsed == BridgeUnit.Degrees ? value * Math.PI / 180d : value;
    }

    public static double FromRadians(double value, string unit)
    {
        if (!TryParse(unit, out var parsed) || !Finite(value) || parsed is not (BridgeUnit.Degrees or BridgeUnit.Radians))
            throw new ArgumentException("Angle unit and finite value required.", nameof(unit));
        return parsed == BridgeUnit.Degrees ? value * 180d / Math.PI : value;
    }

    public static string Format(double value, string unit) => $"{value.ToString("G17", CultureInfo.InvariantCulture)} {unit}";
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
