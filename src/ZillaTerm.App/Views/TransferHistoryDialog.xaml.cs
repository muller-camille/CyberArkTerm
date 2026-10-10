using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ZillaTerm.App.Localization;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.App.Views;

/// <summary>
/// Historique des transferts de l'onglet Fichiers : date, sens, serveur, élément, destination, nombre de fichiers et
/// résultat, filtrable par sens. Pour un transfert : ses sommes de contrôle, et le dossier d'un téléchargement.
/// </summary>
public partial class TransferHistoryDialog : Window
{
    private readonly TransferHistory _history;
    private readonly Action _save;

    /// <param name="save">Enregistre l'historique (après « Effacer »).</param>
    public TransferHistoryDialog(TransferHistory history, Action save)
    {
        InitializeComponent();
        _history = history;
        _save = save;
        BuildColumns();
        // Branché après le chargement : la sélection initiale du filtre ne doit pas remplir une grille sans colonnes.
        FilterBox.SelectionChanged += (_, _) => ShowRecords();
        RecordsGrid.SelectionChanged += (_, _) => UpdateButtons();
        ShowRecords();
    }

    /// <summary>Texte de la colonne « Résultat » (le même que dans la file des transferts).</summary>
    public static string Result(TransferRecord record) => record.State switch
    {
        TransferState.Done when record.Different > 0 => Text.Format(Strings.QueueStateDifferent, record.Different),
        TransferState.Done when record.Files.Count(f => !f.Verified && !f.Failed && !f.Interrupted) is > 0 and var unverified =>
            Text.Format(Strings.QueueStateUnverified, unverified, record.Files.Count),
        TransferState.Done when record.Files.Count > 0 => Text.Format(Strings.QueueStateVerified, record.Identical, record.Files.Count),
        TransferState.Done => Strings.QueueStateDone,
        TransferState.Failed => "✗ " + record.Error,
        _ => Strings.QueueStateCancelled,
    };

    /// <summary>Transfert en échec ou fichier différent de l'original : ligne en rouge.</summary>
    public static bool HasProblem(TransferRecord record) => record.State == TransferState.Failed || record.Different > 0;

    private void BuildColumns()
    {
        // Texte tronqué (« … ») et complet dans l'infobulle : serveur, élément et destination peuvent être longs.
        void Add(string header, Binding binding, double width = 1, DataGridLengthUnitType unit = DataGridLengthUnitType.Star)
        {
            var cell = new Style(typeof(TextBlock));
            cell.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            cell.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(TextBlock.Text)) { RelativeSource = RelativeSource.Self }));
            RecordsGrid.Columns.Add(new DataGridTextColumn { Header = header, Binding = binding, Width = new DataGridLength(width, unit), ElementStyle = cell });
        }

        var row = new Style(typeof(DataGridRow));
        var problem = new DataTrigger { Binding = new Binding(".") { Converter = new ProblemConverter() }, Value = true };
        problem.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("ErrorBrush")));
        problem.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
        row.Triggers.Add(problem);
        RecordsGrid.RowStyle = row;

        Add(Strings.HistoryColTime, new Binding(nameof(TransferRecord.Time)) { Converter = new LocalTimeConverter() }, 1, DataGridLengthUnitType.Auto);
        Add(Strings.ChecksColDirection, new Binding(nameof(TransferRecord.Upload)) { Converter = new DirectionConverter() }, 1, DataGridLengthUnitType.Auto);
        Add(Strings.HistoryColServer, new Binding(nameof(TransferRecord.Server)));
        Add(Strings.HistoryColItem, new Binding(nameof(TransferRecord.Label)), 1.2);
        Add(Strings.HistoryColDestination, new Binding(nameof(TransferRecord.Destination)), 1.5);
        Add(Strings.HistoryColFiles, new Binding(nameof(TransferRecord.FileCount)), 1, DataGridLengthUnitType.Auto);
        // Téléchargements : toujours par la connexion SFTP de l'onglet.
        Add(Strings.HistoryColProtocol, new Binding(nameof(TransferRecord.Protocol)) { TargetNullValue = "SFTP" }, 1, DataGridLengthUnitType.Auto);
        Add(Strings.ChecksColResult, new Binding(".") { Converter = new ResultConverter() }, 1.5);
    }

    private void ShowRecords()
    {
        var records = FilterBox.SelectedIndex switch
        {
            1 => _history.Records.Where(r => r.Upload),
            2 => _history.Records.Where(r => !r.Upload),
            _ => _history.Records,
        };
        RecordsGrid.ItemsSource = records.ToList();
        EmptyText.Visibility = _history.Records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = _history.Records.Count > 0;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var record = RecordsGrid.SelectedItem as TransferRecord;
        ChecksButton.IsEnabled = record is { Files.Count: > 0 };
        OpenFolderButton.IsEnabled = record is { Upload: false } && Directory.Exists(record.Destination);
    }

    private void OnChecks(object sender, RoutedEventArgs e)
    {
        if (RecordsGrid.SelectedItem is TransferRecord { Files.Count: > 0 } record)
        {
            new TransferChecksDialog(record.Files) { Owner = this }.ShowDialog();
        }
    }

    private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(RecordsGrid, (DependencyObject)e.OriginalSource) is DataGridRow)
        {
            OnChecks(sender, e);
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        // Seulement un dossier existant, passé en argument à l'Explorateur (jamais exécuté comme un programme).
        if (RecordsGrid.SelectedItem is not TransferRecord { Upload: false } record || !Directory.Exists(record.Destination))
        {
            return;
        }

        try
        {
            Services.WindowsExplorer.OpenFolder(record.Destination);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, Strings.HistoryTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Destructive(this, Strings.HistoryTitle, Strings.HistoryClearHeading, Strings.HistoryClearAction,
                subject: Text.Format(Strings.HistoryClearCount, _history.Records.Count), message: Strings.HistoryClearMessage))
        {
            return;
        }

        _history.Clear();
        _save();
        ShowRecords();
    }

    private sealed class LocalTimeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is DateTime time ? time.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class DirectionConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is true ? Strings.ChecksUploaded : Strings.ChecksDownloaded;

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class ProblemConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is TransferRecord record && HasProblem(record);

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class ResultConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is TransferRecord record ? Result(record) : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
