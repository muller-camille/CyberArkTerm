using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CyberArkTerm.App.Services.Rdp;

/// <summary>
/// Contrôle Bureau à distance de Windows (ActiveX de mstscax.dll, celui qu'utilise mstsc) hébergé dans WinForms.
/// Ses propriétés sont lues et écrites par IDispatch (<see cref="Dispatch"/>), sans assembly d'interopérabilité.
/// </summary>
internal sealed class RdpClientHost : AxHost
{
    // Classes « NotSafeForScripting » (pour une application, pas une page web), de la plus récente à la plus ancienne :
    // MsRdpClient10 (Windows 10), 9 (Windows 8.1), 8, 7.
    private static readonly string[] ClassIds =
    [
        "a0c63c30-f08d-4ab4-907c-34905d770c7d",
        "8b918b82-7985-4c24-89df-c33ad2bbfbcd",
        "a3bc03a0-041d-42e3-ad22-882b7865c9c5",
        "54d38bf7-b1ef-4479-9674-1bd6ea465258",
    ];

    private RdpClientHost(string classId)
        : base(classId)
    {
    }

    /// <summary>Vrai si le contrôle Bureau à distance est installé sur ce poste.</summary>
    public static bool IsAvailable => ClassIds.Any(IsRegistered);

    /// <summary>Objet COM du contrôle, une fois celui-ci créé (<see cref="Control.CreateControl()"/>).</summary>
    public object? Ocx => GetOcx();

    public static RdpClientHost Create() =>
        new(ClassIds.FirstOrDefault(IsRegistered) ?? throw new COMException("MsRdpClient", unchecked((int)0x80040154)));

    private static bool IsRegistered(string classId)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{{{classId}}}\InprocServer32");
            return key is not null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

/// <summary>Appels IDispatch sur un objet COM : propriétés et méthodes nommées.</summary>
internal static class Dispatch
{
    public static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture);

    public static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value], CultureInfo.InvariantCulture);

    public static object? Call(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args, CultureInfo.InvariantCulture);

    /// <summary>Première propriété disponible parmi <paramref name="names"/> (les plus récentes d'abord).</summary>
    public static object? First(object target, params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                if (Get(target, name) is { } value)
                {
                    return value;
                }
            }
            catch (Exception e) when (IsDispatchError(e))
            {
            }
        }

        return null;
    }

    /// <summary>Erreur renvoyée par un appel IDispatch (membre inconnu, valeur refusée...).</summary>
    public static bool IsDispatchError(Exception e) =>
        e is COMException or TargetInvocationException or MissingMemberException or ArgumentException or InvalidCastException
            or NotSupportedException;
}

/// <summary>Réglages étendus du contrôle (échelle d'affichage...) : interface qui n'est pas accessible par IDispatch.</summary>
[ComImport]
[Guid("302D8188-0052-4807-806A-362B628F9AC5")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IMsRdpExtendedSettings
{
    [DispId(1)]
    void SetProperty([MarshalAs(UnmanagedType.BStr)] string name, [MarshalAs(UnmanagedType.Struct)] ref object value);

    [DispId(1)]
    [return: MarshalAs(UnmanagedType.Struct)]
    object GetProperty([MarshalAs(UnmanagedType.BStr)] string name);
}

/// <summary>
/// Réglages « non scriptables » du contrôle (IMsRdpClientNonScriptable5, sans IDispatch) : seul l'emplacement de
/// DisableRemoteAppCapsCheck compte. Les 56 méthodes qui le précèdent dans la table (mots de passe, redirections,
/// multi-écran…) ne sont jamais appelées ; elles ne font que réserver leur place.
/// </summary>
[ComImport]
[Guid("4F6996D5-D7B1-412C-B0FF-063718566907")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMsRdpClientNonScriptable5
{
    void Reserved00();

    void Reserved01();

    void Reserved02();

    void Reserved03();

    void Reserved04();

    void Reserved05();

    void Reserved06();

    void Reserved07();

    void Reserved08();

    void Reserved09();

    void Reserved10();

    void Reserved11();

    void Reserved12();

    void Reserved13();

    void Reserved14();

    void Reserved15();

    void Reserved16();

    void Reserved17();

    void Reserved18();

    void Reserved19();

    void Reserved20();

    void Reserved21();

    void Reserved22();

    void Reserved23();

    void Reserved24();

    void Reserved25();

    void Reserved26();

    void Reserved27();

    void Reserved28();

    void Reserved29();

    void Reserved30();

    void Reserved31();

    void Reserved32();

    void Reserved33();

    void Reserved34();

    void Reserved35();

    void Reserved36();

    void Reserved37();

    void Reserved38();

    void Reserved39();

    void Reserved40();

    void Reserved41();

    void Reserved42();

    void Reserved43();

    void Reserved44();

    void Reserved45();

    void Reserved46();

    void Reserved47();

    void Reserved48();

    void Reserved49();

    void Reserved50();

    void Reserved51();

    void Reserved52();

    void Reserved53();

    void Reserved54();

    void Reserved55();

    void SetDisableRemoteAppCapsCheck([MarshalAs(UnmanagedType.VariantBool)] bool disable);
}
