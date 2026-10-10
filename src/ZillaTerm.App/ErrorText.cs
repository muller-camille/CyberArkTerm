using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using ZillaTerm.App.Localization;
using ZillaTerm.Core;

namespace ZillaTerm.App;

/// <summary>
/// Traduit les exceptions réseau/API en messages compréhensibles pour l'utilisateur. Les erreurs courantes du PVWA, du
/// SSH et du réseau disent quoi faire, le texte d'origine à la fin (<see cref="ServerErrorText"/>).
/// </summary>
internal static class ErrorText
{
    public static string Describe(Exception e) => ServerErrorText.Explain(e) ?? e switch
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
