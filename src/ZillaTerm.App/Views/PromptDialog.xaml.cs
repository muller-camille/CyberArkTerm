using System.Windows;
using ZillaTerm.App.Localization;

namespace ZillaTerm.App.Views;

/// <summary>Question posée par le PSMP lors de l'authentification (mot de passe, code MFA...).</summary>
public partial class PromptDialog : Window
{
    private readonly bool _echo;

    /// <param name="session">Session qui pose la question (« compte → cible ») ; null si inconnue.</param>
    /// <param name="hint">Aide selon la question (mot de passe CyberArk, code MFA…).</param>
    /// <param name="direct">Serveur joint directement (accès d'urgence) : pas de PSMP en jeu.</param>
    public PromptDialog(string instruction, string prompt, bool echo, string? session = null, string? hint = null, bool direct = false)
    {
        InitializeComponent();
        _echo = echo;
        InstructionText.Text = string.IsNullOrWhiteSpace(instruction)
            ? (direct ? Strings.PromptDefaultInstructionServer : Strings.PromptDefaultInstruction)
            : instruction.Trim();
        PromptText.Text = prompt.Trim();
        if (!string.IsNullOrWhiteSpace(session))
        {
            SessionText.Text = Text.Format(Strings.PromptSession, session);
            SessionText.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(hint))
        {
            HintText.Text = hint;
            HintText.Visibility = Visibility.Visible;
        }

        // Nom accessible des champs : la question du serveur.
        System.Windows.Automation.AutomationProperties.SetName(SecretBox, PromptText.Text);
        System.Windows.Automation.AutomationProperties.SetName(TextBox, PromptText.Text);
        SecretBox.Visibility = echo ? Visibility.Collapsed : Visibility.Visible;
        TextBox.Visibility = echo ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            Activate();
            if (echo)
            {
                TextBox.Focus();
            }
            else
            {
                SecretBox.Focus();
            }
        };
    }

    public string Answer { get; private set; } = "";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Answer = _echo ? TextBox.Text : SecretBox.Password;
        DialogResult = true;
    }
}
