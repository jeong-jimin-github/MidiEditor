using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MidiEditor.Controls;

internal static class DrawingTools
{
    public static readonly Typeface Regular = new("Segoe UI");
    public static readonly Typeface SemiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return fallback;
        }
    }

    public static FormattedText Text(string text, double size, Brush brush, bool bold = false) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bold ? SemiBold : Regular, size, brush, 1.0);

    public static bool IsBlackKey(int pitch)
    {
        var note = ((pitch % 12) + 12) % 12;
        return note is 1 or 3 or 6 or 8 or 10;
    }

    public static string NoteName(int pitch)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return $"{names[pitch % 12]}{pitch / 12 - 1}";
    }
}

