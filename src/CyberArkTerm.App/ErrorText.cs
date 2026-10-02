using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;

namespace CyberArkTerm.App;

/// <summary>Traduit les exceptions réseau/API en messages compréhensibles pour l'utilisateur.</summary>
internal static class ErrorText
{
    public static string Describe(Exception e) => e switch
    {
        PvwaException p => p.Message,
        ArgumentException a => a.Message,
        TaskCanceledException or TimeoutException => Strings.ErrorPvwaTimeout,
        HttpRequestException { InnerException: AuthenticationException } => Strings.ErrorTlsRejected,
        HttpRequestException h => Text.Format(Strings.ErrorPvwaUnreachable, h.Message),
        Win32Exception w => Text.Format(Strings.ErrorLaunchClient, w.Message),
        FileNotFoundException f => f.Message,
        _ => e.Message,
    };
}
