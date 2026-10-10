using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZillaTerm.Core;

namespace ZillaTerm.App;

/// <summary>Étiquette d'un serveur à l'écran : sa couleur, le texte lisible dessus, et la pastille « PROD ».</summary>
internal static class ServerTagView
{
    /// <summary>
    /// Étiquette d'un serveur parmi celles des réglages ; un nom inconnu (liste partagée, étiquette supprimée) garde son
    /// nom, en gris ; aucune : null.
    /// </summary>
    public static ServerTag? Resolve(IEnumerable<ServerTag> tags, string? name) =>
        ServerTagRules.NormalizeName(name) is { } normalized
            ? ServerTagRules.Find(tags, normalized) ?? new ServerTag(normalized, ServerTagRules.NeutralColor)
            : null;

    public static SolidColorBrush Background(ServerTag tag) => Frozen(Color(tag.Color));

    /// <summary>Noir sur une couleur claire, blanc sur une couleur foncée.</summary>
    public static SolidColorBrush Foreground(ServerTag tag) =>
        ServerTagRules.NeedsDarkText(tag.Color) ? Brushes.Black : Brushes.White;

    /// <summary>Pastille « PROD » : le nom sur la couleur de l'étiquette.</summary>
    public static Border Chip(ServerTag tag, double fontSize = 10) => new()
    {
        Background = Background(tag),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(5, 0, 5, 1),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = tag.Name,
            Foreground = Foreground(tag),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
        },
    };

    private static Color Color(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(ServerTagRules.IsValidColor(hex) ? hex.Trim() : ServerTagRules.NeutralColor);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
