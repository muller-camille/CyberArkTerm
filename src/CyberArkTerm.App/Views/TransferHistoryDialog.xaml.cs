using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

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

    /// <summary>Texte de la colonne « Résultat ».</summary>
    public static string Result(TransferRecord record) => record.State switch
    {
        TransferState.Done when record.Different > 0 => Text.Format(Strings.QueueStateDifferent, record.Different),
        TransferState.Done => Text.Format(Strings.HistoryDone, record.Identical),
        TransferState.Failed => "✗ " + record.Error,
        _ => Strings.QueueStateCancelled,
    };

    private void BuildColumns()
    {
        void Add(string header, Binding binding, double width = 1, DataGridLengthUnitType unit = DataGridLengthUnitType.Star) =>
            RecordsGrid.Columns.Add(new DataGridTextColumn { Header = header, Binding = binding, Width = new DataGridLength(width, unit) });

        Add(Strings.HistoryColTime, new Binding(nameof(TransferRecord.Time)) { Converter = new LocalTimeConverter() }, 1, DataGridLengthUnitType.Auto);
        Add(Strings.ChecksColDirection, new Binding(nameof(TransferRecord.Upload)) { Converter = new DirectionConverter() }, 1, DataGridLengthUnitType.Auto);
        Add(Strings.HistoryColServer, new Binding(nameof(TransferRecord.Server)));
        Add(Strings.HistoryColItem, new Binding(nameof(TransferRecord.Label)), 1.2);
        Add(Strings.HistoryColDestination, new Binding(nameof(TransferRecord.Destination)), 1.5);
        Add(Strings.HistoryColFiles, new Binding(nameof(TransferRecord.FileCount)), 1, DataGridLengthUnitType.Auto);
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
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { record.Destination }, UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            MessageBox.Show(this, ex.Message, Strings.HistoryTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Text.Format(Strings.HistoryClearConfirm, _history.Records.Count), Strings.HistoryTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
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

    private sealed class ResultConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is TransferRecord record ? Result(record) : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
