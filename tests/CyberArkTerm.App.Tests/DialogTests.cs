using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Views;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Ssh;

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
        });
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
