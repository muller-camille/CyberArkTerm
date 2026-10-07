using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.App.Services.KeePass;
using CyberArkTerm.App.Services.Rdp;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Localization;
using CyberArkTerm.Core.Rdp;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Fenêtre principale : barre d'outils, arbre des sessions à gauche,
/// onglet Accueil, connexion PSM ou SSH (PSMP) sur double-clic.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Client du PVWA ; null en accès d'urgence (sans CyberArk, coffres KeePass seulement).</summary>
    private readonly PvwaClient? _client;
    private readonly AppSettings _settings;
    private readonly string _sessionUser;
    private readonly string _vaultUser;
    private readonly SessionLauncher _launcher = new();
    private readonly DispatcherTimer _searchDebounce;
    private readonly CancellationTokenSource _lifetime = new();
    private List<PvwaAccount> _accounts = [];

    /// <summary>Comptes qui répondent à la recherche de « Disponibles » (affichés, comptés, exportés).</summary>
    private List<PvwaAccount> _shown = [];
    private Dictionary<string, PvwaAccount> _byId = [];
    private string _query = "";
    private PvwaAccount? _current;
    private SavedSession? _currentSaved;
    private bool _loading;

    /// <summary>Dernier chargement des comptes en échec : les connexions récentes ne restent pas grisées.</summary>
    private bool _loadFailed;

    // Comptes du PVWA chargés au moins une fois : avant, un compte absent n'est pas encore un compte disparu.
    private bool _accountsLoaded;
    private bool _connecting;
    private bool _loggedOff;

    /// <param name="client">Client du PVWA connecté ; null pour l'accès d'urgence sans CyberArk.</param>
    internal MainWindow(PvwaClient? client, AppSettings settings, string sessionUser, string vaultUser, KeePassManager keePass)
    {
        InitializeComponent();
        _client = client;
        _settings = settings;
        _sessionUser = sessionUser;
        _vaultUser = vaultUser;
        _keePass = keePass;
        Title = client is null ? Strings.EmergencyTitle : $"CyberArkTerm — {client.BaseUri.Host}";
        SessionText.Text = client is null ? Strings.EmergencySession : $"{sessionUser} @ {client.BaseUri.Host}";
        UpdateDebugLogIndicator();

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
            new KeyValuePair<string, GroupBy>(CoreStrings.ColumnSafe, GroupBy.Safe),
            new KeyValuePair<string, GroupBy>(CoreStrings.ColumnPlatform, GroupBy.Platform),
            new KeyValuePair<string, GroupBy>(Strings.GroupByKind, GroupBy.Kind),
        };
        GroupByBox.SelectedValue = settings.GroupBy;

        _psmpUi = new SshInteraction(this, settings, SaveSettings);
        _directUi = new SshInteraction(this, settings, SaveSettings, direct: true);
        _keePass.Changed += OnKeePassChanged;
        SystemEvents.SessionSwitch += OnWindowsSessionSwitch;
        Closed += (_, _) =>
        {
            CloseAllVncSessions();
            _keePass.Changed -= OnKeePassChanged;
            SystemEvents.SessionSwitch -= OnWindowsSessionSwitch;
        };

        CompareWindow.CleanTemporaryFiles();
        CyberArkTerm.App.Terminal.TerminalAppearance.Apply(settings.TerminalTheme, settings.TerminalFontSize, settings.TerminalRightClickPastes);
        FilesPanel.Initialize(settings, SaveSettings);
        FilesPanel.OpenSessions = () => MainTabs.Items.OfType<TabItem>().Select(t => t.Tag).OfType<RemoteSession>().ToList();
        FilesPanel.ShowTerminalRequested += ShowTerminal;
        if (IsOffline)
        {
            // Accès d'urgence : ni comptes CyberArk ni PSM, seulement les coffres KeePass de l'onglet « Courants ».
            AvailableTab.Visibility = Visibility.Collapsed;
            QuickPanel.Visibility = HomeLists.Visibility = NewFolderButton.Visibility = Visibility.Collapsed;
            ImportServersButton.Visibility = ExportServersButton.Visibility = Visibility.Collapsed;
            SharedListsButton.Visibility = SharedSeparator.Visibility = Visibility.Collapsed;
            NoSavedText.Text = Strings.NoKeePassHelp;
            SideTabs.SelectedItem = CurrentTab;
            CountText.Text = "";
        }
        else
        {
            RefreshRecent();
            StartSharedLists();
        }

        RefreshSaved();
        UpdateWelcome();
        UpdateActions();
        StartKeepAlive();
        Loaded += async (_, _) =>
        {
            _ = CheckForUpdateAsync();
            if (!IsOffline)
            {
                await LoadAccountsAsync();
            }
        };
    }

    /// <summary>
    /// Option des Paramètres (désactivée par défaut) : une recherche de nouvelle version par jour au plus, discrète :
    /// un lien dans la barre d'état si une version plus récente existe ; aucune erreur affichée.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        if (UpdateService.Available is { } known)
        {
            ShowUpdateLink(known);
            return;
        }

        if (!_settings.CheckForUpdates || DateTime.UtcNow - _settings.LastUpdateCheck < TimeSpan.FromDays(1))
        {
            return;
        }

        try
        {
            var latest = await UpdateChecker.CheckAsync(UpdateService.Http, _lifetime.Token);
            _settings.LastUpdateCheck = DateTime.UtcNow;
            SaveSettings();
            if (UpdateChecker.IsNewer(latest.Version, UpdateChecker.CurrentVersion))
            {
                UpdateService.Available = latest;
                ShowUpdateLink(latest);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            DebugLog.Write("update", "Recherche de nouvelle version impossible", ex);
        }
    }

    private void ShowUpdateLink(UpdateInfo info)
    {
        UpdateLinkText.Text = Text.Format(Strings.UpdateAvailableLink, info.Version);
        UpdateLink.Visibility = Visibility.Visible;
    }

    private void OnUpdateLink(object sender, RoutedEventArgs e) => ShowAbout();

    private void OnAbout(object sender, RoutedEventArgs e) => ShowAbout();

    private void ShowAbout()
    {
        new AboutWindow(_settings, SaveSettings) { Owner = this }.ShowDialog();
        if (UpdateService.Available is { } info && UpdateChecker.IsNewer(info.Version, UpdateChecker.CurrentVersion))
        {
            ShowUpdateLink(info);
        }
    }

    /// <summary>Accès d'urgence, sans connexion à CyberArk.</summary>
    private bool IsOffline => _client is null;

    /// <summary>Client du PVWA, pour les actions qui n'existent qu'avec CyberArk.</summary>
    private PvwaClient Client => _client ?? throw new InvalidOperationException(Strings.EmergencyWelcome);

    /// <summary>Vrai si la fenêtre a été fermée pour revenir à l'écran de connexion.</summary>
    public bool LogoutRequested { get; private set; }

    private bool HasPsmp => !string.IsNullOrWhiteSpace(_settings.PsmpAddress);

    // ===================== Chargement et filtre =====================

    private async Task LoadAccountsAsync()
    {
        if (_loading || IsOffline)
        {
            return;
        }

        _loading = true;
        _loadFailed = false;
        RefreshRecent();
        CommandManager.InvalidateRequerySuggested();
        LoadProgress.Value = 0;
        LoadProgress.IsIndeterminate = true;
        LoadProgress.Visibility = Visibility.Visible;
        CountText.Text = Strings.LoadingAccounts;

        var progress = new Progress<(int Loaded, int Total)>(p =>
        {
            LoadProgress.IsIndeterminate = false;
            LoadProgress.Maximum = Math.Max(p.Total, 1);
            LoadProgress.Value = p.Loaded;
            CountText.Text = Text.Format(Strings.LoadingAccountsProgress, p.Loaded, p.Total);
        });

        try
        {
            var accounts = await Client.GetAccountsAsync(progress, _lifetime.Token);
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
            _accountsLoaded = true;
            ApplyFilter();
            SessionLibrary.MigrateFavorites(_settings, _byId, Client.BaseUri.Host);
            SaveSettings();
            RefreshSaved();
            RefreshRecent();
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
            CountText.Text = Strings.LoadFailedShort;
            // Un clic sur une connexion récente expliquera alors que les comptes n'ont pas pu être chargés.
            _loadFailed = true;
            RefreshRecent();
            MessageBox.Show(this, Text.Format(Strings.LoadFailed, ErrorText.Describe(ex)),
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
        _shown = _accounts.Where(a => AccountFilter.Matches(a, _query)).ToList();
        RebuildTree();
        UpdateCount();
    }

    private void RebuildTree()
    {
        var groups = AccountGrouping.Group(_shown, _settings.GroupBy);
        bool expand = _query.Trim().Length > 0 || groups.Count == 1;
        SessionTree.ItemsSource = groups
            .Select(g => new FolderNode(g.Name, g.Accounts.Select(a => new AccountNode(a)).ToList(), expand))
            .ToList();
    }

    private void UpdateCount()
    {
        int shown = _shown.Count;
        CountText.Text = shown == _accounts.Count
            ? Text.Format(Strings.AccountCount, _accounts.Count)
            : Text.Format(Strings.AccountCountFiltered, shown, _accounts.Count);
    }

    private void UpdateWelcome()
    {
        WelcomeText.Text = _client is null ? Strings.EmergencyWelcome : Text.Format(Strings.Welcome, _client.BaseUri.Host, _sessionUser, _accounts.Count);
    }

    /// <summary>
    /// Connexions récentes : grisées tant que les comptes du PVWA se chargent (un clic ne pourrait que répondre, à
    /// tort, que le compte n'existe plus). Après un échec, elles redeviennent cliquables pour en donner la raison.
    /// </summary>
    private void RefreshRecent()
    {
        bool usable = _accountsLoaded || _loadFailed;
        RecentList.ItemsSource = _settings.Recent.ToList();
        NoRecentText.Visibility = _settings.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentList.Visibility = _settings.Recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentList.IsEnabled = usable;
        RecentList.Opacity = usable ? 1 : 0.45;
        RecentLoadingText.Visibility = usable || _settings.Recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Compte introuvable : comptes pas encore chargés, chargement échoué, ou compte vraiment disparu.</summary>
    private string MissingAccountText(string gone) =>
        _accountsLoaded ? gone : _loading ? Strings.AccountsStillLoading : Strings.AccountsNotLoaded;

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
        // Dans « Courants », Ctrl+F cherche parmi les serveurs courants ; ailleurs, parmi tous les comptes.
        var box = SideTabs.SelectedItem == CurrentTab ? SavedSearchBox : SearchBox;
        if (box == SearchBox)
        {
            SideTabs.SelectedIndex = 0;
        }

        box.Focus();
        box.SelectAll();
    }

    private void CanRefresh(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_loading && !IsOffline;

    private async void OnRefresh(object sender, ExecutedRoutedEventArgs e)
    {
        if (_sharedLists.Count > 0)
        {
            _ = ReloadSharedAsync(_sharedLists.ToList());
        }

        await LoadAccountsAsync();
    }

    // ===================== Sélection =====================

    /// <summary>
    /// Cible du bouton « Connecter » : la dernière sélection faite, compte CyberArk ou entrée KeePass (jamais les deux,
    /// pour qu'une entrée KeePass choisie plus tôt ne prenne pas la place du compte qu'on vient de sélectionner).
    /// </summary>
    private void SetCurrent(PvwaAccount? account, SavedSession? saved = null, KeePassEntryNode? keePass = null)
    {
        _current = account;
        _currentSaved = saved;
        _currentKeePass = keePass;
        UpdateActions();
    }

    private void SetCurrentFrom(object? item)
    {
        switch (item)
        {
            case KeePassEntryNode entry:
                SetCurrent(null, keePass: entry);
                break;
            case SavedSessionNode node:
                SetCurrent(node.Account, node.Session);
                break;
            case SharedServerNode node:
                SetCurrent(node.Account, node.Session);
                break;
            case AccountNode node:
                SetCurrent(node.Account);
                break;
            case PvwaAccount account:
                SetCurrent(account);
                break;
            default:
                SetCurrent(null);
                break;
        }
    }

    /// <summary>
    /// Revenir dans une liste redonne à sa sélection le rôle de cible, même si elle n'a pas changé (cliquer sur
    /// l'élément déjà sélectionné ne déclenche pas SelectionChanged).
    /// </summary>
    private void OnSelectorFocusChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            SetCurrentFrom(sender switch
            {
                TreeView tree => tree.SelectedItem,
                System.Windows.Controls.Primitives.Selector selector => selector.SelectedItem,
                _ => null,
            });
        }
    }

    /// <summary>Une entrée KeePass n'est plus la cible une fois l'onglet Courants quitté : elle n'est plus visible.</summary>
    private void OnSideTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, SideTabs) && _currentKeePass is not null && !CurrentTab.IsSelected)
        {
            SetCurrent(null);
        }
    }

    private void UpdateActions()
    {
        bool has = _current is not null && !_connecting;
        ConnectButton.IsEnabled = has || _currentKeePass is not null;
        AdvancedButton.IsEnabled = has;
        AddCurrentButton.IsEnabled = _current is not null && _currentSaved is null;
        SshButton.IsEnabled = has && HasPsmp;
        SshButton.ToolTip = HasPsmp ? Strings.ToolSshTip : Strings.SetPsmpAddress;
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => SetCurrentFrom(e.NewValue);

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
            if (item.DataContext is SavedSessionNode { IsMarked: false } or SavedFolderNode && _savedMarks.Count > 0)
            {
                // Clic droit hors des serveurs choisis : la sélection de plusieurs serveurs est abandonnée.
                ClearSavedMarks();
            }

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

    private void OnConnectRecent(object sender, RoutedEventArgs e) => ConnectRecent();

    private void OnRecentMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Clic droit dans la zone vide de la liste : pas de menu (le clic droit sur une ligne la sélectionne).
        if (ItemUnder<ListBoxItem>(e.OriginalSource) is not { DataContext: RecentSession recent } || RecentList.SelectedItem != recent)
        {
            e.Handled = true;
        }
    }

    private void OnRecentMenuOpened(object sender, RoutedEventArgs e)
    {
        if (RecentList.SelectedItem is not RecentSession recent)
        {
            ((ContextMenu)sender).IsOpen = false;
            return;
        }

        // Compte d'un autre PVWA, supprimé, ou accès d'urgence sans CyberArk : rien à ajouter.
        var account = _byId.GetValueOrDefault(recent.AccountId);
        foreach (var item in ((ContextMenu)sender).Items.OfType<MenuItem>().Where(i => i.Tag as string == "addcurrent"))
        {
            BuildAddToCurrentMenu(item, account is null ? null : folder => AddToCurrent(account, recent, folder));
            item.IsEnabled = account is not null;
            item.ToolTip = account is null ? Text.Format(Strings.AccountGone, recent.Label) : null;
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
            SetStatus(MissingAccountText(Text.Format(Strings.AccountGone, recent.Label)), isError: true);
            return;
        }

        SetCurrent(account);
        var mode = SessionLibrary.RecentMode(recent.Mode);
        var request = mode == ConnectMode.Psm
            ? DefaultRequest(account, ConnectMode.Psm) with { Component = recent.Mode, RemoteMachine = recent.RemoteMachine }
            : DefaultRequest(account, mode) with { RemoteMachine = recent.RemoteMachine };
        _ = ConnectAsync(account, request, showDialog: false);
    }

    private void OnConnectDefault(object sender, RoutedEventArgs e)
    {
        if (_currentKeePass is { } entry)
        {
            _ = ConnectKeePassAsync(entry, null);
        }
        else if (_currentSaved is { } saved)
        {
            ConnectSaved(saved, advanced: false);
        }
        else if (_current is { } account)
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

    private void OnConnectSftp(object sender, RoutedEventArgs e)
    {
        if (_current is { } account)
        {
            _ = ConnectAsync(account, DefaultRequest(account, ConnectMode.Sftp));
        }
    }

    private void OnConnectAdvanced(object sender, RoutedEventArgs e)
    {
        if (_currentSaved is { } saved)
        {
            ConnectSaved(saved, advanced: true);
        }
        else if (_current is { } account)
        {
            _ = ConnectAsync(account, DefaultRequest(account, null), showDialog: true);
        }
    }

    /// <summary>
    /// Requête par défaut, d'après la plateforme (si le PSMP est renseigné) : fichiers seuls en SFTP pour une plateforme
    /// « SFTP », SSH via le PSMP pour une plateforme « SSH » ou Unix, sinon PSM avec le composant mémorisé.
    /// « Connexion avancée » permet d'en choisir un autre.
    /// </summary>
    private ConnectRequest DefaultRequest(PvwaAccount account, ConnectMode? mode)
    {
        var effective = mode ?? AccountClassifier.DefaultMode(account, HasPsmp);
        var machines = AccountClassifier.RemoteMachineList(account);
        return new ConnectRequest(effective, _settings.ResolveComponent(account), machines.Count == 1 ? machines[0] : null);
    }

    /// <summary>
    /// Ouvre la session. La fenêtre « Connexion avancée » s'affiche si elle est demandée, s'il faut choisir
    /// la machine cible, ou si le PVWA refuse la demande (motif exigé, composant inconnu...) pour corriger et réessayer.
    /// </summary>
    private async Task ConnectAsync(PvwaAccount account, ConnectRequest request, bool showDialog = false, SavedSession? saved = null)
    {
        if (_connecting)
        {
            return;
        }

        if (request.Mode != ConnectMode.Psm && !HasPsmp)
        {
            SetStatus(request.Mode == ConnectMode.Sftp ? Strings.SftpUnavailable : Strings.SshUnavailable, isError: true);
            return;
        }

        showDialog |= AccountClassifier.NeedsRemoteMachine(account) && string.IsNullOrWhiteSpace(request.RemoteMachine);
        string? error = null;
        bool componentError = false;
        _connecting = true;
        UpdateActions();
        try
        {
            while (true)
            {
                if (showDialog)
                {
                    var dialog = new ConnectDialog(account, request, _settings, _vaultUser, error, componentError) { Owner = this };
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
                    await LaunchAsync(account, request, saved);
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
                    // Composant absent de la plateforme du compte (il peut porter un autre nom, par ex. WIN-PSM) :
                    // le dialogue le dit et invite à saisir le bon.
                    componentError = ex is PvwaException { IsUnknownComponent: true };
                    error = componentError
                        ? Text.Format(Strings.PsmComponentUnknown, request.Component, account.PlatformId)
                        : ErrorText.Describe(ex);
                    SetStatus(Text.Format(Strings.ConnectFailed, error), isError: true);
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

    private async Task LaunchAsync(PvwaAccount account, ConnectRequest request, SavedSession? saved)
    {
        var target = string.IsNullOrWhiteSpace(request.RemoteMachine) ? account.Address : request.RemoteMachine.Trim();
        var label = $"{account.UserName}@{target}";
        if (request.Mode == ConnectMode.Psm)
        {
            SetStatus(Text.Format(Strings.PsmOpening, label, request.Component));
            LoadProgress.IsIndeterminate = true;
            LoadProgress.Visibility = Visibility.Visible;
            var options = new PsmConnectOptions
            {
                ConnectionComponent = request.Component,
                Reason = request.Reason,
                TicketingSystemName = request.TicketingSystem,
                TicketId = request.TicketId,
                RemoteMachine = request.RemoteMachine,
            };
            byte[] rdp;
            DebugLog.Write("psm", $"Demande de connexion PSM pour {label} (compte {account.Id}, composant {request.Component}, machine cible « {request.RemoteMachine} »)");
            try
            {
                rdp = await Client.PsmConnectAsync(account.Id, options, _lifetime.Token);
            }
            finally
            {
                LoadProgress.Visibility = _loading ? Visibility.Visible : Visibility.Collapsed;
            }

            LogRdpFile(rdp);
            // Session PSM dans Connexion Bureau à distance (mstsc), en fenêtre(s) à part : le fichier du PVWA tel quel.
            _launcher.LaunchRdp(rdp, label);
            SetStatus(Text.Format(Strings.PsmStarted, label, request.Component));
            AddRecent(account, label, request.Component, request.RemoteMachine);
        }
        else if (request.Mode == ConnectMode.Sftp)
        {
            // Fichiers seuls : une session PSMP SFTP, sans terminal, toujours dans l'application.
            var login = PsmpTarget.BuildLogin(_vaultUser, account, request.RemoteMachine);
            await OpenPsmpFilesTabAsync(account, login, label, saved, () => ConnectAsync(account, request, saved: saved));
            AddRecent(account, label, RecentModes.Sftp, request.RemoteMachine);
        }
        else if (_settings.SshInApp)
        {
            var login = PsmpTarget.BuildLogin(_vaultUser, account, request.RemoteMachine);
            await OpenSshTabAsync(account, login, label, saved, () => ConnectAsync(account, request, saved: saved));
            AddRecent(account, label, RecentModes.Ssh, request.RemoteMachine);
        }
        else
        {
            var login = PsmpTarget.BuildLogin(_vaultUser, account, request.RemoteMachine);
            _launcher.LaunchSsh(login, _settings.PsmpAddress, _settings.PsmpPort, label);
            SetStatus(Text.Format(Strings.SshStarted, label, _settings.PsmpAddress));
            AddRecent(account, label, RecentModes.Ssh, request.RemoteMachine);
        }
    }

    /// <summary>Fichier .rdp renvoyé par le PVWA, dans le journal de débogage (jetons et signature masqués).</summary>
    private static void LogRdpFile(byte[] rdp)
    {
        if (DebugLog.Enabled)
        {
            DebugLog.Write("psm", $"Fichier .rdp reçu du PVWA ({rdp.Length} octets) :\n{DebugLog.DescribeRdpFile(RdpConnectionSettings.ReadFile(rdp))}");
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
        var account = _current;
        // Clic droit sur un safe (« Disponibles » groupé par safe) : seulement ses membres ; sur un autre dossier : rien.
        var safe = account is not null ? account.SafeName : SafeOfFolder(menu.PlacementTarget);
        if (account is null && safe is null)
        {
            menu.IsOpen = false;
            return;
        }

        foreach (var element in menu.Items.OfType<FrameworkElement>())
        {
            element.Visibility = account is null && element.Tag as string is not ("safemembers" or "addaccount" or "importaccounts")
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        // Action du double-clic en gras : PSM, SSH ou fichiers seuls selon la plateforme.
        var defaultTag = account is null ? null : AccountClassifier.DefaultMode(account, HasPsmp) switch
        {
            ConnectMode.Ssh => "ssh",
            ConnectMode.Sftp => "sftp",
            _ => "psm",
        };
        foreach (var item in menu.Items.OfType<MenuItem>().Where(i => i.Tag as string is "psm" or "ssh" or "sftp"))
        {
            item.FontWeight = item.Tag as string == defaultTag ? FontWeights.SemiBold : FontWeights.Normal;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Tag as string)
            {
                case "addcurrent" when account is not null:
                    BuildAddToCurrentMenu(item, folder => AddToCurrent(account, folder));
                    break;
                case "ssh":
                    item.IsEnabled = HasPsmp;
                    item.ToolTip = HasPsmp ? null : Strings.SetPsmpAddress;
                    break;
                case "sftp":
                    item.IsEnabled = HasPsmp;
                    item.ToolTip = HasPsmp ? Strings.MenuConnectSftpTip : Strings.SetPsmpAddress;
                    break;
                case "safemembers":
                    SetSafeMenuItem(item, safe, Strings.MenuSafeMembersOf, Strings.MenuSafeMembers);
                    break;
                case "addaccount":
                    SetSafeMenuItem(item, safe, Strings.MenuAddAccountTo, Strings.MenuAddAccount);
                    break;
                case "importaccounts":
                    item.CommandParameter = safe;
                    item.IsEnabled = _client is not null;
                    break;
            }
        }
    }

    /// <summary>Entrée de menu qui porte sur un safe : son nom dans le libellé, désactivée sans safe ou sans CyberArk.</summary>
    private void SetSafeMenuItem(MenuItem item, string? safe, string format, string fallback)
    {
        bool available = _client is not null && !string.IsNullOrWhiteSpace(safe);
        // TextBlock : un « _ » dans le nom du safe n'est pas un raccourci clavier.
        item.Header = new TextBlock { Text = available ? Text.Format(format, safe) : fallback };
        item.CommandParameter = safe;
        item.IsEnabled = available;
    }

    /// <summary>Safe d'un dossier de l'arbre « Disponibles » quand les comptes sont groupés par safe, sinon null.</summary>
    private string? SafeOfFolder(UIElement? target) =>
        _settings.GroupBy == GroupBy.Safe && target is TreeViewItem { DataContext: FolderNode folder }
            && folder.Children.FirstOrDefault()?.Account.SafeName is { } safe && !string.IsNullOrWhiteSpace(safe)
            ? safe
            : null;

    private void OnShowSafeMembers(object sender, RoutedEventArgs e) => ShowSafeMembers((sender as MenuItem)?.CommandParameter as string);

    /// <summary>Membres du safe et leurs droits, lus sur le PVWA (droit « View Safe Members » nécessaire).</summary>
    private void ShowSafeMembers(string? safeName)
    {
        if (_client is not { } client || string.IsNullOrWhiteSpace(safeName))
        {
            return;
        }

        var actions = new SafeMemberActions(
            (member, ct) => client.AddSafeMemberAsync(safeName, member, ct),
            (member, ct) => client.UpdateSafeMemberAsync(safeName, member, ct),
            (name, ct) => client.RemoveSafeMemberAsync(safeName, name, ct));
        var dialog = new SafeMembersDialog(safeName, ct => client.GetSafeMembersAsync(safeName, ct), actions) { Owner = this };
        dialog.ShowDialog();
        if (dialog.SessionExpired)
        {
            OnSessionExpired();
        }
    }

    private void OnCopyAddress(object sender, RoutedEventArgs e) => CopyCurrent(a => a.Address ?? "");

    private void OnCopyUser(object sender, RoutedEventArgs e) => CopyCurrent(a => a.UserName ?? "");

    private void OnCopyDomainUser(object sender, RoutedEventArgs e) =>
        CopyCurrent(a => a.LogonDomain.Length > 0 ? $"{a.LogonDomain}\\{a.UserName}" : a.UserName ?? "");

    private void CopyCurrent(Func<PvwaAccount, string> selector)
    {
        if (_current is { } account)
        {
            Clipboard.SetText(selector(account));
        }
    }

    // ===================== Barre d'outils =====================

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var rows = _shown;
        if (rows.Count == 0)
        {
            MessageBox.Show(this, Strings.NothingToExport, "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = Strings.ExportTitle,
            Filter = Strings.ExportFilter,
            FileName = $"{Strings.ExportFileName}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
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
            SetStatus(Text.Format(Strings.Exported, rows.Count, dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Text.Format(Strings.ExportFailed, ex.Message), "CyberArkTerm", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var language = _settings.Language;
        var accepted = new SettingsDialog(_settings, _keePass.Store) { Owner = this }.ShowDialog() == true;
        // Coffre local supprimé ou créé depuis les paramètres : l'arbre « Courants » peut changer.
        RefreshSaved();
        if (accepted)
        {
            SaveSettings();
            CyberArkTerm.App.Terminal.TerminalAppearance.Apply(_settings.TerminalTheme, _settings.TerminalFontSize, _settings.TerminalRightClickPastes);
            UpdateActions();
            StartKeepAlive();
            SetStatus(_settings.Language == language ? Strings.SettingsSaved : Strings.SettingsSavedLanguage);
        }
    }

    /// <summary>Bouton Historique : envois et téléchargements de l'onglet Fichiers, même sans session.</summary>
    private void OnTransferHistory(object sender, RoutedEventArgs e) => FilesPanel.ShowHistory();

    /// <summary>Affiche le terminal d'une session : son onglet, la vue parallèle ou sa fenêtre séparée.</summary>
    private void ShowTerminal(SshSession session)
    {
        if (_detached.TryGetValue(session, out var window))
        {
            window.Activate();
            window.View.FocusTerminal();
        }
        else if (_parallel?.ViewOf(session) is { } inParallel)
        {
            ShowParallel();
            inParallel.FocusTerminal();
        }
        else if (TabOf(session) is { } tab)
        {
            MainTabs.SelectedItem = tab;
            (tab.Content as SshSessionView)?.FocusTerminal();
        }
    }

    /// <summary>Bouton Paramètres : menu sous le bouton (paramètres, journal de débogage).</summary>
    private void OnSettingsMenu(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)FindResource("SettingsMenu");
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnSettingsMenuOpened(object sender, RoutedEventArgs e)
    {
        foreach (var item in ((ContextMenu)sender).Items.OfType<MenuItem>().Where(i => i.Tag as string == "debuglog"))
        {
            item.IsChecked = _settings.DebugLogEnabled;
        }
    }

    private void OnToggleDebugLog(object sender, RoutedEventArgs e)
    {
        _settings.DebugLogEnabled = ((MenuItem)sender).IsChecked;
        AppDebugLog.Apply(_settings);
        SaveSettings();
        UpdateDebugLogIndicator();
        SetStatus(DebugLog.FilePath is { } path ? Text.Format(Strings.DebugLogStarted, path) : Strings.DebugLogStopped);
    }

    /// <summary>Ouvre l'Explorateur sur le fichier du journal, pour le relire ou le joindre à un message.</summary>
    private void OnShowDebugLog(object sender, RoutedEventArgs e)
    {
        var path = DebugLog.FilePath ?? DebugLog.DefaultPath;
        if (!File.Exists(path))
        {
            SetStatus(Strings.DebugLogMissing, isError: true);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    /// <summary>Barre d'état : rappel visible tant que le journal de débogage est actif.</summary>
    private void UpdateDebugLogIndicator()
    {
        DebugLogText.Visibility = DebugLog.Enabled ? Visibility.Visible : Visibility.Collapsed;
        DebugLogText.ToolTip = DebugLog.FilePath;
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(AppSettings.DefaultPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatus(Text.Format(Strings.PreferencesNotSaved, e.Message), isError: true);
        }
    }

    private void OnSessionExpired()
    {
        MessageBox.Show(this, Strings.SessionExpired,
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

        if (!ConfirmCloseEditedFiles() || !ConfirmCloseRdpSessions() || !FilesPanel.ConfirmCancelTransfers(this, null))
        {
            // Fichiers modifiés non renvoyés, sessions Bureau à distance ouvertes ou transferts en cours : l'utilisateur
            // garde la fenêtre.
            e.Cancel = true;
            LogoutRequested = false;
            return;
        }

        // Fermeture de la session PVWA avant de quitter (au plus 5 s d'attente).
        e.Cancel = true;
        _loggedOff = true;
        ClearPasswordClipboard();
        IsEnabled = false;
        // Transferts annulés d'abord : le fichier interrompu est supprimé tant que la connexion est ouverte.
        await FilesPanel.CancelTransfersAsync(null);
        _lifetime.Cancel();
        _searchDebounce.Stop();
        try
        {
            if (_client is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _client.LogoffAsync(timeout.Token);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Au pire, la session expirera d'elle-même côté PVWA.
        }
        finally
        {
            CloseAllSshSessions();
            // Les coffres KeePass ouverts se referment avec la fenêtre (le coffre local reste déverrouillé).
            _keePass.LockAll();
            _launcher.Cleanup();
            _client?.Dispose();
            _lifetime.Dispose();
        }

        if (!await CloseAllRdpSessionsAsync() && !LogoutRequested)
        {
            // Un contrôle Bureau à distance bloqué garde sa fenêtre dans celle-ci : la détruire attendrait son thread.
            // Tout le reste est déjà fermé (session PVWA, coffres) : on quitte directement.
            DebugLog.Write("app", "Session Bureau à distance bloquée à la fermeture : arrêt immédiat de l'application.");
            Environment.Exit(0);
        }

        Close();
    }
}
