using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services;
using ZillaTerm.Core.Duty;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.App.Views;

/// <summary>
/// Astreinte : le texte de l'équipe (consignes, numéros d'urgence), l'enregistrement de ce qui est fait, une note, la
/// capture d'un Bureau à distance, et les astreintes enregistrées avec leur rapport.
/// </summary>
public partial class DutyWindow : Window
{
    private readonly DutyRecorder _recorder;
    private readonly string _who;
    private readonly string _mode;
    private readonly DispatcherTimer _timer;

    /// <param name="who">Utilisateur, écrit au début de l'enregistrement.</param>
    /// <param name="mode">Session CyberArk ou accès d'urgence, écrit au début de l'enregistrement.</param>
    public DutyWindow(string teamText, DutyRecorder recorder, string who, string mode)
    {
        InitializeComponent();
        _recorder = recorder;
        _who = who;
        _mode = mode;
        SetTeamText(teamText);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateState();
        _timer.Start();
        recorder.Changed += OnRecorderChanged;
        Closed += (_, _) =>
        {
            _timer.Stop();
            recorder.Changed -= OnRecorderChanged;
        };
        UpdateState();
        RefreshJournals();
        Loaded += (_, _) => RecordButton.Focus();
    }

    /// <summary>Texte de l'équipe (affiché tel quel, sans interprétation).</summary>
    public void SetTeamText(string text)
    {
        bool empty = string.IsNullOrWhiteSpace(text);
        TeamText.Text = empty ? "" : text;
        TeamText.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        NoTeamText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Durée lisible (« 1 h 05 »).</summary>
    internal static string Duration(TimeSpan duration) =>
        Text.Format(Strings.DutyDuration, (int)Math.Max(0, duration.TotalHours), Math.Max(0, duration.Minutes));

    /// <summary>Textes du rapport dans la langue de l'interface.</summary>
    internal static DutyReportText ReportText() => new()
    {
        Title = Strings.DutyReportTitle,
        Period = Strings.DutyReportPeriod,
        Unfinished = Strings.DutyReportUnfinished,
        Damaged = Strings.DutyReportDamaged,
        Confidential = Strings.DutyReportConfidential,
        Summary = Strings.DutyReportSummary,
        Timeline = Strings.DutyReportTimeline,
        TimeHeader = Strings.DutyReportTime,
        SourceHeader = Strings.DutyReportSession,
        EventHeader = Strings.DutyReportEvent,
        Kind = kind => kind switch
        {
            DutyKind.Started => Strings.DutyKindStarted,
            DutyKind.Stopped => Strings.DutyKindStopped,
            DutyKind.Note => Strings.DutyKindNote,
            DutyKind.Terminal => Strings.DutyKindTerminal,
            DutyKind.Connection => Strings.DutyKindConnection,
            DutyKind.Action => Strings.DutyKindAction,
            DutyKind.Transfer => Strings.DutyKindTransfer,
            _ => Strings.DutyKindScreenshot,
        },
        Count = (kind, count) => Text.Format(kind switch
        {
            DutyKind.Connection => Strings.DutyCountConnection,
            DutyKind.Action => Strings.DutyCountAction,
            DutyKind.Transfer => Strings.DutyCountTransfer,
            DutyKind.Screenshot => Strings.DutyCountScreenshot,
            DutyKind.Note => Strings.DutyCountNote,
            _ => Strings.DutyCountTerminal,
        }, count),
        Duration = Duration,
    };

    private void OnRecorderChanged()
    {
        UpdateState();
        RefreshJournals();
    }

    private void UpdateState()
    {
        bool recording = _recorder.IsRecording;
        RecordLabel.Text = recording ? Strings.DutyStopRecording : Strings.DutyStartRecording;
        RecordDot.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
        StopSquare.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        NoteBox.IsEnabled = recording;
        NoteButton.IsEnabled = recording && NoteBox.Text.Trim().Length > 0;
        CaptureButton.IsEnabled = recording;
        StatusText.Text = recording && _recorder.StartedAt is { } start
            ? Text.Format(Strings.DutyRecordingSince, start.ToLocalTime().ToString("t", CultureInfo.CurrentCulture),
                Duration(DateTime.UtcNow - start), _recorder.Count)
            : Strings.DutyNotRecording;
        if (_recorder.Failure is { } failure)
        {
            ShowMessage(Text.Format(Strings.DutyWriteFailed, failure.Message), error: true);
        }
        else if (_recorder.TerminalTextCapped)
        {
            ShowMessage(Strings.DutyTerminalCapped, error: true);
        }
    }

    private void ShowMessage(string text, bool error = false)
    {
        MessageText.Text = text;
        MessageText.SetResourceReference(TextBlock.ForegroundProperty, error ? "DangerBrush" : "MutedBrush");
    }

    private void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            // L'astreinte arrêtée reste sélectionnée : son rapport s'exporte tout de suite.
            var path = _recorder.JournalPath;
            _recorder.Stop(Strings.DutyStoppedByUser);
            JournalList.SelectedItem = JournalList.Items.OfType<ListBoxItem>()
                .FirstOrDefault(item => item.Tag is DutyFile file && string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
            ShowMessage(Strings.DutyStoppedHint);
            return;
        }

        try
        {
            _recorder.Start(_who, _mode);
            MessageText.Text = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ShowMessage(Text.Format(Strings.DutyStartFailed, ex.Message), error: true);
        }
    }

