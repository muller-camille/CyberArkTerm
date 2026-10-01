using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CyberArkTerm.App.Services;

namespace CyberArkTerm.App.Views;

/// <summary>Contenu d'un onglet de session SSH : le terminal, et un voile pendant la connexion ou après la fermeture.</summary>
public partial class SshSessionView : UserControl
{
    public SshSessionView(SshSession session, string target)
    {
        InitializeComponent();
        Session = session;
        Target = target;
        Terminal.Emulator = session.Emulator;
        Terminal.Input += session.Send;
        Terminal.TerminalResized += session.Resize;
        session.ScreenUpdated += Terminal.Refresh;
        session.StateChanged += UpdateOverlay;
        UpdateOverlay();
    }

    public SshSession Session { get; }

    /// <summary>« coffre@compte@cible via psmp », affiché pendant la connexion.</summary>
    public string Target { get; }

    public (int Columns, int Rows) TerminalSize => Terminal.ActualWidth > 0 ? Terminal.SizeInCells : (100, 30);

    public void FocusTerminal() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Terminal.Focus());

    public async Task ConnectAsync()
    {
        // Laisse la mise en page se faire pour connaître la taille du terminal.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var (columns, rows) = TerminalSize;
        try
        {
            await Session.ConnectAsync(columns, rows);
            FocusTerminal();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // L'état « Failed » et son message sont affichés par UpdateOverlay.
        }
    }

    private void UpdateOverlay()
    {
        switch (Session.State)
        {
            case SshSessionState.Connected:
                Overlay.Visibility = Visibility.Collapsed;
                break;
            case SshSessionState.Connecting:
                Overlay.Visibility = Visibility.Visible;
                OverlayText.Text = "Connexion via le PSMP…";
                OverlayDetail.Text = Target;
                ReconnectButton.Visibility = Visibility.Collapsed;
                break;
            default:
                Overlay.Visibility = Visibility.Visible;
                OverlayText.Text = Session.State == SshSessionState.Failed ? "Connexion impossible" : "Session terminée";
                OverlayDetail.Text = Session.Error ?? "";
                ReconnectButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void OnReconnect(object sender, RoutedEventArgs e) => await ConnectAsync();
}
