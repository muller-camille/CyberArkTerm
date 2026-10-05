using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using CyberArkTerm.App.Views;
using CyberArkTerm.Core;

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
    public void ImportWindowOpens()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunWithTheme(() =>
        {
            var import = new ImportAccountsDialog(["Prod"], ["WinDomain"], "Prod", "WinDomain", (_, _) => Task.FromResult(new PvwaAccount()));
            Assert.Equal(("Prod", "WinDomain"), (import.SafeBox.Text, import.PlatformBox.Text));
            Assert.False(import.ImportButton.IsEnabled);
            Assert.Equal(8, import.RowsGrid.Columns.Count);
            import.Close();
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
