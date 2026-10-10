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
    /// <param name="x11">Transfert X11 coché pour ce serveur (réglage personnel, hors du serveur enregistré).</param>
    /// <param name="tags">Étiquettes proposées (Paramètres) ; null : celles par défaut.</param>
    public SessionPropertiesDialog(SavedSession session, PvwaAccount? account, IEnumerable<string> folders, bool sshAvailable,
        IReadOnlyList<string> components, bool x11 = false, IReadOnlyList<ServerTag>? tags = null)
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
        FillTags(tags ?? ServerTagRules.Defaults(), session.Tag);
        FolderBox.ItemsSource = folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        FolderBox.Text = session.Folder;
        ComponentBox.ItemsSource = components;
        ComponentBox.Text = session.Component ?? "";
        MachineBox.ItemsSource = account is null ? [] : AccountClassifier.RemoteMachineList(account);
        MachineBox.Text = session.RemoteMachine ?? "";
        ReasonBox.Text = session.Reason ?? "";
        StartDirBox.Text = session.StartDirectory ?? "";
        X11Box.IsChecked = x11;
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(X11Box, true);
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

    /// <summary>
    /// Une ligne par étiquette (pastille de sa couleur), puis « (aucune) » ; une étiquette du serveur absente des
    /// Paramètres reste proposée, en gris, pour ne pas être perdue sans le vouloir.
    /// </summary>
    private void FillTags(IReadOnlyList<ServerTag> tags, string? current)
    {
        var items = tags.Select(t => (Tag: t, Known: true)).ToList();
        if (ServerTagRules.NormalizeName(current) is { } name && ServerTagRules.Find(tags, name) is null)
        {
            items.Add((new ServerTag(name, ServerTagRules.NeutralColor), false));
        }

        foreach (var (tag, known) in items)
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            row.Children.Add(ServerTagView.Chip(tag, 11));
            if (!known)
            {
                row.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = Strings.TagUnknown,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var item = new System.Windows.Controls.ComboBoxItem { Content = row, Tag = tag.Name };
            System.Windows.Automation.AutomationProperties.SetName(item, known ? tag.Name : $"{tag.Name} {Strings.TagUnknown}");
            TagBox.Items.Add(item);
        }

        TagBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Strings.TagNoneItem, Tag = null });
        TagBox.SelectedItem = TagBox.Items.Cast<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, ServerTagRules.NormalizeName(current), StringComparison.OrdinalIgnoreCase))
            ?? TagBox.Items[^1];
    }

    /// <summary>Étiquette choisie ; null : aucune.</summary>
    public string? ChosenTag => (TagBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ComponentBox is null)
        {
            return;
        }

        ComponentBox.IsEnabled = PsmRadio.IsChecked == true;
        ReasonBox.IsEnabled = PsmRadio.IsChecked == true;
        StartDirBox.IsEnabled = PsmRadio.IsChecked != true;
        X11Box.IsEnabled = SshRadio.IsChecked == true;
        X11Box.ToolTip = X11Box.IsEnabled ? Strings.X11ForwardingTip : Strings.X11SshOnly;
    }

    /// <summary>Transfert X11 choisi (sessions SSH seulement), une fois la fenêtre validée.</summary>
    public bool X11Forwarding { get; private set; }

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
        _session.Tag = ChosenTag;
        X11Forwarding = _session.Mode == ConnectMode.Ssh && X11Box.IsChecked == true;
        DialogResult = true;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
