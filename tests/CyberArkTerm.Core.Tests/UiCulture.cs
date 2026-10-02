using System.Globalization;

namespace CyberArkTerm.Core.Tests;

/// <summary>Change la langue et les formats régionaux du test en cours ; les rétablit à la fin.</summary>
internal sealed class UiCulture : IDisposable
{
    private readonly CultureInfo _previousUi = CultureInfo.CurrentUICulture;
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    private UiCulture(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    public static UiCulture Use(string name) => new(name);

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _previousUi;
        CultureInfo.CurrentCulture = _previous;
    }
}
