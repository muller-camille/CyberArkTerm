using System.Globalization;
using System.Windows;
using CyberArkTerm.Core;
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
        PsmpBox.Text = settings.PsmpAddress;
        PortBox.Text = settings.PsmpPort.ToString(CultureInfo.InvariantCulture);
        PreferSshBox.IsChecked = settings.PreferSshForUnix;
        SshInAppBox.IsChecked = settings.SshInApp;
        FollowBox.IsChecked = settings.FollowTerminalFolder;
        (settings.UploadProtocol == TransferProtocol.Sftp ? SftpRadio : ScpRadio).IsChecked = true;
        HostKeysText.Text = settings.KnownHosts.Count == 0
            ? "Aucune clé de PSMP mémorisée."
            : $"Clés de PSMP acceptées : {string.Join(", ", settings.KnownHosts.Keys)}";
        ComponentsText.Text = settings.ComponentByPlatform.Count == 0
            ? "Aucun : le composant est déduit de la plateforme (PSM-RDP pour Windows, PSM-SSH pour Unix...)."
            : string.Join(", ", settings.ComponentByPlatform.Select(kv => $"{kv.Key} : {kv.Value}"));
        Loaded += (_, _) => PsmpBox.Focus();
    }

    private void OnForgetComponents(object sender, RoutedEventArgs e)
    {
        _forgetComponents = true;
        ComponentsText.Text = "Seront oubliés à l'enregistrement.";
    }

    private void OnForgetHostKeys(object sender, RoutedEventArgs e)
    {
        _forgetHostKeys = true;
        HostKeysText.Text = "Seront oubliées à l'enregistrement : l'empreinte sera redemandée à la prochaine connexion.";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var host = PsmpBox.Text.Trim();
        if (host.Length > 0 && Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            ShowError("Adresse PSMP invalide : saisissez un nom d'hôte ou une adresse IP.");
            return;
        }

        if (!int.TryParse(PortBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            ShowError("Port invalide (1 à 65535).");
            return;
        }

        _settings.PsmpAddress = host;
        _settings.PsmpPort = port;
        _settings.PreferSshForUnix = PreferSshBox.IsChecked == true;
        _settings.SshInApp = SshInAppBox.IsChecked == true;
        _settings.FollowTerminalFolder = FollowBox.IsChecked == true;
        _settings.UploadProtocol = SftpRadio.IsChecked == true ? TransferProtocol.Sftp : TransferProtocol.Scp;
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

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
