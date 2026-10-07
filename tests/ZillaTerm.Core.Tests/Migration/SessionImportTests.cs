using System.Text.Json;
using ZillaTerm.Core.Migration;

namespace ZillaTerm.Core.Tests.Migration;

/// <summary>Correspondance avec les comptes du PVWA, import dans « Mes serveurs » et export du résultat.</summary>
public sealed class SessionImportTests
{
    private const string Pvwa = "pvwa.corp.local";

    private static PvwaAccount Account(string id, string user, string address, string platform, string? domain = null, string? machines = null,
        bool restricted = false) => new()
    {
        Id = id,
        UserName = user,
        Address = address,
        PlatformId = platform,
        SafeName = "Safe",
        PlatformAccountProperties = domain is null ? null : new() { ["LogonDomain"] = JsonSerializer.SerializeToElement(domain) },
        RemoteMachinesAccess = machines is null ? null : new() { RemoteMachines = machines, AccessRestrictedToRemoteMachines = restricted },
    };

    private static readonly PvwaAccount Root = Account("1", "root", "web01.corp.local", "UnixSSH");
    private static readonly PvwaAccount Oracle = Account("2", "oracle", "web01.corp.local", "Oracle");
    private static readonly PvwaAccount LocalAdmin = Account("3", "admin", "dc01.corp.local", "WinServerLocal");
    private static readonly PvwaAccount DomainAdmin = Account("4", "admin", "corp.local", "WinDomain", "corp.local");
    private static readonly PvwaAccount ShortRoot = Account("5", "root", "app01", "UnixSSH");
    private static readonly PvwaAccount Deploy = Account("6", "deploy", "app01.corp.local", "UnixSSH");
    private static readonly PvwaAccount Service = Account("7", "svc", "corp.local", "WinDomain", "corp.local", "srv-a;srv-b.corp.local", restricted: true);
    private static readonly PvwaAccount MixedRoot = Account("8", "root", "mixed01", "UnixSSH");
    private static readonly PvwaAccount MixedAdmin = Account("9", "Administrator", "mixed01", "WinServerLocal");
    private static readonly PvwaAccount ByIp = Account("10", "root", "10.0.0.5", "LinuxSSH");
    private static readonly PvwaAccount Transfer = Account("11", "transfer", "sftp01.corp.local", "UnixSFTP");

    private static readonly PvwaAccount[] Accounts =
        [Root, Oracle, LocalAdmin, DomainAdmin, ShortRoot, Deploy, Service, MixedRoot, MixedAdmin, ByIp, Transfer];

    private static SessionMatcher Matcher() => new(Accounts, KnownDomains.From(Accounts.Select(a => a.Address), []));

    private static ImportedSession Ssh(string host, string? user = null, string folder = "", string name = "") =>
        ImportedSession.Terminal(folder, name, ImportProtocol.Ssh, host, null, user);

    private static ImportedSession Rdp(string host, string? user = null, string folder = "", string name = "") =>
        ImportedSession.Rdp(folder, name, host, user);

    private static string[] Ids(ImportedSession session) => Matcher().Candidates(session).Select(c => c.Account.Id).ToArray();

    [Fact]
    public void FindsTheAccountOfTheServer()
    {
        Assert.Equal(["1"], Ids(Ssh("web01.corp.local", "root")));
        Assert.Equal(["1"], Ids(Ssh("WEB01.corp.local.", "ROOT")));

        // Nom court d'un côté, nom complet de l'autre : même serveur. Deux noms complets différents : non.
        Assert.Equal(["1"], Ids(Ssh("web01", "root")));
        Assert.Empty(Ids(Ssh("web01.paris.corp", "root")));
        Assert.Equal(["10"], Ids(Ssh("10.0.0.5", "root")));

        // Jamais un compte de base de données ; sans utilisateur, tous les comptes possibles du serveur.
        Assert.Empty(Ids(Ssh("web01.corp.local", "oracle")));
        Assert.Equal(["6", "5"], Ids(Ssh("app01.corp.local")));
    }

