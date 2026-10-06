using System.Globalization;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Terminal;
using CyberArkTerm.Core;
using CyberArkTerm.Core.KeePass;
using CyberArkTerm.Core.Localization;
using CyberArkTerm.Core.Ssh;
using CyberArkTerm.Core.Terminal;

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
        UpdateCheckBox.IsChecked = settings.CheckForUpdates;
        UpdateStore();
        LanguageBox.DisplayMemberPath = "Value";
        LanguageBox.SelectedValuePath = "Key";
        LanguageBox.ItemsSource = new[] { new KeyValuePair<string, string>("", CoreStrings.LanguageSystem) }
            .Concat(UiLanguage.Supported.Select(code => new KeyValuePair<string, string>(code, UiLanguage.NativeName(code))))
            .ToList();
        LanguageBox.SelectedValue = UiLanguage.Normalize(settings.Language);
        PsmpBox.Text = settings.PsmpAddress;
        PortBox.Text = settings.PsmpPort.ToString(CultureInfo.InvariantCulture);
        SshInAppBox.IsChecked = settings.SshInApp;
        FollowBox.IsChecked = settings.FollowTerminalFolder;
        EditorBox.Text = settings.TextEditor;
        CompareToolBox.Text = settings.CompareTool;
        ThemeBox.ItemsSource = TerminalTheme.All;
        ThemeBox.SelectedItem = TerminalTheme.Find(settings.TerminalTheme);
        FontSizeBox.Text = settings.TerminalFontSize.ToString(CultureInfo.CurrentCulture);
        CompareArgsBox.Text = settings.CompareToolArguments;
        (settings.UploadProtocol == TransferProtocol.Sftp ? SftpRadio : ScpRadio).IsChecked = true;
        ArchiveBox.IsChecked = settings.OfferArchive;
        ArchiveThresholdBox.Text = settings.ArchiveThreshold.ToString(CultureInfo.InvariantCulture);
        TailSessionBox.IsChecked = settings.TailIndependentSession;
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

        if (!int.TryParse(ArchiveThresholdBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var threshold) || threshold < 2)
        {
            ShowError(Strings.InvalidArchiveThreshold);
            return;
        }

        if (!double.TryParse(FontSizeBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var fontSize)
            || fontSize < TerminalAppearance.MinFontSize || fontSize > TerminalAppearance.MaxFontSize)
        {
            ShowError(Text.Format(Strings.InvalidFontSize, TerminalAppearance.MinFontSize, TerminalAppearance.MaxFontSize));
            return;
        }

        var compareArgs = CompareArgsBox.Text.Trim();
        if (compareArgs.Length == 0)
        {
            compareArgs = AppSettings.DefaultCompareArguments;
        }

        if (!compareArgs.Contains("{0}", StringComparison.Ordinal) || !compareArgs.Contains("{1}", StringComparison.Ordinal)
            || !IsValidFormat(compareArgs))
        {
            ShowError(Strings.InvalidCompareArguments);
            return;
        }

        _settings.Language = LanguageBox.SelectedValue as string ?? "";
        _settings.PsmpAddress = host;
        _settings.PsmpPort = port;
        _settings.SshInApp = SshInAppBox.IsChecked == true;
        _settings.FollowTerminalFolder = FollowBox.IsChecked == true;
        _settings.KeepPvwaSessionAlive = KeepAliveBox.IsChecked == true;
        _settings.CheckForUpdates = UpdateCheckBox.IsChecked == true;
        _settings.UploadProtocol = SftpRadio.IsChecked == true ? TransferProtocol.Sftp : TransferProtocol.Scp;
        _settings.OfferArchive = ArchiveBox.IsChecked == true;
        _settings.ArchiveThreshold = threshold;
        _settings.TailIndependentSession = TailSessionBox.IsChecked == true;
        _settings.TextEditor = EditorBox.Text.Trim().Trim('"');
        _settings.CompareTool = CompareToolBox.Text.Trim().Trim('"');
        _settings.TerminalTheme = (ThemeBox.SelectedItem as TerminalTheme ?? TerminalTheme.Campbell).Id;
        _settings.TerminalFontSize = fontSize;
        _settings.CompareToolArguments = compareArgs;
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

    private void OnBrowseCompareTool(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Strings.CompareToolLabel.Replace("_", "").TrimEnd(':', ' '), Filter = Strings.ProgramsFilter };
        if (dialog.ShowDialog(this) == true)
        {
            CompareToolBox.Text = dialog.FileName;
        }
    }

    /// <summary>Arguments utilisables avec string.Format (accolades équilibrées, au plus {0} et {1}).</summary>
    private static bool IsValidFormat(string template)
    {
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, template, "a", "b");
            return true;
        }
        catch (FormatException)
        {
            return false;
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
