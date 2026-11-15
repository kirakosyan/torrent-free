using System.Globalization;
using System.Text;

namespace TorrentFree.Services;

public readonly record struct NumericInputResult(string Text, double Value);

/// <summary>One parsing contract for global and per-transfer numeric editors.</summary>
public static class NumericInput
{
    public static NumericInputResult Parse(string? text, bool allowDecimal, double maximum, CultureInfo culture)
    {
        var normalized = new StringBuilder();
        var separatorSeen = false;
        foreach (var rune in (text ?? string.Empty).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber)
                normalized.Append((char)('0' + (int)Rune.GetNumericValue(rune)));
            else if (allowDecimal && !separatorSeen && rune.Value is '.' or ',' or 0x066B)
            {
                normalized.Append('.');
                separatorSeen = true;
            }
        }

        var canonical = normalized.ToString();
        var value = canonical is "" or "." ? 0 :
            double.TryParse(canonical, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : double.PositiveInfinity;
        if (!double.IsFinite(value) || value > maximum)
            return new(maximum.ToString(culture), maximum);
        return new(canonical.Replace(".", culture.NumberFormat.NumberDecimalSeparator), value);
    }
}