    [Fact]
    public void PrefersAccountsOfTheRightKind()
    {
        Assert.Equal(["9"], Ids(Rdp("mixed01")));
        Assert.Equal(["8"], Ids(Ssh("mixed01")));

        // Aucun compte du bon type : un compte d'un autre type plutôt que rien.
        Assert.Equal(["9"], Ids(Ssh("mixed01", "Administrator")));
    }

    [Fact]
    public void UsesDomainAccountsWithTheServerAsTargetMachine()
    {
        var matcher = Matcher();
        var candidates = matcher.Candidates(Rdp("srv77.corp.local", "CORP\\admin"));
        var domain = Assert.Single(candidates);
        Assert.Equal(("4", "srv77.corp.local"), (domain.Account.Id, domain.RemoteMachine));
        Assert.Equal("admin@corp.local → srv77.corp.local", domain.Display);
        Assert.Equal("WinDomain · Safe", domain.Details);

        // Compte local du serveur sans domaine indiqué ; .\admin ou DC01\admin : le compte local seulement.
        Assert.Equal(["3"], Ids(Rdp("dc01.corp.local", "admin")));
        Assert.Equal(["3"], Ids(Rdp("dc01.corp.local", ".\\admin")));
        Assert.Equal(["3"], Ids(Rdp("dc01.corp.local", "DC01\\admin")));

        // CORP\admin sur dc01 : le compte de domaine, pas le compte local du même nom.
        Assert.Equal(["4"], Ids(Rdp("dc01.corp.local", "CORP\\admin")));
        Assert.Equal(["4"], Ids(Rdp("dc01.corp.local", "admin@corp.local")));

        // Compte de domaine limité à ses machines.
        Assert.Empty(Ids(Rdp("srv-c", "svc")));
        Assert.Equal(["7"], Ids(Rdp("srv-a", "svc")));
        Assert.Equal(["7"], Ids(Rdp("srv-b", "svc")));
    }

    [Fact]
    public void ImportsIntoMyServersThroughPsmOrPsmp()
    {
        var settings = new AppSettings { PsmpAddress = "psmp.corp.local" };
        var import = new SessionImport(settings, Pvwa, Matcher(),
        [
            Ssh("web01.corp.local", "root", "Prod/Web", "web front"),
            Rdp("srv77.corp.local", "CORP\\admin", "Prod/Windows", "srv77"),
            ImportedSession.Terminal("Logs", "app", ImportProtocol.Sftp, "app01.corp.local", null, "deploy"),
            ImportedSession.Rdp("", "via psm", "psm01", "jdoe", startProgram: "psm /u admin /a dc01.corp.local /c PSM-RDP-Custom"),
            Ssh("unknown01", "root", "", "inconnu"),
            ImportedSession.Unsupported("", "vnc", "VNC", "vnc01"),
            Ssh("app01.corp.local", null, "", "à vérifier"),
        ], "Importés");

        Assert.Equal(
            [ImportState.Ready, ImportState.Ready, ImportState.Ready, ImportState.Ready, ImportState.NoAccount, ImportState.Unsupported, ImportState.Check],
            import.Items.Select(i => i.State));
        Assert.Equal("Importés/Prod/Web", import.Items[0].TargetFolder);
        using (UiCulture.Use("fr-FR"))
        {
            Assert.Equal("Non importé : aucun compte dans le PVWA pour root@unknown01", import.Items[4].StateText);
        }

        // Le compte choisi pour une session ambiguë peut être changé ; décocher exclut la session.
        var check = import.Items[6];
        Assert.Equal("6", check.Chosen!.Account.Id);
        import.Choose(check, check.Candidates[1]);
        Assert.Equal("5", check.Chosen!.Account.Id);
        import.Items[2].Include = false;

        Assert.Equal(4, import.Apply());
        Assert.True(import.Applied);
        Assert.Equal(ImportState.Excluded, import.Items[2].State);

        var web = import.Items[0].Created!;
        Assert.Equal(("1", "web front", "Importés/Prod/Web", ConnectMode.Ssh, Pvwa), (web.AccountId, web.Name, web.Folder, web.Mode, web.PvwaHost));

        var srv77 = import.Items[1].Created!;
        Assert.Equal(("4", "srv77.corp.local", ConnectMode.Psm, (string?)null), (srv77.AccountId, srv77.RemoteMachine, srv77.Mode, srv77.Component));

        // Session qui passait par PSM : compte et serveur cibles, composant gardé.
        var viaPsm = import.Items[3].Created!;
        Assert.Equal(("3", ConnectMode.Psm, "PSM-RDP-Custom", "via psm"), (viaPsm.AccountId, viaPsm.Mode, viaPsm.Component, viaPsm.Name));

        Assert.Contains("Importés/Prod/Windows", settings.SessionFolderList);
        Assert.Equal(4, settings.Sessions.Count);

        // Compte d'une plateforme « SFTP » : fichiers seuls via le PSMP, même pour une session SSH.
        var sftp = new SessionImport(settings, Pvwa, Matcher(), [Ssh("sftp01.corp.local", "transfer")]);
        Assert.Equal(ConnectMode.Sftp, sftp.Items[0].Mode);

        // Second import des mêmes sessions : rien en double.
        var again = new SessionImport(settings, Pvwa, Matcher(), [Ssh("web01.corp.local", "root", "Prod/Web", "web front")], "Importés");
        Assert.Equal(ImportState.AlreadyPresent, again.Items[0].State);
        Assert.Equal(0, again.Apply());
        using (UiCulture.Use("en-US"))
        {
            Assert.Equal("Already in My servers, folder Importés/Prod/Web", again.Items[0].StateText);
        }
    }

