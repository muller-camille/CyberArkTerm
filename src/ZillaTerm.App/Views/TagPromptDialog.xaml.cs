using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ZillaTerm.App.Localization;
using ZillaTerm.Core;

namespace ZillaTerm.App.Views;

/// <summary>
/// Étiquette (PROD, QA, DEV…) d'un serveur ajouté à « Mes serveurs » : celle devinée d'après son nom, son safe ou son
/// dossier est proposée, à confirmer ou changer. Annuler n'ajoute pas le serveur.
/// </summary>
public partial class TagPromptDialog : Window
{
    private readonly List<(RadioButton Button, string? Tag)> _choices = [];

    /// <param name="selected">Étiquette cochée au départ ; null : « Aucune étiquette ».</param>
    /// <param name="guessed">L'étiquette cochée a été devinée : le dire.</param>
    public TagPromptDialog(string server, IReadOnlyList<ServerTag> tags, string? selected, bool guessed)
    {
        InitializeComponent();
        HeadingText.Text = Text.Format(Strings.TagPromptHeading, server);
        foreach (var tag in tags)
        {
            var button = new RadioButton
            {
                GroupName = "ServerTag",
                Content = ServerTagView.Chip(tag, 12),
                Margin = new Thickness(0, 0, 0, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(button, tag.Name);
            Add(button, tag.Name);
        }

        Add(new RadioButton { GroupName = "ServerTag", Content = new AccessText { Text = Strings.TagNone } }, null);
        var initial = _choices.FirstOrDefault(c => string.Equals(c.Tag, selected, StringComparison.OrdinalIgnoreCase));
        (initial.Button ?? _choices[^1].Button).IsChecked = true;
        GuessText.Visibility = guessed && initial.Tag is not null ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => _choices.First(c => c.Button.IsChecked == true).Button.Focus();
    }

    /// <summary>Étiquette choisie ; null : aucune.</summary>
    public string? ChosenTag => _choices.FirstOrDefault(c => c.Button.IsChecked == true).Tag;

    private void Add(RadioButton button, string? tag)
    {
        _choices.Add((button, tag));
        Choices.Children.Add(button);
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
