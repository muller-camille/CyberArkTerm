using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core.Ftp;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Onglet d'une session de fichiers seuls (SFTP, FTP, FTPS des entrées KeePass) : état de la connexion et chiffrement ;
/// les fichiers eux-mêmes sont dans l'onglet Fichiers à gauche.
/// </summary>
public partial class FilesSessionView : UserControl
{
    /// <param name="target">« utilisateur@serveur », affiché sous le titre.</param>
    public FilesSessionView(SshSession session, string target)
    {
        InitializeComponent();
        Session = session;
        TitleText.Text = Text.Format(Strings.FilesSessionTitle, session.Label);
        DetailText.Text = Text.Format(Strings.FilesSessionDetail, session.FilesProtocol, target);
        session.StateChanged += UpdateState;
        UpdateState();
    }

    public SshSession Session { get; }

    /// <summary>« Afficher les fichiers » : la fenêtre ouvre l'onglet Fichiers.</summary>
    public event Action? ShowFilesRequested;

    /// <summary>Connexion (ou reconnexion) ; l'échec est affiché dans l'onglet.</summary>
    public async Task ConnectAsync()
    {
        try
        {
            await Session.ConnectAsync(0, 0);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Déjà décrit par l'état de la session.
        }
    }

    private void UpdateState()
    {
        var state = Session.State;
        ReconnectButton.Visibility = state is SshSessionState.Failed or SshSessionState.Closed ? Visibility.Visible : Visibility.Collapsed;
        ShowFilesButton.IsEnabled = state == SshSessionState.Connected;
        StateText.Foreground = state is SshSessionState.Failed or SshSessionState.Closed ? Brushes.Firebrick : (Brush)FindResource("MutedBrush");
        var browser = Session.OpenedBrowser;
        CleartextBanner.Visibility = state == SshSessionState.Connected && browser is FtpFileBrowser { IsEncrypted: false }
            ? Visibility.Visible
            : Visibility.Collapsed;
        StateText.Text = state switch
        {
            SshSessionState.Connecting => Strings.FilesSessionConnecting,
            SshSessionState.Connected => browser is FtpFileBrowser { IsEncrypted: false }
                ? Strings.FilesSessionConnectedClear
                : Strings.FilesSessionConnected,
            _ => Text.Format(Strings.FilesSessionFailed, Session.Error ?? Strings.SessionClosedByServer),
        };
    }

    private void OnShowFiles(object sender, RoutedEventArgs e) => ShowFilesRequested?.Invoke();

    private void OnReconnect(object sender, RoutedEventArgs e) => _ = ConnectAsync();
}
