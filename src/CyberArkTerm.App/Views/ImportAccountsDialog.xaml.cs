using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Localization;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Import de comptes depuis un fichier CSV : aperçu des lignes, puis création une par une (<c>POST /API/Accounts</c>)
/// avec le résultat de chacune. Les mots de passe du fichier ne sont jamais affichés ni convertis en chaînes, et ils
/// sont effacés de la mémoire après l'envoi de leur ligne ou à la fermeture.
/// </summary>
public partial class ImportAccountsDialog : Window
{
    /// <summary>Taille maximale du fichier lu.</summary>
    private const int MaxFileSize = 10 * 1024 * 1024;

    private readonly Func<NewAccount, CancellationToken, Task<PvwaAccount>> _create;
    private readonly CancellationTokenSource _closing = new();
    private AccountImport? _import;
    private List<ImportRowView> _rows = [];
    private string? _path;
    private bool _running;
    private bool _stopRequested;
    private bool _closeWhenStopped;

    public ImportAccountsDialog(IEnumerable<string> safes, IEnumerable<string> platforms, string? safe, string? platform,
        Func<NewAccount, CancellationToken, Task<PvwaAccount>> create)
    {
        InitializeComponent();
        _create = create;
        SafeBox.ItemsSource = safes.ToList();
        SafeBox.Text = safe ?? "";
        PlatformBox.ItemsSource = platforms.ToList();
        PlatformBox.Text = platform ?? "";
        // Branchés après le chargement : les valeurs ci-dessus ne relisent pas un fichier qui n'est pas encore choisi.
        SafeBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Reload()));
        PlatformBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Reload()));
        BuildColumns();
        Closed += (_, _) =>
        {
            _closing.Cancel();
            _import?.Clear();
        };
    }

    /// <summary>Nombre de comptes créés (la liste des comptes est à recharger s'il n'est pas nul).</summary>
    public int Created { get; private set; }

    /// <summary>Le PVWA a répondu que la session avait expiré : la fenêtre principale doit se déconnecter.</summary>
    public bool SessionExpired { get; private set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Pendant l'import : arrêt après la ligne en cours, puis fermeture.
        if (_running)
        {
            _stopRequested = _closeWhenStopped = true;
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private void BuildColumns()
    {
        void Add(string header, string path, double width = 1, DataGridLengthUnitType unit = DataGridLengthUnitType.Star) =>
            RowsGrid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = new DataGridLength(width, unit) });

        Add(Strings.ImportColLine, nameof(ImportRowView.Line), 1, DataGridLengthUnitType.Auto);
        Add(CoreStrings.ColumnSafe, nameof(ImportRowView.Safe));
        Add(CoreStrings.ColumnPlatform, nameof(ImportRowView.Platform));
        Add(CoreStrings.ColumnServer, nameof(ImportRowView.Address), 1.5);
        Add(CoreStrings.ColumnUser, nameof(ImportRowView.UserName));
        Add(CoreStrings.ColumnDomain, nameof(ImportRowView.LogonDomain), 0.7);
        Add(Strings.ImportColPassword, nameof(ImportRowView.Password), 1, DataGridLengthUnitType.Auto);
        Add(Strings.ImportColResult, nameof(ImportRowView.Status), 2);
    }

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Strings.ImportTitle, Filter = Strings.ImportOpenFilter };
        if (dialog.ShowDialog(this) == true)
        {
            _path = dialog.FileName;
            FileText.Text = _path;
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

    /// <summary>Relit le fichier choisi avec le safe et la plateforme par défaut actuels (avant l'import seulement).</summary>
    private void Reload()
    {
        if (_path is null || _running || Created > 0)
        {
            return;
        }

        _import?.Clear();
        _import = null;
        _rows = [];
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

            _rows = _import.Rows.Select(r => new ImportRowView(r)).ToList();
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

            RowsGrid.ItemsSource = _rows;
            int ready = _import?.Ready ?? 0;
            SummaryText.Text = _rows.Count == 0 ? "" : Text.Format(Strings.ImportSummary, ready, _rows.Count - ready);
            PasswordWarning.Visibility = _rows.Any(r => r.Row.HasPassword) ? Visibility.Visible : Visibility.Collapsed;
            ImportButton.IsEnabled = ready > 0;
            ProgressText.Text = "";
        }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (_running || _import is null)
        {
            return;
        }

        var pending = _rows.Where(r => r.Row.Account is not null).ToList();
        int done = 0, refused = 0;
        SetRunning(true);
        try
        {
            foreach (var row in pending)
            {
                if (_stopRequested)
                {
                    break;
                }

                try
                {
                    await _create(row.Row.Account!, _closing.Token);
                    row.Status = Strings.ImportStatusCreated;
                    Created++;
                }
                catch (PvwaException ex) when (ex.IsUnauthorized)
                {
                    SessionExpired = true;
                    break;
                }
                catch (OperationCanceledException) when (_closing.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    row.Status = Text.Format(Strings.ImportStatusRefused, ErrorText.Describe(ex));
                    refused++;
                }
                finally
                {
                    if (row.Row.Account!.Secret is { } secret)
                    {
                        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(secret.AsSpan()));
                    }

                    done++;
                    ProgressText.Text = Text.Format(Strings.ImportProgress, done, pending.Count, Created, refused);
                }
            }
        }
        finally
        {
            SetRunning(false);
        }

        ProgressText.Text = done < pending.Count
            ? Text.Format(Strings.ImportStopped, Created, refused)
            : Text.Format(Strings.ImportDone, Created, refused);
        // Les mots de passe ont été effacés : un second import n'est possible qu'en rechoisissant le fichier.
        ImportButton.IsEnabled = false;
        if (SessionExpired || _closeWhenStopped)
        {
            Close();
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => _stopRequested = true;

    private void SetRunning(bool running)
    {
        _running = running;
        _stopRequested &= running;
        ImportButton.IsEnabled = !running;
        ChooseButton.IsEnabled = SafeBox.IsEnabled = PlatformBox.IsEnabled = !running && Created == 0;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }

    /// <summary>Ligne du tableau ; le mot de passe n'est jamais affiché, seulement sa présence.</summary>
    public sealed class ImportRowView(ImportRow row) : INotifyPropertyChanged
    {
        private string _status = row.Error is { } error ? Text.Format(Strings.ImportStatusError, error) : Strings.ImportStatusReady;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ImportRow Row { get; } = row;

        public int Line => Row.Line;

        public string Safe => Row.Safe;

        public string Platform => Row.Platform;

        public string Address => Row.Address;

        public string UserName => Row.UserName;

        public string LogonDomain => Row.LogonDomain;

        public string Password => Row.HasPassword ? "••••••" : "";

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }
}
