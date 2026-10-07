using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ZillaTerm.App.Views;

/// <summary>
/// Ligne d'erreur d'un dialogue, la même partout : couleur d'erreur du thème (contraste élevé compris), texte replié,
/// et annoncée par les lecteurs d'écran dès qu'elle apparaît ou change.
/// </summary>
public class ErrorLine : TextBlock
{
    static ErrorLine()
    {
        TextProperty.OverrideMetadata(typeof(ErrorLine), new FrameworkPropertyMetadata(string.Empty, OnShownChanged));
        VisibilityProperty.OverrideMetadata(typeof(ErrorLine), new PropertyMetadata(Visibility.Visible, OnShownChanged));
    }

    public ErrorLine()
    {
        SetResourceReference(ForegroundProperty, "ErrorBrush");
        TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Assertive);
    }

    private static void OnShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ErrorLine { Visibility: Visibility.Visible } line && !string.IsNullOrEmpty(line.Text)
            && UIElementAutomationPeer.CreatePeerForElement(line) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
