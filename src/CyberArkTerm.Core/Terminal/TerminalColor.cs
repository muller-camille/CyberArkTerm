namespace CyberArkTerm.Core.Terminal;

/// <summary>
/// Couleurs du terminal : <see cref="Default"/>, index 0-255 (palette xterm) ou couleur vraie (<see cref="Rgb"/>).
/// </summary>
public static class TerminalColor
{
    public const int Default = -1;

    private const int RgbFlag = 0x1000000;

    // Palette 16 couleurs « Campbell » (celle de Windows Terminal).
    private static readonly uint[] Base16 =
    [
        0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
        0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2,
    ];

    public const uint DefaultForeground = 0xCCCCCC;
    public const uint DefaultBackground = 0x0C0C0C;

    public static int Rgb(byte r, byte g, byte b) => RgbFlag | (r << 16) | (g << 8) | b;

    /// <summary>Valeur 0xRRGGBB à afficher. Le gras éclaircit les 8 couleurs de base, comme xterm.</summary>
    public static uint ToRgb(int color, bool foreground, bool bold = false)
    {
        if (color == Default)
        {
            return foreground ? (bold ? 0xF2F2F2u : DefaultForeground) : DefaultBackground;
        }

        if ((color & RgbFlag) != 0)
        {
            return (uint)(color & 0xFFFFFF);
        }

        if (color < 16)
        {
            return Base16[foreground && bold && color < 8 ? color + 8 : color];
        }

        if (color < 232)
        {
            int c = color - 16;
            return (Level(c / 36) << 16) | (Level(c / 6 % 6) << 8) | Level(c % 6);
        }

        uint gray = (uint)(8 + (color - 232) * 10);
        return (gray << 16) | (gray << 8) | gray;
    }

    private static uint Level(int v) => v == 0 ? 0u : (uint)(55 + v * 40);
}