    private void OnNoteChanged(object sender, TextChangedEventArgs e) =>
        NoteButton.IsEnabled = _recorder.IsRecording && NoteBox.Text.Trim().Length > 0;

    private void OnNoteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AddNote();
        }
    }

    private void OnAddNote(object sender, RoutedEventArgs e) => AddNote();

    private void AddNote()
    {
        var note = NoteBox.Text.Trim();
        if (note.Length == 0 || !_recorder.IsRecording)
        {
            return;
        }

        _recorder.Record(DutyKind.Note, "", note);
        NoteBox.Clear();
        ShowMessage(Strings.DutyNoteAdded);
    }

    private void OnCapture(object sender, RoutedEventArgs e)
    {
        var windows = WindowCapture.RemoteDesktopWindows();
        switch (windows.Count)
        {
            case 0:
                ShowMessage(Strings.DutyNoRdpWindow, error: true);
                break;
            case 1:
                Capture(windows[0]);
                break;
            default:
                var menu = new ContextMenu { PlacementTarget = CaptureButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                foreach (var window in windows)
                {
                    var item = new MenuItem { Header = window.Title.Length > 0 ? window.Title : window.Handle.ToString(CultureInfo.InvariantCulture) };
                    item.Click += (_, _) => Capture(window);
                    menu.Items.Add(item);
                }

                menu.IsOpen = true;
                break;
        }
    }

    private void Capture(RemoteDesktopWindow window)
    {
        var png = WindowCapture.CapturePng(window.Handle);
        if (png is null)
        {
            ShowMessage(Strings.DutyCaptureFailed, error: true);
            return;
        }

        _recorder.Record(DutyKind.Screenshot, window.Title, Text.Format(Strings.DutyCaptureOf, window.Title), png);
        ShowMessage(Text.Format(Strings.DutyCaptured, window.Title));
    }

    private void RefreshJournals()
    {
        var selected = (JournalList.SelectedItem as ListBoxItem)?.Tag as DutyFile;
        JournalList.Items.Clear();
        IReadOnlyList<DutyFile> files;
        try
        {
            files = DutyJournal.List(_recorder.Directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            files = [];
        }

        foreach (var file in files)
        {
            var label = Text.Format(Strings.DutyJournalItem, file.Start.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                RemotePath.FormatSize(file.Length));
            if (string.Equals(file.Path, _recorder.JournalPath, StringComparison.OrdinalIgnoreCase))
            {
                label = Text.Format(Strings.DutyInProgress, label);
            }

            var item = new ListBoxItem { Content = label, Tag = file };
            JournalList.Items.Add(item);
            if (selected is not null && string.Equals(selected.Path, file.Path, StringComparison.OrdinalIgnoreCase))
            {
                JournalList.SelectedItem = item;
            }
        }

        if (files.Count == 0)
        {
            JournalList.Items.Add(new ListBoxItem { Content = Strings.DutyNoJournal, IsEnabled = false });
        }

        UpdateJournalButtons();
    }

    private DutyFile? SelectedJournal => (JournalList.SelectedItem as ListBoxItem)?.Tag as DutyFile;

    private void OnJournalSelected(object sender, SelectionChangedEventArgs e) => UpdateJournalButtons();

    private void UpdateJournalButtons()
    {
        var file = SelectedJournal;
        ExportButton.IsEnabled = file is not null;
        // L'astreinte en cours ne se supprime pas : il faut d'abord l'arrêter.
        DeleteButton.IsEnabled = file is not null && !string.Equals(file.Path, _recorder.JournalPath, StringComparison.OrdinalIgnoreCase);
    }

    private void OnJournalDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedJournal is not null)
        {
            Export(SelectedJournal);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (SelectedJournal is { } file)
        {
            Export(file);
        }
    }

    private void Export(DutyFile file)
    {
        DutyRecording recording;
        try
        {
            if (string.Equals(file.Path, _recorder.JournalPath, StringComparison.OrdinalIgnoreCase))
            {
                _recorder.Flush();
            }

            recording = DutyJournal.Read(file.Path, _recorder.Protector);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or CryptographicException)
        {
            ShowMessage(Text.Format(Strings.DutyReadFailed, ex.Message), error: true);
            return;
        }

        var local = file.Start.ToLocalTime();
        var dialog = new SaveFileDialog
        {
            FileName = $"astreinte-{local:yyyyMMdd-HHmm}.html",
            DefaultExt = ".html",
            Filter = "HTML (*.html)|*.html",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var html = DutyReport.ToHtml(recording, ReportText(), TimeZoneInfo.Local, CultureInfo.CurrentUICulture);
            File.WriteAllText(dialog.FileName, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage(ex.Message, error: true);
            return;
        }

        ShowMessage(Text.Format(Strings.DutyReportSaved, dialog.FileName));
        WindowsExplorer.ShowFile(dialog.FileName);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (SelectedJournal is not { } file || string.Equals(file.Path, _recorder.JournalPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!ConfirmDialog.Confirm(this, new ConfirmRequest
            {
                Title = Strings.DutyTitle,
                Heading = Strings.DutyDeleteHeading,
                Message = Text.Format(Strings.DutyDeleteDetail, file.Start.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
                Kind = ConfirmKind.Warning,
                Actions = [Strings.DutyDeleteAction],
                DangerAction = 0,
            }))
        {
            return;
        }

        try
        {
            File.Delete(file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage(ex.Message, error: true);
        }

        RefreshJournals();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
