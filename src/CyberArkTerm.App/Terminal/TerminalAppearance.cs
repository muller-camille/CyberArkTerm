using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.App.Terminal;

/// <summary>
/// Palette, taille de police et clic droit des terminaux (Paramètres) ; un changement s'applique aux terminaux ouverts.
/// </summary>
public static class TerminalAppearance
{
    public const double MinFontSize = 8;
    public const double MaxFontSize = 32;
    public const double DefaultFontSize = 14;

    public static TerminalTheme Theme { get; private set; } = TerminalTheme.Campbell;

    /// <summary>Taille de police par défaut (Ctrl+molette la change pour un terminal, Ctrl+0 y revient).</summary>
    public static double FontSize { get; private set; } = DefaultFontSize;

    /// <summary>Le clic droit colle le presse-papiers au lieu d'ouvrir le menu (Maj+clic droit l'ouvre alors).</summary>
    public static bool RightClickPastes { get; private set; }

    public static event Action? Changed;

    public static void Apply(string? themeId, double fontSize, bool rightClickPastes)
    {
        Theme = TerminalTheme.Find(themeId);
        FontSize = Math.Clamp(fontSize, MinFontSize, MaxFontSize);
        RightClickPastes = rightClickPastes;
        Changed?.Invoke();
    }
}
