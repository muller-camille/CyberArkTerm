using System.IO;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core.Ssh;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Avec quoi comparer un fichier du serveur : le même chemin (ou un autre) sur un serveur dont une session SSH est
/// ouverte, ou un fichier de ce poste.
/// </summary>
public partial class CompareDialog : Window
{
    /// <param name="label">Fichier comparé, par ex. « root@srv01 : /etc/app.conf ».</param>
    /// <param name="sessions">Sessions SSH ouvertes ; la première autre que <paramref name="current"/> est proposée.</param>
    public CompareDialog(string label, string path, IReadOnlyList<SshSession> sessions, SshSession current)
    {
        InitializeComponent();
        IntroText.Text = Text.Format(Strings.CompareIntro, label);
        var connected = sessions.Where(s => s.State == SshSessionState.Connected).ToList();
        SessionBox.ItemsSource = connected;
        SessionBox.SelectedItem = connected.FirstOrDefault(s => !ReferenceEquals(s, current)) ?? current;
        RemotePathBox.Text = path;
        Loaded += (_, _) => SessionBox.Focus();
    }

    /// <summary>Serveur choisi (null pour un fichier de ce poste).</summary>
    public SshSession? Session => ServerRadio.IsChecked == true ? SessionBox.SelectedItem as SshSession : null;

    public string RemoteFile => RemotePathBox.Text.Trim();

    public string LocalFile => LocalPathBox.Text.Trim().Trim('"');

    /// <summary>
    /// Explorateur du serveur choisi, ouvert sur le chemin saisi (le même par défaut) : le fichier choisi remplace le
    /// chemin. La connexion SFTP du serveur est ouverte si son onglet Fichiers ne l'a pas encore fait.
    /// </summary>
    private async void OnBrowseServer(object sender, RoutedEventArgs e)
    {
        if (SessionBox.SelectedItem is not SshSession session)
        {
            ShowError(Strings.CompareNoServer);
            return;
        }

        IRemoteFiles browser;
        BrowseServerButton.IsEnabled = false;
        try
        {
            browser = await session.GetBrowserAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowError(Text.Format(Strings.SftpFailed, ex.Message));
            return;
        }
        finally
        {
            BrowseServerButton.IsEnabled = true;
        }

        if (!IsLoaded)
        {
            return;
        }

        var picker = new RemoteFileDialog(session.Label, RemotePathBox.Text, browser.HomeDirectory, browser.BrowseAsync) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPath is { } path)
        {
            RemotePathBox.Text = path;
            ServerRadio.IsChecked = true;
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Strings.CompareLocal.Replace("_", "") };
        if (dialog.ShowDialog(this) == true)
        {
            LocalPathBox.Text = dialog.FileName;
            LocalRadio.IsChecked = true;
        }
    }

    private void OnCompare(object sender, RoutedEventArgs e)
    {
        string? error = ServerRadio.IsChecked == true
            ? Session is null ? Strings.CompareNoServer : RemoteFile.Length == 0 ? Strings.CompareNoPath : null
            : !File.Exists(LocalFile) ? Strings.CompareNoLocalFile : null;
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        DialogResult = true;
    }
}
