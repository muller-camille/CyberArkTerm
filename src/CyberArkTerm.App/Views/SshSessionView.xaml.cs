using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.App.Terminal;
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
        Terminal.SearchRequested += ShowSearch;
        Terminal.SaveRequested += SaveContent;
        Terminal.ScrollbackCleared += () =>
        {
            if (SearchBar.Visibility == Visibility.Visible)
            {
                RunSearch(keepPosition: false);
            }
        };
        session.ScreenUpdated += OnScreenUpdatedForSearch;
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            RunSearch(keepPosition: true);
        };
        // Fond autour du terminal : celui de la palette.
        Loaded += (_, _) =>
        {
            ApplyBackground();
            TerminalAppearance.Changed += ApplyBackground;
        };
        Unloaded += (_, _) => TerminalAppearance.Changed -= ApplyBackground;
    }

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private IReadOnlyList<TerminalMatch> _matches = [];
    private int _current = -1;

    private void ApplyBackground()
    {
        var rgb = TerminalAppearance.Theme.Background;
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        RootGrid.Background = brush;
    }

    // ===================== Recherche (Ctrl+Maj+F) =====================

    public void ShowSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
        RunSearch(keepPosition: false);
    }

    /// <summary>Occurrences de la recherche (tests).</summary>
    internal IReadOnlyList<TerminalMatch> Matches => _matches;

    internal int CurrentMatch => _current;

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RunSearch(keepPosition: false);

    /// <summary>Cherche dans tout le terminal ; la plus récente occurrence d'abord.</summary>
    private void RunSearch(bool keepPosition)
    {
        int fromEnd = _matches.Count - 1 - _current;
        _matches = TerminalSearch.Find(Session.Emulator, SearchBox.Text);
        _current = _matches.Count == 0 ? -1 : keepPosition ? Math.Max(0, _matches.Count - 1 - fromEnd) : _matches.Count - 1;
        Terminal.ShowMatches(_matches, _current, scroll: !keepPosition);
        UpdateSearchCount();
    }

    /// <summary>Nouvelles lignes pendant la recherche : les occurrences sont recalculées un peu après.</summary>
    private void OnScreenUpdatedForSearch()
    {
        if (SearchBar.Visibility == Visibility.Visible && SearchBox.Text.Length > 0)
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    /// <param name="direction">-1 : plus ancien, 1 : plus récent.</param>
    internal void MoveSearch(int direction)
    {
        if (_matches.Count == 0)
        {
            return;
        }

        _current = ((_current + direction) % _matches.Count + _matches.Count) % _matches.Count;
        Terminal.ShowMatches(_matches, _current);
        UpdateSearchCount();
    }

    private void UpdateSearchCount() =>
        SearchCount.Text = SearchBox.Text.Length == 0 ? ""
            : _matches.Count == 0 ? Strings.TailSearchNone
            : Text.Format(Strings.TailSearchCount, _current + 1, _matches.Count);

    private void OnSearchOlder(object sender, RoutedEventArgs e) => MoveSearch(-1);

    private void OnSearchNewer(object sender, RoutedEventArgs e) => MoveSearch(1);

    private void OnSearchClose(object sender, RoutedEventArgs e) => CloseSearch();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                MoveSearch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : -1);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseSearch();
                e.Handled = true;
                break;
        }
    }

    private void CloseSearch()
    {
        _searchTimer.Stop();
        SearchBar.Visibility = Visibility.Collapsed;
        _matches = [];
        _current = -1;
        Terminal.ClearMatches();
        FocusTerminal();
    }

    // ===================== Enregistrement (Ctrl+Maj+S) =====================

    /// <summary>Contenu du terminal (historique compris) dans un fichier texte de ce poste, à la demande.</summary>
    public void SaveContent()
    {
        var name = string.Concat(Session.Label.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.TerminalSaveTitle,
            Filter = Strings.TerminalSaveFilter,
            FileName = $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
            DefaultExt = ".txt",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            FocusTerminal();
            return;
        }

        try
        {
            System.IO.File.WriteAllText(dialog.FileName, TerminalSearch.AllText(Session.Emulator) + Environment.NewLine, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, Strings.TerminalSaveTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        FocusTerminal();
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

    /// <summary>Actions de la session (reconnecter, dupliquer…) à la fin du menu du clic droit dans le terminal.</summary>
    public Action<ItemCollection>? SessionMenu
    {
        get => Terminal.ExtraMenuItems;
        set => Terminal.ExtraMenuItems = value;
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
            // Le terminal a pu changer de place pendant la connexion (vue parallèle, fenêtre séparée) : bonne taille au serveur.
            Terminal.Refit();
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
