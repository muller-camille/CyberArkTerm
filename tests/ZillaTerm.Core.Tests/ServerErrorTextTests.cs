using System.Net;
using System.Net.Sockets;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;

namespace ZillaTerm.Core.Tests;

/// <summary>Erreurs du PVWA, du SSH et du réseau : un message traduit qui dit quoi faire, et le texte d'origine à la fin.</summary>
public class ServerErrorTextTests
{
    private static PvwaException Pvwa(HttpStatusCode status, string? code, string? message) =>
        new(status, code, message is null ? $"HTTP {(int)status}" : code is null ? message : $"{message} ({code})", message);

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "ITATS004E", "Authentication failure for User [jdoe].", "refuse l'identifiant ou le mot de passe")]
    [InlineData(HttpStatusCode.Forbidden, "ITATS002E", "Can't Logon. User [jdoe] is Suspended. Please contact your administrator.", "suspendu")]
    [InlineData(HttpStatusCode.Forbidden, null, "User password has expired.", "a expiré : changez-le")]
    [InlineData(HttpStatusCode.Forbidden, "PASWS204E", "You must specify a reason for this operation.", "exige un motif")]
    [InlineData(HttpStatusCode.Forbidden, null, "Ticketing system validation failed: ticket ID is missing.", "exige un ticket valide")]
    [InlineData(HttpStatusCode.Forbidden, null, "This account requires dual control confirmation.", "double validation")]
    [InlineData(HttpStatusCode.BadRequest, null, "Connection component PSM-RDP is not configured for platform WinServerLocal.", "Options avancées")]
    [InlineData(HttpStatusCode.Conflict, "PASWS027E", "Account already exists", "existe déjà")]
    [InlineData(HttpStatusCode.Unauthorized, "PASWS006E", "Session expired", "identifiez-vous de nouveau")]
    [InlineData(HttpStatusCode.Forbidden, "PASWS041E", "Not authorized.", "droits nécessaires")]
    [InlineData(HttpStatusCode.NotFound, null, "Account was not found.", "Introuvable dans CyberArk")]
    [InlineData(HttpStatusCode.InternalServerError, null, null, "réessayez dans un instant")]
    public void Pvwa_CommonErrors_AreExplainedAndKeepTheServerText(HttpStatusCode status, string? code, string? message, string expected)
    {
        using var _ = UiCulture.Use("fr-FR");
        var error = Pvwa(status, code, message);

        var text = ServerErrorText.Explain(error);

        Assert.NotNull(text);
        Assert.Contains(expected, text, StringComparison.Ordinal);
        // Le texte et le code du PVWA restent visibles pour le support.
        Assert.EndsWith("Message du PVWA : " + error.Message, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "The PVWA refused the user name or the password.", "PVWA message: ")]
    [InlineData("it-IT", "Il PVWA rifiuta il nome utente o la password.", "Messaggio del PVWA: ")]
    public void Pvwa_FollowsTheInterfaceLanguage(string culture, string advice, string detail)
    {
        using var _ = UiCulture.Use(culture);

        var text = ServerErrorText.Explain(Pvwa(HttpStatusCode.Forbidden, "ITATS004E", "Authentication failure for User [jdoe]."));

        Assert.StartsWith(advice, text, StringComparison.Ordinal);
        Assert.EndsWith(detail + "Authentication failure for User [jdoe]. (ITATS004E)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Pvwa_WithoutKnownCause_IsLeftToTheCaller()
    {
        // Message déjà traduit par ZillaTerm (réponse inattendue), RADIUS et composant inconnu : traités ailleurs.
        Assert.Null(ServerErrorText.Explain(new PvwaException(HttpStatusCode.OK, null, "Réponse vide du PVWA.")));
        Assert.Null(ServerErrorText.Explain(Pvwa(HttpStatusCode.InternalServerError, PvwaException.RadiusChallengeCode, "Enter the code")));
        Assert.Null(ServerErrorText.Explain(Pvwa(HttpStatusCode.InternalServerError, PvwaException.UnknownComponentCode,
            "Failed to get the relevant connection component (CAWS00001E)")));
        Assert.Null(ServerErrorText.Explain(Pvwa(HttpStatusCode.BadRequest, "PASWS999E", "Something unusual happened for an unknown reason.")));
        // À l'identification ou sans réponse de l'API, ni safe ni compte supprimé : le texte d'origine.
        Assert.Null(ServerErrorText.Explain(Pvwa(HttpStatusCode.Forbidden, "ITATS999E", "Logon is not allowed from this station.")));
        Assert.Null(ServerErrorText.Explain(Pvwa(HttpStatusCode.NotFound, null, null)));
        Assert.Null(ServerErrorText.Explain(Pvwa(HttpStatusCode.Unauthorized, null, null)));
    }

    [Theory]
    [InlineData(SocketError.HostNotFound, "Nom de serveur introuvable (DNS)")]
    [InlineData(SocketError.ConnectionRefused, "refuse la connexion sur ce port")]
    [InlineData(SocketError.TimedOut, "n'a pas répondu à temps")]
    [InlineData(SocketError.HostUnreachable, "n'a pas répondu à temps")]
    [InlineData(SocketError.ConnectionReset, "coupée par le serveur ou le réseau")]
    public void Network_CommonErrors_AreExplained(SocketError error, string expected)
    {
        using var _ = UiCulture.Use("fr-FR");
        var exception = new SocketException((int)error);

        var text = ServerErrorText.Explain(exception);

        Assert.NotNull(text);
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.EndsWith("Détail : " + exception.Message, text, StringComparison.Ordinal);
        // Une coupure enveloppée dans le flux (VNC, FTP) est reconnue de même.
        Assert.Contains(expected, ServerErrorText.Explain(new IOException("Unable to read data.", exception)), StringComparison.Ordinal);
    }

    [Fact]
    public void Ssh_CommonErrors_AreExplained()
    {
        using var _ = UiCulture.Use("fr-FR");

        Assert.Equal("Le serveur SSH refuse l'authentification. Vérifiez le mot de passe (et Verr. Maj) avant de réessayer ; via le PSMP, "
                     + "c'est votre mot de passe CyberArk, et plusieurs échecs suspendent votre compte. Détail : Permission denied (password).",
            ServerErrorText.Explain(new SshAuthenticationException("Permission denied (password).")));
        Assert.StartsWith("Le serveur n'a pas répondu à temps",
            ServerErrorText.Explain(new SshOperationTimeoutException("Connection failed to establish within 30000 milliseconds.")), StringComparison.Ordinal);
        Assert.StartsWith("La connexion a été coupée",
            ServerErrorText.Explain(new SshConnectionException("An established connection was aborted by the server.", DisconnectReason.ConnectionLost)),
            StringComparison.Ordinal);
        Assert.StartsWith("Le serveur a fermé la connexion sans répondre en SSH",
            ServerErrorText.Explain(new SshConnectionException("The connection to the remote server was closed before a valid SSH identification string was received.")),
            StringComparison.Ordinal);
        Assert.StartsWith("Le serveur SSH refuse l'authentification",
            ServerErrorText.Explain(new SshConnectionException("Too many authentication failures", DisconnectReason.NoMoreAuthenticationMethodsAvailable)),
            StringComparison.Ordinal);
        Assert.Null(ServerErrorText.Explain(new SshConnectionException("No matching key exchange algorithm (server offers diffie-hellman-group1-sha1)")));
        Assert.Null(ServerErrorText.Explain(new InvalidOperationException("x")));
    }
}
