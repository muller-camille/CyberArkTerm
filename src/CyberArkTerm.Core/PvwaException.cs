using System.Net;

namespace CyberArkTerm.Core;

/// <summary>
/// Erreur renvoyée par l'API REST du PVWA (corps JSON <c>{ "ErrorCode": ..., "ErrorMessage": ... }</c>).
/// </summary>
public sealed class PvwaException : Exception
{
    /// <summary>Code renvoyé par le PVWA quand un serveur RADIUS demande une réponse à un challenge (OTP).</summary>
    public const string RadiusChallengeCode = "ITATS542I";

    /// <summary>
    /// Code renvoyé par le PVWA quand le composant de connexion PSM demandé n'existe pas pour la plateforme du compte
    /// (« Failed to get the relevant connection component »).
    /// </summary>
    public const string UnknownComponentCode = "EPVWA093E";

    public PvwaException(HttpStatusCode statusCode, string? errorCode, string message, string? serverMessage = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ErrorCode { get; }

    /// <summary>Texte brut renvoyé par le PVWA (par ex. la question d'un challenge RADIUS).</summary>
    public string? ServerMessage { get; }

    public bool IsRadiusChallenge => string.Equals(ErrorCode, RadiusChallengeCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>Composant de connexion PSM inconnu pour la plateforme du compte.</summary>
    public bool IsUnknownComponent => string.Equals(ErrorCode, UnknownComponentCode, StringComparison.OrdinalIgnoreCase);

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
}
