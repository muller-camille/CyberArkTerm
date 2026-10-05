using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Première étape de l'import de comptes : choix du fichier CSV (ou enregistrement d'un modèle), safe et plateforme
/// par défaut, et résumé de ce qui sera créé. « Importer » confirme ; rien n'est envoyé au PVWA ici. Les mots de
/// passe lus ne sont jamais convertis en chaînes ; ils sont effacés si la fenêtre est fermée sans confirmer.
/// </summary>
public partial class ImportAccountsDialog : Window
{
    /// <summary>Taille maximale du fichier lu.</summary>
    private const int MaxFileSize = 10 * 1024 * 1024;

    /// <summary>Nombre de lignes en erreur détaillées avant l'import ; toutes figurent dans la fenêtre suivante.</summary>
    private const int ShownLineErrors = 3;

    private AccountImport? _import;
    private string? _path;

    public ImportAccountsDialog(IEnumerable<string> safes, IEnumerable<string> platforms, string? safe, string? platform)
    {
        InitializeComponent();
        SafeBox.ItemsSource = safes.ToList();
        SafeBox.Text = safe ?? "";
        PlatformBox.ItemsSource = platforms.ToList();
        PlatformBox.Text = platform ?? "";
        // Branchés après le chargement : les valeurs ci-dessus ne relisent pas un fichier qui n'est pas encore choisi.
        SafeBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Reload()));
        PlatformBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Reload()));
        // Fermée sans confirmer : les mots de passe lus sont effacés.
        Closed += (_, _) => _import?.Clear();
    }

    /// <summary>
    /// Lignes confirmées par « Importer », à passer à <see cref="ImportProgressDialog"/> ; null si la fenêtre a été
    /// fermée sans confirmer. L'appelant devient responsable de leur effacement (<see cref="AccountImport.Clear"/>).
    /// </summary>
    public AccountImport? Confirmed { get; private set; }

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Strings.ImportTitle, Filter = Strings.ImportOpenFilter };
        if (dialog.ShowDialog(this) == true)
        {
            _path = dialog.FileName;
            FileText.Text = _path;
            FileText.ToolTip = _path;
            Reload();
        }
    }

    private void OnSaveTemplate(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = Strings.ImportTemplate.Replace("_", ""), Filter = Strings.ExportFilter, FileName = Strings.ImportTemplateFileName + ".csv" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // BOM UTF-8 pour qu'Excel lise correctement les accents ; séparateur de la région Windows.
            File.WriteAllText(dialog.FileName, AccountCsv.Template(CsvExporter.DefaultSeparator(System.Globalization.CultureInfo.CurrentCulture)),
                new UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(Text.Format(Strings.ImportFileError, ex.Message));
        }
    }

    /// <summary>Relit le fichier choisi avec le safe et la plateforme par défaut actuels.</summary>
    private void Reload()
    {
        if (_path is null)
        {
            return;
        }

        _import?.Clear();
        _import = null;
        ErrorMessage.Visibility = Visibility.Collapsed;
        byte[]? bytes = null;
        char[]? text = null;
        try
        {
            if (new FileInfo(_path).Length > MaxFileSize)
            {
                ShowError(Strings.ImportFileTooLarge);
                return;
            }

            bytes = File.ReadAllBytes(_path);
            text = AccountCsv.Decode(bytes);
            _import = AccountCsv.Parse(text, SafeBox.Text, PlatformBox.Text);
            if (_import.Error is { } error)
            {
                ShowError(error);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(Text.Format(Strings.ImportFileError, ex.Message));
        }
        finally
        {
            // Le fichier peut contenir des mots de passe : copies effacées dès la lecture finie.
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }

            if (text is not null)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(text.AsSpan()));
            }

            ShowSummary();
        }
    }

    private void ShowSummary()
    {
        var rows = _import?.Rows ?? [];
        int ready = _import?.Ready ?? 0;
        var errors = rows.Where(r => r.Error is not null).ToList();
        SummaryText.Text = Text.Format(Strings.ImportSummary, ready, errors.Count);
        SummaryText.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var lines = errors.Take(ShownLineErrors).Select(r => Text.Format(Strings.ImportLineError, r.Line, r.Error)).ToList();
        if (errors.Count > ShownLineErrors)
        {
            lines.Add(Text.Format(Strings.ImportMoreErrors, errors.Count - ShownLineErrors));
        }

        LineErrorsText.Text = string.Join(Environment.NewLine, lines);
        LineErrorsText.Visibility = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PasswordWarning.Visibility = rows.Any(r => r.HasPassword) ? Visibility.Visible : Visibility.Collapsed;
        ImportButton.IsEnabled = ready > 0;
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (_import is { Ready: > 0 } import)
        {
            // Retiré de _import avant la fermeture : Closed n'efface que ce qui n'a pas été confirmé.
            Confirmed = import;
            _import = null;
            DialogResult = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }
}
