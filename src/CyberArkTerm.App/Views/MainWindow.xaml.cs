using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Fenêtre principale : barre d'outils, arbre des sessions à gauche,
/// onglets Accueil / Tous les comptes, connexion PSM ou SSH (PSMP) sur double-clic.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    private readonly PvwaClient _client;
    private readonly AppSettings _settings;
    private readonly string _sessionUser;
    private readonly string _vaultUser;
    private readonly SessionLauncher _launcher = new();
    private readonly DispatcherTimer _searchDebounce;
    private readonly CancellationTokenSource _lifetime = new();
    private List<PvwaAccount> _accounts = [];
    private Dictionary<string, PvwaAccount> _byId = [];
    private ListCollectionView? _view;
    private string _query = "";
    private PvwaAccount? _current;
    private bool _loading;
    private bool _connecting;
    private bool _loggedOff;

    public MainWindow(PvwaClient client, AppSettings settings, string sessionUser, string vaultUser)
    {
        InitializeComponent();
        _client = client;
        _settings = settings;
        _sessionUser = sessionUser;
        _vaultUser = vaultUser;
        Title = $"CyberArkTerm — {client.BaseUri.Host}";
        SessionText.Text = $"{sessionUser} @ {client.BaseUri.Host}";

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilter();
        };

        GroupByBox.DisplayMemberPath = "Key";
        GroupByBox.SelectedValuePath = "Value";
        GroupByBox.ItemsSource = new[]
        {
            new KeyValuePair<string, GroupBy>("Safe", GroupBy.Safe),
            new KeyValuePair<string, GroupBy>("Plateforme", GroupBy.Platform),
            new KeyValuePair<string, GroupBy>("Type de cible", GroupBy.Kind),
        };
        GroupByBox.SelectedValue = settings.GroupBy;

        RefreshRecent();
        UpdateWelcome();
        UpdateActions();
        Loaded += async (_, _) => await LoadAccountsAsync();
    }

    /// <summary>Vrai si la fenêtre a été fermée pour revenir à l'écran de connexion.</summary>
    public bool LogoutRequested { get; private set; }

    private bool HasPsmp => !string.IsNullOrWhiteSpace(_settings.PsmpAddress);

    // ===================== Chargement et filtre =====================

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
            _byId = [];
            foreach (var a in accounts)
            {
                _byId.TryAdd(a.Id, a);
            }

            _current = null;
            _view = new ListCollectionView(_accounts) { Filter = o => AccountFilter.Matches((PvwaAccount)o, _query) };
            AccountsGrid.ItemsSource = _view;
            ApplyFilter();
            RefreshFavorites();
            RefreshQuickResults();
            UpdateWelcome();
            UpdateActions();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Fenêtre en cours de fermeture.
        }
        catch (PvwaException ex) when (ex.IsUnauthorized)
        {
            OnSessionExpired();
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
        RebuildTree();
        UpdateCount();
    }

    private void RebuildTree()
    {
        var groups = AccountGrouping.Group(_accounts.Where(a => AccountFilter.Matches(a, _query)), _settings.GroupBy);
        bool expand = _query.Trim().Length > 0 || groups.Count == 1;
        SessionTree.ItemsSource = groups
            .Select(g => new FolderNode(g.Name, g.Accounts.Select(a => new AccountNode(a)).ToList(), expand))
            .ToList();
    }

    private void UpdateCount()
    {
        int shown = _view?.Count ?? 0;
        CountText.Text = shown == _accounts.Count
            ? string.Format(French, "{0:N0} compte(s)", _accounts.Count)
            : string.Format(French, "{0:N0} compte(s) affiché(s) sur {1:N0}", shown, _accounts.Count);
    }

    private void UpdateWelcome()
    {
        WelcomeText.Text = string.Format(French, "Connecté à {0} en tant que {1} · {2:N0} compte(s) disponible(s)",
            _client.BaseUri.Host, _sessionUser, _accounts.Count);
    }

    private void RefreshFavorites()
    {
        var favorites = _settings.Favorites.Select(id => _byId.GetValueOrDefault(id)).OfType<PvwaAccount>().ToList();
        FavoritesList.ItemsSource = favorites;
        NoFavoritesText.Visibility = favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshRecent()
    {
        RecentList.ItemsSource = _settings.Recent.ToList();
        NoRecentText.Visibility = _settings.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentList.Visibility = _settings.Recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshQuickResults()
    {
        var text = QuickBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            QuickResultsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var results = _accounts.Where(a => AccountFilter.Matches(a, text)).Take(50).ToList();
        QuickResults.ItemsSource = results;
        QuickResultsPanel.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Count > 0)
        {
            QuickResults.SelectedIndex = 0;
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Enter)
        {
            _searchDebounce.Stop();
            ApplyFilter();
            FocusFirstTreeAccount();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
        }
    }

    private void FocusFirstTreeAccount()
    {
        SessionTree.UpdateLayout();
        if (SessionTree.ItemsSource is IReadOnlyList<FolderNode> { Count: > 0 } folders
            && SessionTree.ItemContainerGenerator.ContainerFromItem(folders[0]) is TreeViewItem folder)
        {
            folder.IsExpanded = true;
            folder.UpdateLayout();
            if (folder.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem first)
            {
                first.IsSelected = true;
                first.Focus();
                return;
            }

            folder.Focus();
        }
    }

    private void OnGroupByChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupByBox.SelectedValue is GroupBy groupBy && groupBy != _settings.GroupBy)
        {
            _settings.GroupBy = groupBy;
            SaveSettings();
            RebuildTree();
        }
    }

    private void OnFind(object sender, ExecutedRoutedEventArgs e)
    {
        SideTabs.SelectedIndex = 0;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CanRefresh(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_loading;

    private async void OnRefresh(object sender, ExecutedRoutedEventArgs e) => await LoadAccountsAsync();

    // ===================== Sélection =====================

    private void SetCurrent(PvwaAccount? account)
    {
        _current = account;
        UpdateActions();
    }

    private void UpdateActions()
    {
        bool has = _current is not null && !_connecting;
        ConnectButton.IsEnabled = has;
        AdvancedButton.IsEnabled = has;
        FavoriteButton.IsEnabled = _current is not null;
        SshButton.IsEnabled = has && HasPsmp;
        SshButton.ToolTip = HasPsmp ? "Ouvrir le compte en SSH via le PSMP" : "Renseignez l'adresse du PSMP dans les paramètres";
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        SetCurrent((e.NewValue as AccountNode)?.Account);

    private void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is PvwaAccount account)
        {
            SetCurrent(account);
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: PvwaAccount account })
        {
            SetCurrent(account);
        }
    }

    private void OnTreeItemRightClick(object sender, MouseButtonEventArgs e)
    {
        // Comme dans l'explorateur : le clic droit sélectionne l'élément avant d'ouvrir le menu.
        if (sender is TreeViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
            e.Handled = true;
        }
    }

    // ===================== Ouverture des sessions =====================

    private void OnTreeItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: AccountNode node })
        {
            e.Handled = true;
            _ = ConnectAsync(node.Account, DefaultRequest(node.Account, null));
        }
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SessionTree.SelectedItem is AccountNode node)
        {
            e.Handled = true;
            _ = ConnectAsync(node.Account, DefaultRequest(node.Account, null));
        }
    }

    private void OnAccountItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: PvwaAccount account })
        {
            e.Handled = true;
            _ = ConnectAsync(account, DefaultRequest(account, null));
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: PvwaAccount account })
        {
            e.Handled = true;
            _ = ConnectAsync(account, DefaultRequest(account, null));
        }
    }

    private void OnGridRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: PvwaAccount account })
        {
            e.Handled = true;
            _ = ConnectAsync(account, DefaultRequest(account, null));
        }
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AccountsGrid.SelectedItem is PvwaAccount account)
        {
            e.Handled = true;
            _ = ConnectAsync(account, DefaultRequest(account, null));
        }
    }

    private void OnQuickChanged(object sender, TextChangedEventArgs e) => RefreshQuickResults();

    private void OnQuickKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when QuickResults.SelectedItem is PvwaAccount account:
                e.Handled = true;
                _ = ConnectAsync(account, DefaultRequest(account, null));
                break;
            case Key.Down when QuickResults.Items.Count > 0:
                e.Handled = true;
                QuickResults.SelectedIndex = Math.Min(QuickResults.SelectedIndex + 1, QuickResults.Items.Count - 1);
                QuickResults.ScrollIntoView(QuickResults.SelectedItem);
                break;
            case Key.Up when QuickResults.Items.Count > 0:
                e.Handled = true;
                QuickResults.SelectedIndex = Math.Max(QuickResults.SelectedIndex - 1, 0);
                QuickResults.ScrollIntoView(QuickResults.SelectedItem);
                break;
            case Key.Escape:
                QuickBox.Clear();
                break;
        }
    }

    private void OnRecentDoubleClick(object sender, MouseButtonEventArgs e) => ConnectRecent();

    private void OnRecentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConnectRecent();
        }
    }

    private void ConnectRecent()
    {
        if (RecentList.SelectedItem is not RecentSession recent)
        {
            return;
        }

        if (!_byId.TryGetValue(recent.AccountId, out var account))
        {
            SetStatus($"Le compte {recent.Label} n'est plus disponible (supprimé ou droits retirés).", isError: true);
            return;
        }

        SetCurrent(account);
        var request = recent.Mode == RecentModes.Ssh
            ? DefaultRequest(account, ConnectMode.Ssh) with { RemoteMachine = recent.RemoteMachine }
            : DefaultRequest(account, ConnectMode.Psm) with { Component = recent.Mode, RemoteMachine = recent.RemoteMachine };
        _ = ConnectAsync(account, request, showDialog: false);
    }

    private void OnConnectDefault(object sender, RoutedEventArgs e)
    {
        if (_current is { } account)
        {
            _ = ConnectAsync(account, DefaultRequest(account, null));
        }
    }

    private void OnConnectPsm(object sender, RoutedEventArgs e)
    {
        if (_current is { } account)
        {
            _ = ConnectAsync(account, DefaultRequest(account, ConnectMode.Psm));
        }
    }

    private void OnConnectSsh(object sender, RoutedEventArgs e)
    {
        if (_current is { } account)
        {
            _ = ConnectAsync(account, DefaultRequest(account, ConnectMode.Ssh));
        }
    }

    private void OnConnectAdvanced(object sender, RoutedEventArgs e)
    {
        if (_current is { } account)
        {
            _ = ConnectAsync(account, DefaultRequest(account, null), showDialog: true);
        }
    }

    /// <summary>Requête par défaut : SSH pour Unix si l'utilisateur l'a choisi, sinon PSM avec le composant mémorisé.</summary>
    private ConnectRequest DefaultRequest(PvwaAccount account, ConnectMode? mode)
    {
        var effective = mode
            ?? (_settings.PreferSshForUnix && HasPsmp && AccountClassifier.Classify(account) == AccountKind.Unix
                ? ConnectMode.Ssh
                : ConnectMode.Psm);
        var machines = AccountClassifier.RemoteMachineList(account);
        return new ConnectRequest(effective, _settings.ResolveComponent(account), machines.Count == 1 ? machines[0] : null);
    }

    /// <summary>
    /// Ouvre la session. La fenêtre « Connexion avancée » s'affiche si elle est demandée, s'il faut choisir
    /// la machine cible, ou si le PVWA refuse la demande (motif exigé, composant inconnu...) pour corriger et réessayer.
    /// </summary>
    private async Task ConnectAsync(PvwaAccount account, ConnectRequest request, bool showDialog = false)
    {
        if (_connecting)
        {
            return;
        }

        if (request.Mode == ConnectMode.Ssh && !HasPsmp)
        {
            SetStatus("Connexion SSH indisponible : renseignez l'adresse du PSMP dans les paramètres.", isError: true);
            return;
        }

        showDialog |= AccountClassifier.NeedsRemoteMachine(account) && string.IsNullOrWhiteSpace(request.RemoteMachine);
        string? error = null;
        _connecting = true;
        UpdateActions();
        try
        {
            while (true)
            {
                if (showDialog)
                {
                    var dialog = new ConnectDialog(account, request, _settings, _vaultUser, error) { Owner = this };
                    if (dialog.ShowDialog() != true || dialog.Result is null)
                    {
                        SetStatus("");
                        return;
                    }

                    request = dialog.Result;
                    if (request.RememberComponent)
                    {
                        _settings.RememberComponent(account.PlatformId, request.Component);
                        SaveSettings();
                    }
                }

                try
                {
                    await LaunchAsync(account, request);
                    return;
                }
                catch (PvwaException ex) when (ex.IsUnauthorized)
                {
                    OnSessionExpired();
                    return;
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is PvwaException or ArgumentException or HttpRequestException
                                               or TaskCanceledException or IOException or UnauthorizedAccessException
                                               or Win32Exception or InvalidOperationException)
                {
                    error = ErrorText.Describe(ex);
                    SetStatus("Échec de la connexion : " + error, isError: true);
                    showDialog = true;
                }
            }
        }
        finally
        {
            _connecting = false;
            UpdateActions();
        }
    }

    private async Task LaunchAsync(PvwaAccount account, ConnectRequest request)
    {
        var target = string.IsNullOrWhiteSpace(request.RemoteMachine) ? account.Address : request.RemoteMachine;
        var label = $"{account.UserName}@{target}";
        if (request.Mode == ConnectMode.Psm)
        {
            SetStatus($"Ouverture de la session PSM {label} ({request.Component})…");
            LoadProgress.IsIndeterminate = true;
            LoadProgress.Visibility = Visibility.Visible;
            byte[] rdp;
            try
            {
                rdp = await _client.PsmConnectAsync(account.Id, new PsmConnectOptions
                {
                    ConnectionComponent = request.Component,
                    Reason = request.Reason,
                    TicketingSystemName = request.TicketingSystem,
                    TicketId = request.TicketId,
                    RemoteMachine = request.RemoteMachine,
                }, _lifetime.Token);
            }
            finally
            {
                LoadProgress.Visibility = _loading ? Visibility.Visible : Visibility.Collapsed;
            }

            _launcher.LaunchRdp(rdp, label);
            SetStatus($"Session PSM lancée : {label} ({request.Component})");
            AddRecent(account, label, request.Component, request.RemoteMachine);
        }
        else
        {
            var login = PsmpTarget.BuildLogin(_vaultUser, account, request.RemoteMachine);
            _launcher.LaunchSsh(login, _settings.PsmpAddress, _settings.PsmpPort, label);
            SetStatus($"Session SSH lancée : {label} via {_settings.PsmpAddress}");
            AddRecent(account, label, RecentModes.Ssh, request.RemoteMachine);
        }
    }

    private void AddRecent(PvwaAccount account, string label, string mode, string? remoteMachine)
    {
        _settings.AddRecent(new RecentSession
        {
            AccountId = account.Id,
            Label = label,
            Mode = mode,
            RemoteMachine = remoteMachine,
            When = DateTime.Now,
        });
        SaveSettings();
        RefreshRecent();
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB4, 0xA9))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9F, 0xD3, 0x9F));
    }

    // ===================== Menu contextuel, favoris, copie =====================

    private void OnAccountMenuOpened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        if (_current is null)
        {
            // Clic droit sur un dossier : rien à proposer.
            menu.IsOpen = false;
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Tag as string)
            {
                case "fav":
                    item.Header = _settings.IsFavorite(_current.Id) ? "Retirer des _favoris" : "Ajouter aux _favoris";
                    break;
                case "ssh":
                    item.IsEnabled = HasPsmp;
                    item.ToolTip = HasPsmp ? null : "Renseignez l'adresse du PSMP dans les paramètres";
                    break;
            }
        }
    }

    private void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (_current is not { } account)
        {
            return;
        }

        _settings.ToggleFavorite(account.Id);
        SaveSettings();
        RefreshFavorites();
        SetStatus(_settings.IsFavorite(account.Id)
            ? $"{account.UserName}@{account.Address} ajouté aux favoris"
            : $"{account.UserName}@{account.Address} retiré des favoris");
    }

    private void OnCopyAddress(object sender, RoutedEventArgs e) => CopyAccounts(sender, a => a.Address ?? "");

    private void OnCopyUser(object sender, RoutedEventArgs e) => CopyAccounts(sender, a => a.UserName ?? "");

    private void OnCopyDomainUser(object sender, RoutedEventArgs e) =>
        CopyAccounts(sender, a => a.LogonDomain.Length > 0 ? $"{a.LogonDomain}\\{a.UserName}" : a.UserName ?? "");

    private void CopyAccounts(object sender, Func<PvwaAccount, string> selector)
    {
        // Depuis la liste « Tous les comptes », on copie toute la sélection ; ailleurs, le compte courant.
        var target = (sender as MenuItem)?.Parent is ContextMenu { PlacementTarget: DataGrid grid } ? grid : null;
        var accounts = target is { SelectedItems.Count: > 1 }
            ? target.SelectedItems.Cast<PvwaAccount>().ToList()
            : _current is null ? [] : [_current];
        if (accounts.Count > 0)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, accounts.Select(selector)));
        }
    }

    // ===================== Barre d'outils =====================

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
            SetStatus(string.Format(French, "{0:N0} compte(s) exporté(s) vers {1}", rows.Count, dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Export impossible :\n\n" + ex.Message, "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (new SettingsDialog(_settings) { Owner = this }.ShowDialog() == true)
        {
            SaveSettings();
            UpdateActions();
            SetStatus("Paramètres enregistrés");
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(AppSettings.DefaultPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatus("Préférences non enregistrées : " + e.Message, isError: true);
        }
    }

    private void OnSessionExpired()
    {
        MessageBox.Show(this, "Votre session CyberArk a expiré. Veuillez vous reconnecter.",
            "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Information);
        LogoutRequested = true;
        Close();
    }

    private void OnLogout(object sender, RoutedEventArgs e)
    {
        LogoutRequested = true;
        Close();
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

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
            _launcher.Cleanup();
            _client.Dispose();
            _lifetime.Dispose();
        }

        Close();
    }
}
