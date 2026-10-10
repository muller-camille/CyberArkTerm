using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
using ZillaTerm.Core.Localization;

namespace ZillaTerm.Core;

/// <summary>
/// Erreurs courantes du PVWA, des serveurs SSH et du réseau traduites en un message qui dit quoi faire. Le texte
/// d'origine (avec le code du PVWA) reste à la fin, pour le support. Null quand l'erreur n'a pas d'explication propre :
/// l'appelant montre alors le texte d'origine.
/// </summary>
public static class ServerErrorText
{
    public static string? Explain(Exception e) => e switch
    {
        PvwaException p => Explain(p),
        SshAuthenticationException => WithDetail(CoreStrings.SshAuthenticationRefused, e.Message),
        SshOperationTimeoutException => WithDetail(CoreStrings.NetworkTimeout, e.Message),
        SshConnectionException c => Explain(c),
        SocketException s => Advice(s.SocketErrorCode) is { } advice ? WithDetail(advice, s.Message) : null,
        // Coupure pendant une lecture ou une écriture (VNC, FTP) : le flux enveloppe l'erreur du réseau.
        IOException { InnerException: SocketException s } => Advice(s.SocketErrorCode) is { } advice ? WithDetail(advice, e.Message) : null,
        _ => null,
    };

    /// <summary>
    /// Refus du PVWA, reconnu à son code ou à son texte (en anglais, quelle que soit la langue du PVWA), sinon à son
    /// statut HTTP. Le challenge RADIUS et le composant de connexion inconnu ont leur propre fenêtre : null.
    /// </summary>
    public static string? Explain(PvwaException e)
    {
        if (e.IsRadiusChallenge || e.IsUnknownComponent)
        {
            return null;
        }

        var text = e.ServerMessage ?? "";
        // Réponse d'erreur de l'API (corps JSON), et refus de l'identification (codes ITATS) : un 403 ou un 404 sans
        // corps (page d'IIS, mauvaise adresse) ou à l'identification ne parle ni d'un safe ni d'un compte supprimé.
        bool api = e.ServerMessage is not null;
        bool logon = e.ErrorCode?.StartsWith("ITATS", StringComparison.OrdinalIgnoreCase) == true;
        string? advice =
            Is(e, "ITATS004E") || Has(text, "authentication failure") ? CoreStrings.PvwaBadCredentials
            : Has(text, "suspended") || (Has(text, "user") && Has(text, "disabled")) ? CoreStrings.PvwaUserSuspended
            : Has(text, "password") && !Has(text, "session") && (Has(text, "expired") || Has(text, "must change"))
                ? CoreStrings.PvwaPasswordExpired
            : Has(text, "dual control") || Has(text, "confirmation") ? CoreStrings.PvwaDualControl
            : Has(text, "ticket") ? CoreStrings.PvwaTicketRequired
            : Has(text, "reason") && (Has(text, "must") || Has(text, "required") || Has(text, "specify") || Has(text, "missing"))
                ? CoreStrings.PvwaReasonRequired
            : Has(text, "connection component") ? CoreStrings.PvwaComponentNotConfigured
            : Has(text, "already exists") ? CoreStrings.PvwaAlreadyExists
            : e.StatusCode switch
            {
                HttpStatusCode.Unauthorized when Has(text, "session") || Has(text, "token") => CoreStrings.PvwaSessionExpired,
                HttpStatusCode.Forbidden when api && !logon => CoreStrings.PvwaForbidden,
                HttpStatusCode.NotFound when api => CoreStrings.PvwaNotFound,
                >= HttpStatusCode.InternalServerError => CoreStrings.PvwaServerError,
                _ => null,
            };
        return advice is null ? null : string.Format(CultureInfo.CurrentCulture, CoreStrings.ErrorWithPvwaMessage, advice, e.Message);
    }

    private static string? Explain(SshConnectionException e)
    {
        string? advice = e.DisconnectReason switch
        {
            DisconnectReason.NoMoreAuthenticationMethodsAvailable or DisconnectReason.IllegalUserName => CoreStrings.SshAuthenticationRefused,
            DisconnectReason.ConnectionLost => CoreStrings.NetworkConnectionLost,
            // Fermée avant l'échange des versions : autre service sur ce port, ou serveur qui refuse d'emblée.
            _ when Has(e.Message, "identification") => CoreStrings.SshNoSshAnswer,
            _ when Has(e.Message, "not connected") || Has(e.Message, "aborted") => CoreStrings.NetworkConnectionLost,
            _ => null,
        };
        return advice is null ? null : WithDetail(advice, e.Message);
    }

    private static string? Advice(SocketError error) => error switch
    {
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => CoreStrings.NetworkHostNotFound,
        SocketError.ConnectionRefused => CoreStrings.NetworkRefused,
        SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable or SocketError.HostDown
            or SocketError.NetworkDown => CoreStrings.NetworkTimeout,
        SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown or SocketError.NetworkReset
            => CoreStrings.NetworkConnectionLost,
        _ => null,
    };

    private static string WithDetail(string advice, string detail) =>
        string.Format(CultureInfo.CurrentCulture, CoreStrings.ErrorWithDetail, advice, detail);

    private static bool Is(PvwaException e, string code) => string.Equals(e.ErrorCode, code, StringComparison.OrdinalIgnoreCase);

    private static bool Has(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);
}
