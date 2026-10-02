using System.Windows;
using System.Windows.Controls;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App.Views;

/// <summary>
/// Droits Unix (chmod) d'un ou plusieurs éléments : cases rwx, bits spéciaux et valeur octale synchronisés,
/// propagation au contenu des dossiers.
/// </summary>
public partial class PermissionsDialog : Window
{
    private readonly CheckBox[] _boxes = new CheckBox[9];
    private readonly int _initialSpecial;
    private bool _updating;

    /// <param name="target">Nom de l'élément, ou « N éléments ».</param>
    /// <param name="mode">Droits proposés au départ (ceux du premier élément, bits spéciaux compris).</param>
    /// <param name="hasDirectory">Un dossier fait partie de la sélection : la propagation au contenu est proposée.</param>
    public PermissionsDialog(string target, string directory, int mode, bool hasDirectory)
    {
        InitializeComponent();
        TargetText.Text = target;
        PathText.Text = directory;
        RecursivePanel.Visibility = hasDirectory ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < 9; i++)
        {
            var box = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(box, 1 + i / 3);
            Grid.SetColumn(box, 1 + i % 3);
            box.Click += (_, _) => ShowMode(ModeFromBoxes(), updateOctal: true);
            BitsGrid.Children.Add(box);
            _boxes[i] = box;
        }

        _initialSpecial = mode & UnixPermissions.SpecialMask;
        ShowMode(mode & (UnixPermissions.RwxMask | UnixPermissions.SpecialMask), updateOctal: true);
        Loaded += (_, _) =>
        {
            OctalBox.Focus();
            OctalBox.SelectAll();
        };
    }

    /// <summary>Droits choisis (rwx et bits spéciaux), valables quand <see cref="Window.DialogResult"/> vaut <c>true</c>.</summary>
    public int Mode { get; private set; }

    /// <summary>Les bits spéciaux ont été changés : ils sont alors appliqués aux éléments sélectionnés.</summary>
    public bool SpecialChanged => (Mode & UnixPermissions.SpecialMask) != _initialSpecial;

    /// <summary>Appliquer aussi au contenu des dossiers (sous-dossiers et fichiers).</summary>
    public bool Recursive => RecursivePanel.Visibility == Visibility.Visible && RecursiveBox.IsChecked == true;

    /// <summary>Dans le contenu, x seulement pour les dossiers et les fichiers déjà exécutables.</summary>
    public bool ExecuteOnlyIfAlready => SmartExecuteBox.IsChecked == true;

    private int ModeFromBoxes() =>
        Enumerable.Range(0, 9).Where(i => _boxes[i].IsChecked == true).Sum(i => UnixPermissions.Bits[i])
        | (SetUidBox.IsChecked == true ? UnixPermissions.SetUid : 0)
        | (SetGidBox.IsChecked == true ? UnixPermissions.SetGid : 0)
        | (StickyBox.IsChecked == true ? UnixPermissions.Sticky : 0);

    private void ShowMode(int mode, bool updateOctal)
    {
        _updating = true;
        for (int i = 0; i < 9; i++)
        {
            _boxes[i].IsChecked = (mode & UnixPermissions.Bits[i]) != 0;
        }

        SetUidBox.IsChecked = (mode & UnixPermissions.SetUid) != 0;
        SetGidBox.IsChecked = (mode & UnixPermissions.SetGid) != 0;
        StickyBox.IsChecked = (mode & UnixPermissions.Sticky) != 0;
        if (updateOctal)
        {
            OctalBox.Text = UnixPermissions.ToOctal(mode);
        }

        SymbolicText.Text = UnixPermissions.ToSymbolic(mode);
        ErrorText.Visibility = Visibility.Collapsed;
        _updating = false;
    }

    private void OnSpecialClick(object sender, RoutedEventArgs e) => ShowMode(ModeFromBoxes(), updateOctal: true);

    private void OnRecursiveClick(object sender, RoutedEventArgs e) => SmartExecuteBox.IsEnabled = RecursiveBox.IsChecked == true;

    private void OnOctalChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updating && UnixPermissions.TryParseOctal(OctalBox.Text, out var mode))
        {
            ShowMode(mode, updateOctal: false);
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!UnixPermissions.TryParseOctal(OctalBox.Text, out var mode))
        {
            ErrorText.Text = Strings.PermissionsInvalid;
            ErrorText.Visibility = Visibility.Visible;
            OctalBox.Focus();
            return;
        }

        Mode = mode;
        DialogResult = true;
    }
}
