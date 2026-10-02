using System.Globalization;

namespace CyberArkTerm.App.Localization;

/// <summary>Mise en forme des textes traduits (nombres et dates selon les réglages régionaux de Windows).</summary>
internal static class Text
{
    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
