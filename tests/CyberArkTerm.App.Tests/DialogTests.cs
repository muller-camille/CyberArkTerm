using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Views;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;
using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.App.Tests;

/// <summary>
/// Les fenêtres s'ouvrent avec le thème de l'application : une erreur au chargement du XAML (ressource manquante,
/// gestionnaire appelé avant que ses champs existent) les ferait planter chez l'utilisateur.
/// </summary>
[Collection(WpfCollection.Name)]
public sealed class DialogTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AccountWindowsOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var create = new AccountDialog(["Prod"], ["WinDomain"], "Prod", "WinDomain", "CORP", (_, _) => Task.FromResult(new PvwaAccount()));
            Assert.Equal(("Prod", "CORP"), (create.SafeBox.Text, create.DomainBox.Text));
            Assert.True(create.CpmBox.IsChecked);
            Assert.False(create.ReasonBox.IsEnabled);
            create.CpmBox.IsChecked = false;
            Assert.True(create.ReasonBox.IsEnabled);
            create.Close();

            var account = JsonSerializer.Deserialize<PvwaAccount>("""
                {"id":"1","name":"Op-srv01","address":"srv01","userName":"svc","platformId":"WinDomain","safeName":"Prod",
                 "secretManagement":{"automaticManagementEnabled":false,"manualManagementReason":"Compte applicatif"}}
                """, Web)!;
            var edit = new AccountDialog(account, ["WinDomain"], (_, _) => Task.FromResult(account));
            Assert.False(edit.SafeBox.IsEnabled);
            Assert.Equal(Visibility.Collapsed, edit.PasswordBox.Visibility);
            Assert.Equal(("srv01", "Compte applicatif"), (edit.AddressBox.Text, edit.ReasonBox.Text));
            Assert.True(edit.ReasonBox.IsEnabled);
            edit.Close();
        });
    }

    [Fact]
    public void PasswordAndSafeMembersWindowsOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var retrieve = new RetrievePasswordDialog("svc@srv01", TimeSpan.FromSeconds(20), "Incident 42",
                (_, _) => Task.FromResult(Array.Empty<char>()), _ => true);
            Assert.Equal("Incident 42", retrieve.ReasonBox.Text);
            retrieve.Close();

            new SafeMembersDialog("Prod", _ => Task.FromResult(new List<SafeMember>())).Close();
        });
    }

    [Fact]
    public void SafeMemberWindowsOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var add = new SafeMemberDialog("Prod", (_, _) => Task.CompletedTask);
            Assert.Equal(Visibility.Visible, add.SearchInBox.Visibility);
            Assert.Equal("Vault", add.SearchInBox.Text);
            Assert.Equal((2, 3), (add.LeftGroups.Children.Count, add.RightGroups.Children.Count));
            add.Close();

            var member = JsonSerializer.Deserialize<SafeMember>("""
                {"memberName":"Unix Admins","memberType":"Group","membershipExpirationDate":1767225600,
                 "permissions":{"listAccounts":true,"addAccounts":true}}
                """, Web)!;
            var edit = new SafeMemberDialog("Prod", member, (_, _) => Task.CompletedTask);
            Assert.True(edit.NameBox.IsReadOnly);
            Assert.False(edit.TypeBox.IsEnabled);
            Assert.Equal(1, edit.TypeBox.SelectedIndex);
            Assert.Equal(Visibility.Collapsed, edit.SearchInBox.Visibility);
            Assert.NotNull(edit.UntilBox.SelectedDate);
            edit.Close();

            var actions = new SafeMemberActions((_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
            new SafeMembersDialog("Prod", _ => Task.FromResult(new List<SafeMember>()), actions).Close();
        });
    }

    [Fact]
    public void ImportWindowOpens()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var choose = new ImportAccountsDialog(["Prod"], ["WinDomain"], "Prod", "WinDomain");
            Assert.Equal(("Prod", "WinDomain"), (choose.SafeBox.Text, choose.PlatformBox.Text));
            Assert.False(choose.ImportButton.IsEnabled);
            choose.Close();
            Assert.Null(choose.Confirmed);
        });
    }

    /// <summary>
    /// Import sur un PVWA fictif : une ligne créée, une refusée, une incomplète, puis la session expire ; les lignes
    /// suivantes ne sont pas envoyées, les mots de passe sont effacés et le résultat CSV ne les contient pas.
    /// </summary>
    [Fact]
    public void ImportShowsEachLineAndSavesTheResult()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string csv = "safe,platform,address,userName,password\n"
            + "Prod,WinDomain,srv01,adm1,Secret-1\n"
            + "Prod,WinDomain,srv02,dup,\n"
            + "Prod,WinDomain,,adm3,\n"
            + "Prod,WinDomain,srv04,expire,Secret-4\n"
            + "Prod,WinDomain,srv05,adm5,Secret-5\n";
        var import = AccountCsv.Parse(csv, null, null);
        var sent = new List<string>();
        Task<PvwaAccount> Create(NewAccount account, CancellationToken ct)
        {
            sent.Add(account.UserName);
            return account.UserName switch
            {
                "dup" => Task.FromException<PvwaAccount>(new PvwaException(HttpStatusCode.BadRequest, "PASWS027E", "Account already exists")),
                "expire" => Task.FromException<PvwaAccount>(new PvwaException(HttpStatusCode.Unauthorized, null, "Session expired")),
                _ => Task.FromResult(new PvwaAccount { Id = "id-" + account.UserName }),
            };
        }

        var path = Path.Combine(Path.GetTempPath(), $"cat-import-{Guid.NewGuid():N}.csv");
        try
        {
            RunWithTheme(() =>
            {
                string? asked = null;
                var progress = new ImportProgressDialog(import, Create) { AskToSave = summary => { asked = summary; return false; } };
                Assert.Equal(8, progress.RowsGrid.Columns.Count);

                // Les PVWA fictifs répondent tout de suite : l'import se termine sans boucle de messages.
                progress.RunAsync().GetAwaiter().GetResult();

                Assert.Equal(["adm1", "dup", "expire"], sent);
                Assert.Equal(
                    [ImportOutcome.Created, ImportOutcome.Refused, ImportOutcome.NotImported, ImportOutcome.NotSent, ImportOutcome.NotSent],
                    progress.Rows.Select(r => r.Outcome));
                Assert.Equal(("id-adm1", "Account already exists"), (import.Rows[0].AccountId, import.Rows[1].Detail));
                Assert.Equal(1, progress.Created);
                Assert.True(progress.SessionExpired);
                Assert.NotNull(asked);

                // Session expirée : la fenêtre s'est fermée et a effacé tous les mots de passe.
                Assert.All(import.Rows, r => Assert.True(r.Account?.Secret is null || r.Account.Secret.All(c => c == '\0')));

                progress.WriteResult(path);
            });

            var result = File.ReadAllText(path);
            Assert.Equal(6, result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Contains("id-adm1", result);
            Assert.Contains("Account already exists", result);
            Assert.DoesNotContain("Secret-", result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Vérification d'un transfert : fichiers à revoir en premier (différent, puis non vérifié), bilan sur deux lignes,
    /// motif d'un fichier non vérifié.
    /// </summary>
    [Fact]
    public void TransferChecksWindowOpens()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            byte[] hash = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
            var ok = new TransferCheck("a", @"C:\a", "/srv/a", 10, hash, 10, [.. hash]) { Upload = true };
            var different = ok with { Name = "b", RemotePath = "/srv/b", RemoteSha256 = new byte[32] };
            var unverified = ok with { Name = "c", RemotePath = "/srv/c", RemoteLength = -1, RemoteSha256 = [], Error = "Permission denied" };

            var dialog = new TransferChecksDialog([ok, different, unverified]);

            Assert.Equal(6, dialog.ChecksGrid.Columns.Count);
            Assert.Equal(["/srv/b", "/srv/c", "/srv/a"], ((IEnumerable<TransferCheck>)dialog.ChecksGrid.ItemsSource).Select(c => c.RemotePath));
            Assert.Equal(2, dialog.HeadingText.Text.Split(Environment.NewLine).Length);
            Assert.Contains("Permission denied", TransferChecksDialog.Result(unverified));
            Assert.NotEqual(TransferChecksDialog.Result(ok), TransferChecksDialog.Result(different));
            Assert.Single(TransferChecksDialog.Heading([ok]).Split(Environment.NewLine));
            dialog.Close();
        });
    }

    /// <summary>
    /// Panneau de la file des transferts : visible pendant la série, élément en cours dans la barre d'état, élément en
    /// attente retiré, puis bilan unique à la fin.
    /// </summary>
    [Fact]
    public void TransferQueuePanelFollowsTheQueue()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            // Sans contexte de synchronisation, la fin d'un transfert se poursuit sur ce fil (pas de boucle de messages ici).
            SynchronizationContext.SetSynchronizationContext(null);
            var panel = new FileBrowserPanel();
            Assert.Equal(Visibility.Collapsed, panel.QueuePanel.Visibility);
            var release = new TaskCompletionSource();
            var running = new TransferItem(true, "deploy/", "/opt/app", async (_, _) => await release.Task) { Protocol = "SCP", FileCount = 12 };
            var waiting = new TransferItem(false, "app.log", @"C:\Temp", (_, _) => Task.CompletedTask);

            panel.Queue.Enqueue(running);
            panel.Queue.Enqueue(waiting);

            Assert.Equal(Visibility.Visible, panel.QueuePanel.Visibility);
            Assert.Equal(2, panel.ActiveTransfers(null));
            Assert.Contains("deploy/", panel.StatusText.Text);
            Assert.Contains("(1/12)", panel.StatusText.Text);
            Assert.Contains(Text.Format(Strings.QueuePending, 1), panel.StatusText.Text);
            Assert.Equal(Strings.QueueStateWaiting, TransferStatusConverter.StateText(waiting));

            panel.Queue.Cancel(waiting);
            release.SetResult();

            Assert.Equal((TransferState.Done, TransferState.Cancelled), (running.State, waiting.State));
            Assert.Equal(Visibility.Collapsed, panel.QueuePanel.Visibility);
            Assert.Equal(0, panel.ActiveTransfers(null));
            Assert.Contains(Text.Format(Strings.QueueSummaryCancelled, 1), panel.StatusText.Text);

            // Historique : l'envoi terminé y figure ; l'élément retiré avant d'avoir commencé, non.
            var record = Assert.Single(panel.History.Records);
            Assert.Equal(("deploy/", true, TransferState.Done, "SCP"), (record.Label, record.Upload, record.State, record.Protocol));
        });
    }

    /// <summary>Onglet Fichiers : tri par colonne (« .. » et dossiers en tête), flèche dans l'en-tête, réglage enregistré.</summary>
    [Fact]
    public void FileListSortsByTheClickedColumn()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var settings = new AppSettings();
            int saved = 0;
            var panel = new FileBrowserPanel();
            panel.Initialize(settings, () => saved++, new TransferHistory());
            Assert.Equal(false, (panel.NameColumn.Header as FrameworkElement)?.Tag); // croissant
            RemoteEntry E(string name, bool dir, long size, int day) =>
                new(name, "/opt/" + name, dir, false, size, new DateTime(2026, 10, day), dir ? "drwxr-xr-x" : "-rw-r--r--");
            var big = E("big.tar", false, 9000, 1);
            panel.FileList.ItemsSource = new List<RemoteEntry>
            {
                RemoteEntry.ParentLink("/opt"), E("app", true, 4096, 2), E("a.log", false, 10, 9), big, E("z.conf", false, 200, 5),
            };
            panel.FileList.SelectedItems.Add(big);

            panel.SortBy(RemoteSortColumn.Modified, descending: true);

            Assert.Equal(["..", "app", "a.log", "z.conf", "big.tar"],
                panel.FileList.Items.Cast<RemoteEntry>().Select(e => e.Name));
            Assert.Equal(true, (panel.ModifiedColumn.Header as FrameworkElement)?.Tag); // décroissant
            Assert.Equal(CyberArkTerm.Core.Localization.CoreStrings.ColumnName, panel.NameColumn.Header);
            Assert.Same(big, Assert.Single(panel.FileList.SelectedItems.Cast<RemoteEntry>()));
            Assert.Equal((RemoteSortColumn.Modified, true, 1), (settings.FileSortColumn, settings.FileSortDescending, saved));

            panel.SortBy(RemoteSortColumn.Size, descending: false);
            Assert.Equal(["..", "app", "a.log", "z.conf", "big.tar"],
                panel.FileList.Items.Cast<RemoteEntry>().Select(e => e.Name));
        });
    }

    /// <summary>
    /// Archive .tar.gz envoyée : encadré bien visible avec la commande d'extraction de sa session (une seule ligne, sans
    /// retour à la ligne), qui reste jusqu'à ce qu'on le ferme ou que la session se ferme.
    /// </summary>
    [Fact]
    public void ArchiveExtractBoxShowsTheCommandOfItsSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var (srv01, _) = NewSshView("root@srv01");
            var (srv02, _) = NewSshView("root@srv02");
            var panel = new FileBrowserPanel();
            panel.Attach(srv01);
            Assert.Equal(Visibility.Collapsed, panel.ExtractPanel.Visibility);

            const string command = "cd '/opt/app' && gzip -dc './deploy.tar.gz' | tar -xf - && rm -f './deploy.tar.gz'";
            var archive = new TransferItem(true, "deploy.tar.gz", "/opt/app", (item, _) =>
            {
                item.ExtractCommand = command;
                return Task.CompletedTask;
            }) { Owner = srv01 };
            panel.Queue.Enqueue(archive);

            Assert.Equal(Visibility.Visible, panel.ExtractPanel.Visibility);
            Assert.Equal(command, panel.ExtractCommandBox.Text);
            Assert.Equal(Text.Format(Strings.ArchiveToExtractOne, "/opt/app", "root@srv01"), panel.ExtractTitle.Text);
            // Session non connectée : rien ne peut être écrit dans son terminal.
            Assert.False(panel.InsertExtractButton.IsEnabled);

            // L'encadré suit la session affichée.
            panel.Attach(srv02);
            Assert.Equal(Visibility.Collapsed, panel.ExtractPanel.Visibility);
            panel.Attach(srv01);
            Assert.Equal(Visibility.Visible, panel.ExtractPanel.Visibility);

            panel.ReleaseTails(srv01);
            Assert.Equal(Visibility.Collapsed, panel.ExtractPanel.Visibility);
            srv01.Dispose();
            srv02.Dispose();
        });
    }

    /// <summary>Historique : filtre par sens, résultat lisible, sommes et dossier disponibles pour un téléchargement.</summary>
    [Fact]
    public void TransferHistoryWindowOpens()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            byte[] hash = [1, 2, 3];
            var history = new TransferHistory();
            history.Add(new TransferRecord
            {
                Upload = false, Label = "app.log", Destination = Path.GetTempPath(), State = TransferState.Done, FileCount = 1,
                Files = [new TransferCheck("app.log", @"C:\Temp\app.log", "/var/log/app.log", 3, hash, 3, [.. hash])],
            });
            history.Add(new TransferRecord { Upload = true, Label = "deploy/", Destination = "/opt/app", State = TransferState.Failed, Error = "permission refusée" });
            int saved = 0;

            var dialog = new TransferHistoryDialog(history, () => saved++);

            Assert.Equal(8, dialog.RecordsGrid.Columns.Count);
            Assert.Contains(dialog.RecordsGrid.Columns, c => Equals(c.Header, Strings.HistoryColProtocol));
            Assert.Equal(2, ((IEnumerable<TransferRecord>)dialog.RecordsGrid.ItemsSource).Count());
            dialog.FilterBox.SelectedIndex = 2;
            var download = Assert.Single((IEnumerable<TransferRecord>)dialog.RecordsGrid.ItemsSource);
            dialog.RecordsGrid.SelectedItem = download;
            Assert.True(dialog.ChecksButton.IsEnabled);
            Assert.True(dialog.OpenFolderButton.IsEnabled);
            Assert.Equal(Text.Format(Strings.HistoryDone, 1), TransferHistoryDialog.Result(download));
            Assert.Equal("✗ permission refusée", TransferHistoryDialog.Result(history.Records[0]));
            Assert.Equal(0, saved);
            dialog.Close();
        });
    }

    /// <summary>
    /// Gros envoi : la proposition d'archive donne le nombre de fichiers, la taille et la destination ; l'option des
    /// Paramètres (case et seuil) est lue et enregistrée ; l'état « archive » s'affiche dans la file.
    /// </summary>
    [Fact]
    public void ArchiveOfferAndSettingOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var offer = new ArchiveOfferDialog(2345, 5L * 1024 * 1024, "/opt/app");
            Assert.Contains("/opt/app", offer.MessageText.Text);
            Assert.Contains(RemotePath.FormatSize(5L * 1024 * 1024), offer.MessageText.Text);
            Assert.False(offer.UseArchive);
            Assert.False(offer.DontOfferAgain);
            Assert.Equal(Text.Format(Strings.ArchiveOfferArchive, "._tar.gz"), offer.ArchiveButton.Content);
            offer.Close();

            // gzip absent du serveur : archive .tar, et la fenêtre le dit.
            var plain = new ArchiveOfferDialog(2345, 5L * 1024 * 1024, "/opt/app", compressed: false);
            Assert.Contains(Strings.ArchiveOfferNoGzip, plain.MessageText.Text);
            Assert.DoesNotContain(".tar.gz", plain.MessageText.Text);
            Assert.Equal(Text.Format(Strings.ArchiveOfferArchive, "._tar"), plain.ArchiveButton.Content);
            plain.Close();

            var settings = new AppSettings { OfferArchive = true, ArchiveThreshold = 500 };
            var dialog = new SettingsDialog(settings);
            Assert.Equal((true, "500"), (dialog.ArchiveBox.IsChecked, dialog.ArchiveThresholdBox.Text));
            dialog.Close();

            var item = new TransferItem(true, "deploy.tar.gz (2345)", "/opt/app", (_, _) => Task.CompletedTask);
            item.Report(new TransferProgress("deploy.tar.gz", 50, 200, Packing: true));
            Assert.True(item.Packing);
        });
    }

    /// <summary>Suivi d'un fichier : nouvelles lignes, filtre, exclusion, contexte, recherche, repère.</summary>
    [Fact]
    public void TailWindowFollowsFiltersAndSearches()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var link = new MemoryLink("root@srv01");
            var file = link.Add("/var/log/app.log");
            file.Append("a\nERROR disque\nb\nc\nd\nerror réseau\n");
            var window = new TailWindow(new AppSettings(), () => { });
            window.AddFeed(link, "/var/log/app.log");
            Assert.Contains("/var/log/app.log", window.Title);

            window.PollAsync().GetAwaiter().GetResult();
            Assert.Equal("a\nERROR disque\nb\nc\nd\nerror réseau\n", window.Shown);
            Assert.Equal(RemotePath.FormatSize(file.Length), window.Feeds[0].Status);

            window.FilterBox.Text = "error";
            Assert.Equal("ERROR disque\nerror réseau\n", window.Shown);
            window.ExcludeBox.Text = "DISQUE";
            Assert.Equal("error réseau\n", window.Shown);
            window.ExcludeBox.Text = "";

            // Contexte d'une ligne, comme grep -C 1.
            window.ContextBox.SelectedItem = 1;
            Assert.Equal("a\nERROR disque\nb\n--\nd\nerror réseau\n", window.Shown);
            window.ContextBox.SelectedItem = 0;

            // Expression régulière ; une expression invalide n'est pas appliquée.
            window.FilterBox.Text = "r(é|e)seau$";
            window.RegexBox.IsChecked = true;
            window.RegexBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal("error réseau\n", window.Shown);
            window.FilterBox.Text = "(";
            Assert.Equal(6, window.Shown.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            window.FilterBox.Text = "";

            // Les lignes qui arrivent passent par le filtre.
            window.FilterBox.Text = "error";
            file.Append("info\nERROR base\n");
            window.PollAsync().GetAwaiter().GetResult();
            Assert.EndsWith("error réseau\nERROR base\n", window.Shown);
            window.FilterBox.Text = "";

            // Recherche sans filtrer : sélection de la ligne trouvée, la vue ne suit plus la fin.
            window.SearchBox.Text = "réseau";
            window.FindNext(1);
            Assert.Equal(5, window.LogList.SelectedIndex);
            Assert.False(window.FollowBox.IsChecked);
            Assert.Equal(Text.Format(Strings.TailSearchCount, 1, 1), window.SearchCount.Text);
            window.SearchBox.Text = "introuvable";
            window.FindNext(1);
            Assert.Equal(Strings.TailSearchNone, window.SearchCount.Text);

            window.AddUserMarker();
            Assert.Matches("—— \\d\\d:\\d\\d:\\d\\d ——\n$", window.Shown);
            window.Close();
            Assert.True(link.Disposed);
        });
    }

    /// <summary>Alertes : seulement sur les nouvelles lignes ; la notification ne contient pas la ligne.</summary>
    [Fact]
    public void TailWindowRaisesAlerts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var link = new MemoryLink("root@srv01");
            var file = link.Add("/var/log/app.log");
            file.Append("java.lang.OutOfMemoryError: ancien\n");
            var notes = new List<string>();
            var window = new TailWindow(new AppSettings { TailAlerts = "OutOfMemory, refused" }, () => { }) { Notifier = (_, text) => notes.Add(text) };
            window.AddFeed(link, "/var/log/app.log");
            window.PollAsync().GetAwaiter().GetResult();
            Assert.Equal(0, window.AlertCount);

            file.Append("Connection REFUSED by db01\nok\njava.lang.OutOfMemoryError: Java heap space\n");
            window.PollAsync().GetAwaiter().GetResult();
            Assert.Equal(2, window.AlertCount);
            Assert.Equal(Visibility.Visible, window.AlertButton.Visibility);
            var note = Assert.Single(notes);
            Assert.Contains("app.log", note);
            Assert.DoesNotContain("heap", note);
            Assert.DoesNotContain("db01", note);

            // Une seule notification par période, même si d'autres alertes arrivent.
            file.Append("refused again\n");
            window.PollAsync().GetAwaiter().GetResult();
            Assert.Equal(3, window.AlertCount);
            Assert.Single(notes);
            window.Close();
        });
    }

    /// <summary>Vue combinée, connexion perdue puis reprise, fin de session, enregistrement.</summary>
    [Fact]
    public void TailWindowCombinesResumesAndRecords()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var connector = new CyberArkTerm.Core.Ssh.SshConnector("127.0.0.1", 22, "root", new NoInteraction());
            var session = new CyberArkTerm.App.Services.SshSession(null, "root@srv02", connector,
                System.Windows.Threading.Dispatcher.CurrentDispatcher, followTerminal: false, saved: null);
            var first = new MemoryLink("root@srv01");
            var second = new MemoryLink("root@srv02", session);
            first.Add("/var/log/app.log").Append("un\n");
            var other = second.Add("/var/log/other.log");
            other.Append("deux\n");
            var window = new TailWindow(new AppSettings(), () => { });
            window.AddFeed(first, "/var/log/app.log");
            window.AddFeed(second, "/var/log/other.log");
            Assert.Equal(Text.Format(Strings.TailTitleMany, 2), window.Title);
            window.PollAsync().GetAwaiter().GetResult();
            Assert.Contains("[root@srv01 app.log] un\n", window.Shown);
            Assert.Contains("[root@srv02 other.log] deux\n", window.Shown);

            var directory = Path.Combine(Path.GetTempPath(), $"cat-tail-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var record = Path.Combine(directory, "record.log");
                window.StartRecording(record);

                // Connexion perdue : repère, attente ; reconnexion : les lignes écrites entre-temps arrivent.
                second.Connected = false;
                window.PollAsync().GetAwaiter().GetResult();
                Assert.Contains("[root@srv02 other.log] " + Strings.TailLostMarker, window.Shown);
                Assert.Equal(Strings.TailWaiting, window.Feeds[1].Status);
                Assert.Equal(Visibility.Visible, window.ReconnectButton.Visibility);
                other.Append("trois\n");
                second.Connected = true;
                second.Generation++;
                window.PollAsync().GetAwaiter().GetResult();
                Assert.EndsWith("[root@srv02 other.log] " + Strings.TailResumedMarker + "\n[root@srv02 other.log] trois\n", window.Shown);

                // Session fermée : le fichier n'est plus suivi, les lignes restent.
                window.EndSession(session);
                Assert.True(second.Disposed);
                Assert.Equal(Strings.TailSessionClosed, window.Feeds[1].Status);
                Assert.Contains(Strings.TailSessionClosedMarker, window.Shown);

                var save = Path.Combine(directory, "save.log");
                window.SaveTo(save);
                Assert.Equal(window.Shown.TrimEnd('\n').Split('\n'), File.ReadAllLines(save));

                window.Close();
                var recorded = File.ReadAllText(record);
                Assert.StartsWith("[root@srv01 app.log] un\r\n[root@srv02 other.log] deux\r\n", recorded);
                Assert.Contains("[root@srv02 other.log] trois\r\n", recorded);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
                session.Dispose();
            }
        });
    }

    /// <summary>Couleur du niveau, mots surlignés et préfixe du fichier dans le texte affiché d'une ligne.</summary>
    [Fact]
    public void TailLinesAreColoured()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var feed = new TailFeed(new MemoryLink("root@srv01"), "/var/log/app.log", TailBrushes.Sources[0]);
            var style = new TailStyle(["db01"], null, Colors: true, Prefixes: true, Wrap: false);
            var block = new System.Windows.Controls.TextBlock();
            TailRowText.SetRow(block, new TailRow(new TailLine("12:00 ERROR db01 down", feed, TailLevel.Error, false), TailShownKind.Line, style));
            Assert.Same(TailBrushes.Error, block.Foreground);
            var runs = block.Inlines.OfType<System.Windows.Documents.Run>().ToList();
            Assert.Equal("[root@srv01 app.log] ", runs[0].Text);
            Assert.Contains(runs, r => r.Text == "db01" && ReferenceEquals(r.Background, TailBrushes.Highlight));

            TailRowText.SetRow(block, new TailRow(new TailLine("12:00 INFO ok", feed, TailLevel.None, false), TailShownKind.Line, style with { Colors = false }));
            Assert.NotSame(TailBrushes.Error, block.Foreground);

            var settings = new AppSettings { TailIndependentSession = true };
            var dialog = new SettingsDialog(settings);
            Assert.True(dialog.TailSessionBox.IsChecked);
            dialog.Close();
        });
    }

    /// <summary>Onglet détaché : le terminal passe dans la fenêtre séparée, puis en ressort pour revenir dans l'onglet.</summary>
    [Fact]
    public void DetachedWindowHoldsTheTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var connector = new CyberArkTerm.Core.Ssh.SshConnector("127.0.0.1", 22, "root", new NoInteraction());
            var session = new CyberArkTerm.App.Services.SshSession(null, "root@srv01", connector,
                System.Windows.Threading.Dispatcher.CurrentDispatcher, followTerminal: false, saved: null);
            var view = new SshSessionView(session, "root@srv01", "…");

            var window = new DetachedSessionWindow(view, session.Label);
            Assert.Same(view, window.Content);
            Assert.Contains("root@srv01", window.Title);

            Assert.Same(view, window.TakeView());
            Assert.Null(window.Content);
            window.SessionClosed = true;
            window.Close();
            session.Dispose();
        });
    }

    private static (CyberArkTerm.App.Services.SshSession Session, SshSessionView View) NewSshView(string label)
    {
        var connector = new CyberArkTerm.Core.Ssh.SshConnector("127.0.0.1", 22, "root", new NoInteraction());
        var session = new CyberArkTerm.App.Services.SshSession(null, label, connector,
            System.Windows.Threading.Dispatcher.CurrentDispatcher, followTerminal: false, saved: null);
        return (session, new SshSessionView(session, label, "…"));
    }

    /// <summary>Vue parallèle : grille, saisie simultanée vers les sessions cochées, encodage par session, collage, limite.</summary>
    [Fact]
    public void ParallelViewSendsTypingToTheTickedSessions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var views = Enumerable.Range(1, 9).Select(i => NewSshView($"root@srv0{i}")).ToList();
            var sent = new List<(string Label, string Text)>();
            (int Lines, int Sessions)? asked = null;
            bool answer = false;
            var parallel = new ParallelView
            {
                IsConnected = _ => true,
                Deliver = (view, input) => sent.Add((view.Session.Label, input.Encode(view.Session.Emulator))),
                ConfirmPaste = (lines, sessions) =>
                {
                    asked = (lines, sessions);
                    return answer;
                },
            };
            foreach (var (_, view) in views.Take(3))
            {
                parallel.Add(view);
            }

            Assert.Equal((1, 3), (parallel.PanesGrid.RowDefinitions.Count, parallel.PanesGrid.ColumnDefinitions.Count));
            Assert.Equal(Visibility.Collapsed, parallel.BroadcastBanner.Visibility);
            var (s1, v1) = views[0];
            var (s2, v2) = views[1];
            var (s3, v3) = views[2];

            // Sans saisie simultanée : seulement la session où l'on tape.
            v1.InputRouter!(v1, TerminalInput.Typed("uptime\r"));
            Assert.Equal([("root@srv01", "uptime\r")], sent);

            // Avec : toutes les sessions cochées, chacune avec l'encodage de son terminal (vim dans srv02).
            parallel.Broadcast = true;
            Assert.Equal(Visibility.Visible, parallel.BroadcastBanner.Visibility);
            Assert.Contains("root@srv03", parallel.BroadcastText.Text);
            s2.Emulator.Feed("\x1b[?1h");
            sent.Clear();
            v1.InputRouter!(v1, TerminalInput.Special(CyberArkTerm.Core.Terminal.TerminalKey.Up));
            Assert.Equal([("root@srv01", "\x1b[A"), ("root@srv02", "\x1bOA"), ("root@srv03", "\x1b[A")], sent);

            // Session décochée : exclue, et ce qu'on y tape ne va qu'à elle.
            parallel.SetIncluded(s3, false);
            Assert.DoesNotContain("root@srv03", parallel.BroadcastText.Text);
            sent.Clear();
            v2.InputRouter!(v2, TerminalInput.Typed("id\r"));
            Assert.Equal(["root@srv01", "root@srv02"], sent.Select(x => x.Label));
            sent.Clear();
            v3.InputRouter!(v3, TerminalInput.Typed("w\r"));
            Assert.Equal(["root@srv03"], sent.Select(x => x.Label));

            // Collage de plusieurs lignes vers plusieurs sessions : confirmation ; refusé, rien n'est envoyé.
            sent.Clear();
            v1.InputRouter!(v1, TerminalInput.Pasted("cd /tmp\nls"));
            Assert.Equal((2, 2), asked);
            Assert.Empty(sent);
            answer = true;
            v1.InputRouter!(v1, TerminalInput.Pasted("cd /tmp\nls"));
            Assert.Equal(2, sent.Count);

            // Ajoutée pendant la saisie simultanée : pas cochée.
            parallel.Add(views[3].View);
            Assert.False(parallel.IsIncluded(views[3].Session));
            Assert.Equal((2, 2), (parallel.PanesGrid.RowDefinitions.Count, parallel.PanesGrid.ColumnDefinitions.Count));

            // Au plus 8 sessions.
            foreach (var (_, view) in views.Skip(4))
            {
                parallel.Add(view);
            }

            Assert.Equal(8, parallel.Sessions.Count);
            Assert.True(parallel.IsFull);
            Assert.False(parallel.Contains(views[8].Session));

            // Une seule session à l'écran, puis toutes.
            parallel.ToggleZoom(s2);
            Assert.Equal((1, 1), (parallel.PanesGrid.RowDefinitions.Count, parallel.PanesGrid.ColumnDefinitions.Count));
            parallel.ToggleZoom(s2);
            Assert.Equal((2, 4), (parallel.PanesGrid.RowDefinitions.Count, parallel.PanesGrid.ColumnDefinitions.Count));

            // Retirée : le terminal est rendu, sans aiguillage de la saisie.
            Assert.Same(v1, parallel.Remove(s1));
            Assert.Null(v1.InputRouter);
            Assert.Null(v1.Parent);
            Assert.Equal(7, parallel.Sessions.Count);

            foreach (var (session, _) in views)
            {
                session.Dispose();
            }
        });
    }

    /// <summary>Choix des sessions de la vue parallèle : au plus 8 cochées.</summary>
    [Fact]
    public void ParallelDialogKeepsAtMostEightSessions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var sessions = Enumerable.Range(1, 9).Select(i => NewSshView($"root@srv0{i}").Session).ToList();
            var dialog = new ParallelDialog(sessions, sessions);
            Assert.Equal(sessions.Take(8), dialog.Selected);
            var boxes = dialog.SessionsPanel.Children.OfType<System.Windows.Controls.CheckBox>().ToList();
            Assert.False(boxes[8].IsEnabled);
            Assert.Contains("8", dialog.CountText.Text);
            dialog.Close();

            dialog = new ParallelDialog(sessions, [sessions[2]]);
            Assert.Equal([sessions[2]], dialog.Selected);
            Assert.True(dialog.SessionsPanel.Children.OfType<System.Windows.Controls.CheckBox>().All(b => b.IsEnabled));
            dialog.Close();
            foreach (var session in sessions)
            {
                session.Dispose();
            }
        });
    }

    /// <summary>Comparaison : lignes côte à côte, navigation, seulement les différences ; binaire par somme SHA-256.</summary>
    [Fact]
    public void CompareWindowShowsTheDifferences()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var left = System.Text.Encoding.UTF8.GetBytes("listen 80;\nserver_name a;\nroot /var/www;\n" + string.Concat(Enumerable.Range(1, 30).Select(i => $"l{i}\n")) + "}\n");
            var right = System.Text.Encoding.UTF8.GetBytes("listen 443;\nserver_name a;\nroot /var/www;\n" + string.Concat(Enumerable.Range(1, 30).Select(i => $"l{i}\n")) + "ssl on;\n}\n");
            var window = new CompareWindow("root@srv01 : /etc/app.conf", left, "root@srv02 : /etc/app.conf", right, new AppSettings());
            Assert.Contains("app.conf", window.Title);
            Assert.Equal(2, window.Result!.Blocks);
            Assert.Contains("2", window.SummaryText.Text);
            Assert.Equal(Visibility.Collapsed, window.ToolButton.Visibility);

            window.GoToDifference(1);
            Assert.Equal(0, window.RowsList.SelectedIndex);
            window.GoToDifference(1);
            Assert.Equal(DiffKind.Added, ((DiffRow)window.RowsList.SelectedItem).Kind);

            window.OnlyDiffBox.IsChecked = true;
            window.OnlyDiffBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Contains(((IEnumerable<DiffRow>)window.RowsList.ItemsSource), r => r.Kind == DiffKind.Gap);
            window.Close();
            Assert.All(left, b => Assert.Equal(0, b));   // contenu effacé de la mémoire à la fermeture

            var binary = new CompareWindow("a", [1, 0, 2], "b", [1, 0, 2], new AppSettings { CompareTool = @"C:\Tools\WinMergeU.exe" });
            Assert.Equal(Visibility.Visible, binary.BinaryPanel.Visibility);
            Assert.Equal(Strings.CompareIdentical, binary.SummaryText.Text);
            Assert.Equal(Visibility.Visible, binary.ToolButton.Visibility);
            binary.Close();
        });
    }

    /// <summary>Envoi vers plusieurs serveurs, choix d'une comparaison, choix de serveurs, À propos : les fenêtres s'ouvrent.</summary>
    [Fact]
    public void MultiServerWindowsOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var (s1, _) = NewSshView("root@srv01");
            var (s2, _) = NewSshView("root@srv02");
            var upload = new MultiUploadDialog([s1, s2], [s1], "~/deploy", "SCP");
            Assert.Equal("~/deploy", upload.Destination);
            // Sessions non connectées : on ne peut pas les choisir.
            Assert.Empty(upload.Sessions);
            Assert.All(upload.ServersPanel.Children.OfType<System.Windows.Controls.CheckBox>(), b => Assert.False(b.IsEnabled));
            var file = Path.GetTempFileName();
            upload.AddPath(file);
            upload.AddPath(file);
            Assert.Single(upload.Paths);
            File.Delete(file);
            upload.Close();

            var compare = new CompareDialog("root@srv01 : /etc/app.conf", "/etc/app.conf", [s1, s2], s1);
            Assert.Equal("/etc/app.conf", compare.RemoteFile);
            compare.Close();

            var choose = new ParallelDialog(["web01", "web02", "web03"], [0, 1, 2], 2, "intro", Strings.ParallelOpen);
            Assert.Equal([0, 1], choose.SelectedIndexes);
            Assert.Equal("intro", choose.IntroText.Text);
            choose.Close();

            var about = new AboutWindow(new AppSettings(), () => { });
            Assert.Contains(UpdateChecker.CurrentVersion.ToString(), about.VersionText.Text);
            about.Close();

            s1.Dispose();
            s2.Dispose();
        });
    }

    /// <summary>Recherche dans le terminal (historique compris) et taille de police.</summary>
    [Fact]
    public void TerminalSearchFindsTextInTheHistory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var (session, view) = NewSshView("root@srv01");
            session.Emulator.Feed(string.Concat(Enumerable.Range(1, 60).Select(i => $"line {i}{(i % 20 == 0 ? " ERROR" : "")}\r\n")));
            view.ShowSearch();
            view.SearchBox.Text = "error";
            Assert.Equal(3, view.Matches.Count);
            Assert.Equal(2, view.CurrentMatch);
            view.MoveSearch(-1);
            Assert.Equal(1, view.CurrentMatch);
            view.MoveSearch(1);
            view.MoveSearch(1);
            Assert.Equal(0, view.CurrentMatch);

            CyberArkTerm.App.Terminal.TerminalAppearance.Apply("solarized-light", 16, rightClickPastes: false);
            Assert.Same(CyberArkTerm.Core.Terminal.TerminalTheme.SolarizedLight, CyberArkTerm.App.Terminal.TerminalAppearance.Theme);
            CyberArkTerm.App.Terminal.TerminalAppearance.Apply(null, 99, rightClickPastes: false);
            Assert.Equal(CyberArkTerm.App.Terminal.TerminalAppearance.MaxFontSize, CyberArkTerm.App.Terminal.TerminalAppearance.FontSize);
            CyberArkTerm.App.Terminal.TerminalAppearance.Apply(null, 14, rightClickPastes: false);
            session.Dispose();
        });
    }

    /// <summary>Menu du clic droit dans le terminal : actions selon l'état, effacer l'historique, actions de la session.</summary>
    [Fact]
    public void TerminalMenuOffersTheTerminalAndSessionActions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var (session, view) = NewSshView("root@srv01");
            view.SessionMenu = items => items.Add(new System.Windows.Controls.MenuItem { Header = "session" });
            session.Emulator.Feed(string.Concat(Enumerable.Range(1, 60).Select(i => $"line {i}{(i % 20 == 0 ? " ERROR" : "")}\r\n")));
            view.ShowSearch();
            view.SearchBox.Text = "error";
            Assert.Equal(3, view.Matches.Count);

            var menu = view.Terminal.BuildMenu(atCursor: true)!;
            var entries = menu.Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            System.Windows.Controls.MenuItem Entry(string header) => entries.Single(i => i.Header as string == header);
            Assert.False(Entry(Strings.MenuTerminalCopy).IsEnabled); // rien de sélectionné
            Assert.Equal(Strings.ShortcutTerminalPaste, Entry(Strings.MenuTerminalPaste).InputGestureText);
            Assert.NotNull(Entry(Strings.MenuTerminalSearch));
            Assert.NotNull(Entry(Strings.MenuTerminalSave));
            Assert.Equal(3, Entry(Strings.MenuTerminalFont).Items.Count);
            Assert.Equal("session", entries[^1].Header);

            // Effacer l'historique : l'écran reste, la recherche ne trouve plus que ce qui est à l'écran.
            Entry(Strings.MenuTerminalClear).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.Equal(0, session.Emulator.ScrollbackCount);
            Assert.Equal(2, view.Matches.Count); // lignes 40 et 60 : l'écran (30 lignes) montre les lignes 32 à 60
            Assert.False(view.Terminal.BuildMenu(atCursor: false)!.Items.OfType<System.Windows.Controls.MenuItem>()
                .Single(i => i.Header as string == Strings.MenuTerminalClear).IsEnabled);

            // Tout sélectionner : jusqu'à l'invite, sans les lignes vides du bas de l'écran (après « clear »).
            session.Emulator.Feed("\x1b[2J\x1b[H[root@srv01 ~]# ");
            view.Terminal.BuildMenu(atCursor: false)!.Items.OfType<System.Windows.Controls.MenuItem>()
                .Single(i => i.Header as string == Strings.MenuTerminalSelectAll).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.Equal("[root@srv01 ~]#", view.Terminal.SelectedText);
            session.Dispose();
        });
    }

    private sealed class NoInteraction : CyberArkTerm.Core.Ssh.ISshInteraction
    {
        public bool CheckHostKey(string host, int port, string algorithm, string sha256Fingerprint) => false;

        public string? Prompt(string instruction, string prompt, bool echo) => null;
    }

    private sealed class MemoryFile : ITailSource
    {
        private readonly List<byte> _content = [];

        public long Length => _content.Count;

        public void Append(string text) => _content.AddRange(System.Text.Encoding.UTF8.GetBytes(text));

        public Task<long> GetSizeAsync(CancellationToken ct) => Task.FromResult((long)_content.Count);

        public Task<byte[]> ReadAsync(long offset, int count, CancellationToken ct) =>
            Task.FromResult(_content.Skip((int)offset).Take(count).ToArray());
    }

    /// <summary>Connexion de suivi en mémoire : fichiers par chemin, coupure et reconnexion à la demande.</summary>
    private sealed class MemoryLink(string server, CyberArkTerm.App.Services.SshSession? session = null) : ITailLink
    {
        private readonly Dictionary<string, MemoryFile> _files = [];

        public bool Connected { get; set; } = true;

        public bool Disposed { get; private set; }

        public string Server => server;

        public CyberArkTerm.App.Services.SshSession? Session => session;

        public bool Dedicated => false;

        public int Generation { get; set; } = 1;

        public bool IsConnecting => false;

        public string? Error => null;

        public bool CanReconnect => false;

        public MemoryFile Add(string path) => _files[path] = new MemoryFile();

        public bool CheckConnected() => Connected && !Disposed;

        public void RequestReconnect()
        {
        }

        public Task ReconnectAsync() => Task.CompletedTask;

        public ITailSource Source(string path) => _files[path];

        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// Thread STA avec l'objet <c>Application</c> et le thème (comme dans CyberArkTerm), retiré ensuite pour que les
    /// autres tests ne trouvent pas de ressources liées à ce thread.
    /// </summary>
    private static void RunWithTheme(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var theme = new ResourceDictionary { Source = new Uri("pack://application:,,,/CyberArkTerm;component/Theme.xaml") };
            app.Resources.MergedDictionaries.Add(theme);
            try
            {
                test();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                app.Resources.MergedDictionaries.Remove(theme);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
