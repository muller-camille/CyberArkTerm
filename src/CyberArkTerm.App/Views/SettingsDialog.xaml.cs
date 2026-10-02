using System.Globalization;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.Localization;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;
    private bool _forgetComponents;
    private bool _forgetHostKeys;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
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
}
