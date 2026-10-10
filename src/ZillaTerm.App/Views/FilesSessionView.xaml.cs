using System.Windows;
using System.Windows.Controls;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services;
using ZillaTerm.Core.Ftp;

namespace ZillaTerm.App.Views;

/// <summary>
/// Onglet d'une session de fichiers seuls (SFTP, FTP, FTPS des entrées KeePass) : état de la connexion et chiffrement ;
/// les fichiers eux-mêmes sont dans l'onglet Fichiers à gauche.
/// </summary>
public partial class FilesSessionView : UserControl
{
    /// <param name="target">« utilisateur@serveur », affiché sous le titre.</param>
    public FilesSessionView(FilesSession session, string target)
    {
        InitializeComponent();
        Session = session;
        TitleText.Text = Text.Format(Strings.FilesSessionTitle, session.Label);
        DetailText.Text = Text.Format(Strings.FilesSessionDetail, session.Protocol, target);
        session.StateChanged += UpdateState;
        UpdateState();
    }

    public FilesSession Session { get; }

    /// <summary>« Afficher les fichiers » : la fenêtre ouvre l'onglet Fichiers, élargi au besoin pour toutes ses colonnes.</summary>
    public event Action? ShowFilesRequested;

    /// <summary>
    /// Avant une reconnexion : faux pour y renoncer (transferts en cours que l'utilisateur ne veut pas annuler). La fenêtre
    /// y annule les transferts de la session, qui utilisent la connexion remplacée.
    /// </summary>
    public Func<Task<bool>>? BeforeReconnect { get; set; }

    /// <summary>Connexion (ou reconnexion) ; l'échec est affiché dans l'onglet.</summary>
    public async Task ConnectAsync()
    {
        if (BeforeReconnect is { } before && !await before())
        {
            return;
        }

        try
        {
            await Session.ConnectAsync();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Déjà décrit par l'état de la session.
        }
    }

    private void UpdateState()
    {
        var state = Session.State;
        ReconnectButton.Visibility = state is RemoteSessionState.Failed or RemoteSessionState.Closed ? Visibility.Visible : Visibility.Collapsed;
        ShowFilesButton.IsEnabled = state == RemoteSessionState.Connected;
        StateProgress.Visibility = state == RemoteSessionState.Connecting ? Visibility.Visible : Visibility.Collapsed;
        StateText.SetResourceReference(TextBlock.ForegroundProperty, state is RemoteSessionState.Failed or RemoteSessionState.Closed ? "ErrorBrush" : "MutedBrush");
        var browser = Session.OpenedBrowser;
        CleartextBanner.Visibility = state == RemoteSessionState.Connected && browser is FtpFileBrowser { IsEncrypted: false }
            ? Visibility.Visible
            : Visibility.Collapsed;
        StateText.Text = state switch
        {
            RemoteSessionState.Connecting => Strings.FilesSessionConnecting,
            RemoteSessionState.Connected => browser is FtpFileBrowser { IsEncrypted: false }
                ? Strings.FilesSessionConnectedClear
                : Strings.FilesSessionConnected,
            _ => Text.Format(Strings.FilesSessionFailed, Session.Error ?? Strings.SessionClosedByServer),
        };
    }

    private void OnShowFiles(object sender, RoutedEventArgs e) => ShowFilesRequested?.Invoke();

    private void OnReconnect(object sender, RoutedEventArgs e) => _ = ConnectAsync();
}
