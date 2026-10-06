using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.App.Terminal;

/// <summary>Palette et taille de police des terminaux (Paramètres) ; un changement s'applique aux terminaux ouverts.</summary>
public static class TerminalAppearance
{
    public const double MinFontSize = 8;
    public const double MaxFontSize = 32;
    public const double DefaultFontSize = 14;

    public static TerminalTheme Theme { get; private set; } = TerminalTheme.Campbell;

    /// <summary>Taille de police par défaut (Ctrl+molette la change pour un terminal, Ctrl+0 y revient).</summary>
    public static double FontSize { get; private set; } = DefaultFontSize;

    public static event Action? Changed;

    public static void Apply(string? themeId, double fontSize)
    {
        Theme = TerminalTheme.Find(themeId);
        FontSize = Math.Clamp(fontSize, MinFontSize, MaxFontSize);
        Changed?.Invoke();
    }
}
