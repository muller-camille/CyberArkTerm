using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services;
using ZillaTerm.Core;

namespace ZillaTerm.App.Views;

/// <summary>
/// Disposition mémorisée : position, taille et état de la fenêtre, largeur du panneau de gauche, panneau replié
/// (Ctrl+B ou double-clic sur le séparateur, pour laisser toute la place à la session). Replié, le panneau garde sa
/// bande d'onglets verticaux : un clic sur l'un d'eux le rouvre.
/// </summary>
public partial class MainWindow
{
    private const double MinSideWidth = 260;
    private bool _sideCollapsed;
    // Largeur du panneau déplié, gardée pendant qu'il est replié.
    private GridLength _sideWidth = new(400);

    /// <summary>Remet la fenêtre et le panneau comme à la dernière fermeture (avant l'affichage de la fenêtre).</summary>
    private void RestoreLayout()
    {
        var screen = (SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_settings.MainWindowPlacement?.FitIn(screen, MinWidth, MinHeight) is { } placement)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
            Width = placement.Width;
            Height = placement.Height;
            if (placement.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        if (_settings.SidePanelWidth >= MinSideWidth)
        {
            SideColumn.Width = new GridLength(_settings.SidePanelWidth);
        }

        // Replié : appliqué une fois la bande d'onglets mesurée.
        if (_settings.SidePanelCollapsed)
        {
            Loaded += (_, _) => SetSidePanelCollapsed(true, save: false);
        }

        SideTabs.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_sideCollapsed && ItemUnder<TabItem>(e.OriginalSource) is not null)
            {
                SetSidePanelCollapsed(false);
            }
        };
    }

    /// <summary>Retient la disposition de la fenêtre (à la fermeture).</summary>
    private void RememberLayout()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.MainWindowPlacement = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                WindowState == WindowState.Maximized);
        }
    }

    private bool SidePanelCollapsed => _sideCollapsed;

    /// <summary>Replie le panneau de gauche (toute la largeur pour la session) ou le déplie à sa largeur précédente.</summary>
    private void SetSidePanelCollapsed(bool collapsed, bool save = true)
    {
        if (collapsed && !_sideCollapsed)
        {
            _sideWidth = SideColumn.Width;
        }

        _sideCollapsed = collapsed;
        // Replié : seul le contenu disparaît, la bande d'onglets verticaux reste (la colonne prend sa largeur).
        if (SideTabs.Template?.FindName("SidePanelContent", SideTabs) is FrameworkElement content)
        {
            content.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        }

        if (collapsed)
        {
            SideColumn.MinWidth = 0;
            SideColumn.Width = GridLength.Auto;
            SplitterColumn.Width = new GridLength(0);
            SideSplitter.Visibility = Visibility.Collapsed;
            if (SideTabs.IsKeyboardFocusWithin)
            {
                // Le curseur ne reste pas dans un panneau invisible.
                FocusCurrentSession();
            }
        }
        else
        {
            SideColumn.Width = _sideWidth;
            SideColumn.MinWidth = MinSideWidth;
            SplitterColumn.Width = new GridLength(5);
            SideSplitter.Visibility = Visibility.Visible;
        }

        if (save && _settings.SidePanelCollapsed != collapsed)
        {
            _settings.SidePanelCollapsed = collapsed;
            SaveSettings();
        }

        UpdateFilesBadge();
    }

    /// <summary>
    /// Pastille de l'onglet « Fichiers » (et barre des tâches) : nombre de transferts en cours ou en attente, sinon « ! »
    /// pour un transfert en échec ou différent que l'on n'a pas encore vu (l'onglet Fichiers affiché le marque vu).
    /// </summary>
    private void UpdateFilesBadge()
    {
        if (FilesTab.IsSelected && !SidePanelCollapsed)
        {
            FilesPanel.MarkTransfersSeen();
        }

        int active = FilesPanel.ActiveTransfers(null);
        int problems = FilesPanel.UnseenProblems;
        string? tip = null;
        if (active > 0)
        {
            FilesBadgeText.Text = active.ToString(System.Globalization.CultureInfo.CurrentCulture);
            FilesBadge.SetResourceReference(Border.BackgroundProperty, "SideStripMarkerBrush");
            FilesBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "SideStripBrush");
            tip = Text.Format(Strings.FilesBadgeActive, active);
        }
        else if (problems > 0)
        {
            FilesBadgeText.Text = "!";
            FilesBadge.SetResourceReference(Border.BackgroundProperty, "StateFailedBrush");
            FilesBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "DangerForeground");
            tip = Text.Format(Strings.FilesBadgeProblem, problems);
        }

        FilesBadge.Visibility = tip is null ? Visibility.Collapsed : Visibility.Visible;
        FilesTab.ToolTip = tip is null ? Strings.TabFilesTip : Strings.TabFilesTip + "\n" + tip;
        System.Windows.Automation.AutomationProperties.SetHelpText(FilesTab, tip ?? "");
        TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
        TaskbarItemInfo.ProgressState = active > 0 ? System.Windows.Shell.TaskbarItemProgressState.Indeterminate
            : problems > 0 ? System.Windows.Shell.TaskbarItemProgressState.Error
            : System.Windows.Shell.TaskbarItemProgressState.None;
        TaskbarItemInfo.ProgressValue = problems > 0 && active == 0 ? 1 : 0;
    }

    /// <summary>
    /// Onglet « Fichiers » : seulement quand une session SSH ou de fichiers (via le PSMP, ou en accès d'urgence) est
    /// ouverte ; sans elle il n'a rien à montrer (l'historique des transferts reste dans la barre d'outils). S'il était
    /// affiché, « Mes serveurs » prend sa place.
    /// </summary>
    private void UpdateFilesTabVisibility()
    {
        bool any = MainTabs.Items.OfType<TabItem>().Any(t => t.Tag is RemoteSession);
        FilesTab.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (!any && FilesTab.IsSelected)
        {
            SideTabs.SelectedItem = CurrentTab;
        }
    }

    private void ToggleSidePanel() => SetSidePanelCollapsed(!SidePanelCollapsed);

    /// <summary>Onglet de gauche choisi par l'application ; <paramref name="expand"/> faux : un panneau replié le reste.</summary>
    private bool _quietSideSelection;

    private void SelectSideTab(TabItem tab, bool expand)
    {
        _quietSideSelection = !expand;
        try
        {
            SideTabs.SelectedItem = tab;
        }
        finally
        {
            _quietSideSelection = false;
        }
    }

    private void OnSideSplitterDragged(object sender, DragCompletedEventArgs e)
    {
        _settings.SidePanelWidth = Math.Round(SideColumn.ActualWidth);
        SaveSettings();
    }

    private void OnSideSplitterDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ToggleSidePanel();
        e.Handled = true;
    }

    /// <summary>Curseur dans la session de l'onglet affiché (terminal, vue parallèle), sinon sur l'onglet.</summary>
    private void FocusCurrentSession()
    {
        switch (MainTabs.SelectedItem)
        {
            case TabItem { Content: SshSessionView view }:
                view.FocusTerminal();
                break;
            case TabItem { Content: ParallelView parallel }:
                parallel.FocusActive();
                break;
            case TabItem tab when tab == HomeTab && !IsOffline:
                QuickBox.Focus();
                break;
            case TabItem tab:
                tab.Focus();
                break;
        }
    }
}
