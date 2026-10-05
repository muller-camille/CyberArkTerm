using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.App.Views;

/// <summary>Contenu d'un onglet de session SSH : le terminal, et un voile pendant la connexion ou après la fermeture.</summary>
public partial class SshSessionView : UserControl
{
    /// <param name="connectingText">Message pendant la connexion (via le PSMP, ou directe pour un accès d'urgence).</param>
    public SshSessionView(SshSession session, string target, string connectingText)
    {
        InitializeComponent();
        Session = session;
        Target = target;
        _connectingText = connectingText;
        Terminal.Emulator = session.Emulator;
        Terminal.Input += OnInput;
        Terminal.TerminalResized += session.Resize;
        session.ScreenUpdated += Terminal.Refresh;
        session.StateChanged += UpdateOverlay;
        UpdateOverlay();
    }

    private readonly string _connectingText;

    public SshSession Session { get; }

    /// <summary>« coffre@compte@cible via psmp », affiché pendant la connexion.</summary>
    public string Target { get; }

    /// <summary>
    /// Destinataires de la saisie (vue parallèle avec saisie simultanée) ; sans aiguillage, elle va à cette session.
    /// </summary>
    public Action<SshSessionView, TerminalInput>? InputRouter { get; set; }

    /// <summary>Saisie reçue, encodée selon l'état du terminal de cette session.</summary>
    public void Send(TerminalInput input) => Session.SendInput(input.Encode(Session.Emulator));

    private void OnInput(TerminalInput input)
    {
        if (InputRouter is { } router && input.IsTyping)
        {
            router(this, input);
        }
        else
        {
            Send(input);
        }
    }

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
                OverlayText.Text = _connectingText;
                OverlayDetail.Text = Target;
                ReconnectButton.Visibility = Visibility.Collapsed;
                break;
            default:
                Overlay.Visibility = Visibility.Visible;
                OverlayText.Text = Session.State == SshSessionState.Failed ? Strings.ConnectionImpossible : Strings.SessionEnded;
                OverlayDetail.Text = Session.Error ?? "";
                ReconnectButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void OnReconnect(object sender, RoutedEventArgs e) => await ConnectAsync();
}
