using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.App.Services.Rdp;
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

    private async Task OpenSshTabAsync(PvwaAccount account, string login, string label, SavedSession? saved, Func<Task>? duplicate)
    {
        SetStatus(Text.Format(Strings.SshOpening, label, _settings.PsmpAddress));
        var key = await GetPsmpKeyAsync();
        var connector = new SshConnector(_settings.PsmpAddress, _settings.PsmpPort, login, _psmpUi, key);
        var session = new SshSession(account, label, connector, Dispatcher, _settings.FollowTerminalFolder, saved);
        ShowSshTab(session, $"{login}@{_settings.PsmpAddress}", Strings.ConnectingViaPsmp, "IconSsh",
            Text.Format(Strings.SshOpened, label, _settings.PsmpAddress), duplicate);
    }

    /// <summary>Onglet terminal + panneau « Fichiers » pour une session SSH (via le PSMP ou directe).</summary>
    /// <param name="target">« utilisateur@serveur », affiché pendant la connexion.</param>
    /// <param name="duplicate">Ouvre une autre session sur le même compte ou la même entrée (menu de l'onglet).</param>
    private void ShowSshTab(SshSession session, string target, string connectingText, string icon, string openedMessage,
        Func<Task>? duplicate)
    {
        var label = session.Label;
        session.Editor = new RemoteEditor(session, this, _settings, (text, error) => SetStatus(text, error),
            directory => FilesPanel.OnRemoteChanged(session, directory));
        var view = new SshSessionView(session, target, connectingText);
        var tab = new TabItem { Content = view, Tag = session };
        tab.Header = TabHeader(tab, label, icon, duplicate);
        session.StateChanged += () =>
        {
            switch (session.State)
            {
                case SshSessionState.Connected:
                    SetStatus(openedMessage);
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

    /// <summary>En-tête d'un onglet de session : icône, nom, bouton de fermeture, menu (clic droit).</summary>
    private object TabHeader(TabItem tab, string label, string icon, Func<Task>? duplicate)
    {
        var closeButton = new Button
        {
            Style = (Style)FindResource("TabCloseButton"),
            Content = new Image { Source = (System.Windows.Media.ImageSource)FindResource("IconClose"), Width = 11, Height = 11 },
            ToolTip = Strings.CloseSessionTip,
        };
        closeButton.Click += (_, _) => CloseSessionTab(tab);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = System.Windows.Media.Brushes.Transparent };
        header.Children.Add(new Image { Source = (System.Windows.Media.ImageSource)FindResource(icon), Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0) });
        header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(closeButton);
        // Clic molette sur l'onglet : fermeture.
        header.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                CloseSessionTab(tab);
            }
        };
        header.ContextMenu = TabMenu(tab, duplicate);
        if (tab.Tag is SshSession)
        {
            header.ToolTip = Strings.TabDetachTip;
            EnableDragToDetach(tab, header);
        }

        return header;
    }

    /// <summary>Menu de l'en-tête d'un onglet de session : reconnecter, dupliquer, fermer, fermer les autres.</summary>
    private ContextMenu TabMenu(TabItem tab, Func<Task>? duplicate)
    {
        static Image MenuIcon(object source) => new() { Source = (System.Windows.Media.ImageSource)source, Width = 16, Height = 16 };

        var reconnect = new MenuItem { Header = Strings.MenuTabReconnect, Icon = MenuIcon(FindResource("IconRefresh")) };
        reconnect.Click += async (_, _) => await ReconnectTabAsync(tab);
        var copy = new MenuItem
        {
            Header = Strings.MenuTabDuplicate,
            ToolTip = Strings.MenuTabDuplicateTip,
            Icon = MenuIcon(FindResource("IconConnect")),
            IsEnabled = duplicate is not null,
        };
        copy.Click += async (_, _) =>
        {
            if (duplicate is not null)
            {
                await duplicate();
            }
        };
        var close = new MenuItem { Header = Strings.MenuTabClose, Icon = MenuIcon(FindResource("IconClose")) };
        close.Click += (_, _) => CloseSessionTab(tab);
        var closeOthers = new MenuItem { Header = Strings.MenuTabCloseOthers };
        closeOthers.Click += async (_, _) => await CloseOtherTabsAsync(tab);
        // Fenêtre séparée : terminal SSH seulement (voir MainWindow.Detach.cs).
        var detach = new MenuItem { Header = Strings.MenuTabDetach, Visibility = tab.Tag is SshSession ? Visibility.Visible : Visibility.Collapsed };
        detach.Click += (_, _) => DetachTab(tab);
        // Vue parallèle : terminal SSH seulement (voir MainWindow.Parallel.cs).
        var parallel = new MenuItem
        {
            Header = Strings.MenuTabAddParallel,
            Icon = MenuIcon(FindResource("IconParallel")),
            Visibility = tab.Tag is SshSession ? Visibility.Visible : Visibility.Collapsed,
        };
        parallel.Click += (_, _) =>
        {
            if (tab.Tag is SshSession session)
            {
                ToggleParallel(session);
            }
        };
        var menu = new ContextMenu { Items = { reconnect, copy, detach, parallel, new Separator(), close, closeOthers } };
        menu.Opened += (_, _) =>
        {
            closeOthers.IsEnabled = SessionTabs().Any(t => t != tab);
            detach.IsEnabled = tab.Content is SshSessionView;
            bool inParallel = tab.Tag is SshSession session && _parallel?.Contains(session) == true;
            parallel.Header = inParallel ? Strings.MenuTabRemoveParallel : Strings.MenuTabAddParallel;
        };
        return menu;
    }

    /// <summary>Onglets de session (SSH, Bureau à distance), dans l'ordre affiché.</summary>
    private IEnumerable<TabItem> SessionTabs() => MainTabs.Items.OfType<TabItem>().Where(t => t.Tag is SshSession or RdpSession);

    private void CloseSessionTab(TabItem tab)
    {
        switch (tab.Tag)
        {
            case SshSession:
                CloseSshTab(tab);
                break;
            case RdpSession:
                _ = CloseRdpTabAsync(tab);
                break;
        }
    }

    /// <summary>Nouvelle connexion dans le même onglet (nouvelle demande au PVWA) ; confirmation si la session est ouverte.</summary>
    private async Task ReconnectTabAsync(TabItem tab)
    {
        // Session de la vue parallèle : c'est là que son terminal est affiché.
        MainTabs.SelectedItem = tab.Tag is SshSession inView && _parallel?.Contains(inView) == true ? _parallelTab : tab;
        bool Confirm(string label) =>
            MessageBox.Show(this, Text.Format(Strings.TabReconnectConfirm, label), "CyberArkTerm",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

        switch (tab.Tag)
        {
            case SshSession ssh when SshViewOf(tab) is { } view:
                if (ssh.State != SshSessionState.Connected || Confirm(ssh.Label))
                {
                    await view.ConnectAsync();
                }

                break;
            case RdpSession rdp:
                if (!rdp.IsConnected || Confirm(rdp.Label))
                {
                    await rdp.ReconnectAsync();
                }

                break;
        }
    }

    /// <summary>Ferme les autres onglets de session, après une seule confirmation.</summary>
    private async Task CloseOtherTabsAsync(TabItem keep)
    {
        var others = SessionTabs().Where(t => t != keep).ToList();
        if (others.Count == 0
            || MessageBox.Show(this, others.Count == 1 ? Strings.TabCloseOtherConfirm : Text.Format(Strings.TabCloseOthersConfirm, others.Count), "CyberArkTerm",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        MainTabs.SelectedItem = keep;
        var closing = new List<Task>();
        foreach (var tab in others)
        {
            switch (tab.Tag)
            {
                case SshSession:
                    // Seule question possible : des fichiers modifiés pas encore renvoyés.
                    CloseSshTab(tab);
                    break;
                case RdpSession rdp:
                    closing.Add(RemoveRdpTabAsync(rdp));
                    break;
            }
        }

        await Task.WhenAll(closing);
        MainTabs.SelectedItem = keep;
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

        if (!FilesPanel.ConfirmCancelTransfers(this, session))
        {
            return;
        }

        CloseDetachedWindow(session);
        DropFromParallel(session);
        FilesPanel.ReleaseTails(session);
        MainTabs.Items.Remove(tab);
        _sshSessions.Remove(session);
        _ = DisposeAfterTransfersAsync(session);
        MainTabs.SelectedItem ??= HomeTab;
        SetStatus(Text.Format(Strings.SshClosed, session.Label));
    }

    /// <summary>Annule les transferts de la session, laisse le fichier interrompu être supprimé, puis ferme ses connexions.</summary>
    private async Task DisposeAfterTransfersAsync(SshSession session)
    {
        try
        {
            await FilesPanel.CancelTransfersAsync(session);
        }
        finally
        {
            session.Dispose();
        }
    }

    /// <summary>Vrai si l'on peut fermer : aucun fichier modifié non renvoyé, ou l'utilisateur accepte de les perdre.</summary>
    private bool ConfirmCloseEditedFiles() =>
        RemoteEditor.ConfirmClose(this, _sshSessions.Select(s => s.Editor).OfType<RemoteEditor>());

    private void CloseAllSshSessions()
    {
        CloseParallel();
        CloseAllDetachedWindows();
        FilesPanel.CloseTailWindows();
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
        // Vue parallèle : l'onglet Fichiers suit la session où l'on travaille.
        FilesPanel.Attach(tab?.Tag as SshSession ?? (tab?.Tag as ParallelView)?.ActiveSession);
        ShowRdpView(tab);
        if (tab?.Content is SshSessionView view)
        {
            view.FocusTerminal();
        }
        else if (tab?.Content is ParallelView parallel)
        {
            parallel.FocusActive();
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
                _mfaKey = await Client.GetMfaCachingSshKeyAsync(_lifetime.Token);
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
