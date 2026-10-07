using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ZillaTerm.App.Localization;

namespace ZillaTerm.App.Views;

/// <summary>
/// Bande des onglets de la zone principale : quand les onglets ne tiennent plus, elle défile (molette, onglet choisi
/// ramené à l'écran) et le bouton « ⌄ » liste tous les onglets avec l'état de leur session.
/// </summary>
public partial class MainWindow
{
    private ScrollViewer? _tabScroll;
    private Button? _tabListButton;

    /// <summary>Branche la bande d'onglets une fois le modèle du TabControl appliqué.</summary>
    private void SetUpTabStrip()
    {
        MainTabs.ApplyTemplate();
        _tabScroll = MainTabs.Template.FindName("TabScroll", MainTabs) as ScrollViewer;
        _tabListButton = MainTabs.Template.FindName("TabListButton", MainTabs) as Button;
        if (_tabScroll is null || _tabListButton is null)
        {
            return;
        }

        _tabListButton.ToolTip = Strings.TabListTip;
        _tabListButton.Click += (_, _) => ShowTabList();
        _tabScroll.ScrollChanged += (_, _) =>
            _tabListButton.Visibility = _tabScroll.ScrollableWidth > 0.5 ? Visibility.Visible : Visibility.Collapsed;
        _tabScroll.PreviewMouseWheel += (_, e) =>
        {
            if (_tabScroll.ScrollableWidth > 0)
            {
                _tabScroll.ScrollToHorizontalOffset(_tabScroll.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        };
    }

    /// <summary>L'onglet choisi reste visible dans la bande, même au clavier (Ctrl+Tab) ou depuis la liste.</summary>
    private void BringTabIntoView(TabItem? tab)
    {
        if (tab is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => tab.BringIntoView());
        }
    }

    /// <summary>Liste de tous les onglets (nom et état de la session) ; choisir une entrée affiche l'onglet.</summary>
    private void ShowTabList()
    {
        var menu = new ContextMenu { PlacementTarget = _tabListButton, Placement = PlacementMode.Bottom };
        foreach (var tab in MainTabs.Items.OfType<TabItem>().Where(t => t.Visibility == Visibility.Visible))
        {
            var text = tab.Header switch
            {
                SessionTabHeader session => $"{session.Label} · {SessionTabHeader.StateText(session.State)}",
                Panel panel => panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? "",
                TextBlock block => block.Text,
                _ => tab.Header?.ToString() ?? "",
            };
            // Le nom ne passe pas par les touches d'accès (« _ » des noms de comptes).
            var item = new MenuItem { Header = new TextBlock { Text = text }, IsCheckable = false, IsChecked = tab == MainTabs.SelectedItem };
            item.Click += (_, _) => MainTabs.SelectedItem = tab;
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
