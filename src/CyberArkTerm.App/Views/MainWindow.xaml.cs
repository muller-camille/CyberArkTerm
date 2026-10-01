using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CyberArkTerm.Core;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

public partial class MainWindow : Window
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly PvwaClient _client;
    private readonly DispatcherTimer _searchDebounce;
    private readonly CancellationTokenSource _lifetime = new();
    private List<PvwaAccount> _accounts = [];
    private ListCollectionView? _view;
    private string _query = "";
    private bool _loading;
    private bool _loggedOff;

    public MainWindow(PvwaClient client, string sessionUser)
    {
        InitializeComponent();
        _client = client;
        SessionText.Text = $"{sessionUser} @ {client.BaseUri.Host}";

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilter();
        };

        Loaded += async (_, _) => await LoadAccountsAsync();
    }

    /// <summary>Vrai si la fenêtre a été fermée pour revenir à l'écran de connexion.</summary>
    public bool LogoutRequested { get; private set; }

    private async Task LoadAccountsAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        CommandManager.InvalidateRequerySuggested();
        LoadProgress.Value = 0;
        LoadProgress.IsIndeterminate = true;
        LoadProgress.Visibility = Visibility.Visible;
        CountText.Text = "Chargement des comptes…";

        var progress = new Progress<(int Loaded, int Total)>(p =>
        {
            LoadProgress.IsIndeterminate = false;
            LoadProgress.Maximum = Math.Max(p.Total, 1);
            LoadProgress.Value = p.Loaded;
            CountText.Text = string.Format(French, "Chargement des comptes… {0:N0} / {1:N0}", p.Loaded, p.Total);
        });

        try
        {
            var accounts = await _client.GetAccountsAsync(progress, _lifetime.Token);
            accounts.Sort((a, b) =>
            {
                int c = StringComparer.OrdinalIgnoreCase.Compare(a.Address, b.Address);
                return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.UserName, b.UserName);
            });
            _accounts = accounts;
            _view = new ListCollectionView(_accounts) { Filter = o => AccountFilter.Matches((PvwaAccount)o, _query) };
            AccountsGrid.ItemsSource = _view;
            UpdateCount();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Fenêtre en cours de fermeture.
        }
        catch (PvwaException ex) when (ex.IsUnauthorized)
        {
            MessageBox.Show(this, "Votre session CyberArk a expiré. Veuillez vous reconnecter.",
                "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            LogoutRequested = true;
            Close();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CountText.Text = "Échec du chargement.";
            MessageBox.Show(this, "Impossible de charger les comptes :\n\n" + ErrorText.Describe(ex),
                "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _loading = false;
            LoadProgress.Visibility = Visibility.Collapsed;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ApplyFilter()
    {
        _query = SearchBox.Text;
        _view?.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        int shown = _view?.Count ?? 0;
        CountText.Text = shown == _accounts.Count
            ? string.Format(French, "{0:N0} compte(s)", _accounts.Count)
            : string.Format(French, "{0:N0} compte(s) affiché(s) sur {1:N0}", shown, _accounts.Count);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnFind(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CanRefresh(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_loading;

    private async void OnRefresh(object sender, ExecutedRoutedEventArgs e) => await LoadAccountsAsync();

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var rows = _view?.Cast<PvwaAccount>().ToList() ?? [];
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "Aucun compte à exporter.", "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exporter la liste des comptes",
            Filter = "Fichier CSV (*.csv)|*.csv",
            FileName = $"comptes-psm-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // BOM UTF-8 pour qu'Excel détecte correctement les accents.
            using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            CsvExporter.Write(writer, rows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Export impossible :\n\n" + ex.Message, "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopyAddress(object sender, RoutedEventArgs e) => CopySelected(a => a.Address ?? "");

    private void OnCopyUser(object sender, RoutedEventArgs e) => CopySelected(a => a.UserName ?? "");

    private void OnCopyDomainUser(object sender, RoutedEventArgs e) =>
        CopySelected(a => a.LogonDomain.Length > 0 ? $"{a.LogonDomain}\\{a.UserName}" : a.UserName ?? "");

    private void CopySelected(Func<PvwaAccount, string> selector)
    {
        var values = AccountsGrid.SelectedItems.Cast<PvwaAccount>().Select(selector).ToList();
        if (values.Count > 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, values));
        }
    }

    private void OnLogout(object sender, RoutedEventArgs e)
    {
        LogoutRequested = true;
        Close();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_loggedOff || e.Cancel)
        {
            return;
        }

        // Fermeture de la session PVWA avant de quitter (au plus 5 s d'attente).
        e.Cancel = true;
        _loggedOff = true;
        IsEnabled = false;
        _lifetime.Cancel();
        _searchDebounce.Stop();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _client.LogoffAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Au pire, la session expirera d'elle-même côté PVWA.
        }
        finally
        {
            _client.Dispose();
            _lifetime.Dispose();
        }

        Close();
    }
}
