using System.Windows;
using System.Windows.Media;

namespace CyberArkTerm.App;

/// <summary>
/// Contraste élevé de Windows : les couleurs de la palette (Theme.xaml) sont remplacées par les couleurs système, au
/// démarrage et chaque fois que l'utilisateur active, quitte ou change de thème contrasté. L'interface référence ces
/// couleurs en DynamicResource : les fenêtres déjà ouvertes suivent sans redémarrage.
/// </summary>
internal static class Palette
{
    private enum Role
    {
        Background,
        Text,
        Border,
        Highlight,
        HighlightText,
        Button,
        ButtonText,
        Line,
    }

    private static readonly (object Key, Role Role)[] Map =
    [
        // Surfaces.
        ("SidePanelBrush", Role.Background), ("ContentBrush", Role.Background), ("CodeBrush", Role.Background),
        ("AlternateRowBrush", Role.Background), ("MainTabBrush", Role.Background), ("MainTabHoverBrush", Role.Background),
        ("TabStripBrush", Role.Background), ("ToolbarBrush", Role.Background), ("SideStripBrush", Role.Background),
        ("StatusBrush", Role.Background), ("ErrorBackgroundBrush", Role.Background), ("WarningBackgroundBrush", Role.Background),
        ("AccentLightBrush", Role.Background), ("ToolHoverBrush", Role.Background), ("ToolPressedBrush", Role.Background),
        ("ToolFocusBrush", Role.Background), ("SideStripHoverBrush", Role.Background), ("DiffEmptyBrush", Role.Background),
        ("DropHintBrush", Role.Background),

        // Textes : la couleur ne porte plus de sens, le texte le dit (« Erreur », « ⚠ »…).
        ("MutedBrush", Role.Text), ("SidePanelForeground", Role.Text), ("HeadingBrush", Role.Text), ("PlaceholderBrush", Role.Text),
        ("SideStripForeground", Role.Text), ("StatusForeground", Role.Text), ("StatusMutedForeground", Role.Text),
        ("StatusOkForeground", Role.Text), ("StatusErrorForeground", Role.Text), ("StatusWarningForeground", Role.Text),
        ("ErrorBrush", Role.Text), ("WarningBrush", Role.Text), ("SuccessBrush", Role.Text), ("DiffRemovedForeground", Role.Text),
        ("DiffAddedForeground", Role.Text), ("LineNumberBrush", Role.Text),

        // Bordures et séparateurs.
        ("ToolbarBorderBrush", Role.Border), ("MainTabBorderBrush", Role.Border), ("ErrorBorderBrush", Role.Border),
        ("WarningBorderBrush", Role.Border), ("CodeBorderBrush", Role.Border), ("GridLineBrush", Role.Line),

        // Accent, sélection et focus : couleur de sélection de Windows, avec son texte.
        ("AccentBrush", Role.Highlight), ("SideStripMarkerBrush", Role.Highlight), ("ToolHoverBorderBrush", Role.Highlight),
        ("SideStripFocusBrush", Role.Highlight), ("DangerFocusBrush", Role.Highlight), ("DiffRemovedBrush", Role.Highlight),
        ("DiffAddedBrush", Role.Highlight), ("BroadcastBrush", Role.Highlight),
        (SystemColors.InactiveSelectionHighlightBrushKey, Role.Highlight), (SystemColors.InactiveSelectionHighlightTextBrushKey, Role.HighlightText),
        ("SideStripSelectedForeground", Role.HighlightText), ("DiffTextBrush", Role.HighlightText),
        ("BroadcastForeground", Role.HighlightText),

        // Bouton d'action irréversible : un bouton système (le libellé dit le danger).
        ("DangerBrush", Role.Button), ("DangerHoverBrush", Role.Button), ("DangerPressedBrush", Role.Button),
        ("DangerForeground", Role.ButtonText), ("DangerBorderBrush", Role.ButtonText),
    ];

    private static ResourceDictionary? _applied;

    /// <summary>Applique la palette maintenant, puis suit les changements de contraste et de couleurs système.</summary>
    public static void Follow(Application app)
    {
        Apply(app);
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
            {
                app.Dispatcher.BeginInvoke(() => Apply(app));
            }
        };
        // Passer d'un thème contrasté à un autre ne change que les couleurs système.
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.Accessibility)
            {
                app.Dispatcher.BeginInvoke(() => Apply(app));
            }
        };
    }

    private static void Apply(Application app)
    {
        var merged = app.Resources.MergedDictionaries;
        if (_applied is not null)
        {
            merged.Remove(_applied);
            _applied = null;
        }

        if (!SystemParameters.HighContrast)
        {
            return;
        }

        // Dernier dictionnaire fusionné : il l'emporte sur Theme.xaml.
        _applied = HighContrast();
        merged.Add(_applied);
    }

    /// <summary>Couleurs de la palette en contraste élevé (couleurs système du moment).</summary>
    internal static ResourceDictionary HighContrast()
    {
        var dictionary = new ResourceDictionary();
        foreach (var (key, role) in Map)
        {
            dictionary[key] = Brush(role);
        }

        return dictionary;
    }

    /// <summary>Clés de la palette remplacées en contraste élevé.</summary>
    internal static IEnumerable<string> Keys => Map.Select(m => m.Key).OfType<string>();

    private static Brush Brush(Role role) => role switch
    {
        Role.Background => SystemColors.WindowBrush,
        Role.Text => SystemColors.WindowTextBrush,
        Role.Border => SystemColors.WindowTextBrush,
        Role.Line => SystemColors.GrayTextBrush,
        Role.Highlight => SystemColors.HighlightBrush,
        Role.HighlightText => SystemColors.HighlightTextBrush,
        Role.Button => SystemColors.ControlBrush,
        _ => SystemColors.ControlTextBrush,
    };
}
