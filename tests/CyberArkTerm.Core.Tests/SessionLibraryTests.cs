namespace CyberArkTerm.Core.Tests;

public class SessionLibraryTests
{
    private static PvwaAccount Account(string id, string user = "root", string address = "srv", string platform = "UnixSSH") =>
        new() { Id = id, UserName = user, Address = address, PlatformId = platform, SafeName = "S" };

    [Theory]
    [InlineData(" Prod / Web/ ", "Prod/Web")]
    [InlineData("\\a\\\\b", "a/b")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizesFolderPaths(string? path, string expected) => Assert.Equal(expected, SessionFolders.Normalize(path));

    [Fact]
    public void FolderHelpers()
    {
        Assert.Equal("Prod", SessionFolders.Parent("Prod/Web"));
        Assert.Equal("", SessionFolders.Parent("Prod"));
        Assert.Equal("Web", SessionFolders.Name("Prod/Web"));
        Assert.True(SessionFolders.IsWithin("Prod/Web/Front", "prod"));
        Assert.False(SessionFolders.IsWithin("Production", "Prod"));
        Assert.Equal("Recette/Web/Front", SessionFolders.Rebase("Prod/Web/Front", "Prod", "Recette"));
    }

    [Fact]
    public void BuildsSortedTreeWithEmptyFoldersAndHostFilter()
    {
        var settings = new AppSettings();
        SessionLibrary.AddFolder(settings, "Prod/Web");
        SessionLibrary.AddFolder(settings, "Admin");
        SessionLibrary.AddSession(settings, Account("2", address: "web02"), "pvwa", "Prod/Web");
        SessionLibrary.AddSession(settings, Account("1", address: "web01"), "pvwa", "prod/web");
        SessionLibrary.AddSession(settings, Account("3", address: "db01"), "pvwa", "");
        SessionLibrary.AddSession(settings, Account("4", address: "other"), "autre-pvwa", "");

        var root = SessionLibrary.BuildTree(settings, "PVWA");

        Assert.Equal(["Admin", "Prod"], root.Folders.Select(f => f.Name));
        Assert.Equal(["root@db01"], root.Sessions.Select(s => s.Name));
        var web = root.Folders[1].Folders.Single();
        Assert.Equal("Prod/Web", web.Path);
        Assert.Equal(["root@web01", "root@web02"], web.Sessions.Select(s => s.Name));
        Assert.Equal(3, root.TotalSessions);
        Assert.Empty(root.Folders[0].Sessions);
    }

    [Fact]
    public void RenameFolderMovesSubfoldersAndSessions()
    {
        var settings = new AppSettings();
        var session = SessionLibrary.AddSession(settings, Account("1"), "pvwa", "Prod/Web/Front");
        SessionLibrary.AddFolder(settings, "Prod/Batch");

        var renamed = SessionLibrary.RenameFolder(settings, "Prod", "Production");

        Assert.Equal("Production", renamed);
        Assert.Equal("Production/Web/Front", session.Folder);
        Assert.Contains("Production/Batch", settings.SessionFolderList);
        Assert.DoesNotContain(settings.SessionFolderList, f => f.StartsWith("Prod/", StringComparison.Ordinal) || f == "Prod");
    }

    [Fact]
    public void MoveFolderRefusesToMoveIntoItself()
    {
        var settings = new AppSettings();
        SessionLibrary.AddFolder(settings, "A/B");

        Assert.Throws<ArgumentException>(() => SessionLibrary.MoveFolder(settings, "A", "A/B/C"));
        SessionLibrary.MoveFolder(settings, "A/B", "B");
        Assert.Contains("B", settings.SessionFolderList);
        Assert.DoesNotContain("A/B", settings.SessionFolderList);
    }

    [Fact]
    public void DeleteFolderRemovesContent()
    {
        var settings = new AppSettings();
        SessionLibrary.AddSession(settings, Account("1"), "pvwa", "Prod/Web");
        SessionLibrary.AddSession(settings, Account("2"), "pvwa", "Prod");
        SessionLibrary.AddSession(settings, Account("3"), "pvwa", "Recette");

        int removed = SessionLibrary.DeleteFolder(settings, "Prod");

        Assert.Equal(2, removed);
        Assert.Equal(["Recette"], settings.SessionFolderList);
        Assert.Single(settings.Sessions);
        Assert.Throws<ArgumentException>(() => SessionLibrary.DeleteFolder(settings, ""));
    }

    [Fact]
    public void NewSessionCopiesAccountAndPicksModeByPlatform()
    {
        var settings = new AppSettings();

        var unix = SessionLibrary.AddSession(settings, Account("1", platform: "UnixSSH"), "pvwa", "");
        var win = SessionLibrary.AddSession(settings, Account("2", "admin", "srv-win", "WinServerLocal"), "pvwa", "Windows");

        Assert.Equal(ConnectMode.Ssh, unix.Mode);
        Assert.Equal(ConnectMode.Psm, win.Mode);
        Assert.Equal("admin@srv-win", win.Name);
        Assert.Equal("WinServerLocal", win.PlatformId);
        Assert.Contains("Windows", settings.SessionFolderList);
        Assert.NotEqual(unix.Id, win.Id);
    }

    /// <summary>
    /// Connexion récente ajoutée aux « Courants » : même mode, même composant PSM, même machine cible, même nom que dans
    /// la liste des connexions récentes.
    /// </summary>
    [Fact]
    public void RecentConnectionKeepsItsConfiguration()
    {
        var settings = new AppSettings();
        var domain = Account("2", "adm-t0", "corp.local", "WinDomain");

        var psm = SessionLibrary.AddFromRecent(settings, domain,
            new RecentSession { AccountId = "2", Label = "adm-t0@srv01", Mode = " WIN-PSM ", RemoteMachine = " srv01 " }, "pvwa", "Prod");
        var ssh = SessionLibrary.AddFromRecent(settings, Account("1", platform: "WinServerLocal"),
            new RecentSession { AccountId = "1", Label = "root@srv", Mode = "ssh" }, "pvwa", "");
        var unnamed = SessionLibrary.AddFromRecent(settings, Account("3", address: "db01"),
            new RecentSession { AccountId = "3", Mode = "PSM-RDP" }, "pvwa", "");

        Assert.Equal((ConnectMode.Psm, "WIN-PSM", "srv01", "adm-t0@srv01", "Prod"), (psm.Mode, psm.Component, psm.RemoteMachine, psm.Name, psm.Folder));
        Assert.Equal(("2", "pvwa", "WinDomain"), (psm.AccountId, psm.PvwaHost, psm.PlatformId));
        Assert.Equal((ConnectMode.Ssh, (string?)null, (string?)null), (ssh.Mode, ssh.Component, ssh.RemoteMachine));
        Assert.Equal("root@db01", unnamed.Name);
        Assert.Contains("Prod", settings.SessionFolderList);
        Assert.Equal(3, settings.Sessions.Count);
    }

    /// <summary>
    /// Recherche dans « Courants » : seuls les serveurs qui répondent à tous les mots et leurs dossiers restent (pas les
    /// dossiers vides) ; le nom du dossier, le composant et la machine cible comptent.
    /// </summary>
    [Fact]
    public void SearchKeepsMatchingServersAndTheirFolders()
    {
        var settings = new AppSettings();
        SessionLibrary.AddFolder(settings, "Vide");
        SessionLibrary.AddSession(settings, Account("1", address: "web01"), "pvwa", "Prod/Web");
        SessionLibrary.AddSession(settings, Account("2", address: "db01"), "pvwa", "Prod/Db");
        SessionLibrary.AddSession(settings, Account("3", address: "web02"), "pvwa", "Recette");
        SessionLibrary.AddSession(settings, Account("4", address: "web03"), "autre-pvwa", "Prod");
        var domain = SessionLibrary.AddSession(settings, Account("5", "adm", "corp.local", "WinDomain"), "pvwa", "");
        domain.Component = "WIN-PSM";
        domain.RemoteMachine = "srv-app01";

        var web = SessionLibrary.BuildTree(settings, "pvwa", "WEB");
        var prodWeb = SessionLibrary.BuildTree(settings, "pvwa", "prod web");
        var byMachine = SessionLibrary.BuildTree(settings, "pvwa", "app01 win-psm");
        var none = SessionLibrary.BuildTree(settings, "pvwa", "inconnu");
        var all = SessionLibrary.BuildTree(settings, "pvwa", "  ");

        Assert.Equal(["Prod", "Recette"], web.Folders.Select(f => f.Name));
        Assert.Equal(["root@web01"], web.Folders[0].Folders.Single().Sessions.Select(s => s.Name));
        Assert.Equal(2, web.TotalSessions);
        Assert.Equal("Prod/Web", prodWeb.Folders.Single().Folders.Single().Path);
        Assert.Equal(["adm@corp.local"], byMachine.Sessions.Select(s => s.Name));
        Assert.Empty(byMachine.Folders);
        Assert.Equal(0, none.TotalSessions);
        Assert.Empty(none.Folders);
        Assert.Contains(all.Folders, f => f.Name == "Vide");
        Assert.Equal(4, all.TotalSessions);
    }

    [Fact]
    public void FavoritesAreMigratedOnce()
    {
        var settings = new AppSettings { Favorites = ["1", "missing"] };
        var accounts = new Dictionary<string, PvwaAccount> { ["1"] = Account("1") };

        SessionLibrary.MigrateFavorites(settings, accounts, "pvwa");
        SessionLibrary.MigrateFavorites(settings, accounts, "pvwa");

        Assert.Single(settings.Sessions);
        Assert.Empty(settings.Favorites);
    }

    [Fact]
    public void SessionsSurviveSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cat-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings();
            var s = SessionLibrary.AddSession(settings, Account("1"), "pvwa", "Prod");
            s.StartDirectory = "/opt/appli";
            s.Reason = "Maintenance";
            settings.KnownHosts["psmp:22"] = "ssh-ed25519 SHA256:abc";
            settings.Save(path);

            var loaded = AppSettings.Load(path);

            var copy = Assert.Single(loaded.Sessions);
            Assert.Equal(("Prod", "/opt/appli", "Maintenance", ConnectMode.Ssh), (copy.Folder, copy.StartDirectory, copy.Reason, copy.Mode));
            Assert.Equal("ssh-ed25519 SHA256:abc", loaded.KnownHosts["psmp:22"]);
            Assert.Equal(CyberArkTerm.Core.Ssh.TransferProtocol.Sftp, loaded.PreferredUploadProtocol);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
