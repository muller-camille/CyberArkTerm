using System.Windows;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Views;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Services;

/// <summary>
/// Questions posées par SSH.NET pendant la connexion (clé d'hôte, mot de passe, code MFA), relayées sur le thread de
/// l'interface. <paramref name="direct"/> : serveur joint directement (accès d'urgence) plutôt que le PSMP.
/// </summary>
internal sealed class SshInteraction(Window owner, AppSettings settings, Action saveSettings, bool direct = false) : ISshInteraction
{
    private Dispatcher Dispatcher => owner.Dispatcher;

    public bool CheckHostKey(string host, int port, string algorithm, string sha256Fingerprint) =>
        Dispatcher.Invoke(() =>
        {
            var status = KnownHosts.Check(settings.KnownHosts, host, port, algorithm, sha256Fingerprint);
            if (status == HostKeyStatus.Trusted)
            {
                return true;
            }

            var text = status == HostKeyStatus.Unknown
                ? (direct ? Strings.HostKeyUnknownServer : Strings.HostKeyUnknown)
                : (direct ? Strings.HostKeyChangedServer : Strings.HostKeyChanged);
            string message = Text.Format(text, host, port, algorithm, sha256Fingerprint);
            var answer = MessageBox.Show(owner, message, direct ? Strings.HostKeyTitleServer : Strings.HostKeyTitle, MessageBoxButton.YesNo,
                status == HostKeyStatus.Unknown ? MessageBoxImage.Question : MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return false;
            }

            KnownHosts.Remember(settings.KnownHosts, host, port, algorithm, sha256Fingerprint);
            saveSettings();
            return true;
        });

    public string? Prompt(string instruction, string prompt, bool echo) =>
        Dispatcher.Invoke(() =>
        {
            var dialog = new PromptDialog(instruction, prompt, echo) { Owner = owner };
            if (direct)
            {
                dialog.Title = Strings.PromptTitleServer;
            }

            return dialog.ShowDialog() == true ? dialog.Answer : null;
        });
}
