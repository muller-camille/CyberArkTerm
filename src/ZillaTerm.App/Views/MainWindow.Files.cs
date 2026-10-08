using System.Windows;
using System.Windows.Controls;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Services;
using ZillaTerm.Core;
using ZillaTerm.Core.Ftp;
using ZillaTerm.Core.KeePass;
using ZillaTerm.Core.Localization;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.App.Views;

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
    private void OpenPsmpFilesTab(PvwaAccount account, PsmpEndpoint psmp, string login, string label, SavedSession? saved,
        Func<Task>? duplicate, ConnectRequest request)
    {
        var connector = new SshConnector(psmp.Host, psmp.Port, login, _psmpUi.For(label), PsmpKeyAsync);
        var session = new FilesSession(label, "SFTP", connector.OpenFileBrowserAsync, Dispatcher, account, saved)
        {
            Psmp = psmp.Host,
            Request = request,
        };
        ShowFilesTab(session, $"{login}@{psmp.Host}", duplicate);
    }

    /// <summary>Ouvre la session de fichiers d'une entrée KeePass ; le mot de passe est lu à chaque connexion.</summary>
    private void OpenKeePassFiles(KeePassTarget target, string label, Func<string?> password, Func<Task> duplicate)
    {
        Func<CancellationToken, Task<IRemoteFiles>> open;
        if (target.Protocol == RemoteProtocol.Sftp)
        {
            var connector = new SshConnector(target.Host, target.Port, target.UserName, _directUi.For(label), password: password,
                addressWhat: CoreStrings.ServerAddressWhat);
            open = connector.OpenFileBrowserAsync;
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

        var session = new FilesSession(label, KeePassTarget.Name(target.Protocol), open, Dispatcher);
        var who = target.UserName.Length > 0 ? $"{target.UserName}@{target.Address}" : target.Address;
        ShowFilesTab(session, who, duplicate);
    }

    private void ShowFilesTab(FilesSession session, string target, Func<Task>? duplicate)
    {
        session.Editor = new RemoteEditor(session, this, _settings, (text, error) => SetStatus(text, error),
            directory => FilesPanel.OnRemoteChanged(session, directory));
        var view = new FilesSessionView(session, target)
        {
            // Reconnexion : la connexion actuelle sera fermée, ses transferts sont donc annulés d'abord (avec accord).
            BeforeReconnect = async () =>
            {
                if (!FilesPanel.ConfirmCancelTransfers(this, session))
                {
                    return false;
                }

                await FilesPanel.CancelTransfersAsync(session);
                return true;
            },
        };
        view.ShowFilesRequested += () => ShowSideTab(FilesTab);
        var tab = new TabItem { Content = view, Tag = session };
        tab.Header = TabHeader(tab, session.Label, "IconFiles", duplicate);
        session.StateChanged += () =>
        {
            switch (session.State)
            {
                case RemoteSessionState.Connected:
                    SetStatus(Text.Format(Strings.FilesOpened, session.Label, session.Protocol));
                    break;
                case RemoteSessionState.Failed:
                    SetStatus(Text.Format(Strings.FilesSessionError, session.Label, session.Error), isError: true);
                    break;
            }
        };

        _remoteSessions.Add(session);
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

        static string Spaced(string sha256) => string.Join(" ", sha256.Chunk(16).Select(c => new string(c)));
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        IReadOnlyList<string> details =
        [
            Text.Format(Strings.FtpCertSubject, certificate.Subject),
            Text.Format(Strings.FtpCertIssuer, certificate.Issuer),
            Text.Format(Strings.FtpCertValidity, certificate.NotBefore.ToString("d", culture), certificate.NotAfter.ToString("d", culture)),
        ];
        var request = status == HostKeyStatus.Unknown
            ? new ConfirmRequest
            {
                Title = Strings.FtpCertificateTitle,
                Heading = Strings.FtpCertVerifyHeading,
                Subject = target.Address,
                Message = Text.Format(Strings.FtpCertUnknownMessage, certificate.Problem),
                Bullets = details,
                Codes = [(Strings.FtpCertFingerprint, Spaced(certificate.Sha256))],
                Kind = ConfirmKind.Warning,
                Actions = [Strings.HostKeyTrust],
                CancelLabel = Strings.HostKeyCancel,
            }
            : new ConfirmRequest
            {
                Title = Strings.FtpCertificateTitle,
                Banner = Strings.HostKeyChangedBanner,
                Heading = Strings.FtpCertChangedHeading,
                Subject = target.Address,
                Message = Text.Format(Strings.FtpCertChangedMessage, certificate.Problem),
                Bullets = details,
                Codes =
                [
                    (Strings.FtpCertOld, Spaced(KnownHosts.Known(_settings.KnownHosts, host, target.Port)?.Sha256 ?? "")),
                    (Strings.FtpCertNew, Spaced(certificate.Sha256)),
                ],
                Kind = ConfirmKind.Danger,
                Actions = [Strings.FtpCertReplace],
                DangerAction = 0,
                CancelLabel = Strings.HostKeyCancel,
                Acknowledge = Strings.HostKeyAckServer,
            };
        if (!ConfirmDialog.Confirm(this, request))
        {
            return false;
        }

        KnownHosts.Remember(_settings.KnownHosts, host, target.Port, "X.509", certificate.Sha256);
        SaveSettings();
        return true;
    }

    private bool ConfirmFtpCleartext(KeePassTarget target) =>
        ConfirmDialog.Confirm(this, new ConfirmRequest
        {
            Title = Strings.FtpCleartextTitle,
            Heading = Strings.FtpCleartextHeading,
            Subject = target.Address,
            Message = Strings.FtpCleartextMessage,
            Kind = ConfirmKind.Warning,
            Actions = [Strings.FtpCleartextAction],
            DangerAction = 0,
        });
}
