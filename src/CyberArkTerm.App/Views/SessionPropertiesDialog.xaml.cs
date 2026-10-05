using System.Globalization;
using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>Configuration propre à un serveur de l'onglet « Courants ».</summary>
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
        SshRadio.ToolTip = sshAvailable ? null : Strings.SetPsmpAddress;
        (session.Mode == ConnectMode.Ssh ? SshRadio : PsmRadio).IsChecked = true;
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
        StartDirBox.IsEnabled = SshRadio.IsChecked == true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var start = StartDirBox.Text.Trim();
        if (start.Length > 0 && !start.StartsWith('/'))
        {
            MessageBox.Show(this, Strings.StartDirMustBeAbsolute, Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _session.Name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : $"{_session.UserName}@{_session.Address}";
        _session.Folder = SessionFolders.Normalize(FolderBox.Text);
        _session.Mode = SshRadio.IsChecked == true ? ConnectMode.Ssh : ConnectMode.Psm;
        _session.Component = NullIfEmpty(ComponentBox.Text);
        _session.RemoteMachine = NullIfEmpty(MachineBox.Text);
        _session.Reason = NullIfEmpty(ReasonBox.Text);
        _session.StartDirectory = NullIfEmpty(start);
        DialogResult = true;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
