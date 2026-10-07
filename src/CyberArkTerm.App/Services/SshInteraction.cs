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
/// <paramref name="context"/> : session qui pose la question (plusieurs peuvent s'ouvrir ensemble).
/// </summary>
internal sealed class SshInteraction(Window owner, AppSettings settings, Action saveSettings, bool direct = false, string? context = null)
    : ISshInteraction
{
    private Dispatcher Dispatcher => owner.Dispatcher;

    /// <summary>Les mêmes questions, nommant la session <paramref name="session"/> (« compte → cible »).</summary>
    public SshInteraction For(string session) => new(owner, settings, saveSettings, direct, session);

    public bool CheckHostKey(string host, int port, string algorithm, string sha256Fingerprint) =>
        Dispatcher.Invoke(() =>
        {
            var status = KnownHosts.Check(settings.KnownHosts, host, port, algorithm, sha256Fingerprint);
            if (status == HostKeyStatus.Trusted)
            {
                return true;
            }

            if (!AskHostKey(owner, direct, host, port, algorithm, sha256Fingerprint,
                    status == HostKeyStatus.Changed ? KnownHosts.Known(settings.KnownHosts, host, port) : null))
            {
                return false;
            }

            KnownHosts.Remember(settings.KnownHosts, host, port, algorithm, sha256Fingerprint);
            saveSettings();
            return true;
        });

    /// <summary>
    /// Clé d'hôte inconnue ou changée : empreinte à comparer (copiable), « Annuler la connexion » par défaut. Une clé
    /// changée montre l'ancienne et la nouvelle empreinte, et ne s'accepte qu'après avoir coché « J'ai confirmé… ».
    /// </summary>
    internal static bool AskHostKey(Window owner, bool direct, string host, int port, string algorithm, string sha256,
        (string Algorithm, string Sha256)? previous)
    {
        var receives = direct ? Strings.HostKeyServerReceives : Strings.HostKeyPsmpReceives;
        var title = direct ? Strings.HostKeyTitleServer : Strings.HostKeyTitle;
        var subject = $"{host}:{port}";
        if (previous is not { } old)
        {
            return ConfirmDialog.Confirm(owner, new ConfirmRequest
            {
                Title = title,
                Heading = direct ? Strings.HostKeyVerifyServer : Strings.HostKeyVerifyPsmp,
                Subject = subject,
                Message = direct ? Strings.HostKeyFirstServer : Strings.HostKeyFirstPsmp,
                Codes = [(Text.Format(Strings.HostKeyFingerprint, algorithm), "SHA256:" + sha256)],
                Bullets = [receives, Strings.HostKeyRemembered],
                Kind = ConfirmKind.Question,
                Actions = [Strings.HostKeyTrust],
                CancelLabel = Strings.HostKeyCancel,
            });
        }

        return ConfirmDialog.Confirm(owner, new ConfirmRequest
        {
            Title = title,
            Banner = Strings.HostKeyChangedBanner,
            Heading = direct ? Strings.HostKeyChangedServerHeading : Strings.HostKeyChangedPsmpHeading,
            Subject = subject,
            Message = direct ? Strings.HostKeyChangedServerMessage : Strings.HostKeyChangedPsmpMessage,
            Codes =
            [
                (Text.Format(Strings.HostKeyOld, old.Algorithm), "SHA256:" + old.Sha256),
                (Text.Format(Strings.HostKeyNew, algorithm), "SHA256:" + sha256),
            ],
            Kind = ConfirmKind.Danger,
            Actions = [Strings.HostKeyReplace],
            DangerAction = 0,
            CancelLabel = Strings.HostKeyCancel,
            Acknowledge = direct ? Strings.HostKeyAckServer : Strings.HostKeyAckPsmp,
        });
    }

    public string? Prompt(string instruction, string prompt, bool echo) =>
        Dispatcher.Invoke(() =>
        {
            // Aide selon la question : mot de passe (gardé pour les fichiers de l'onglet) ou code MFA (redemandé).
            string? hint = SshConnector.IsPasswordPrompt(prompt) && !echo
                ? (direct ? Strings.PromptHintPassword : Strings.PromptHintVaultPassword)
                : !direct && LooksLikeOneTimeCode(prompt) ? Strings.PromptHintMfa : null;
            var dialog = new PromptDialog(instruction, prompt, echo, context, hint, direct) { Owner = owner };
            if (direct)
            {
                dialog.Title = Strings.PromptTitleServer;
            }

            return dialog.ShowDialog() == true ? dialog.Answer : null;
        });

    private static bool LooksLikeOneTimeCode(string prompt) =>
        new[] { "code", "otp", "token", "passcode", "mfa", "radius" }.Any(w => prompt.Contains(w, StringComparison.OrdinalIgnoreCase));
}
