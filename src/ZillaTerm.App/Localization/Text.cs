using ZillaTerm.Core.Localization;

namespace ZillaTerm.App.Localization;

/// <summary>
/// Mise en forme des textes traduits (nombres et dates selon les réglages régionaux de Windows), avec l'accord des mots
/// avec un nombre : « {0:# fichier|# fichiers} » (voir <see cref="PluralFormat"/>).
/// </summary>
internal static class Text
{
    public static string Format(string format, params object?[] args) => PluralFormat.Format(format, args);
}
