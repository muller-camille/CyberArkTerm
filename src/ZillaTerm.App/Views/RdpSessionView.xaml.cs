using System.Windows;
using System.Windows.Controls;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services.Rdp;

namespace ZillaTerm.App.Views;

/// <summary>
/// Contenu d'un onglet Bureau à distance : barre d'état, contrôle Bureau à distance, et message quand la session
/// n'est pas affichée (connexion en cours, fin de session, erreur).
/// </summary>
public partial class RdpSessionView : UserControl
{
    internal RdpSessionView(RdpSession session)
    {
        InitializeComponent();
        Session = session;
        Body.Children.Insert(0, session.Host);
        session.StateChanged += Update;
        Update();
    }

    internal RdpSession Session { get; }

    private void Update()
    {
        var session = Session;
        Overlay.Visibility = session.ShowsDesktop ? Visibility.Collapsed : Visibility.Visible;
        FullScreenButton.IsEnabled = session.IsConnected;
        DisconnectButton.IsEnabled = session.HasControl;
        var server = session.Server.Length > 0 ? session.Server : session.Label;
        switch (session.State)
        {
            case RdpSessionState.Connecting:
                StatusLine.Text = Text.Format(Strings.RdpConnecting, server);
                OverlayText.Text = Text.Format(Strings.RdpConnecting, session.Label);
                OverlayDetail.Text = "";
                ReconnectButton.Visibility = Visibility.Collapsed;
                break;
            case RdpSessionState.Connected:
                StatusLine.Text = Text.Format(Strings.RdpConnected, session.Label, server);
                break;
            default:
                var failed = session.State == RdpSessionState.Failed;
                StatusLine.Text = failed ? Strings.ConnectionImpossible : Strings.SessionEnded;
                OverlayText.Text = StatusLine.Text;
                OverlayDetail.Text = session.Error ?? "";
                ReconnectButton.Visibility = Visibility.Visible;
                break;
        }

        if (session.IsNotResponding)
        {
            // Barre de l'onglet (WPF) : la fenêtre du contrôle reste à sa place, rien ne s'affiche par-dessus.
            StatusLine.Text = Text.Format(Strings.RdpNotResponding, session.Label);
        }

        if (session.IsNotResponding)
        {
            StatusLine.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
            StatusLine.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            StatusLine.ClearValue(TextBlock.ForegroundProperty);
            StatusLine.ClearValue(TextBlock.FontWeightProperty);
        }
    }

    private void OnFullScreen(object sender, RoutedEventArgs e) => Session.EnterFullScreen();

    private void OnDisconnect(object sender, RoutedEventArgs e) => Session.Disconnect();

    private async void OnReconnect(object sender, RoutedEventArgs e) => await Session.ConnectAsync();
}
