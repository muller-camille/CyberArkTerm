using System.Windows;
using System.Windows.Input;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services;
using ZillaTerm.Core.Duty;

namespace ZillaTerm.App.Views;

/// <summary>Astreinte : bouton de la barre, indicateur d'enregistrement, fenêtre, et événements du journal.</summary>
public partial class MainWindow
{
    private DutyWindow? _dutyWindow;

    private static DutyRecorder Duty => DutyRecorder.Current;

    /// <summary>Session CyberArk ou accès d'urgence, écrit au début de l'enregistrement.</summary>
    private string DutyMode => _client is null ? Strings.DutyModeEmergency : Text.Format(Strings.DutyModeCyberArk, PvwaHost);

    private void InitDuty()
    {
        Duty.Changed += UpdateDutyIndicator;
        Closed += (_, _) => Duty.Changed -= UpdateDutyIndicator;
        UpdateDutyIndicator();
        if (_client is null)
        {
            // L'enregistrement continue d'une fenêtre à l'autre : le passage en accès d'urgence y figure.
            Duty.Record(DutyKind.Action, "", Strings.DutyEmergencyEntered);
        }
    }

    private void UpdateDutyIndicator()
    {
        bool recording = Duty.IsRecording;
        DutyBadge.Visibility = DutyIndicator.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        foreach (var view in _rdpViews)
        {
            view.DutyRecording = recording;
        }
    }

    private void OnDuty(object sender, RoutedEventArgs e) => ShowDuty();

    private void OnDutyIndicatorClick(object sender, MouseButtonEventArgs e) => ShowDuty();

    private void ShowDuty()
    {
        if (_dutyWindow is { IsLoaded: true })
        {
            _dutyWindow.SetTeamText(_settings.DutyText);
            _dutyWindow.Activate();
            return;
        }

        _dutyWindow = new DutyWindow(_settings.DutyText, Duty, _sessionUser, DutyMode) { Owner = this };
        _dutyWindow.Closed += (_, _) => _dutyWindow = null;
        _dutyWindow.Show();
    }

    /// <summary>Événement du journal d'astreinte (sans effet hors enregistrement).</summary>
    private static void DutyRecord(DutyKind kind, string source, string text) => Duty.Record(kind, source, text);

    /// <summary>Session PSM ouverte : composant, machine cible, motif et ticket, pour le journal.</summary>
    private static string DutyPsmText(ConnectRequest request)
    {
        var lines = new List<string> { Text.Format(Strings.DutyPsmLaunched, request.Component) };
        if (!string.IsNullOrWhiteSpace(request.RemoteMachine))
        {
            lines.Add(Text.Format(Strings.DutyMachine, request.RemoteMachine));
        }

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            lines.Add(Text.Format(Strings.DutyReason, request.Reason));
        }

        if (!string.IsNullOrWhiteSpace(request.TicketingSystem) || !string.IsNullOrWhiteSpace(request.TicketId))
        {
            lines.Add(Text.Format(Strings.DutyTicket, request.TicketingSystem ?? "", request.TicketId ?? ""));
        }

        return string.Join('\n', lines);
    }

    /// <summary>Message de la barre d'état d'une action sur un compte, gardé aussi dans le journal d'astreinte.</summary>
    private void ActionStatus(string source, string text)
    {
        SetStatus(text);
        Duty.Record(DutyKind.Action, source, text);
    }

    /// <summary>Message de la barre d'état d'une session, gardé aussi dans le journal d'astreinte.</summary>
    private void SessionStatus(string source, string text, bool isError = false)
    {
        SetStatus(text, isError);
        Duty.Record(DutyKind.Connection, source, text);
    }
}
