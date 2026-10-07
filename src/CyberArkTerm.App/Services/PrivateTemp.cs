using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CyberArkTerm.App.Services;

/// <summary>
/// Dossier temporaire de l'application (copies en cours d'édition, fichiers .rdp et leur jeton PSM, archives à envoyer,
/// fichiers glissés vers l'Explorateur), réservé à l'utilisateur Windows : « %TEMP%\CyberArkTerm » s'il lui appartient
/// (ses droits sont alors limités à lui seul), sinon « %LOCALAPPDATA%\CyberArkTerm\Temp ». Un dossier créé d'avance par
/// un autre utilisateur (TEMP pointant vers un dossier partagé) n'est jamais utilisé.
/// </summary>
internal static class PrivateTemp
{
    private static readonly Lazy<string> RootPath = new(Prepare);

    public static string Root => RootPath.Value;

    public static string Combine(string name) => Path.Combine(Root, name);

    private static string Prepare()
    {
        var temp = Path.Combine(Path.GetTempPath(), "CyberArkTerm");
        if (TryRestrict(temp))
        {
            return temp;
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyberArkTerm", "Temp");
        TryRestrict(local);
        return local;
    }

    /// <summary>
    /// Crée le dossier s'il manque ; vrai s'il appartient à l'utilisateur et que ses droits ont été réservés à lui seul
    /// (les fichiers et dossiers qu'il contient en héritent).
    /// </summary>
    private static bool TryRestrict(string path)
    {
        try
        {
            var directory = Directory.CreateDirectory(path);
            if (!OperatingSystem.IsWindows())
            {
                return true;
            }

            using var identity = WindowsIdentity.GetCurrent();
            var me = identity.User;
            if (me is null || directory.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) != me)
            {
                return false;
            }

            var restricted = new DirectorySecurity();
            restricted.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            restricted.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(restricted);
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Core.Diagnostics.DebugLog.Write("temp", $"Droits du dossier temporaire {path} non réservés : {e.Message}");
            return false;
        }
    }
}
