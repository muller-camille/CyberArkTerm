using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ftp;
using CyberArkTerm.Core.KeePass;
using CyberArkTerm.Core.Localization;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Sessions de fichiers seuls (comptes CyberArk en SFTP via le PSMP ; accès d'urgence, entrées KeePass sftp://, ftp://,
/// ftpes://, ftps://) : un onglet d'état, les fichiers dans l'onglet Fichiers (même explorateur, mêmes transferts
/// vérifiés que les sessions SSH).
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Fichiers d'un compte CyberArk sans terminal : une session PSMP SFTP (enregistrée par CyberArk comme les autres),
    /// rouverte à la reconnexion ; les envois peuvent aussi passer par SCP si la plateforme l'autorise.
    /// </summary>
    private async Task OpenPsmpFilesTabAsync(PvwaAccount account, string login, string label, SavedSession? saved, Func<Task>? duplicate)
    {
        SetStatus(Text.Format(Strings.SftpFilesOpening, label, _settings.PsmpAddress));
        var key = await GetPsmpKeyAsync();
        var connector = new SshConnector(_settings.PsmpAddress, _settings.PsmpPort, login, _psmpUi, key);
        var session = SshSession.ForFiles(label, "SFTP", async ct =>
        {
            var sftp = await connector.ConnectSftpAsync(ct);
            sftp.KeepAliveInterval = TimeSpan.FromSeconds(30);
            return new RemoteFileBrowser(sftp, connector.ConnectScpAsync);
        }, Dispatcher, account, saved);
        ShowFilesTab(session, $"{login}@{_settings.PsmpAddress}", duplicate);
    }

    /// <summary>Ouvre la session de fichiers d'une entrée KeePass ; le mot de passe est lu à chaque connexion.</summary>
    private void OpenKeePassFiles(KeePassTarget target, string label, Func<string?> password, Func<Task> duplicate)
    {
        Func<CancellationToken, Task<IRemoteFiles>> open;
        if (target.Protocol == RemoteProtocol.Sftp)
        {
            var connector = new SshConnector(target.Host, target.Port, target.UserName, _directUi, password: password,
                addressWhat: CoreStrings.ServerAddressWhat);
            open = async ct =>
            {
                var sftp = await connector.ConnectSftpAsync(ct);
                sftp.KeepAliveInterval = TimeSpan.FromSeconds(30);
                return new RemoteFileBrowser(sftp, connector.ConnectScpAsync);
            };
        }
        else
        {
            // Connexion en clair acceptée une fois pour la session (connexions dédiées au suivi comprises).
            bool cleartextAccepted = false;
            var connection = new FtpConnection
            {
                Host = target.Host,
                Port = target.Port,
                UserName = target.UserName.Length > 0 ? target.UserName : "anonymous",
                Password = password,
                Security = target.Protocol switch
                {
                    RemoteProtocol.Ftps => FtpSecurity.Implicit,
                    RemoteProtocol.Ftpes => FtpSecurity.Explicit,
                    _ => FtpSecurity.Opportunistic,
                },
                TrustCertificate = certificate => Dispatcher.Invoke(() => TrustFtpCertificate(target, certificate)),
                AllowCleartext = () => cleartextAccepted || (cleartextAccepted = Dispatcher.Invoke(() => ConfirmFtpCleartext(target))),
            };
            open = async ct => await FtpFileBrowser.ConnectAsync(connection, ct);
        }

        var session = SshSession.ForFiles(label, KeePassTarget.Name(target.Protocol), open, Dispatcher);
        var who = target.UserName.Length > 0 ? $"{target.UserName}@{target.Address}" : target.Address;
        ShowFilesTab(session, who, duplicate);
    }

    private void ShowFilesTab(SshSession session, string target, Func<Task>? duplicate)
    {
        session.Editor = new RemoteEditor(session, this, _settings, (text, error) => SetStatus(text, error),
            directory => FilesPanel.OnRemoteChanged(session, directory));
        var view = new FilesSessionView(session, target);
        view.ShowFilesRequested += () => SideTabs.SelectedItem = FilesTab;
        var tab = new TabItem { Content = view, Tag = session };
        tab.Header = TabHeader(tab, session.Label, "IconFiles", duplicate);
        session.StateChanged += () =>
        {
            switch (session.State)
            {
                case SshSessionState.Connected:
                    SetStatus(Text.Format(Strings.FilesOpened, session.Label, session.FilesProtocol));
                    break;
                case SshSessionState.Failed:
                    SetStatus(Text.Format(Strings.FilesSessionError, session.Label, session.Error), isError: true);
                    break;
            }
        };

        _sshSessions.Add(session);
        MainTabs.Items.Add(tab);
        MainTabs.SelectedItem = tab;
        SideTabs.SelectedItem = FilesTab;
        _ = view.ConnectAsync();
    }

    /// <summary>
    /// Certificat FTPS que Windows n'approuve pas (auto-signé, autre nom…) : accepté s'il a déjà été épinglé pour ce
    /// serveur, sinon question ; un certificat différent de celui épinglé est signalé comme un changement suspect.
    /// </summary>
    private bool TrustFtpCertificate(KeePassTarget target, FtpCertificate certificate)
    {
        var host = "ftps://" + target.Host;
        var status = KnownHosts.Check(_settings.KnownHosts, host, target.Port, "X.509", certificate.Sha256);
        if (status == HostKeyStatus.Trusted)
        {
            return true;
        }

        var fingerprint = string.Join(" ", certificate.Sha256.Chunk(16).Select(c => new string(c)));
        var message = Text.Format(status == HostKeyStatus.Unknown ? Strings.FtpCertificateUnknown : Strings.FtpCertificateChanged,
            target.Address, certificate.Problem, certificate.Subject, certificate.Issuer,
            certificate.NotBefore.ToString("d", System.Globalization.CultureInfo.CurrentCulture),
            certificate.NotAfter.ToString("d", System.Globalization.CultureInfo.CurrentCulture), fingerprint);
        if (MessageBox.Show(this, message, Strings.FtpCertificateTitle, MessageBoxButton.YesNo,
                status == HostKeyStatus.Unknown ? MessageBoxImage.Question : MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return false;
        }

        KnownHosts.Remember(_settings.KnownHosts, host, target.Port, "X.509", certificate.Sha256);
        SaveSettings();
        return true;
    }

    private bool ConfirmFtpCleartext(KeePassTarget target) =>
        MessageBox.Show(this, Text.Format(Strings.FtpCleartextConfirm, target.Address), Strings.FtpCleartextTitle,
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
