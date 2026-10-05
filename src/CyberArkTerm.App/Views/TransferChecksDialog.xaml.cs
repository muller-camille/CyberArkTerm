using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Vérification des fichiers d'un transfert : pour chacun, taille, somme SHA-256 sur ce poste et sur le serveur, et
/// résultat (identique, différent, non vérifié). Les sommes peuvent être copiées au format de <c>sha256sum -c</c>.
/// </summary>
public partial class TransferChecksDialog : Window
{
    private readonly IReadOnlyList<TransferCheck> _checks;
    private readonly bool _upload;

    /// <param name="upload">Envoi vers le serveur (sinon téléchargement) : explique comment les sommes ont été obtenues.</param>
    public TransferChecksDialog(IReadOnlyList<TransferCheck> checks, bool upload)
    {
        InitializeComponent();
        _checks = checks;
        _upload = upload;
        HeadingText.Text = Heading(checks);
        HeadingText.Foreground = new SolidColorBrush(checks.Any(c => c.Verified && !c.Matches) ? Colors.Firebrick
            : checks.Any(c => !c.Verified) ? Color.FromRgb(0xB2, 0x6A, 0x00)
            : Color.FromRgb(0x1E, 0x7B, 0x45));
        IntroText.Text = upload ? Strings.ChecksIntroUpload : Strings.ChecksIntroDownload;
        BuildColumns();
        // Les fichiers à revoir d'abord.
        ChecksGrid.ItemsSource = checks.OrderBy(c => c.Matches ? 2 : c.Verified ? 0 : 1).ToList();
    }

    /// <summary>Résumé : tous identiques, ou combien diffèrent et combien n'ont pas pu être vérifiés.</summary>
    public static string Heading(IReadOnlyList<TransferCheck> checks)
    {
        int different = checks.Count(c => c.Verified && !c.Matches);
        int unverified = checks.Count(c => !c.Verified);
        var lines = new List<string>();
        if (different > 0)
        {
            lines.Add(Text.Format(Strings.ChecksSomeDiffer, different, checks.Count));
        }

        if (unverified > 0)
        {
            lines.Add(Text.Format(Strings.ChecksSomeUnverified, unverified, checks.Count));
        }

        return lines.Count == 0 ? Text.Format(Strings.ChecksAllOk, checks.Count) : string.Join(Environment.NewLine, lines);
    }

    /// <summary>Texte de la colonne « Résultat ».</summary>
    public static string Result(TransferCheck check) =>
        !check.Verified ? Text.Format(Strings.ChecksUnverified, check.Error) : check.Matches ? Strings.ChecksIdentical : Strings.ChecksDifferent;

    private void BuildColumns()
    {
        var hash = (Style)FindResource("HashText");
        ChecksGrid.Columns.Add(new DataGridTextColumn
        {
            Header = Strings.ChecksColFile, Binding = new Binding(nameof(TransferCheck.RemotePath)), Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 160,
        });
        ChecksGrid.Columns.Add(new DataGridTextColumn
        {
            Header = Strings.ChecksColSize, Binding = new Binding(".") { Converter = new SizeConverter() },
        });
        ChecksGrid.Columns.Add(new DataGridTextColumn { Header = Strings.ChecksColLocal, Binding = new Binding(nameof(TransferCheck.LocalHash)), ElementStyle = hash });
        ChecksGrid.Columns.Add(new DataGridTextColumn { Header = Strings.ChecksColRemote, Binding = new Binding(nameof(TransferCheck.RemoteHash)), ElementStyle = hash });
        ChecksGrid.Columns.Add(new DataGridTextColumn
        {
            Header = Strings.ChecksColResult, Binding = new Binding(".") { Converter = new ResultConverter() }, ElementStyle = (Style)FindResource("ResultText"),
        });
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        // Somme de l'original seulement pour les fichiers vérifiés ; une ligne par fichier, fin de ligne Unix.
        var lines = _checks.Where(c => c.Verified).Select(c => c.ToSha256SumLine(_upload)).ToList();
        try
        {
            Clipboard.SetText(string.Join("\n", lines) + "\n");
            CopiedText.Text = Text.Format(Strings.ChecksCopied, lines.Count);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopiedText.Text = Strings.ClipboardBusy;
        }
    }

    private sealed class SizeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is TransferCheck check ? RemotePath.FormatSize(Math.Max(check.LocalLength, check.RemoteLength)) : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    private sealed class ResultConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is TransferCheck check ? Result(check) : "";

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
