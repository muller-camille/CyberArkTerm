using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.App.Views;

/// <summary>Création ou modification d'une entrée de coffre KeePass (serveur joignable en SSH ou en RDP).</summary>
public partial class KeePassEntryDialog : Window
{
    private readonly bool _editing;
    private bool _syncing;

    /// <param name="entry">Entrée à modifier ; null pour en créer une dans <paramref name="group"/>.</param>
    public KeePassEntryDialog(string vaultName, IEnumerable<string> groups, KeePassEntry? entry, string group)
    {
        InitializeComponent();
        _editing = entry is not null;
        Title = _editing ? Strings.KeePassEditEntryTitle : Strings.KeePassNewEntryTitle;
        VaultText.Text = vaultName;
        GroupBox.ItemsSource = groups.Where(g => g.Length > 0).Order(StringComparer.OrdinalIgnoreCase).ToList();
        GroupBox.Text = entry?.Group ?? group;
        TitleBox.Text = entry?.Title ?? "";
        UrlBox.Text = entry?.Url ?? "";
        UserBox.Text = entry?.UserName ?? "";
        NotesBox.Text = entry?.Notes ?? "";
        PasswordHint.Visibility = _editing ? Visibility.Visible : Visibility.Collapsed;
        UpdateTarget();
        Loaded += (_, _) => TitleBox.Focus();
    }

    /// <summary>Valeurs saisies ; mot de passe null s'il n'a pas été changé (modification).</summary>
    public KeePassEntryData? Result { get; private set; }

    /// <summary>Dossier choisi dans le coffre (« » pour la racine).</summary>
    public string Group { get; private set; } = "";

    private string Password => ShowBox.IsChecked == true ? PasswordText.Text : PasswordBox.Password;

    private void OnUrlChanged(object sender, TextChangedEventArgs e) => UpdateTarget();

    private void UpdateTarget()
    {
        if (TargetText is null)
        {
            return;
        }

        var target = KeePassTarget.From(new KeePassEntry { Id = "", Title = TitleBox.Text, Url = UrlBox.Text, UserName = UserBox.Text });
        var where = target.UserName.Length > 0 ? $"{target.UserName}@{target.Address}" : target.Address;
        TargetText.Text = target.Host.Length == 0 ? Strings.KeePassTargetNone : target.Protocol switch
        {
            RemoteProtocol.Ssh => Text.Format(Strings.KeePassTargetSsh, where),
            RemoteProtocol.Rdp => Text.Format(Strings.KeePassTargetRdp, where),
            _ => Strings.KeePassTargetUnknown,
        };
    }

    private void OnShowChanged(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        if (ShowBox.IsChecked == true)
        {
            PasswordText.Text = PasswordBox.Password;
            PasswordText.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            PasswordBox.Password = PasswordText.Text;
            PasswordText.Text = "";
            PasswordBox.Visibility = Visibility.Visible;
            PasswordText.Visibility = Visibility.Collapsed;
        }

        _syncing = false;
        UpdateHint();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => UpdateHint();

    private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
        {
            UpdateHint();
        }
    }

    private void UpdateHint() =>
        PasswordHint.Visibility = _editing && Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            title = KeePassTarget.From(new KeePassEntry { Id = "", Url = UrlBox.Text }).Host;
        }

        var password = Password;
        Result = new KeePassEntryData(title, UserBox.Text.Trim(), _editing && password.Length == 0 ? null : password,
            UrlBox.Text.Trim(), NotesBox.Text);
        Group = KeePassGroupPath.Normalize(GroupBox.Text);
        PasswordBox.Clear();
        PasswordText.Clear();
        DialogResult = true;
    }
}
