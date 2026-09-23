using System;

namespace CastBarTranslator.Presentation;

public static class CastBarFontSizeFitter
{
    private const byte AbsoluteMinimumFontSize = 8;

    public static byte SelectFontSize(
        byte nativeFontSize,
        ushort availableWidth,
        Func<byte, ushort> measureWidestLine)
    {
        ArgumentNullException.ThrowIfNull(measureWidestLine);

        var minimumFontSize = GetMinimumFontSize(nativeFontSize);
        for (var fontSize = (int)nativeFontSize; fontSize >= minimumFontSize; fontSize--)
        {
            if (measureWidestLine((byte)fontSize) <= availableWidth)
                return (byte)fontSize;
        }

        // Keep both complete explicit lines at this floor. Native rendering may still overflow.
        return minimumFontSize;
    }

    public static byte GetMinimumFontSize(byte nativeFontSize)
    {
        if (nativeFontSize == 0)
            return 0;

        var scaledMinimum = nativeFontSize * 2 / 3;
        return (byte)Math.Min(
            nativeFontSize,
            Math.Max(AbsoluteMinimumFontSize, scaledMinimum));
    }
}
