using System.Windows;
using System.Windows.Controls;
using ZillaTerm.App.Localization;
using ZillaTerm.Core;

namespace ZillaTerm.App.Views;

/// <summary>
/// Serveur sur lequel ouvrir la session d'un compte de domaine (enregistré pour son domaine, pas pour un serveur) :
/// serveurs déjà utilisés avec ce compte proposés, option « Garder ce serveur dans « Mes serveurs » » mémorisée.
/// À l'ajout d'un tel compte dans « Mes serveurs », le serveur est facultatif (vide : demandé à chaque connexion).
/// </summary>
public partial class ServerPromptDialog : Window
{
    private readonly IReadOnlyList<string> _allowed;
    private readonly bool _adding;
    private readonly Func<string, bool> _isSaved;

    /// <param name="known">Serveurs à proposer, le plus probable d'abord (voir <see cref="SessionLibrary.KnownMachines"/>).</param>
    /// <param name="isSaved">Ce serveur est déjà dans « Mes serveurs » pour ce compte.</param>
    /// <param name="keep">Dernier choix de « Garder dans « Mes serveurs » ».</param>
    /// <param name="adding">Ajout à « Mes serveurs » plutôt que connexion.</param>
    /// <param name="domainAccount">Compte de domaine (sinon : compte limité à ses machines, sans phrase sur le domaine).</param>
    public ServerPromptDialog(PvwaAccount account, IReadOnlyList<string> known, IEnumerable<string> folders, Func<string, bool> isSaved,
        bool keep, string folder, bool adding = false, bool domainAccount = true)
    {
        InitializeComponent();
        _adding = adding;
        _isSaved = isSaved;
        _allowed = AccountClassifier.IsRestrictedToRemoteMachines(account) ? AccountClassifier.RemoteMachineList(account) : [];

        KindIcon.Source = (System.Windows.Media.ImageSource?)new KindIconConverter().Convert(account, typeof(object), null,
            System.Globalization.CultureInfo.CurrentCulture);
        TitleText.Text = $"{account.UserName}@{account.Address}";
        DetailsText.Text = Text.Format(Strings.PlatformAndSafe, account.PlatformId, account.SafeName);
        QuestionText.Text = adding ? Strings.ServerPromptAddQuestion : Strings.ServerPromptQuestion;
        var domain = string.IsNullOrWhiteSpace(account.LogonDomain) ? account.Address : account.LogonDomain;
        HintText.Text = Text.Format(Strings.ServerPromptHint, domain);
        HintText.Visibility = domainAccount ? Visibility.Visible : Visibility.Collapsed;

        ServerBox.ItemsSource = known;
        ServerBox.Text = known.FirstOrDefault() ?? "";
        ServerHint.Text = _allowed.Count > 0 ? Text.Format(Strings.ServerPromptRestricted, string.Join(", ", _allowed))
            : adding ? Strings.ServerPromptOptional
            : known.Count > 0 ? Strings.ServerPromptKnown
            : "";
        ServerHint.Visibility = ServerHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        KeepBox.IsChecked = keep;
        FolderBox.ItemsSource = folders.ToList();
        FolderBox.Text = folder;
        if (adding)
        {
            // Le dossier est déjà choisi (menu ou glisser-déposer).
            KeepPanel.Visibility = Visibility.Collapsed;
            AdvancedButton.Visibility = Visibility.Collapsed;
            OkButton.Content = Strings.ServerPromptAdd;
        }

        // Texte saisi ou choisi dans la liste, même avant l'affichage de la fenêtre.
        var serverText = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ComboBox.TextProperty, typeof(ComboBox));
        EventHandler changed = (_, _) => UpdateKeep();
        serverText.AddValueChanged(ServerBox, changed);
        Closed += (_, _) => serverText.RemoveValueChanged(ServerBox, changed);
        UpdateKeep();
        Loaded += (_, _) =>
        {
            ServerBox.Focus();
            (ServerBox.Template.FindName("PART_EditableTextBox", ServerBox) as TextBox)?.SelectAll();
        };
    }

    public string Server => ServerBox.Text.Trim();

    /// <summary>Le serveur est à ajouter à « Mes serveurs » après la connexion.</summary>
    public bool Keep => !_adding && KeepPanel.Visibility == Visibility.Visible && KeepBox.IsChecked == true && Server.Length > 0;

    /// <summary>Case « Garder » cochée : choix mémorisé pour la prochaine fois.</summary>
    public bool KeepChoice => KeepBox.IsChecked == true;

    public string Folder => SessionFolders.Normalize(FolderBox.Text);

    /// <summary>« Avancée… » : la fenêtre de connexion complète s'ouvre ensuite, avec ce serveur.</summary>
    public bool Advanced { get; private set; }

    /// <summary>Un serveur déjà gardé pour ce compte : la case disparaît, une ligne le dit.</summary>
    private void UpdateKeep()
    {
        if (_adding)
        {
            return;
        }

        bool saved = Server.Length > 0 && _isSaved(Server);
        KeepPanel.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
        AlreadySavedText.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        FolderPanel.IsEnabled = KeepBox.IsChecked == true;
    }

    private void OnKeepChanged(object sender, RoutedEventArgs e) => FolderPanel.IsEnabled = KeepBox.IsChecked == true;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Validate(required: !_adding))
        {
            DialogResult = true;
        }
    }

    private void OnAdvanced(object sender, RoutedEventArgs e)
    {
        if (Validate(required: false))
        {
            Advanced = true;
            DialogResult = true;
        }
    }

    /// <summary>Nom d'hôte ou adresse IP ; pour un compte limité à ses machines, l'une d'elles.</summary>
    internal bool Validate(bool required)
    {
        string? error = null;
        if (Server.Length == 0)
        {
            error = required ? Strings.ServerPromptRequired : null;
        }
        else if (Uri.CheckHostName(Server) == UriHostNameType.Unknown)
        {
            error = Strings.ServerPromptInvalid;
        }
        else if (_allowed.Count > 0 && !_allowed.Contains(Server, StringComparer.OrdinalIgnoreCase))
        {
            error = Text.Format(Strings.ServerPromptNotAllowed, Server);
        }

        ErrorText.Text = error ?? "";
        ErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (error is not null)
        {
            ServerBox.Focus();
        }

        return error is null;
    }
}
