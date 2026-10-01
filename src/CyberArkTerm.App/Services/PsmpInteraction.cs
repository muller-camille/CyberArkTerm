using System.Windows;
using System.Windows.Threading;
using CyberArkTerm.App.Views;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Services;

/// <summary>
/// Questions posées par SSH.NET pendant la connexion au PSMP (clé d'hôte, mot de passe, code MFA),
/// relayées sur le thread de l'interface.
/// </summary>
internal sealed class PsmpInteraction(Window owner, AppSettings settings, Action saveSettings) : IPsmpInteraction
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

            string message = status == HostKeyStatus.Unknown
                ? $"Première connexion au PSMP {host}:{port}.\n\nEmpreinte de sa clé ({algorithm}) :\nSHA256:{sha256Fingerprint}\n\n" +
                  "Vérifiez-la auprès de l'équipe CyberArk si besoin. Faire confiance à ce serveur ?"
                : $"ATTENTION : la clé du PSMP {host}:{port} a changé !\n\nNouvelle empreinte ({algorithm}) :\nSHA256:{sha256Fingerprint}\n\n" +
                  "Cela peut indiquer une interception de la connexion. N'acceptez que si l'équipe CyberArk " +
                  "vous a confirmé ce changement. Accepter la nouvelle clé ?";
            var answer = MessageBox.Show(owner, message, "Clé du serveur PSMP", MessageBoxButton.YesNo,
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
            return dialog.ShowDialog() == true ? dialog.Answer : null;
        });
}
