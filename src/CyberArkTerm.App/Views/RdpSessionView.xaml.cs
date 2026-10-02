using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services.Rdp;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Contenu d'un onglet Bureau à distance : barre d'état, contrôle Bureau à distance, et message quand la session
/// n'est pas affichée (demande de connexion en cours, application distante ouverte à part, fin de session, erreur).
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
        FullScreenButton.Visibility = session.IsRemoteApp ? Visibility.Collapsed : Visibility.Visible;
        FullScreenButton.IsEnabled = session.IsConnected;
        DisconnectButton.IsEnabled = session.HasControl;
        var server = session.Server.Length > 0 ? session.Server : session.Label;
        RemoteAppWindowsButton.Visibility = Visibility.Collapsed;
        switch (session.State)
        {
            case RdpSessionState.Connecting:
                StatusLine.Text = Text.Format(Strings.RdpConnecting, server);
                OverlayText.Text = Text.Format(Strings.RdpConnecting, session.Label);
                OverlayDetail.Text = session.RemoteAppFallback ? Strings.RdpRemoteAppFallback : "";
                ReconnectButton.Visibility = Visibility.Collapsed;
                break;
            case RdpSessionState.Connected:
                StatusLine.Text = Text.Format(Strings.RdpConnected, session.Label, server);
                if (session.IsRemoteApp)
                {
                    // Les fenêtres de l'application distante sont sur le bureau de ce poste : l'onglet dit où elles sont.
                    OverlayText.Text = Text.Format(Strings.RdpRemoteAppOpened, session.RemoteAppName);
                    // Après un refus du bureau, la raison reste affichée : la barre d'état est vite remplacée.
                    OverlayDetail.Text = session.RemoteAppFallback
                        ? Text.Format(Strings.PsmDesktopRefused, session.Label) + "\n\n" + Strings.RdpRemoteAppHint
                        : Strings.RdpRemoteAppHint;
                    ReconnectButton.Visibility = Visibility.Collapsed;
                }

                break;
            default:
                var failed = session.State == RdpSessionState.Failed;
                StatusLine.Text = failed ? Strings.ConnectionImpossible : Strings.SessionEnded;
                OverlayText.Text = StatusLine.Text;
                OverlayDetail.Text = session.Error ?? "";
                if (session.RemoteAppFallback)
                {
                    // Bureau refusé : la session est déjà rouverte en fenêtres séparées, rien à proposer.
                    OverlayDetail.Text = Strings.RdpRemoteAppFallback;
                    ReconnectButton.Visibility = Visibility.Collapsed;
                    break;
                }

                if (session.DesktopFromRemoteApp && (session.ConnectedAt is not { } opened || DateTime.UtcNow - opened < TimeSpan.FromMinutes(1)))
                {
                    // Fin rapide d'une application distante ouverte en bureau : le serveur refuse peut-être ce mode.
                    OverlayDetail.Text = (OverlayDetail.Text + "\n\n" + Strings.RdpDesktopFromRemoteAppHint).Trim();
                    RemoteAppWindowsButton.Visibility = Visibility.Visible;
                }

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
            StatusLine.Foreground = Brushes.Firebrick;
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

    private async void OnOpenRemoteAppWindows(object sender, RoutedEventArgs e) => await Session.OpenRemoteAppWindowsAsync();
}
