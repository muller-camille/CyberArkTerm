using System.IO;
using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Envoi des mêmes fichiers vers plusieurs serveurs : fichiers ou dossiers locaux, dossier de destination (« ~ » = le
/// dossier personnel de chaque compte), sessions SSH destinataires. Chaque serveur devient un élément de la file des
/// transferts, vérifié par SHA-256 comme un envoi ordinaire.
/// </summary>
public partial class MultiUploadDialog : Window
{
    private readonly List<(SshSession Session, CheckBox Box)> _servers = [];

    /// <param name="sessions">Sessions SSH ouvertes ; seules les connectées peuvent être choisies.</param>
    /// <param name="selected">Sessions cochées au départ.</param>
    /// <param name="destination">Dossier proposé (celui affiché dans l'onglet Fichiers, ou « ~ »).</param>
    /// <param name="protocol">Protocole d'envoi des Paramètres (SCP ou SFTP), rappelé dans la fenêtre.</param>
    public MultiUploadDialog(IReadOnlyList<SshSession> sessions, IReadOnlyCollection<SshSession> selected, string destination,
        string protocol, IEnumerable<string>? paths = null)
    {
        InitializeComponent();
        DestinationBox.Text = destination;
        ProtocolText.Text = Text.Format(Strings.MultiUploadProtocol, protocol);
        foreach (var session in sessions)
        {
            bool connected = session.State == SshSessionState.Connected;
            var box = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = connected ? session.Label : Text.Format(Strings.MultiUploadDisconnected, session.Label),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                IsChecked = connected && selected.Contains(session),
                IsEnabled = connected,
                Margin = new Thickness(0, 2, 0, 2),
            };
            _servers.Add((session, box));
            ServersPanel.Children.Add(box);
        }

        foreach (var path in paths ?? [])
        {
            AddPath(path);
        }

        Loaded += (_, _) => DestinationBox.Focus();
    }

    /// <summary>Fichiers et dossiers locaux à envoyer.</summary>
    public IReadOnlyList<string> Paths => FilesList.Items.OfType<string>().ToList();

    public string Destination => DestinationBox.Text.Trim();

    /// <summary>Sessions destinataires, dans l'ordre des onglets.</summary>
    public IReadOnlyList<SshSession> Sessions => _servers.Where(s => s.Box.IsChecked == true).Select(s => s.Session).ToList();

    internal void AddPath(string path)
    {
        if (!FilesList.Items.OfType<string>().Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            FilesList.Items.Add(path);
        }
    }

    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = Strings.MultiUploadAddFiles.Replace("_", "") };
        if (dialog.ShowDialog(this) == true)
        {
            foreach (var file in dialog.FileNames)
            {
                AddPath(file);
            }
        }
    }

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = true, Title = Strings.MultiUploadAddFolder.Replace("_", "") };
        if (dialog.ShowDialog(this) == true)
        {
            foreach (var folder in dialog.FolderNames)
            {
                AddPath(folder);
            }
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        foreach (var item in FilesList.SelectedItems.OfType<string>().ToList())
        {
            FilesList.Items.Remove(item);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            foreach (var path in dropped)
            {
                AddPath(path);
            }
        }
    }

    private void OnSend(object sender, RoutedEventArgs e)
    {
        string? error = Paths.Count == 0 ? Strings.MultiUploadNoFile
            : Paths.FirstOrDefault(p => !File.Exists(p) && !Directory.Exists(p)) is { } gone ? Text.Format(Strings.MultiUploadMissing, gone)
            : Destination.Length == 0 ? Strings.MultiUploadNoDestination
            : Sessions.Count == 0 ? Strings.MultiUploadNoServer
            : null;
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
