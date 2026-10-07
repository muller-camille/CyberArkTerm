using System.IO;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.KeePass;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>Ajout d'un coffre KeePass dans « Mes serveurs », ou modification de son emplacement.</summary>
public partial class KeePassFolderDialog : Window
{
    private readonly KeePassFolder _folder;

    public KeePassFolderDialog(KeePassFolder folder)
    {
        InitializeComponent();
        _folder = folder;
        FileBox.Text = folder.FilePath;
        NameBox.Text = folder.Name;
        KeyFileBox.Text = folder.KeyFilePath ?? "";
        UsesPasswordBox.IsChecked = folder.UsesPassword;
        HeadingText.Text = folder.FilePath.Length == 0 ? Strings.KeePassAddHeading : Strings.KeePassEditHeading;
        // La fenêtre et son avertissement s'affichent d'abord ; le choix du fichier vient du bouton « Parcourir ».
        Loaded += (_, _) =>
        {
            if (folder.FilePath.Length == 0)
            {
                BrowseFileButton.Focus();
            }
            else
            {
                NameBox.Focus();
            }
        };
    }

    private void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.KeePassChooseFile, Filter = Strings.KeePassFilesFilter, CheckFileExists = true, FileName = FileBox.Text,
        };
        if (dialog.ShowDialog(this) == true)
        {
            FileBox.Text = dialog.FileName;
            if (NameBox.Text.Trim().Length == 0)
            {
                NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }
    }

    private void OnBrowseKeyFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Strings.KeePassChooseKeyFile, Filter = Strings.AllFilesFilter, CheckFileExists = true, FileName = KeyFileBox.Text };
        if (dialog.ShowDialog(this) == true)
        {
            KeyFileBox.Text = dialog.FileName;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var file = FileBox.Text.Trim().Trim('"');
        var keyFile = KeyFileBox.Text.Trim().Trim('"');
        string? error = !File.Exists(file) ? Text.Format(Strings.KeePassFileMissing, file)
            : keyFile.Length > 0 && !File.Exists(keyFile) ? Text.Format(Strings.KeePassFileMissing, keyFile)
            : UsesPasswordBox.IsChecked != true && keyFile.Length == 0 ? Strings.KeePassNeedsKey
            : null;
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        _folder.FilePath = Path.GetFullPath(file);
        _folder.Name = NameBox.Text.Trim();
        _folder.KeyFilePath = keyFile.Length > 0 ? Path.GetFullPath(keyFile) : null;
        _folder.UsesPassword = UsesPasswordBox.IsChecked == true;
        DialogResult = true;
    }
}