    [Fact]
    public void FallsBackToPsmWithoutPsmp()
    {
        var import = new SessionImport(new AppSettings(), Pvwa, Matcher(),
        [
            Ssh("web01.corp.local", "root"),
            ImportedSession.Terminal("", "files", ImportProtocol.Sftp, "app01.corp.local", null, "deploy"),
            ImportedSession.Terminal("", "telnet", ImportProtocol.Telnet, "web01.corp.local", null, "root"),
        ]);

        Assert.Equal((ConnectMode.Psm, (string?)null), (import.Items[0].Mode, import.Items[0].Component));
        Assert.Equal((ConnectMode.Psm, SessionImport.PsmWinScp), (import.Items[1].Mode, import.Items[1].Component));
        Assert.Equal((ConnectMode.Psm, SessionImport.PsmTelnet), (import.Items[2].Mode, import.Items[2].Component));
        Assert.Equal("PSM (PSM-WinSCP)", import.Items[1].ConnectionText);
    }

    [Fact]
    public void AddsDuplicatesOfTheSameImportOnlyOnce()
    {
        var settings = new AppSettings();
        var import = new SessionImport(settings, Pvwa, Matcher(),
        [
            Ssh("web01.corp.local", "root", "A", "one"),
            Ssh("web01", "root", "A", "two"),
            Ssh("web01", "root", "B", "three"),
        ]);

        Assert.Equal(2, import.Apply());
        Assert.Equal([ImportState.Imported, ImportState.AlreadyPresent, ImportState.Imported], import.Items.Select(i => i.State));
        Assert.Equal(["A", "B"], settings.Sessions.Select(s => s.Folder));
    }

    [Fact]
    public void ExportsTheResultAsCsv()
    {
        var import = new SessionImport(new AppSettings(), Pvwa, Matcher(),
        [
            ImportedSession.Terminal("Prod", "web", ImportProtocol.Ssh, "web01.corp.local", 22, "root"),
            ImportedSession.Terminal("", "=cmd", ImportProtocol.Ssh, "unknown01", 2222, "root"),
        ], "Import");
        import.Apply();

        using var _ = UiCulture.Use("fr-FR");
        using var writer = new StringWriter();
        import.WriteCsv(writer, ';');
        var lines = writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Dossier;Nom;Protocole;Serveur;Utilisateur;Compte PVWA;Safe;Connexion;Dossier dans Mes serveurs;Résultat", lines[0]);
        Assert.Equal("Prod;web;SSH;web01.corp.local;root;root@web01.corp.local;Safe;PSM;Import/Prod;Importé", lines[1]);

        // Pas d'interprétation comme formule par Excel ; port indiqué s'il n'est pas celui par défaut.
        Assert.StartsWith(";'=cmd;SSH;unknown01:2222;root;;;;;", lines[2]);
        Assert.EndsWith("Non importé : aucun compte dans le PVWA pour root@unknown01", lines[2]);
    }
}
