using System.Windows;
using CyberArkTerm.App.Localization;

namespace CyberArkTerm.App.Views;

/// <summary>Question posée par le PSMP lors de l'authentification (mot de passe, code MFA...).</summary>
public partial class PromptDialog : Window
{
    private readonly bool _echo;

    public PromptDialog(string instruction, string prompt, bool echo)
    {
        InitializeComponent();
        _echo = echo;
        InstructionText.Text = string.IsNullOrWhiteSpace(instruction) ? Strings.PromptDefaultInstruction : instruction.Trim();
        PromptText.Text = prompt.Trim();
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
