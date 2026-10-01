using System.Windows;

namespace CyberArkTerm.App.Views;

/// <summary>Saisie d'un nom (dossier, renommage...).</summary>
public partial class InputDialog : Window
{
    private readonly Func<string, string?>? _validate;

    /// <param name="validate">Renvoie un message d'erreur, ou null si la valeur est acceptée.</param>
    public InputDialog(string title, string label, string initial = "", Func<string, string?>? validate = null)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = initial;
        _validate = validate;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var error = Value.Length == 0 ? "Saisissez une valeur." : _validate?.Invoke(Value);
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
