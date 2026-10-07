using System.Globalization;
using System.Windows;
using ZillaTerm.App.Localization;
using ZillaTerm.Core;

namespace ZillaTerm.App.Views;

/// <summary>Configuration propre à un serveur de « Mes serveurs ».</summary>
public partial class SessionPropertiesDialog : Window
{
    private readonly SavedSession _session;

    /// <param name="components">Composants proposés (voir <see cref="AppSettings.KnownComponents"/>).</param>
    public SessionPropertiesDialog(SavedSession session, PvwaAccount? account, IEnumerable<string> folders, bool sshAvailable,
        IReadOnlyList<string> components)
    {
        InitializeComponent();
        _session = session;

        KindIcon.Source = (System.Windows.Media.ImageSource?)new KindIconConverter().Convert(
            (object?)account ?? session, typeof(object), null, CultureInfo.CurrentCulture);
        AccountText.Text = $"{session.UserName}@{session.Address}";
        DetailsText.Text = account is null
            ? Strings.AccountNotInList
            : Text.Format(Strings.PlatformAndSafe, account.PlatformId, account.SafeName);

        NameBox.Text = session.Name;
        FolderBox.ItemsSource = folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        FolderBox.Text = session.Folder;
        ComponentBox.ItemsSource = components;
        ComponentBox.Text = session.Component ?? "";
        MachineBox.ItemsSource = account is null ? [] : AccountClassifier.RemoteMachineList(account);
        MachineBox.Text = session.RemoteMachine ?? "";
        ReasonBox.Text = session.Reason ?? "";
        StartDirBox.Text = session.StartDirectory ?? "";
        // Comme dans « Connexion avancée » : sans PSMP, SSH et SFTP sont grisés (le mode enregistré reste coché, visible).
        SshRadio.IsEnabled = SftpRadio.IsEnabled = sshAvailable;
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(SshRadio, true);
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(SftpRadio, true);
        SshRadio.ToolTip = sshAvailable ? null : Strings.SetPsmpAddress;
        SftpRadio.ToolTip = sshAvailable ? Strings.MenuConnectSftpTip : Strings.SetPsmpAddress;
        (session.Mode switch { ConnectMode.Ssh => SshRadio, ConnectMode.Sftp => SftpRadio, _ => PsmRadio }).IsChecked = true;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ComponentBox is null)
        {
            return;
        }

        ComponentBox.IsEnabled = PsmRadio.IsChecked == true;
        ReasonBox.IsEnabled = PsmRadio.IsChecked == true;
        StartDirBox.IsEnabled = PsmRadio.IsChecked != true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var start = StartDirBox.Text.Trim();
        if (start.Length > 0 && !start.StartsWith('/'))
        {
            ErrorText.Text = Strings.StartDirMustBeAbsolute;
            ErrorText.Visibility = Visibility.Visible;
            StartDirBox.Focus();
            StartDirBox.SelectAll();
            return;
        }

        _session.Name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : $"{_session.UserName}@{_session.Address}";
        _session.Folder = SessionFolders.Normalize(FolderBox.Text);
        _session.Mode = SshRadio.IsChecked == true ? ConnectMode.Ssh : SftpRadio.IsChecked == true ? ConnectMode.Sftp : ConnectMode.Psm;
        _session.Component = NullIfEmpty(ComponentBox.Text);
        _session.RemoteMachine = NullIfEmpty(MachineBox.Text);
        _session.Reason = NullIfEmpty(ReasonBox.Text);
        _session.StartDirectory = NullIfEmpty(start);
        DialogResult = true;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
