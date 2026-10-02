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
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.App.Services.KeePass;
using CyberArkTerm.App.Services.Rdp;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Localization;
using CyberArkTerm.Core.Rdp;
using Microsoft.Win32;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Fenêtre principale : barre d'outils, arbre des sessions à gauche,
/// onglets Accueil / Tous les comptes, connexion PSM ou SSH (PSMP) sur double-clic.
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
    private Dictionary<string, PvwaAccount> _byId = [];
    private ListCollectionView? _view;
    private string _query = "";
    private PvwaAccount? _current;
    private SavedSession? _currentSaved;
    private bool _loading;
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
            _keePass.Changed -= OnKeePassChanged;
            SystemEvents.SessionSwitch -= OnWindowsSessionSwitch;
        };

        FilesPanel.Initialize(settings, SaveSettings);
        if (IsOffline)
        {
            // Accès d'urgence : ni comptes CyberArk ni PSM, seulement les coffres KeePass de l'onglet « Courants ».
            AvailableTab.Visibility = AccountsTab.Visibility = Visibility.Collapsed;
            QuickPanel.Visibility = HomeLists.Visibility = NewFolderButton.Visibility = Visibility.Collapsed;
            ExportButton.IsEnabled = false;
            NoSavedText.Text = Strings.NoKeePassHelp;
            SideTabs.SelectedItem = CurrentTab;
            CountText.Text = "";
        }
        else
        {
            RefreshRecent();
        }

        RefreshSaved();
        UpdateWelcome();
        UpdateActions();
        StartKeepAlive();
        Loaded += async (_, _) =>
        {
            if (!IsOffline)
            {
                await LoadAccountsAsync();
            }
        };
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
            _view = new ListCollectionView(_accounts) { Filter = o => AccountFilter.Matches((PvwaAccount)o, _query) };
            AccountsGrid.ItemsSource = _view;
            ApplyFilter();
            SessionLibrary.MigrateFavorites(_settings, _byId, Client.BaseUri.Host);
            SaveSettings();
            RefreshSaved();
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
            ? Text.Format(Strings.AccountCount, _accounts.Count)
            : Text.Format(Strings.AccountCountFiltered, shown, _accounts.Count);
    }

    private void UpdateWelcome()
    {
        WelcomeText.Text = _client is null ? Strings.EmergencyWelcome : Text.Format(Strings.Welcome, _client.BaseUri.Host, _sessionUser, _accounts.Count);
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

    private void CanRefresh(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_loading && !IsOffline;

    private async void OnRefresh(object sender, ExecutedRoutedEventArgs e) => await LoadAccountsAsync();

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
            SetStatus(Text.Format(Strings.AccountGone, recent.Label), isError: true);
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
    private async Task ConnectAsync(PvwaAccount account, ConnectRequest request, bool showDialog = false, SavedSession? saved = null)
    {
        if (_connecting)
        {
            return;
        }

        if (request.Mode == ConnectMode.Ssh && !HasPsmp)
        {
            SetStatus(Strings.SshUnavailable, isError: true);
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
                    error = ErrorText.Describe(ex);
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
        var target = string.IsNullOrWhiteSpace(request.RemoteMachine) ? account.Address : request.RemoteMachine;
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
            try
            {
                rdp = await Client.PsmConnectAsync(account.Id, options, _lifetime.Token);
            }
            finally
            {
                LoadProgress.Visibility = _loading ? Visibility.Visible : Visibility.Collapsed;
            }

            if (EmbeddableRdp(rdp, label, out var fallbackReason) is { } embeddable)
            {
                var settings = ForPsmTab(embeddable);
                SetStatus(Text.Format(Strings.PsmStarted, label, request.Component));
                AddRecent(account, label, request.Component, request.RemoteMachine);
                // Une reconnexion demande un nouveau jeton au PVWA : le précédent ne sert qu'une fois.
                var session = await OpenRdpTabAsync(label, async ct =>
                {
                    var file = await Client.PsmConnectAsync(account.Id, options, ct);
                    return new RdpConnectionRequest(ForPsmTab(RdpConnectionSettings.FromRdpFile(file)), null);
                }, new RdpConnectionRequest(settings, null));
                if (session.ControlFailed)
                {
                    // Contrôle Bureau à distance inutilisable sur ce poste : le jeton n'a pas servi, mstsc prend le relais.
                    await RemoveRdpTabAsync(session);
                    _launcher.LaunchRdp(rdp, label);
                    SetStatus(Text.Format(Strings.RdpControlFallback, label, session.Error));
                }
            }
            else
            {
                _launcher.LaunchRdp(rdp, label);
                SetStatus(fallbackReason ?? Text.Format(Strings.PsmStarted, label, request.Component));
                AddRecent(account, label, request.Component, request.RemoteMachine);
            }
        }
        else if (_settings.SshInApp)
        {
            var login = PsmpTarget.BuildLogin(_vaultUser, account, request.RemoteMachine);
            await OpenSshTabAsync(account, login, label, saved);
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
                case "addcurrent":
                    BuildAddToCurrentMenu(item, _current);
                    break;
                case "ssh":
                    item.IsEnabled = HasPsmp;
                    item.ToolTip = HasPsmp ? null : Strings.SetPsmpAddress;
                    break;
            }
        }
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
            UpdateActions();
            StartKeepAlive();
            SetStatus(_settings.Language == language ? Strings.SettingsSaved : Strings.SettingsSavedLanguage);
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

        if (!ConfirmCloseEditedFiles() || !ConfirmCloseRdpSessions())
        {
            // Fichiers modifiés non renvoyés, ou sessions Bureau à distance ouvertes : l'utilisateur garde la fenêtre.
            e.Cancel = true;
            LogoutRequested = false;
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
            CloseAllRdpSessions();
            // Les coffres KeePass ouverts se referment avec la fenêtre (le coffre local reste déverrouillé).
            _keePass.LockAll();
            _launcher.Cleanup();
            _client?.Dispose();
            _lifetime.Dispose();
        }

        Close();
    }
}
