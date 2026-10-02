using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services.Rdp;
using CyberArkTerm.Core.Diagnostics;
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
    /// Réglages pour ouvrir ce fichier .rdp dans un onglet (bureau ou application distante), ou null s'il faut le confier
    /// à mstsc : option désactivée ou contrôle Bureau à distance absent (<paramref name="reason"/> dit pourquoi).
    /// </summary>
    private RdpConnectionSettings? EmbeddableRdp(byte[] rdpFile, string label, out string? reason)
    {
        reason = null;
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
            DebugLog.Write("psm", "Fichier .rdp sans adresse de serveur : ouvert avec mstsc.");
            return null;
        }

        if (!RdpClientHost.IsAvailable)
        {
            reason = Text.Format(Strings.RdpControlMissing, label);
            return null;
        }

        return settings;
    }

    /// <summary>
    /// Composant PSM en application distante : ouvert comme un bureau dans l'onglet si l'option le demande et si le
    /// fichier le permet (programme de démarrage présent), sinon tel quel (fenêtres séparées).
    /// </summary>
    private RdpConnectionSettings ForPsmTab(RdpConnectionSettings settings)
    {
        if (!settings.IsRemoteApp)
        {
            return settings;
        }

        if (!_settings.PsmRemoteAppAsDesktop)
        {
            DebugLog.Write("psm", "Application distante en fenêtres séparées : option « applications distantes PSM dans l'onglet » désactivée.");
            return settings;
        }

        if (settings.RemoteAppAsDesktop() is not { } desktop)
        {
            DebugLog.Write("psm", "Application distante en fenêtres séparées : pas de programme de démarrage ou d'application dans le fichier.");
            return settings;
        }

        DebugLog.Write("psm", $"Application distante « {settings.RemoteApplicationTitle} » ouverte comme un bureau : programme de démarrage « {settings.RemoteApplicationProgram} », arguments « {DebugLog.Hidden(settings.RemoteApplicationArgs)} », au lieu de « {settings.StartProgram} ».");
        return desktop;
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
