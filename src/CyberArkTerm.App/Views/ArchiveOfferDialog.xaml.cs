using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Dépôt d'un grand nombre de fichiers : propose de les envoyer dans une seule archive .tar.gz, ou un par un.
/// Fermée sans choisir : rien n'est envoyé.
/// </summary>
public partial class ArchiveOfferDialog : Window
{
    public ArchiveOfferDialog(int files, long bytes, string destination)
    {
        InitializeComponent();
        MessageText.Text = Text.Format(Strings.ArchiveOfferText, files, RemotePath.FormatSize(bytes), destination);
    }

    /// <summary>Envoyer une archive (sinon les fichiers un par un).</summary>
    public bool UseArchive { get; private set; }

    /// <summary>Ne plus proposer l'archive (option des Paramètres décochée).</summary>
    public bool DontOfferAgain => NeverBox.IsChecked == true;

    private void OnArchive(object sender, RoutedEventArgs e)
    {
        UseArchive = true;
        DialogResult = true;
    }

    private void OnFiles(object sender, RoutedEventArgs e) => DialogResult = true;
}
