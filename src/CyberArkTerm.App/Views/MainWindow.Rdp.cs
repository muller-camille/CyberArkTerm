using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services.Rdp;
using CyberArkTerm.Core.Rdp;

namespace CyberArkTerm.App.Views;

/// <summary>Sessions Bureau à distance intégrées : un onglet par session, contrôle Bureau à distance de Windows.</summary>
public partial class MainWindow
{
    private readonly List<RdpSessionView> _rdpViews = [];
    private Grid? _rdpLayer;

    /// <summary>Calque du modèle des onglets où vivent les sessions Bureau à distance (voir Theme.xaml).</summary>
    private Grid RdpLayer => _rdpLayer ??= (Grid)MainTabs.Template.FindName("RdpLayer", MainTabs);

    /// <summary>
    /// Réglages pour ouvrir ce fichier .rdp dans un onglet, ou null s'il faut le confier à mstsc : option désactivée,
    /// application distante (RemoteApp) ou contrôle Bureau à distance absent (le motif s'affiche dans la barre d'état).
    /// </summary>
    private RdpConnectionSettings? EmbeddableRdp(byte[] rdpFile, string label)
    {
        if (!_settings.RdpInApp)
        {
            return null;
        }

        RdpConnectionSettings settings;
        try
        {
            settings = RdpConnectionSettings.FromRdpFile(rdpFile);
        }
        catch (FormatException)
        {
            return null;
        }

        string? reason = settings.IsRemoteApp ? Strings.RdpRemoteAppFallback
            : !RdpClientHost.IsAvailable ? Strings.RdpControlMissing
            : null;
        if (reason is not null)
        {
            SetStatus(Text.Format(reason, label));
            return null;
        }

        return settings;
    }

    /// <summary>
    /// Ouvre un onglet Bureau à distance. <paramref name="prepare"/> fournit les réglages de chaque connexion
    /// (y compris les reconnexions) ; <paramref name="first"/> sert pour la première si on l'a déjà.
    /// </summary>
    private async Task<RdpSession> OpenRdpTabAsync(string label, Func<CancellationToken, Task<RdpConnectionRequest>> prepare,
        RdpConnectionRequest? first = null)
    {
        var pending = first;
        var session = new RdpSession(label, ct =>
        {
            var request = pending;
            pending = null;
            return request is not null ? Task.FromResult(request) : prepare(ct);
        });
        var view = new RdpSessionView(session) { Visibility = Visibility.Hidden };
        var tab = new TabItem { Tag = session };
        tab.Header = TabHeader(label, "IconWindows", () => _ = CloseRdpTabAsync(tab));
        session.StateChanged += () =>
        {
            switch (session.State)
            {
                case RdpSessionState.Connected:
                    SetStatus(Text.Format(Strings.RdpOpened, label));
                    break;
                case RdpSessionState.Failed when session.Error is not null:
                    SetStatus(Text.Format(Strings.RdpSessionError, label, session.Error), isError: true);
                    break;
            }
        };

        RdpLayer.Children.Add(view);
        _rdpViews.Add(view);
        MainTabs.Items.Add(tab);
        MainTabs.SelectedItem = tab;
        // Laisse la mise en page se faire pour connaître la taille de l'onglet.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await session.ConnectAsync();
        return session;
    }

    private async Task CloseRdpTabAsync(TabItem tab)
    {
        if (tab.Tag is not RdpSession session || !MainTabs.Items.Contains(tab))
        {
            return;
        }

        if (session.IsConnected && MessageBox.Show(this, Text.Format(Strings.RdpCloseTabConfirm, session.Label), Strings.RdpTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await RemoveRdpTabAsync(session);
        SetStatus(Text.Format(Strings.RdpClosed, session.Label));
    }

    private async Task RemoveRdpTabAsync(RdpSession session)
    {
        if (MainTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == session) is { } tab)
        {
            MainTabs.Items.Remove(tab);
        }

        MainTabs.SelectedItem ??= HomeTab;
        var view = _rdpViews.First(v => v.Session == session);
        view.Visibility = Visibility.Hidden;
        // Le contrôle reste dans la fenêtre jusqu'à la fin de la déconnexion.
        await session.CloseAsync();
        _rdpViews.Remove(view);
        RdpLayer.Children.Remove(view);
    }

    /// <summary>Vrai si l'on peut fermer : aucune session Bureau à distance connectée, ou l'utilisateur accepte de les fermer.</summary>
    private bool ConfirmCloseRdpSessions()
    {
        var open = _rdpViews.Count(v => v.Session.HasControl);
        return open == 0
            || MessageBox.Show(this, Text.Format(Strings.RdpCloseAllConfirm, open), Strings.RdpTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void CloseAllRdpSessions()
    {
        foreach (var view in _rdpViews)
        {
            view.Session.Dispose();
        }

        _rdpViews.Clear();
    }

    /// <summary>Affiche la session Bureau à distance de l'onglet choisi et masque les autres.</summary>
    private void ShowRdpView(TabItem? tab)
    {
        foreach (var view in _rdpViews)
        {
            bool selected = ReferenceEquals(tab?.Tag, view.Session);
            view.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
            if (selected)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, view.Session.Focus);
            }
        }
    }
}
