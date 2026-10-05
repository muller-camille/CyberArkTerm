using System.Windows;
using CyberArkTerm.App.Localization;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Fenêtre séparée d'un onglet SSH détaché (sur un autre écran, par exemple) : le terminal y est déplacé, la session
/// continue. Fermer la fenêtre ramène le terminal dans son onglet, sans fermer la session ; seule la fermeture de la
/// session (onglet, application) la ferme pour de bon.
/// </summary>
public sealed class DetachedSessionWindow : Window
{
    public DetachedSessionWindow(SshSessionView view, string label)
    {
        View = view;
        Title = Text.Format(Strings.DetachedTitle, label);
        Icon = Application.Current?.MainWindow?.Icon;
        Background = System.Windows.Media.Brushes.White;
        ShowInTaskbar = true;
        Content = view;
        Activated += (_, _) => view.FocusTerminal();
    }

    public SshSessionView View { get; }

    /// <summary>La session se ferme : la fenêtre se ferme sans ramener le terminal dans l'onglet.</summary>
    public bool SessionClosed { get; set; }

    /// <summary>Retire le terminal de la fenêtre (pour le remettre dans son onglet).</summary>
    public SshSessionView TakeView()
    {
        Content = null;
        return View;
    }
}
