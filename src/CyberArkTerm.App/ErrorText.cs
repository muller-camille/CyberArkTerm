using System.Net.Http;
using System.Security.Authentication;
using CyberArkTerm.Core;

namespace CyberArkTerm.App;

/// <summary>Traduit les exceptions réseau/API en messages compréhensibles pour l'utilisateur.</summary>
internal static class ErrorText
{
    public static string Describe(Exception e) => e switch
    {
        PvwaException p => p.Message,
        ArgumentException a => a.Message,
        TaskCanceledException or TimeoutException => "Le PVWA n'a pas répondu à temps.",
        HttpRequestException { InnerException: AuthenticationException } =>
            "Connexion TLS refusée : le certificat du PVWA n'est pas approuvé par ce poste.",
        HttpRequestException h => $"Impossible de joindre le PVWA : {h.Message}",
        _ => e.Message,
    };
}
