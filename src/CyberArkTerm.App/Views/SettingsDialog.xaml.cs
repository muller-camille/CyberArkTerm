using System.Globalization;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.KeePass;
using CyberArkTerm.Core.Localization;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly LocalSecretStore? _store;
    private bool _forgetComponents;
    private bool _forgetHostKeys;

    /// <param name="store">Coffre local des mots de passe maîtres KeePass, géré depuis cette fenêtre.</param>
    public SettingsDialog(AppSettings settings, LocalSecretStore? store = null)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;
        KeepAliveBox.IsChecked = settings.KeepPvwaSessionAlive;
        UpdateStore();
        LanguageBox.DisplayMemberPath = "Value";
        LanguageBox.SelectedValuePath = "Key";
        LanguageBox.ItemsSource = new[] { new KeyValuePair<string, string>("", CoreStrings.LanguageSystem) }
            .Concat(UiLanguage.Supported.Select(code => new KeyValuePair<string, string>(code, UiLanguage.NativeName(code))))
            .ToList();
        LanguageBox.SelectedValue = UiLanguage.Normalize(settings.Language);
        PsmpBox.Text = settings.PsmpAddress;
        PortBox.Text = settings.PsmpPort.ToString(CultureInfo.InvariantCulture);
        PreferSshBox.IsChecked = settings.PreferSshForUnix;
        SshInAppBox.IsChecked = settings.SshInApp;
        FollowBox.IsChecked = settings.FollowTerminalFolder;
        RdpInAppBox.IsChecked = settings.RdpInApp;
        RemoteAppInTabBox.IsChecked = settings.RemoteAppInTab;
        PsmRemoteAppAsDesktopBox.IsChecked = settings.PsmRemoteAppAsDesktop;
        EditorBox.Text = settings.TextEditor;
        (settings.UploadProtocol == TransferProtocol.Sftp ? SftpRadio : ScpRadio).IsChecked = true;
        HostKeysText.Text = settings.KnownHosts.Count == 0
            ? Strings.NoHostKeys
            : Text.Format(Strings.HostKeys, string.Join(", ", settings.KnownHosts.Keys));
        ComponentsText.Text = settings.ComponentByPlatform.Count == 0
            ? Strings.NoComponents
            : string.Join(", ", settings.ComponentByPlatform.Select(kv => Text.Format(Strings.ComponentEntry, kv.Key, kv.Value)));
        Loaded += (_, _) => PsmpBox.Focus();
    }

    private void OnForgetComponents(object sender, RoutedEventArgs e)
    {
        _forgetComponents = true;
        ComponentsText.Text = Strings.ComponentsForgotten;
    }

    private void OnForgetHostKeys(object sender, RoutedEventArgs e)
    {
        _forgetHostKeys = true;
        HostKeysText.Text = Strings.HostKeysForgotten;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var host = PsmpBox.Text.Trim();
        if (host.Length > 0 && Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            ShowError(Strings.InvalidPsmpAddress);
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            ShowError(Strings.InvalidPort);
            return;
        }

        _settings.Language = LanguageBox.SelectedValue as string ?? "";
        _settings.PsmpAddress = host;
        _settings.PsmpPort = port;
        _settings.PreferSshForUnix = PreferSshBox.IsChecked == true;
        _settings.SshInApp = SshInAppBox.IsChecked == true;
        _settings.FollowTerminalFolder = FollowBox.IsChecked == true;
        _settings.RdpInApp = RdpInAppBox.IsChecked == true;
        _settings.RemoteAppInTab = RemoteAppInTabBox.IsChecked == true;
        _settings.PsmRemoteAppAsDesktop = PsmRemoteAppAsDesktopBox.IsChecked == true;
        _settings.KeepPvwaSessionAlive = KeepAliveBox.IsChecked == true;
        _settings.UploadProtocol = SftpRadio.IsChecked == true ? TransferProtocol.Sftp : TransferProtocol.Scp;
        _settings.TextEditor = EditorBox.Text.Trim().Trim('"');
        if (_forgetHostKeys)
        {
            _settings.KnownHosts.Clear();
        }

        if (_forgetComponents)
        {
            _settings.ComponentByPlatform.Clear();
        }

        DialogResult = true;
    }

    private void OnBrowseEditor(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Strings.TextEditorDialogTitle, Filter = Strings.ProgramsFilter };
        if (dialog.ShowDialog(this) == true)
        {
            EditorBox.Text = dialog.FileName;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    // ===================== Coffre local (actions immédiates) =====================

    private void UpdateStore()
    {
        bool exists = _store?.Exists == true;
        bool unlocked = _store?.IsUnlocked == true;
        StoreText.Text = !exists ? Strings.LocalStoreStateNone
            : unlocked ? Text.Format(Strings.LocalStoreStateUnlocked, _store!.Ids.Count)
            : Strings.LocalStoreStateLocked;
        StoreCreateButton.Visibility = _store is not null && !exists ? Visibility.Visible : Visibility.Collapsed;
        StoreUnlockButton.Visibility = exists && !unlocked ? Visibility.Visible : Visibility.Collapsed;
        StoreChangeButton.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
        StoreDeleteButton.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStoreCreate(object sender, RoutedEventArgs e) => ShowStoreDialog(LocalStoreDialog.Mode.Create);

    private void OnStoreUnlock(object sender, RoutedEventArgs e) => ShowStoreDialog(LocalStoreDialog.Mode.Unlock);

    private void OnStoreChange(object sender, RoutedEventArgs e) => ShowStoreDialog(LocalStoreDialog.Mode.ChangePassword);

    private void ShowStoreDialog(LocalStoreDialog.Mode mode)
    {
        if (_store is not null)
        {
            new LocalStoreDialog(_store, mode) { Owner = this }.ShowDialog();
            UpdateStore();
        }
    }

    private void OnStoreDelete(object sender, RoutedEventArgs e)
    {
        if (_store is null || MessageBox.Show(this, Strings.LocalStoreDeleteConfirm, Strings.LocalStoreTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _store.Delete();
            foreach (var folder in _settings.KeePassFolders)
            {
                folder.RememberPassword = false;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Strings.LocalStoreTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateStore();
    }
}
