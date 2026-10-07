using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CyberArkTerm.App.Localization;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Vue parallèle dans une fenêtre séparée (autre écran), comme un onglet SSH détaché : l'onglet « Parallèle » garde sa
/// place, et fermer la fenêtre ramène la vue dans l'onglet sans fermer les sessions.
/// </summary>
public partial class MainWindow
{
    private Window? _parallelWindow;
    private bool _closingParallelWindow;

    /// <param name="screenPoint">Position du curseur (pixels de l'écran) quand l'onglet est glissé hors de la fenêtre.</param>
    private void DetachParallel(Point? screenPoint = null)
    {
        if (_parallel is not { } view || _parallelTab is not { } tab || _parallelWindow is not null || !ReferenceEquals(tab.Content, view))
        {
            return;
        }

        double width = Math.Max(view.ActualWidth, 900) + 16;
        double height = Math.Max(view.ActualHeight, 560) + 39;
        tab.Content = ParallelDetachedPlaceholder();
        var window = new Window
        {
            Title = ParallelWindowTitle(),
            Icon = Icon,
            Width = width,
            Height = height,
            ShowInTaskbar = true,
            Content = view,
        };
        window.SetResourceReference(BackgroundProperty, "ContentBrush");
        if (screenPoint is { } point && PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var position = target.TransformFromDevice.Transform(point);
            window.Left = position.X - 80;
            window.Top = position.Y - 15;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = Left + 40;
            window.Top = Top + 40;
        }

        window.Activated += (_, _) => view.FocusActive();
        window.Closed += (_, _) =>
        {
            _parallelWindow = null;
            window.Content = null;
            if (_closingParallelWindow || _parallelTab is null)
            {
                return;
            }

            // Fenêtre fermée par l'utilisateur : la vue revient dans son onglet, les sessions continuent.
            _parallelTab.Content = view;
            view.IsDetached = false;
            MainTabs.SelectedItem = _parallelTab;
            view.FocusActive();
        };
        _parallelWindow = window;
        view.IsDetached = true;
        window.Show();
        window.Activate();
    }

    /// <summary>« Ramener dans l'onglet » : la fenêtre se ferme, la vue revient.</summary>
    private void ReattachParallel() => _parallelWindow?.Close();

    /// <summary>La vue parallèle se ferme : sa fenêtre aussi, sans ramener la vue dans l'onglet.</summary>
    private void CloseParallelWindow()
    {
        if (_parallelWindow is { } window)
        {
            _closingParallelWindow = true;
            try
            {
                window.Close();
            }
            finally
            {
                _closingParallelWindow = false;
            }
        }
    }

    private string ParallelWindowTitle() =>
        Text.Format(Strings.ParallelWindowTitle, _parallel?.Sessions.Count ?? 0);

    private FrameworkElement ParallelDetachedPlaceholder()
    {
        var show = new Button { Content = Strings.DetachedShow, Padding = new Thickness(10, 3, 10, 3) };
        show.Click += (_, _) =>
        {
            if (_parallelWindow is { } window)
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Activate();
            }
        };
        var back = new Button { Content = Strings.DetachedReattach, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(8, 0, 0, 0) };
        back.Click += (_, _) => ReattachParallel();
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460,
            Children =
            {
                new TextBlock
                {
                    Text = Strings.ParallelDetachedPlaceholder, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14),
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { show, back } },
            },
        };
    }
}
