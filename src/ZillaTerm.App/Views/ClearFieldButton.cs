using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ZillaTerm.App.Localization;

namespace ZillaTerm.App.Views;

/// <summary>
/// « ✕ » d'un champ de recherche ou de filtre : visible seulement quand le champ n'est pas vide, il le vide et y remet
/// le curseur. Placé par-dessus le champ, aligné à droite (le champ garde une marge intérieure à droite pour lui). Hors
/// de l'ordre de tabulation : au clavier, Échap vide déjà ces champs.
/// </summary>
public sealed class ClearFieldButton : Button
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(TextBox), typeof(ClearFieldButton), new PropertyMetadata(null, OnTargetChanged));

    public ClearFieldButton()
    {
        SetResourceReference(StyleProperty, "ClearFieldButton");
        Focusable = false;
        IsTabStop = false;
        Visibility = Visibility.Collapsed;
        ToolTip = Strings.ClearField;
        AutomationProperties.SetName(this, Strings.ClearField);
    }

    /// <summary>Champ vidé par le bouton.</summary>
    public TextBox? Target
    {
        get => (TextBox?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (Target is { } target)
        {
            target.Clear();
            target.Focus();
        }
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (ClearFieldButton)d;
        if (e.OldValue is TextBox old)
        {
            old.TextChanged -= button.OnTextChanged;
            old.IsEnabledChanged -= button.OnEnabledChanged;
        }

        if (e.NewValue is TextBox target)
        {
            target.TextChanged += button.OnTextChanged;
            target.IsEnabledChanged += button.OnEnabledChanged;
        }

        button.Update();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => Update();

    private void OnEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Update();

    private void Update() =>
        Visibility = Target is { IsEnabled: true, IsReadOnly: false } target && target.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}
