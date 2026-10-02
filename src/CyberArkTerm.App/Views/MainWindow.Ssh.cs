using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;
using Renci.SshNet;

namespace CyberArkTerm.App.Views;

/// <summary>Sessions SSH intégrées : un onglet terminal par session, panneau « Fichiers » associé.</summary>
public partial class MainWindow
{
    private readonly SshInteraction _psmpUi;
    private readonly List<SshSession> _sshSessions = [];
    private MfaSshKey? _mfaKey;
    private DateTime _mfaRetryAfter;

    private async Task OpenSshTabAsync(PvwaAccount account, string login, string label, SavedSession? saved)
    {
        SetStatus(Text.Format(Strings.SshOpening, label, _settings.PsmpAddress));
        var key = await GetPsmpKeyAsync();
        var connector = new SshConnector(_settings.PsmpAddress, _settings.PsmpPort, login, _psmpUi, key);
        var session = new SshSession(account, label, connector, Dispatcher, _settings.FollowTerminalFolder, saved);
        session.Editor = new RemoteEditor(session, this, _settings, (text, error) => SetStatus(text, error),
            directory => FilesPanel.OnRemoteChanged(session, directory));
        var view = new SshSessionView(session, $"{login}@{_settings.PsmpAddress}");
        var tab = new TabItem { Content = view, Tag = session };
        tab.Header = TabHeader(label, "IconSsh", () => CloseSshTab(tab));
        session.StateChanged += () =>
        {
            switch (session.State)
            {
                case SshSessionState.Connected:
                    SetStatus(Text.Format(Strings.SshOpened, label, _settings.PsmpAddress));
                    break;
                case SshSessionState.Failed:
                    SetStatus(Text.Format(Strings.SshSessionError, label, session.Error), isError: true);
                    break;
            }
        };

        _sshSessions.Add(session);
        MainTabs.Items.Add(tab);
        MainTabs.SelectedItem = tab;
        SideTabs.SelectedItem = FilesTab;
        // La connexion (et ses éventuelles questions : clé d'hôte, mot de passe, MFA) se poursuit dans l'onglet.
        _ = view.ConnectAsync();
    }

    private object TabHeader(string label, string icon, Action close)
    {
        var closeButton = new Button
        {
            Style = (Style)FindResource("TabCloseButton"),
            Content = new Image { Source = (System.Windows.Media.ImageSource)FindResource("IconClose"), Width = 11, Height = 11 },
            ToolTip = Strings.CloseSessionTip,
        };
        closeButton.Click += (_, _) => close();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = System.Windows.Media.Brushes.Transparent };
        header.Children.Add(new Image { Source = (System.Windows.Media.ImageSource)FindResource(icon), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
        header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(closeButton);
        // Clic molette sur l'onglet : fermeture.
        header.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                close();
            }
        };
        return header;
    }

    private void CloseSshTab(TabItem tab)
    {
        if (tab.Tag is not SshSession session)
        {
            return;
        }

        if (session.Editor is { } editor && !RemoteEditor.ConfirmClose(this, [editor]))
        {
            return;
        }

        MainTabs.Items.Remove(tab);
        _sshSessions.Remove(session);
        session.Dispose();
        MainTabs.SelectedItem ??= HomeTab;
        SetStatus(Text.Format(Strings.SshClosed, session.Label));
    }

    /// <summary>Vrai si l'on peut fermer : aucun fichier modifié non renvoyé, ou l'utilisateur accepte de les perdre.</summary>
    private bool ConfirmCloseEditedFiles() =>
        RemoteEditor.ConfirmClose(this, _sshSessions.Select(s => s.Editor).OfType<RemoteEditor>());

    private void CloseAllSshSessions()
    {
        foreach (var session in _sshSessions)
        {
            session.Dispose();
        }

        _sshSessions.Clear();
    }

    private void OnMainTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged remonte aussi depuis les listes contenues dans les onglets.
        if (!ReferenceEquals(e.OriginalSource, MainTabs))
        {
            return;
        }

        var tab = MainTabs.SelectedItem as TabItem;
        FilesPanel.Attach(tab?.Tag as SshSession);
        ShowRdpView(tab);
        if (tab?.Content is SshSessionView view)
        {
            view.FocusTerminal();
        }
    }

    /// <summary>
    /// Clé « MFA caching » du PVWA : évite de ressaisir mot de passe et MFA à chaque connexion au PSMP.
    /// Si le PVWA ne la fournit pas, le PSMP posera ses questions (mot de passe, code) dans une fenêtre.
    /// </summary>
    private async Task<PrivateKeyFile?> GetPsmpKeyAsync()
    {
        if (_mfaKey is null || _mfaKey.IsExpired(DateTimeOffset.Now))
        {
            _mfaKey = null;
            if (DateTime.UtcNow < _mfaRetryAfter)
            {
                return null;
            }

            try
            {
                _mfaKey = await _client.GetMfaCachingSshKeyAsync(_lifetime.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or (PvwaException and not PvwaException { IsUnauthorized: true })
                                           || (ex is TaskCanceledException && !_lifetime.IsCancellationRequested))
            {
                _mfaKey = null;
            }

            if (_mfaKey is null)
            {
                _mfaRetryAfter = DateTime.UtcNow.AddMinutes(15);
                return null;
            }
        }

        try
        {
            return new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(_mfaKey.PrivateKey)));
        }
        catch (Exception ex) when (ex is Renci.SshNet.Common.SshException or ArgumentException or InvalidOperationException or FormatException)
        {
            _mfaKey = null;
            _mfaRetryAfter = DateTime.UtcNow.AddMinutes(15);
            return null;
        }
    }
}
