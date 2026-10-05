using System.Windows;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;

namespace CyberArkTerm.App.Views;

/// <summary>Paramètres d'une connexion : type, composant PSM, machine cible, motif et ticket.</summary>
public partial class ConnectDialog : Window
{
    private readonly PvwaAccount _account;
    private readonly AppSettings _settings;
    private readonly string _vaultUser;

    /// <param name="componentError">Le PVWA ne connaît pas le composant demandé pour ce compte (EPVWA093E).</param>
    public ConnectDialog(PvwaAccount account, ConnectRequest initial, AppSettings settings, string vaultUser, string? error,
        bool componentError = false)
    {
        InitializeComponent();
        _account = account;
        _settings = settings;
        _vaultUser = vaultUser;

        KindIcon.Source = (System.Windows.Media.ImageSource?)new KindIconConverter().Convert(account, typeof(object), null, System.Globalization.CultureInfo.CurrentCulture);
        TitleText.Text = $"{account.UserName}@{account.Address}";
        DetailsText.Text = Text.Format(Strings.PlatformAndSafe, account.PlatformId, account.SafeName);

        ComponentBox.ItemsSource = AccountClassifier.CommonComponents;
        ComponentBox.Text = initial.Component;
        MachineBox.ItemsSource = AccountClassifier.RemoteMachineList(account);
        // Compte de domaine : on propose la première machine autorisée, modifiable.
        MachineBox.Text = initial.RemoteMachine ?? AccountClassifier.RemoteMachineList(account).FirstOrDefault() ?? "";
        ReasonBox.Text = initial.Reason ?? "";
        TicketSystemBox.Text = initial.TicketingSystem ?? "";
        TicketIdBox.Text = initial.TicketId ?? "";
        RememberBox.Content = Text.Format(Strings.RememberComponent, account.PlatformId);
        // Composant refusé : celui saisi à la place sera retenu pour la plateforme (décochable).
        RememberBox.IsChecked = initial.RememberComponent || componentError;

        bool sshAvailable = !string.IsNullOrWhiteSpace(settings.PsmpAddress);
        SshRadio.IsEnabled = sshAvailable;
        SshRadio.ToolTip = sshAvailable ? null : Strings.SetPsmpAddress;
        (initial.Mode == ConnectMode.Ssh && sshAvailable ? SshRadio : PsmRadio).IsChecked = true;

        MachineBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler((_, _) => UpdateSshHint()));

        if (!string.IsNullOrWhiteSpace(error))
        {
            ErrorText.Text = error;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            if (componentError || (error is not null && error.Contains("component", StringComparison.OrdinalIgnoreCase)))
            {
                ComponentBox.Focus();
                if (ComponentBox.Template.FindName("PART_EditableTextBox", ComponentBox) is System.Windows.Controls.TextBox text)
                {
                    text.SelectAll();
                }
            }
            else if (error is not null)
            {
                ReasonBox.Focus();
            }
            else if (AccountClassifier.NeedsRemoteMachine(account))
            {
                MachineBox.Focus();
            }
            else
            {
                ComponentBox.Focus();
            }
        };
    }

    public ConnectRequest? Result { get; private set; }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (ComponentBox is null)
        {
            return;
        }

        bool psm = PsmRadio.IsChecked == true;
        ComponentBox.IsEnabled = psm;
        ReasonBox.IsEnabled = psm;
        TicketSystemBox.IsEnabled = psm;
        TicketIdBox.IsEnabled = psm;
        RememberBox.IsEnabled = psm;
        UpdateSshHint();
    }

    private void UpdateSshHint()
    {
        if (PsmRadio.IsChecked == true)
        {
            SshHint.Text = Strings.PsmHint;
            return;
        }

        try
        {
            var login = PsmpTarget.BuildLogin(_vaultUser, _account, MachineBox.Text);
            var target = $"{login}@{_settings.PsmpAddress}";
            SshHint.Text = Text.Format(Strings.SshCommandHint, target.Contains(' ') ? $"\"{target}\"" : target);
        }
        catch (ArgumentException ex)
        {
            SshHint.Text = ex.Message;
        }
    }

    private void OnConnect(object sender, RoutedEventArgs e)
    {
        bool psm = PsmRadio.IsChecked == true;
        string component = ComponentBox.Text.Trim();
        if (psm && component.Length == 0)
        {
            ComponentBox.Focus();
            return;
        }

        Result = new ConnectRequest(
            psm ? ConnectMode.Psm : ConnectMode.Ssh,
            component,
            NullIfEmpty(MachineBox.Text),
            NullIfEmpty(ReasonBox.Text),
            NullIfEmpty(TicketSystemBox.Text),
            NullIfEmpty(TicketIdBox.Text),
            psm && RememberBox.IsChecked == true);
        DialogResult = true;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
