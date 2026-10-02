using System.Diagnostics;
using System.IO;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Localization;

namespace CyberArkTerm.App.Services;

/// <summary>Ouvre les sessions : fichier RDP PSM dans mstsc, SSH via PSMP dans Windows Terminal ou une console.</summary>
internal sealed class SessionLauncher
{
    // Le fichier RDP contient un jeton de connexion à usage unique : on le supprime dès que mstsc l'a lu.
    private static readonly TimeSpan RdpFileLifetime = TimeSpan.FromSeconds(60);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CyberArkTerm");

    public void LaunchRdp(byte[] rdpFile, string label)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{SafeFileName(label)}-{Guid.NewGuid():N}.rdp");
        File.WriteAllBytes(path, rdpFile);
        try
        {
            Start(new ProcessStartInfo("mstsc.exe") { ArgumentList = { path }, UseShellExecute = false });
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        _ = DeleteLaterAsync(path);
    }

    /// <summary>
    /// Lance <c>ssh -l &lt;login&gt; -p &lt;port&gt; &lt;psmp&gt;</c>, dans un nouvel onglet Windows Terminal si disponible.
    /// </summary>
    public void LaunchSsh(string login, string psmpHost, int port, string title)
    {
        PsmpTarget.Validate(login, CoreStrings.SshLoginWhat, allowSpaces: true);
        PsmpTarget.Validate(psmpHost, CoreStrings.PsmpAddressWhat);
        var ssh = FindSsh() ?? throw new FileNotFoundException(Strings.SshClientNotFound);
        string[] sshArgs = ["-l", login, "-p", port.ToString(System.Globalization.CultureInfo.InvariantCulture), psmpHost];

        // Identifiant avec espace : ssh.exe est lancé directement, sans passer par wt.exe, pour que l'argument
        // arrive entier à ssh quelle que soit la façon dont Windows Terminal recompose sa ligne de commande.
        var terminal = login.Contains(' ') ? null : FindWindowsTerminal();
        ProcessStartInfo info;
        if (terminal is not null)
        {
            // « ; » sépare les commandes de wt.exe : on l'exclut du titre (les autres arguments sont validés).
            info = new ProcessStartInfo(terminal) { UseShellExecute = false };
            foreach (var arg in (string[])["-w", "0", "new-tab", "--title", title.Replace(";", " "), ssh, .. sshArgs])
            {
                info.ArgumentList.Add(arg);
            }
        }
        else
        {
            // Application console lancée depuis une application graphique : Windows lui ouvre sa propre fenêtre.
            info = new ProcessStartInfo(ssh) { UseShellExecute = false };
            foreach (var arg in sshArgs)
            {
                info.ArgumentList.Add(arg);
            }
        }

        Start(info);
    }

    /// <summary>Supprime les fichiers RDP qui n'auraient pas encore été effacés.</summary>
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                foreach (var file in Directory.EnumerateFiles(_directory, "*.rdp"))
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Start(ProcessStartInfo info)
    {
        using var process = Process.Start(info);
    }

    private static string? FindSsh()
    {
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
        return File.Exists(system) ? system : FindOnPath("ssh.exe");
    }

    private static string? FindWindowsTerminal()
    {
        // wt.exe est un alias d'exécution d'application placé dans %LOCALAPPDATA%\Microsoft\WindowsApps.
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(alias) ? alias : FindOnPath("wt.exe");
    }

    private static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), exe))
            .FirstOrDefault(File.Exists);

    private static async Task DeleteLaterAsync(string path)
    {
        await Task.Delay(RdpFileLifetime).ConfigureAwait(false);
        TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Nom de fichier en ASCII simple (lettres, chiffres, « . », « - », « _ ») : sûr pour tout système de fichiers.</summary>
    private static string SafeFileName(string label)
    {
        var clean = new string(label.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray()).Trim('.');
        return clean.Length > 60 ? clean[..60] : clean;
    }
}
