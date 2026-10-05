using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Suivi d'un fichier du serveur comme <c>tail -f</c> : la fin du fichier, puis chaque nouvelle ligne, relevées par
/// SFTP chaque seconde (aucune commande sur le serveur). Pause, suivi de la fin, retour à la ligne, filtre ; les
/// 10 000 dernières lignes sont gardées.
/// </summary>
public partial class TailWindow : Window
{
    private const int MaxLines = 10_000;

    // Les lignes en trop sont retirées par paquets : la zone de texte n'est pas reconstruite à chaque ligne.
    private const int TrimBatch = 2_000;

    private readonly FileTail _tail;
    private readonly Func<bool> _connected;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _closing = new();
    private readonly List<string> _lines = [];
    private string _partial = "";
    private bool _polling;
    private bool _paused;
    private bool _stopped;
    private DateTime _nextAttempt;
    private string _lastStatus = "";

    /// <param name="isConnected">La connexion SFTP est encore ouverte (sinon le suivi s'arrête).</param>
    public TailWindow(ITailSource source, string path, string server, Func<bool> isConnected)
    {
        InitializeComponent();
        Title = Text.Format(Strings.TailTitle, path, server);
        _tail = new FileTail(source);
        _connected = isConnected;
        _timer.Tick += async (_, _) => await PollAsync();
        // Branché après le chargement : le filtre vide initial ne doit pas déclencher d'affichage.
        FilterBox.TextChanged += (_, _) => Render();
        Loaded += async (_, _) =>
        {
            await PollAsync();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _closing.Cancel();
        };
    }

    private string Filter => FilterBox.Text.Trim();

    /// <summary>Relève ce qui a été ajouté au fichier et l'affiche.</summary>
    internal async Task PollAsync()
    {
        if (_polling || _paused || _stopped || DateTime.UtcNow < _nextAttempt)
        {
            return;
        }

        if (!IsConnected())
        {
            _stopped = true;
            _timer.Stop();
            ShowStatus(Strings.TailClosed, error: true);
            return;
        }

        _polling = true;
        try
        {
            var update = await _tail.PollAsync(_closing.Token);
            if (update.Restarted)
            {
                AddMarker(Strings.TailRestarted);
            }

            if (update.Skipped > 0)
            {
                AddMarker(Text.Format(Strings.TailSkipped, RemotePath.FormatSize(update.Skipped)));
            }

            Append(update.Text);
            ShowStatus(Text.Format(Strings.TailStatus, RemotePath.FormatSize(update.Size), DateTime.Now.ToString("T")), error: false);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Fenêtre fermée.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Fichier supprimé, droits, coupure brève : nouvel essai un peu plus tard.
            _nextAttempt = DateTime.UtcNow.AddSeconds(5);
            ShowStatus(Text.Format(Strings.TailError, ErrorText.Describe(ex)), error: true);
        }
        finally
        {
            _polling = false;
        }
    }

    /// <summary>Contenu affiché (tests).</summary>
    internal string Shown => LogBox.Text;

    private bool IsConnected()
    {
        try
        {
            return _connected();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private void Append(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var parts = (_partial + text).Split('\n');
        _partial = parts[^1];
        var complete = parts[..^1];
        _lines.AddRange(complete);
        if (_lines.Count > MaxLines + TrimBatch)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
            Render();
            return;
        }

        if (Filter.Length == 0)
        {
            // Le texte affiché est exactement celui du fichier : la suite s'ajoute telle quelle (ligne en cours comprise).
            LogBox.AppendText(text);
        }
        else
        {
            var matches = complete.Where(Matches).ToList();
            if (matches.Count > 0)
            {
                LogBox.AppendText(string.Join("\n", matches) + "\n");
            }
        }

        ScrollIfFollowing();
    }

    /// <summary>Ligne de repère (fichier relu, octets sautés), affichée même avec un filtre.</summary>
    private void AddMarker(string marker)
    {
        if (_partial.Length > 0)
        {
            _lines.Add(_partial);
            _partial = "";
        }

        _lines.Add(marker);
        Render();
    }

    private bool Matches(string line) =>
        line.Contains(Filter, StringComparison.CurrentCultureIgnoreCase) || (line.StartsWith("— ", StringComparison.Ordinal) && line.EndsWith(" —", StringComparison.Ordinal));

    private void Render()
    {
        var filter = Filter;
        var shown = filter.Length == 0 ? _lines : _lines.Where(Matches).ToList();
        var text = string.Join("\n", shown) + (shown.Count > 0 ? "\n" : "");
        LogBox.Text = filter.Length == 0 ? text + _partial : text;
        ScrollIfFollowing();
    }

    private void ScrollIfFollowing()
    {
        if (FollowBox.IsChecked == true)
        {
            LogBox.ScrollToEnd();
        }
    }

    private void ShowStatus(string text, bool error)
    {
        _lastStatus = text;
        StatusText.Text = _paused ? Text.Format(Strings.TailPaused, text) : text;
        StatusText.Foreground = error ? System.Windows.Media.Brushes.Firebrick : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private async void OnPause(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? Strings.TailResume : Strings.TailPause;
        StatusText.Text = _paused ? Text.Format(Strings.TailPaused, _lastStatus) : _lastStatus;
        if (!_paused)
        {
            await PollAsync();
        }
    }

    private void OnWrap(object sender, RoutedEventArgs e) =>
        LogBox.TextWrapping = WrapBox.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _lines.Clear();
        _partial = "";
        LogBox.Clear();
    }
}
