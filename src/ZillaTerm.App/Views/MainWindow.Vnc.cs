using System.Windows;
using System.Windows.Controls;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services;

namespace ZillaTerm.App.Views;

/// <summary>Sessions VNC (accès d'urgence, entrées KeePass vnc://) : un onglet par session, client RFB intégré.</summary>
public partial class MainWindow
{
    /// <param name="password">Mot de passe VNC, lu à chaque connexion (null si le coffre a été verrouillé entre-temps).</param>
    /// <param name="duplicate">Ouvre une autre session sur la même entrée (menu de l'onglet).</param>
    private void OpenVncTab(string label, string host, int port, Func<string?> password, Func<Task>? duplicate)
    {
        var session = new VncSession(label, host, port, password);
        var view = new VncSessionView(session);
        var tab = new TabItem { Content = view, Tag = session };
        tab.Header = TabHeader(tab, label, "IconConnect", duplicate);
        session.StateChanged += () => Dispatcher.BeginInvoke(() =>
        {
            switch (session.State)
            {
                case VncSessionState.Connected:
                    SetStatus(Text.Format(Strings.VncOpened, label));
                    break;
                case VncSessionState.Failed or VncSessionState.Closed when session.Error is not null:
                    SetStatus(Text.Format(Strings.VncSessionError, label, session.Error), isError: true);
                    break;
            }
        });

        MainTabs.Items.Add(tab);
        MainTabs.SelectedItem = tab;
        _ = session.ConnectAsync();
    }

    private void CloseVncTab(TabItem tab, Window? owner = null)
    {
        if (tab.Tag is not VncSession session)
        {
            return;
        }

        if (session.IsConnected && !ConfirmCloseSession(owner ?? this, session.Label, Strings.SessionCloseVnc))
        {
            return;
        }

        RemoveVncTab(tab);
        SetStatus(Text.Format(Strings.VncClosed, session.Label));
    }

    private void RemoveVncTab(TabItem tab)
    {
        MainTabs.Items.Remove(tab);
        (tab.Tag as VncSession)?.Dispose();
        MainTabs.SelectedItem ??= HomeTab;
    }

    /// <summary>Fermeture de la fenêtre : connexions VNC fermées.</summary>
    private void CloseAllVncSessions()
    {
        foreach (var session in MainTabs.Items.OfType<TabItem>().Select(t => t.Tag).OfType<VncSession>().ToList())
        {
            session.Dispose();
        }
    }
}
