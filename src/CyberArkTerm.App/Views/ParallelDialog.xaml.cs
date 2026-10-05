using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.App.Localization;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core.Terminal;

namespace CyberArkTerm.App.Views;

/// <summary>Choix des sessions SSH ouvertes à afficher dans la vue parallèle (au plus 8).</summary>
public partial class ParallelDialog : Window
{
    private readonly List<(SshSession Session, CheckBox Box)> _choices = [];

    /// <param name="sessions">Sessions SSH ouvertes, dans l'ordre des onglets.</param>
    /// <param name="selected">Sessions cochées au départ (celles déjà dans la vue, ou celle de l'onglet actif).</param>
    public ParallelDialog(IReadOnlyList<SshSession> sessions, IReadOnlyCollection<SshSession> selected)
    {
        InitializeComponent();
        foreach (var session in sessions)
        {
            var box = new CheckBox
            {
                Content = new TextBlock { Text = session.Label, TextTrimming = TextTrimming.CharacterEllipsis },
                IsChecked = selected.Contains(session),
                Margin = new Thickness(0, 2, 0, 2),
            };
            box.Click += (_, _) => Update();
            _choices.Add((session, box));
            SessionsPanel.Children.Add(box);
        }

        // Au-delà de 8 sessions cochées au départ, les dernières ne sont pas gardées.
        foreach (var (_, box) in _choices.Where(c => c.Box.IsChecked == true).Skip(ParallelLayout.MaxSessions))
        {
            box.IsChecked = false;
        }

        Update();
        Loaded += (_, _) => (_choices.FirstOrDefault(c => c.Box.IsChecked != true).Box ?? _choices.FirstOrDefault().Box)?.Focus();
    }

    /// <summary>Sessions choisies, dans l'ordre des onglets.</summary>
    public IReadOnlyList<SshSession> Selected => _choices.Where(c => c.Box.IsChecked == true).Select(c => c.Session).ToList();

    /// <summary>Au plus 8 cases cochées : les autres sont grisées une fois la limite atteinte.</summary>
    private void Update()
    {
        int count = _choices.Count(c => c.Box.IsChecked == true);
        foreach (var (_, box) in _choices)
        {
            box.IsEnabled = box.IsChecked == true || count < ParallelLayout.MaxSessions;
        }

        CountText.Text = Text.Format(Strings.ParallelSelected, count, ParallelLayout.MaxSessions);
    }

    private void OnShow(object sender, RoutedEventArgs e) => DialogResult = true;
}
