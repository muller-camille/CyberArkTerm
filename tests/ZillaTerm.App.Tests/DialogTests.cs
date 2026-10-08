using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using ZillaTerm.App.Localization;
using ZillaTerm.App.Views;
using ZillaTerm.Core;
using ZillaTerm.Core.Migration;
using ZillaTerm.Core.Ssh;
using ZillaTerm.Core.Terminal;

namespace ZillaTerm.App.Tests;

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

    /// <summary>
    /// Confirmations : boutons au verbe explicite, « Annuler » par défaut ; une action irréversible est rouge et reste
    /// grisée tant que la case « J'ai vérifié… » n'est pas cochée.
    /// </summary>
    [Fact]
    public void ConfirmationsDefaultToCancel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var changed = new ConfirmDialog(new ConfirmRequest
            {
                Title = Strings.HostKeyTitle,
                Banner = Strings.HostKeyChangedBanner,
                Heading = Strings.HostKeyChangedPsmpHeading,
                Subject = "psmp.corp:22",
                Codes = [("old", "SHA256:AAA"), ("new", "SHA256:BBB")],
                Kind = ConfirmKind.Danger,
                Actions = [Strings.HostKeyReplace],
                DangerAction = 0,
                Acknowledge = Strings.HostKeyAckPsmp,
            });
            var buttons = changed.ButtonsPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            Assert.Equal([Strings.HostKeyReplace, Strings.Cancel], buttons.Select(b => (string)b.Content));
            Assert.False(buttons[0].IsEnabled);
            Assert.False(buttons[0].IsDefault);
            Assert.True(buttons[1].IsDefault && buttons[1].IsCancel);
            Assert.Equal(Visibility.Visible, changed.Banner.Visibility);
            changed.AcknowledgeBox.IsChecked = true;
            Assert.True(buttons[0].IsEnabled);
            Assert.Equal(2, changed.CodeList.Items.Count);
            changed.Close();

            // Renvoi d'un fichier enregistré dans l'éditeur : l'action attendue est le bouton par défaut.
            var upload = new ConfirmDialog(new ConfirmRequest
            {
                Title = "t",
                Heading = "h",
                Items = ["a", "b"],
                Actions = [Strings.EditUploadAction],
                DefaultAction = 0,
                CancelLabel = Strings.NotNow,
            });
            var choices = upload.ButtonsPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            Assert.True(choices[0].IsDefault);
            Assert.False(choices[1].IsDefault);
            Assert.Equal(Strings.NotNow, choices[1].Content);
            Assert.Equal(Visibility.Visible, upload.ItemsPanel.Visibility);
            upload.Close();
        });
    }

    /// <summary>Droits de plusieurs fichiers (755 et 644) : seuls les droits changés partent, pas ceux du premier.</summary>
    [Fact]
    public void PermissionsOfMixedItemsOnlyChangeWhatIsTicked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var dialog = new PermissionsDialog("2", "root@srv:/opt", [0b111_101_101, 0b110_100_100], hasDirectory: false);
            var boxes = dialog.BitsGrid.Children.OfType<System.Windows.Controls.CheckBox>().ToList();
            Assert.Equal(9, boxes.Count);
            // x du propriétaire, du groupe et des autres : différents d'un fichier à l'autre.
            Assert.All(new[] { 2, 5, 8 }, i => Assert.Null(boxes[i].IsChecked));
            Assert.Equal(Visibility.Visible, dialog.MixedText.Visibility);
            Assert.Equal("", dialog.OctalBox.Text);
            Assert.Equal("rw?r-?r-?", dialog.SymbolicText.Text);

            boxes[4].IsChecked = true;
            var change = dialog.CurrentChange;
            Assert.Equal(0b111_111_101, change.Apply(0b111_101_101));
            Assert.Equal(0b110_110_100, change.Apply(0b110_100_100));
            Assert.Equal(Strings.PermissionsApply, dialog.OkButton.Content);
            dialog.Close();
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
            // Nouveau membre : se connecter sans voir les mots de passe.
            Assert.Equal(SafeProfile.AccountUser, add.ProfileBox.SelectedValue);
            add.ProfileBox.SelectedValue = SafeProfile.ReadOnly;
            Assert.Equal(SafeProfile.ReadOnly, add.ProfileBox.SelectedValue);
            Assert.Equal(Visibility.Visible, add.SearchInBox.Visibility);
            Assert.Equal("Vault", add.SearchInBox.Text);
            Assert.Equal((2, 3), (add.LeftGroups.Children.Count, add.RightGroups.Children.Count));
            add.Close();

            var member = JsonSerializer.Deserialize<SafeMember>("""
                {"memberName":"Unix Admins","memberType":"Group","membershipExpirationDate":1767225600,
                 "permissions":{"listAccounts":true,"addAccounts":true}}
                """, Web)!;
            var edit = new SafeMemberDialog("Prod", member, (_, _) => Task.CompletedTask);
            Assert.Equal(SafeProfile.Custom, edit.ProfileBox.SelectedValue);
            Assert.Equal(Visibility.Collapsed, edit.ChangesText.Visibility);
            edit.ProfileBox.SelectedValue = SafeProfile.ReadOnly;
            // Lister gardé, ajouter retiré, audit et membres ajoutés.
            Assert.Equal(Text.Format(Strings.SafeMemberChanges, 2, 1), edit.ChangesText.Text);
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
    /// Import des sessions d'un autre logiciel : aperçu (prêt, à vérifier, sans compte, non pris en charge), choix d'un autre
    /// compte, import sous le dossier choisi, résultat de chaque session et export CSV.
    /// </summary>
    [Fact]
    public void SessionImportWindowShowsTheResult()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), $"zt-sessions-{Guid.NewGuid():N}.csv");
        var missing = Path.Combine(Path.GetTempPath(), $"zt-missing-{Guid.NewGuid():N}.csv");
        try
        {
            RunWithTheme(() =>
            {
                var settings = new AppSettings();
                PvwaAccount[] accounts =
                [
                    new() { Id = "1", UserName = "root", Address = "web01.corp.local", PlatformId = "UnixSSH" },
                    new() { Id = "2", UserName = "deploy", Address = "web01.corp.local", PlatformId = "UnixSSH" },
                ];
                SessionImport? done = null;
                var window = new SessionImportWindow(settings, "pvwa.corp.local", accounts, KnownDomains.From([], []), i => done = i);
                Assert.Equal(SessionImportWindow.Sources.Count, window.SourceBox.Items.Count);
                Assert.Equal(9, window.RowsGrid.Columns.Count);
                Assert.False(window.ImportButton.IsEnabled);

                window.Load(
                [
                    ImportedSession.Terminal("Prod", "web", ImportProtocol.Ssh, "web01.corp.local", null, "root"),
                    ImportedSession.Terminal("Prod", "any", ImportProtocol.Ssh, "web01.corp.local", null, null),
                    ImportedSession.Terminal("", "gone", ImportProtocol.Ssh, "old01", null, "root"),
                    ImportedSession.Unsupported("", "vnc", "VNC", "vnc01"),
                ], "test");
                Assert.Equal([ImportState.Ready, ImportState.Check, ImportState.NoAccount, ImportState.Unsupported], window.Rows.Select(r => r.State));
                Assert.True(window.ImportButton.IsEnabled);
                Assert.True(window.Rows[1].CanChoose);
                Assert.False(window.Rows[2].CanInclude);

                // Même compte que la première session, dans le même dossier : ajouté une seule fois.
                window.Rows[1].Chosen = window.Rows[1].Candidates.Single(c => c.Account.Id == "1");
                window.FolderBox.Text = "Migration";
                window.OnImport(window, new RoutedEventArgs());

                Assert.NotNull(done);
                Assert.Equal([ImportState.Imported, ImportState.AlreadyPresent, ImportState.NoAccount, ImportState.Unsupported],
                    window.Rows.Select(r => r.State));
                var saved = Assert.Single(settings.Sessions);
                Assert.Equal(("Migration/Prod", "web", "1"), (saved.Folder, saved.Name, saved.AccountId));
                Assert.False(window.ImportButton.IsEnabled);
                Assert.False(window.FolderBox.IsEnabled);
                Assert.True(window.ExportButton.IsEnabled);
                window.WriteResult(path);

                // Serveur sans compte (gone) : un compte à créer, au format de l'import de comptes.
                Assert.True(window.MissingAccountsButton.IsEnabled);
                Assert.Equal(1, window.WriteMissingAccounts(missing));
                window.Close();
            });

            Assert.Equal(5, File.ReadAllLines(path).Length);
            var accounts = File.ReadAllLines(missing);
            Assert.Equal(2, accounts.Length);
            Assert.Contains("old01", accounts[1]);
        }
        finally
        {
            File.Delete(path);
            File.Delete(missing);
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
            // Somme SHA-256 réelle (32 octets) : le bilan en affiche les 12 premiers caractères.
            var hash = System.Security.Cryptography.SHA256.HashData("app.conf"u8);
            running.Checks.Add(new TransferCheck("app.conf", @"C:\Temp\app.conf", "/opt/app/app.conf", 3, hash, 3, [.. hash]));
            release.SetResult();

            Assert.Equal((TransferState.Done, TransferState.Cancelled), (running.State, waiting.State));
            Assert.Equal(0, panel.ActiveTransfers(null));
            Assert.Contains(Text.Format(Strings.QueueSummaryCancelled, 1), panel.StatusText.Text);
            // Les résultats restent dans la file (vérification SHA-256 par ligne) jusqu'à « Effacer les terminés ».
            Assert.Equal(Visibility.Visible, panel.QueuePanel.Visibility);
            Assert.Equal(Text.Format(Strings.QueueStateVerified, 1, 1), TransferStatusConverter.StateText(running));
            Assert.Equal("Ok", TransferStatusConverter.Outcome(running));
            Assert.Equal((Visibility.Collapsed, Visibility.Visible), (panel.CancelAllButton.Visibility, panel.ClearFinishedButton.Visibility));
            Assert.Equal(0, panel.UnseenProblems);
            panel.Queue.ClearFinished();
            Assert.Equal(Visibility.Collapsed, panel.QueuePanel.Visibility);

            // Historique : l'envoi terminé y figure ; l'élément retiré avant d'avoir commencé, non.
            var record = Assert.Single(panel.History.Records);
            Assert.Equal(("deploy/", true, TransferState.Done, "SCP"), (record.Label, record.Upload, record.State, record.Protocol));

            // Un échec est signalé (pastille de l'onglet Fichiers) jusqu'à ce que l'onglet soit affiché.
            panel.Queue.Enqueue(new TransferItem(true, "fail/", "/opt/app", (_, _) => throw new IOException("disque plein")));
            Assert.Equal(1, panel.UnseenProblems);
            Assert.Equal("Error", TransferStatusConverter.Outcome(panel.Queue.Items[^1]));
            panel.MarkTransfersSeen();
            Assert.Equal(0, panel.UnseenProblems);
        });
    }

    /// <summary>Nouveau nom (renommer, nouveau dossier) : ni « / », ni caractère de contrôle, ni « . » ou « .. ».</summary>
    [Fact]
    public void RemoteNamesAreChecked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Null(FileBrowserPanel.ValidateName("app.conf"));
        Assert.Null(FileBrowserPanel.ValidateName(".bashrc"));
        Assert.Equal(Strings.NameNoSlash, FileBrowserPanel.ValidateName("../etc/passwd"));
        Assert.Equal(Strings.NameNoControl, FileBrowserPanel.ValidateName("a\r\nDELE b"));
        Assert.Equal(Strings.NameReserved, FileBrowserPanel.ValidateName(".."));
        Assert.Equal(Strings.NameReserved, FileBrowserPanel.ValidateName("."));
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
            Assert.Equal(ZillaTerm.Core.Localization.CoreStrings.ColumnName, panel.NameColumn.Header);
            Assert.Same(big, Assert.Single(panel.FileList.SelectedItems.Cast<RemoteEntry>()));
            Assert.Equal((RemoteSortColumn.Modified, true, 1), (settings.FileSortColumn, settings.FileSortDescending, saved));

            panel.SortBy(RemoteSortColumn.Size, descending: false);
            Assert.Equal(["..", "app", "a.log", "z.conf", "big.tar"],
                panel.FileList.Items.Cast<RemoteEntry>().Select(e => e.Name));

            // Filtre du dossier : « .. » reste affiché, le tri garde le filtre, le vider rend tout le dossier.
            panel.FilterBox.Text = "*.log;app";
            Assert.Equal(["..", "app", "a.log"], panel.FileList.Items.Cast<RemoteEntry>().Select(e => e.Name));
            Assert.Equal(Text.Format(Strings.FilesFilterSummary, 2, 4), panel.StatusText.Text);
            panel.SortBy(RemoteSortColumn.Name, descending: true);
            Assert.Equal(["..", "app", "a.log"], panel.FileList.Items.Cast<RemoteEntry>().Select(e => e.Name));
            panel.FilterBox.Text = "";
            Assert.Equal(5, panel.FileList.Items.Count);
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
            Assert.Equal(Text.Format(Strings.QueueStateVerified, 1, 1), TransferHistoryDialog.Result(download));
            Assert.Equal("✗ permission refusée", TransferHistoryDialog.Result(history.Records[0]));
            // Échec en rouge, réussite normale.
            Assert.True(TransferHistoryDialog.HasProblem(history.Records[0]));
            Assert.False(TransferHistoryDialog.HasProblem(download));
            Assert.NotNull(dialog.RecordsGrid.RowStyle);
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
            var connector = new ZillaTerm.Core.Ssh.SshConnector("127.0.0.1", 22, "root", new NoInteraction());
            var session = new ZillaTerm.App.Services.SshSession(null, "root@srv02", connector,
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
            var feed = new TailFeed(new MemoryLink("root@srv01"), "/var/log/app.log", TailBrushes.Source(0));
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

    /// <summary>
    /// Paramètres, page Sécurité : clés acceptées en tableau (serveur, type, empreinte) ; seules les clés choisies sont
    /// oubliées, et seulement à l'enregistrement.
    /// </summary>
    [Fact]
    public void SettingsForgetOnlyTheChosenHostKeys()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var settings = new AppSettings();
            ZillaTerm.Core.Ssh.KnownHosts.Remember(settings.KnownHosts, "psmp.corp.local", 22, "ssh-ed25519", "AAAA");
            ZillaTerm.Core.Ssh.KnownHosts.Remember(settings.KnownHosts, "ftp.corp.local", 990, "X.509", "BBBB");
            var dialog = new SettingsDialog(settings);
            Assert.Equal(2, dialog.HostKeyRows.Count);
            Assert.Contains(new SettingsDialog.HostKeyRow("psmp.corp.local:22", "ssh-ed25519", "SHA256:AAAA"), dialog.HostKeyRows);
            Assert.False(dialog.ForgetKeysButton.IsEnabled);

            dialog.HostKeysGrid.SelectedItems.Add(dialog.HostKeyRows.Single(r => r.Server == "ftp.corp.local:990"));
            dialog.ForgetSelectedKeys();
            Assert.Equal(["psmp.corp.local:22"], dialog.HostKeyRows.Select(r => r.Server));
            Assert.Equal(2, settings.KnownHosts.Count);

            dialog.ApplyForgottenKeys();
            Assert.Equal(["psmp.corp.local:22"], settings.KnownHosts.Keys);
            dialog.Close();
        });
    }

    /// <summary>
    /// Paramètres, page CyberArk : PSMP par domaine. Le domaine suit l'adresse tant qu'il n'a pas été changé, l'essai
    /// d'un serveur montre le PSMP choisi, deux PSMP pour le même domaine sont refusés.
    /// </summary>
    [Fact]
    public void SettingsListPsmpByDomain()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var settings = new AppSettings { PsmpAddress = "psmp.corp.com" };
            settings.PsmpServers.Add(new PsmpServer { Address = "psmp.xxx.ss.com" });
            settings.PsmpServers.Add(new PsmpServer { Address = "psmp01.infra.corp.com", Port = 2022, Domain = "dmz.corp.com" });
            var dialog = new SettingsDialog(settings);
            Assert.Equal(["xxx.ss.com", "dmz.corp.com"], dialog.PsmpRows.Select(r => r.Domain));

            dialog.PsmpTestBox.Text = "blabla.zzz.xxx.ss.com";
            Assert.Equal(Text.Format(Strings.PsmpTestMatch, "psmp.xxx.ss.com", 22, "xxx.ss.com"), dialog.PsmpTestText.Text);
            dialog.PsmpTestBox.Text = "web.dmz.corp.com";
            Assert.Equal(Text.Format(Strings.PsmpTestMatch, "psmp01.infra.corp.com", 2022, "dmz.corp.com"), dialog.PsmpTestText.Text);
            dialog.PsmpTestBox.Text = "srv.other.org";
            Assert.Equal(Text.Format(Strings.PsmpTestFallback, "psmp.corp.com", 22), dialog.PsmpTestText.Text);

            var row = dialog.PsmpRows[0];
            row.Address = "psmp.zzz.xxx.ss.com";
            Assert.Equal("zzz.xxx.ss.com", row.Domain);
            dialog.PsmpRows[1].Address = "psmp02.infra.corp.com";
            Assert.Equal("dmz.corp.com", dialog.PsmpRows[1].Domain);

            var saved = dialog.ReadPsmpServers("psmp.corp.com");
            Assert.NotNull(saved);
            Assert.Equal(("psmp.zzz.xxx.ss.com", 22, ""), (saved[0].Address, saved[0].Port, saved[0].Domain));
            Assert.Equal(("psmp02.infra.corp.com", 2022, "dmz.corp.com"), (saved[1].Address, saved[1].Port, saved[1].Domain));

            // Composant des comptes Windows : proposé parmi les composants usuels, nom vérifié.
            Assert.Equal("", dialog.WindowsComponentBox.Text);
            Assert.Contains("PSM-RDP", dialog.WindowsComponentBox.Items.OfType<string>());

            // Même domaine que le PSMP par défaut : celui de la liste ne servirait jamais.
            row.Domain = "corp.com";
            Assert.Null(dialog.ReadPsmpServers("psmp.corp.com"));
            Assert.Equal(Text.Format(Strings.PsmpDuplicateDomain, "corp.com"), dialog.ErrorText.Text);
            dialog.Close();
        });
    }

    /// <summary>
    /// Composant par plateforme : modifiable dans les Paramètres (modifier, ajouter, enlever) ; lignes vides ignorées,
    /// composant invalide et plateforme en double refusés.
    /// </summary>
    [Fact]
    public void SettingsEditComponentPerPlatform()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var settings = new AppSettings();
            settings.RememberComponent("WinDomain", "PSM-RDP");
            settings.RememberComponent("UnixSSH", "PSM-SSH");
            var dialog = new SettingsDialog(settings);
            Assert.Equal(["UnixSSH", "WinDomain"], dialog.ComponentRows.Select(r => r.Platform));

            dialog.ComponentRows[1].Component = "WIN-PSM";
            dialog.ComponentRows.RemoveAt(0);
            dialog.ComponentRows.Add(new SettingsDialog.ComponentRow { Platform = " WinServerLocal ", Component = "WIN-PSM-T1" });
            dialog.ComponentRows.Add(new SettingsDialog.ComponentRow());
            var saved = dialog.ReadComponents();
            Assert.NotNull(saved);
            Assert.Equal(new Dictionary<string, string> { ["WinDomain"] = "WIN-PSM", ["WinServerLocal"] = "WIN-PSM-T1" }, saved);

            dialog.ComponentRows[1].Component = "WIN PSM";
            Assert.Null(dialog.ReadComponents());
            Assert.Equal(Text.Format(Strings.InvalidComponentRow, 2), dialog.ErrorText.Text);

            dialog.ComponentRows[1].Component = "WIN-PSM-T1";
            dialog.ComponentRows[1].Platform = "windomain";
            Assert.Null(dialog.ReadComponents());
            Assert.Equal(Text.Format(Strings.ComponentDuplicatePlatform, "windomain"), dialog.ErrorText.Text);

            dialog.ComponentRows[2].Component = "PSM-SSH";
            dialog.ComponentRows[1].Platform = "UnixSSH";
            Assert.Null(dialog.ReadComponents());
            Assert.Equal(Text.Format(Strings.ComponentRowPlatformMissing, 3), dialog.ErrorText.Text);
            dialog.Close();
        });
    }

    /// <summary>
    /// Compte de domaine : la fenêtre « Choisir le serveur » propose les serveurs déjà utilisés, cache « Garder dans Mes
    /// serveurs » pour un serveur déjà gardé, refuse un serveur hors des machines autorisées ; à l'ajout, le serveur est
    /// facultatif.
    /// </summary>
    [Fact]
    public void ServerPromptOffersKnownServersAndKeepsTheChoice()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var account = new PvwaAccount
            {
                Id = "5_1", UserName = "adm-t1", Address = "corp.local", PlatformId = "WinDomain", SafeName = "T1-ADMINS",
                RemoteMachinesAccess = new RemoteMachinesAccess { RemoteMachines = "srv01.corp.local;srv02.corp.local" },
            };
            var dialog = new ServerPromptDialog(account, ["srv02.corp.local", "srv01.corp.local"], ["Prod", "Prod/Web"],
                server => server.Equals("srv02.corp.local", StringComparison.OrdinalIgnoreCase), keep: true, folder: "Prod");
            Assert.Equal("srv02.corp.local", dialog.Server);
            Assert.Equal(Strings.ServerPromptKnown, dialog.ServerHint.Text);
            Assert.Equal(Visibility.Collapsed, dialog.KeepPanel.Visibility);
            Assert.Equal(Visibility.Visible, dialog.AlreadySavedText.Visibility);
            Assert.False(dialog.Keep);

            dialog.ServerBox.Text = "srv03.corp.local";
            Assert.Equal(Visibility.Visible, dialog.KeepPanel.Visibility);
            Assert.True(dialog.Keep);
            Assert.Equal("Prod", dialog.Folder);
            Assert.True(dialog.Validate(required: true));

            dialog.ServerBox.Text = "srv 03";
            Assert.False(dialog.Validate(required: true));
            Assert.Equal(Strings.ServerPromptInvalid, dialog.ErrorText.Text);
            dialog.ServerBox.Text = "";
            Assert.False(dialog.Validate(required: true));
            Assert.True(dialog.Validate(required: false));
            dialog.Close();

            // Compte limité à ses machines : un autre serveur est refusé.
            account.RemoteMachinesAccess.AccessRestrictedToRemoteMachines = true;
            var restricted = new ServerPromptDialog(account, ["srv01.corp.local"], [], _ => false, keep: false, folder: "", adding: true);
            Assert.Equal(Visibility.Collapsed, restricted.KeepPanel.Visibility);
            Assert.Equal(Strings.ServerPromptAdd, restricted.OkButton.Content);
            restricted.ServerBox.Text = "srv09.corp.local";
            Assert.False(restricted.Validate(required: false));
            Assert.Equal(Text.Format(Strings.ServerPromptRestricted, "srv01.corp.local, srv02.corp.local"), restricted.ServerHint.Text);
            Assert.Equal(Text.Format(Strings.ServerPromptNotAllowed, "srv09.corp.local"), restricted.ErrorText.Text);
            restricted.ServerBox.Text = "SRV02.corp.local";
            Assert.True(restricted.Validate(required: false));
            restricted.Close();
        });
    }

    /// <summary>
    /// « Connexion avancée » d'un compte de domaine : sans serveur, la connexion est refusée (elle viserait le domaine) ;
    /// avec un serveur, il est la machine cible.
    /// </summary>
    [Fact]
    public void AdvancedConnectionRequiresTheServerOfADomainAccount()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var account = new PvwaAccount { Id = "5_2", UserName = "adm-t1", Address = "corp.example.com", PlatformId = "AD-Admins-T1", SafeName = "T1" };
            var dialog = new ConnectDialog(account, new ConnectRequest(ConnectMode.Psm, "PSM-RDP"), new AppSettings(), "jdupont", null,
                requireMachine: true);
            Assert.Equal("", dialog.MachineBox.Text);
            Assert.False(dialog.Accept());
            Assert.Equal(Strings.ServerPromptRequired, dialog.ErrorText.Text);
            Assert.Null(dialog.Result);

            dialog.MachineBox.Text = "srv01.corp.example.com";
            Assert.True(dialog.Accept());
            Assert.Equal("srv01.corp.example.com", dialog.Result?.RemoteMachine);
            dialog.Close();

            // Compte d'un serveur : la machine cible reste facultative.
            var server = new PvwaAccount { Id = "5_3", UserName = "admin", Address = "srv02.corp.example.com", PlatformId = "WinServerLocal" };
            var direct = new ConnectDialog(server, new ConnectRequest(ConnectMode.Psm, "PSM-RDP"), new AppSettings(), "jdupont", null);
            Assert.True(direct.Accept());
            Assert.Null(direct.Result?.RemoteMachine);
            direct.Close();
        });
    }

    /// <summary>Récapitulatif d'un environnement : « Réglage : valeur » pour un ajout, « ancienne → nouvelle » sinon ; oui / non lisibles.</summary>
    [Fact]
    public void EnvironmentRecapLines()
    {
        Assert.Equal(Text.Format(Strings.EnvLineNew, Strings.EnvDefaultPsmp, "psmp.corp.com:22"),
            EnvironmentImport.Line(new EnvironmentChange(EnvironmentSetting.DefaultPsmp, null, "", "psmp.corp.com:22", true)));
        Assert.Equal(Text.Format(Strings.EnvLineChange, Text.Format(Strings.EnvPlatformComponent, "WinDomain"), "PSM-RDP", "WIN-PSM"),
            EnvironmentImport.Line(new EnvironmentChange(EnvironmentSetting.PlatformComponent, "WinDomain", "PSM-RDP", "WIN-PSM", false)));
        Assert.Equal(Text.Format(Strings.EnvLineChange, Strings.EnvSshInApp, Strings.EnvYes, Strings.EnvNo),
            EnvironmentImport.Line(new EnvironmentChange(EnvironmentSetting.SshInApp, null, "true", "false", false)));
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
            var connector = new ZillaTerm.Core.Ssh.SshConnector("127.0.0.1", 22, "root", new NoInteraction());
            var session = new ZillaTerm.App.Services.SshSession(null, "root@srv01", connector,
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

    private static (ZillaTerm.App.Services.SshSession Session, SshSessionView View) NewSshView(string label)
    {
        var connector = new ZillaTerm.Core.Ssh.SshConnector("127.0.0.1", 22, "root", new NoInteraction());
        var session = new ZillaTerm.App.Services.SshSession(null, label, connector,
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
            v1.InputRouter!(v1, TerminalInput.Special(ZillaTerm.Core.Terminal.TerminalKey.Up));
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
            // Aucun autre serveur connecté : un fichier de ce poste est proposé (le même chemin donnerait le même fichier).
            Assert.True(compare.LocalRadio.IsChecked);
            compare.ServerRadio.IsChecked = true;
            Assert.True(compare.BrowseServerButton.IsEnabled);
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

    /// <summary>
    /// Explorateur d'un serveur (Comparer → Parcourir) : s'ouvre sur le dossier du même chemin, ou son plus proche
    /// parent lisible, fichier présélectionné ; on y navigue et on choisit un fichier.
    /// </summary>
    [Fact]
    public void RemoteFilePickerOpensOnTheSamePathAndReturnsTheChosenFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            static RemoteEntry Entry(string path, bool directory = false) =>
                new(RemotePath.Name(path), path, directory, false, directory ? 0 : 1234, new DateTime(2026, 10, 6, 9, 0, 0), "");
            var tree = new Dictionary<string, List<RemoteEntry>>
            {
                ["/opt/app"] = [Entry("/opt/app/conf", directory: true), Entry("/opt/app/run.sh")],
                ["/opt/app/conf"] = [Entry("/opt/app/conf/app.yml"), Entry("/opt/app/conf/logging.xml")],
            };
            var listed = new List<string>();
            Task<List<RemoteEntry>> List(string directory, bool hidden, CancellationToken ct)
            {
                listed.Add(directory);
                return tree.TryGetValue(directory, out var entries)
                    ? Task.FromResult(entries)
                    : Task.FromException<List<RemoteEntry>>(new IOException("No such file"));
            }

            // Même chemin que le fichier comparé : son dossier s'ouvre, le fichier est sélectionné.
            var picker = new RemoteFileDialog("root@srv02", "/opt/app/conf/app.yml", "/root", List);
            picker.StartAsync().GetAwaiter().GetResult();
            Assert.Equal("/opt/app/conf", picker.CurrentDirectory);
            Assert.Equal("app.yml", ((RemoteEntry?)picker.FileList.SelectedItem)?.Name);
            Assert.True(picker.ChooseButton.IsEnabled);
            Assert.Equal("..", ((RemoteEntry)picker.FileList.Items[0]).Name);
            picker.Close();

            // Dossier absent sur ce serveur : le plus proche parent lisible ; « .. » puis un fichier.
            picker = new RemoteFileDialog("root@srv02", "/opt/app/old/app.yml", "/root", List);
            listed.Clear();
            picker.StartAsync().GetAwaiter().GetResult();
            Assert.Equal(["/opt/app/old", "/opt/app"], listed);
            Assert.Equal("/opt/app", picker.CurrentDirectory);
            Assert.Null(picker.FileList.SelectedItem);
            picker.OpenAsync(Entry("/opt/app/conf", directory: true)).GetAwaiter().GetResult();
            picker.OpenAsync(RemoteEntry.ParentLink("/opt/app/conf")).GetAwaiter().GetResult();
            Assert.Equal("conf", ((RemoteEntry?)picker.FileList.SelectedItem)?.Name);
            Assert.False(picker.ChooseButton.IsEnabled);
            picker.OpenAsync(Entry("/opt/app/run.sh")).GetAwaiter().GetResult();
            Assert.Equal("/opt/app/run.sh", picker.SelectedPath);

            // Chemin saisi : un fichier ouvre son dossier et le sélectionne ; « ~ » est le dossier personnel.
            picker.GoToAsync("/opt/app/conf/logging.xml").GetAwaiter().GetResult();
            Assert.Equal(("/opt/app/conf", "logging.xml"), (picker.CurrentDirectory, ((RemoteEntry?)picker.FileList.SelectedItem)?.Name));
            listed.Clear();
            picker.GoToAsync("~").GetAwaiter().GetResult();
            Assert.Equal(["/root", "/"], listed);
            Assert.Contains("/", picker.StatusText.Text);
            picker.Close();
        });
    }

    /// <summary>Historique d'une liste partagée : journal (le plus récent d'abord) et versions avec leurs auteurs.</summary>
    [Fact]
    public void SharedListHistoryShowsTheChangesAndTheVersions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("cat-shared-").FullName;
        try
        {
            var list = SharedServerList.Create(Path.Combine(directory, "Équipe.json"), "Équipe", "pvwa", "alice");
            list.Add([Server("1", "web01", "Prod"), Server("2", "db01")], "alice");
            list.Remove([list.Content!.Servers.Single(s => s.Name == "db01").Id], "bob");

            RunWithTheme(() =>
            {
                UseDispatcherContext();
                var restored = new List<int>();
                var dialog = new SharedHistoryDialog(new SharedServerList(list.Path), version =>
                {
                    restored.Add(version.Revision);
                    return Task.FromResult<int?>(0);
                });
                Pump(dialog.LoadAsync());

                Assert.Equal(
                    [$"{Strings.SharedActionRemoved} db01 bob", $"{Strings.SharedActionAdded} db01 alice",
                     $"{Strings.SharedActionAdded} web01 alice", $"{Strings.SharedActionCreated} Équipe alice"],
                    dialog.Changes.Select(c => $"{c.Action} {c.Server} {c.By}"));
                Assert.Equal([2, 1], dialog.Versions.Select(v => v.Revision));
                Assert.Equal(("alice", "+ web01, + db01"), (dialog.Versions[0].By, dialog.Versions[0].Summary));
                Assert.Contains("3", dialog.HeaderText.Text);
                Assert.False(dialog.RestoreButton.IsEnabled);
                dialog.VersionsGrid.SelectedIndex = 0;
                Assert.True(dialog.RestoreButton.IsEnabled);
                Assert.Empty(restored);
                dialog.Close();
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Fenêtre principale avant le chargement des comptes : connexions récentes grisées, liste partagée lue en
    /// arrière-plan et affichée avant « Mes serveurs », boutons d'import et d'export dans l'onglet.
    /// </summary>
    [Fact]
    public void MainWindowShowsSharedListsAndWaitsForTheAccounts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("cat-shared-").FullName;
        try
        {
            var list = SharedServerList.Create(Path.Combine(directory, "Équipe.json"), "Équipe", "pvwa.test", "alice");
            list.Add([Server("1", "web01", "Prod"), Server("2", "db01")], "alice");
            var settings = new AppSettings { KeepPvwaSessionAlive = false, SharedLists = [list.Path] };
            settings.Recent.Add(new RecentSession { AccountId = "1", PvwaHost = "pvwa.test", Label = "root@web01", Mode = "PSM-RDP", When = DateTime.Now });
            // Même ID de compte sur un autre PVWA : pas affichée ici.
            settings.Recent.Add(new RecentSession { AccountId = "1", PvwaHost = "other.test", Label = "admin@dc01", Mode = "PSM-RDP", When = DateTime.Now });
            settings.Sessions.Add(new SavedSession { AccountId = "3", PvwaHost = "pvwa.test", Name = "root@app01" });

            RunWithTheme(() =>
            {
                UseDispatcherContext();
                using var client = new PvwaClient(new Uri("https://pvwa.test"), new System.Net.Http.HttpClientHandler());
                var window = new MainWindow(client, settings, "jdoe", "jdoe", new Services.KeePass.KeePassManager());

                Assert.False(window.RecentList.IsEnabled);
                Assert.Equal("root@web01", Assert.Single(Assert.IsAssignableFrom<IEnumerable<RecentSession>>(window.RecentList.ItemsSource)).Label);
                Assert.Equal(Visibility.Visible, window.RecentLoadingText.Visibility);
                Assert.Equal(Visibility.Visible, window.ImportServersButton.Visibility);
                Assert.Equal(Visibility.Visible, window.SharedListsButton.Visibility);

                PumpUntil(() => window.SavedTree.ItemsSource is IEnumerable<object> items && items.OfType<SharedListNode>().Any(n => n.IsReadable));
                var nodes = ((IEnumerable<object>)window.SavedTree.ItemsSource).ToList();
                var shared = Assert.IsType<SharedListNode>(nodes[0]);
                Assert.Equal(("Équipe", " (2)"), (shared.Name, shared.StateText));
                Assert.Equal("Prod", Assert.IsType<SharedFolderNode>(shared.Children[0]).Name);
                var db = Assert.IsType<SharedServerNode>(shared.Children[1]);
                Assert.Equal(("db01", 0.5), (db.Title, db.Opacity));
                Assert.Contains("alice", db.Details);
                Assert.Equal("root@app01", Assert.IsType<SavedSessionNode>(nodes[1]).Title);
                window.StopWatchingSharedLists();
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServerEntry Server(string account, string name, string folder = "") =>
        new() { AccountId = account, Name = name, Folder = folder, Address = name, UserName = "root", PlatformId = "UnixSSH" };

    /// <summary>Session de fichiers seuls (entrée KeePass FTP) : état dans l'onglet, fichiers dans le panneau, pas de terminal.</summary>
    [Fact]
    public void FilesSessionShowsItsStateAndTheFilesPanelBrowsesIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            UseDispatcherContext();
            var attempts = new Queue<TaskCompletionSource<IRemoteFiles>>([new(), new()]);
            var pending = attempts.ToArray();
            var session = new ZillaTerm.App.Services.FilesSession("ftp-test · KeePass", "FTPES", _ => attempts.Dequeue().Task,
                System.Windows.Threading.Dispatcher.CurrentDispatcher);
            var view = new FilesSessionView(session, "alice@ftp.test:21");
            var panel = new FileBrowserPanel();
            panel.Attach(session);
            Assert.Equal(Visibility.Collapsed, panel.FollowBox.Visibility);

            var first = view.ConnectAsync();
            Assert.Equal(Strings.FilesSessionConnecting, view.StateText.Text);
            Assert.Equal(Text.Format(Strings.FilesConnectingBrowse, "FTPES"), panel.MessageText.Text);
            Assert.False(view.ShowFilesButton.IsEnabled);

            pending[0].SetException(new IOException("certificat refusé"));
            Pump(first);
            Assert.Equal(ZillaTerm.App.Services.RemoteSessionState.Failed, session.State);
            Assert.Contains("certificat refusé", view.StateText.Text);
            Assert.Equal(Visibility.Visible, view.ReconnectButton.Visibility);
            Assert.Equal(Strings.FilesClosedBrowse, panel.MessageText.Text);

            var files = new FakeFiles("/srv/ftp", [
                new RemoteEntry("logs", "/srv/ftp/logs", true, false, 0, new DateTime(2026, 10, 6, 9, 0, 0), ""),
                new RemoteEntry("app.conf", "/srv/ftp/app.conf", false, false, 120, new DateTime(2026, 10, 6, 9, 0, 0), ""),
            ]);
            var second = view.ConnectAsync();
            Assert.Equal(Text.Format(Strings.FilesConnectingBrowse, "FTPES"), panel.MessageText.Text);
            pending[1].SetResult(files);
            Pump(second);
            Assert.Equal(ZillaTerm.App.Services.RemoteSessionState.Connected, session.State);
            Assert.Equal(Strings.FilesSessionConnected, view.StateText.Text);
            Assert.True(view.ShowFilesButton.IsEnabled);
            Assert.Equal(Visibility.Collapsed, view.ReconnectButton.Visibility);
            Assert.Equal(Visibility.Collapsed, view.CleartextBanner.Visibility);
            PumpUntil(() => panel.PathBox.Text == "/srv/ftp");
            Assert.Contains(panel.FileList.Items.OfType<RemoteEntry>(), e => e.Name == "app.conf");

            session.Dispose();
            PumpUntil(() => files.Disposed);
        });
    }

    /// <summary>Fichiers d'un serveur en mémoire : une liste fixe, le reste non utilisé.</summary>
    private sealed class FakeFiles(string home, List<RemoteEntry> entries) : IRemoteFiles
    {
        public bool Disposed { get; private set; }
        public string HomeDirectory { get; } = home;
        public string CurrentDirectory { get; private set; } = home;
        public bool IsConnected => !Disposed;
        public bool ChoosesUploadProtocol => false;
        public TransferProtocol UploadProtocol => TransferProtocol.Ftps;

        public Task<List<RemoteEntry>> ListAsync(string path, bool showHidden, CancellationToken ct)
        {
            CurrentDirectory = path;
            return Task.FromResult(entries.ToList());
        }

        public Task<List<RemoteEntry>> BrowseAsync(string directory, bool showHidden, CancellationToken ct) => Task.FromResult(entries.ToList());
        public Task DeleteAsync(RemoteEntry entry, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task RenameAsync(string path, string newPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken ct) => Task.FromResult(true);
        public Task UploadAsync(string localPath, string remoteDirectory, TransferProtocol protocol, ICollection<TransferCheck> checks,
            IProgress<TransferProgress>? progress, bool background, CancellationToken ct) => throw new NotSupportedException();
        public ITailSource TailSource(string path) => throw new NotSupportedException();
        public Task<long> GetSizeAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string path, long offset, int count, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]> ReadAllBytesAsync(string path, long maxBytes, CancellationToken ct) => throw new NotSupportedException();
        public Task<(DateTime LastWriteTime, long Length)> GetStatAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<(DateTime LastWriteTime, long Length)> WriteFileAsync(string remotePath, byte[] content, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<int?> GetModeAsync(string path, CancellationToken ct) => Task.FromResult<int?>(null);
        public Task<PermissionsResult> SetPermissionsAsync(string path, PermissionChange change, bool recursive,
            bool executeOnlyIfAlready, IProgress<int>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<List<RemoteTreeItem>> ListTreeAsync(IReadOnlyList<RemoteEntry> roots, int maxItems, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<TransferCheck> DownloadAsync(RemoteEntry entry, string localPath, IProgress<TransferProgress>? progress, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<TransferCheck> DownloadAsync(RemoteEntry entry, string localPath, ICollection<TransferCheck>? checks,
            IProgress<TransferProgress>? progress, bool background, CancellationToken ct) => throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// Session PVWA expirée : reconnexion sur le même client, avec le même utilisateur, challenge RADIUS compris ; rien
    /// n'est envoyé au PVWA sans mot de passe (Entrée tapée par erreur).
    /// </summary>
    [Fact]
    public void ReconnectDialogSignsInAgainWithTheSameUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            UseDispatcherContext();
            var pvwa = new LogonPvwa();
            using var client = new PvwaClient(new Uri("https://pvwa.test"), pvwa);
            var dialog = new ReconnectDialog(client, AuthMethod.RADIUS, "jdoe", "jdoe", focusPassword: true);
            void Click() => dialog.ReconnectButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Exception? failure = null;
            // Garde-fou : la fenêtre se ferme d'elle-même si le scénario se bloque.
            var guard = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            guard.Tick += (_, _) => dialog.Close();
            dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Assert.Contains("jdoe", dialog.WhoText.Text);
                    Assert.Contains("pvwa.test", dialog.WhoText.Text);
                    Assert.Equal(Visibility.Visible, dialog.CredentialsPanel.Visibility);

                    Click();
                    Assert.Equal(Strings.ReconnectPasswordMissing, dialog.ErrorMessage.Text);
                    Assert.Empty(pvwa.Logons);

                    dialog.PasswordBox.Password = "secret";
                    Click();
                    PumpUntil(() => dialog.ChallengePanel.Visibility == Visibility.Visible);
                    Assert.Equal("Enter the code", dialog.ChallengeText.Text);
                    Assert.Equal(Visibility.Collapsed, dialog.CredentialsPanel.Visibility);

                    dialog.ChallengeBox.Password = "123456";
                    Click();
                }
                catch (Exception e)
                {
                    failure = e;
                    dialog.Close();
                }
            });
            guard.Start();
            var result = dialog.ShowDialog();
            guard.Stop();

            if (failure is not null)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            Assert.True(result);
            Assert.True(client.IsAuthenticated);
            Assert.Equal(["jdoe:secret", "jdoe:123456"], pvwa.Logons);
            Assert.All(pvwa.Paths, path => Assert.Equal("/API/auth/RADIUS/Logon", path));

            // Méthode Windows : pas de mot de passe à saisir.
            var windows = new ReconnectDialog(client, AuthMethod.Windows, "jdoe", @"CORP\jdoe", focusPassword: false);
            Assert.Equal(Visibility.Collapsed, windows.CredentialsPanel.Visibility);
            windows.Close();
        });
    }

    /// <summary>Faux PVWA pour la reconnexion : challenge RADIUS à la première tentative, jeton à la suivante.</summary>
    private sealed class LogonPvwa : System.Net.Http.HttpMessageHandler
    {
        public List<string> Logons { get; } = [];

        public List<string> Paths { get; } = [];

        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Logons.Add($"{body.RootElement.GetProperty("username").GetString()}:{body.RootElement.GetProperty("password").GetString()}");
            var (status, json) = Logons.Count == 1
                ? (HttpStatusCode.InternalServerError, "{\"ErrorCode\":\"ITATS542I\",\"ErrorMessage\":\"Enter the code\"}")
                : (HttpStatusCode.OK, "\"token-2\"");
            return new System.Net.Http.HttpResponseMessage(status)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>Les suites des tâches attendues reviennent sur le fil de la fenêtre, comme dans l'application.</summary>
    private static void UseDispatcherContext() =>
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(
            System.Windows.Threading.Dispatcher.CurrentDispatcher));

    /// <summary>Fait tourner la file du fil de la fenêtre jusqu'à la fin de la tâche.</summary>
    private static void Pump(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> done, int timeoutMs = 10000)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var started = Environment.TickCount64;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            if (done() || Environment.TickCount64 - started > timeoutMs)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Assert.True(done(), "délai dépassé");
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

            ZillaTerm.App.Terminal.TerminalAppearance.Apply("solarized-light", 16, rightClickPastes: false);
            Assert.Same(ZillaTerm.Core.Terminal.TerminalTheme.SolarizedLight, ZillaTerm.App.Terminal.TerminalAppearance.Theme);
            ZillaTerm.App.Terminal.TerminalAppearance.Apply(null, 99, rightClickPastes: false);
            Assert.Equal(ZillaTerm.App.Terminal.TerminalAppearance.MaxFontSize, ZillaTerm.App.Terminal.TerminalAppearance.FontSize);
            ZillaTerm.App.Terminal.TerminalAppearance.Apply(null, 14, rightClickPastes: false);
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

    private sealed class NoInteraction : ZillaTerm.Core.Ssh.ISshInteraction
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
    private sealed class MemoryLink(string server, ZillaTerm.App.Services.RemoteSession? session = null) : ITailLink
    {
        private readonly Dictionary<string, MemoryFile> _files = [];

        public bool Connected { get; set; } = true;

        public bool Disposed { get; private set; }

        public string Server => server;

        public ZillaTerm.App.Services.RemoteSession? Session => session;

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
    /// Thread STA avec l'objet <c>Application</c> et le thème (comme dans ZillaTerm), retiré ensuite pour que les
    /// autres tests ne trouvent pas de ressources liées à ce thread.
    /// </summary>
    private static void RunWithTheme(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var theme = new ResourceDictionary { Source = new Uri("pack://application:,,,/ZillaTerm;component/Theme.xaml") };
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
