using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CyberArkTerm.App.Localization;
using CyberArkTerm.Core;
using CyberArkTerm.Core.KeePass;
using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.App;

/// <summary>Dossier de l'arbre des sessions (un safe, une plateforme...).</summary>
public sealed class FolderNode(string name, IReadOnlyList<AccountNode> children, bool isExpanded)
{
    public string Name { get; } = name;

    public IReadOnlyList<AccountNode> Children { get; } = children;

    public int Count => Children.Count;

    public bool IsExpanded { get; set; } = isExpanded;

    /// <summary>Nom lu par les lecteurs d'écran (les éléments d'arbre liés à des objets utilisent ToString).</summary>
    public override string ToString() => $"{Name} ({Count})";
}

/// <summary>Compte affiché dans l'arbre des sessions.</summary>
public sealed class AccountNode(PvwaAccount account)
{
    public PvwaAccount Account { get; } = account;

    public string Title => $"{Account.UserName}@{Account.Address}";

    /// <summary>La dernière opération du CPM (changement, vérification, réconciliation) a échoué.</summary>
    public bool CpmFailed => Account.SecretManagement?.Failed == true;

    /// <summary>Nom lu par les lecteurs d'écran : compte, plateforme, safe, et l'échec du CPM (signalé à l'écran par un triangle).</summary>
    public override string ToString() =>
        $"{Title}, {Account.PlatformId}, {Account.SafeName}" + (CpmFailed ? ", " + Strings.A11yCpmFailed : "");

    public string Details
    {
        get
        {
            var lines = new List<string> { $"{Account.PlatformId} · {Account.SafeName}" };
            if (!string.IsNullOrWhiteSpace(Account.RemoteMachines))
            {
                lines.Add(Text.Format(Strings.AccountMachines, Account.RemoteMachines));
            }

            lines.AddRange(CpmText.Describe(Account.SecretManagement));
            return string.Join("\n", lines);
        }
    }
}

/// <summary>État du mot de passe vu par le CPM, pour les info-bulles.</summary>
internal static class CpmText
{
    public static IEnumerable<string> Describe(SecretManagement? management)
    {
        if (management is null)
        {
            yield break;
        }

        yield return management.AutomaticManagementEnabled ? Strings.CpmManagedAuto
            : string.IsNullOrWhiteSpace(management.ManualManagementReason) ? Strings.CpmManagedManual
            : Text.Format(Strings.CpmManagedManualReason, management.ManualManagementReason);
        if (management.Failed)
        {
            yield return Strings.CpmLastFailed;
        }

        if (management.LastModified is { } changed)
        {
            yield return Text.Format(Strings.CpmLastChanged, changed.ToString("g", CultureInfo.CurrentCulture));
        }

        if (management.LastVerified is { } verified)
        {
            yield return Text.Format(Strings.CpmLastVerified, verified.ToString("g", CultureInfo.CurrentCulture));
        }

        if (management.LastReconciled is { } reconciled)
        {
            yield return Text.Format(Strings.CpmLastReconciled, reconciled.ToString("g", CultureInfo.CurrentCulture));
        }
    }
}

/// <summary>Icône selon le type de cible (compte, nœud de l'arbre, type ou session récente).</summary>
public sealed class KindIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            AccountNode n => IconFor(AccountClassifier.Classify(n.Account)),
            PvwaAccount a => IconFor(AccountClassifier.Classify(a)),
            AccountKind k => IconFor(k),
            RecentSession r => r.Mode == RecentModes.Ssh ? "IconSsh" : r.Mode == RecentModes.Sftp ? "IconFiles" : "IconConnect",
            SavedSessionNode n => n.Account is { } a ? IconFor(AccountClassifier.Classify(a)) : IconFor(KindOf(n.Session)),
            SavedSession s => IconFor(KindOf(s)),
            SharedServerNode n => n.Account is { } a ? IconFor(AccountClassifier.Classify(a)) : IconFor(KindOf(n.Session)),
            KeePassFolderNode => "IconKeePass",
            KeePassHintNode => "IconPermissions",
            KeePassEntryNode e => e.Target.Protocol switch
            {
                RemoteProtocol.Ssh => "IconUnix",
                RemoteProtocol.Rdp => "IconWindows",
                RemoteProtocol.Vnc => "IconConnect",
                RemoteProtocol.Sftp or RemoteProtocol.Ftp or RemoteProtocol.Ftpes or RemoteProtocol.Ftps => "IconFiles",
                _ => "IconOther",
            },
            _ => "IconOther",
        };
        return Application.Current.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static AccountKind KindOf(SavedSession s) => AccountClassifier.Classify(new PvwaAccount { PlatformId = s.PlatformId });

    private static string IconFor(AccountKind kind) => kind switch
    {
        AccountKind.Windows => "IconWindows",
        AccountKind.Unix => "IconUnix",
        AccountKind.Database => "IconDatabase",
        AccountKind.Network => "IconNetwork",
        _ => "IconOther",
    };
}

internal static class RecentModes
{
    public const string Ssh = RecentSession.SshMode;
    public const string Sftp = RecentSession.SftpMode;
}

/// <summary>Dossier de l'onglet « Courants ».</summary>
public sealed class SavedFolderNode(string path, List<object> children, bool isExpanded)
{
    public string Path { get; } = path;

    public string Name => SessionFolders.Name(Path);

    public List<object> Children { get; } = children;

    public int Count { get; init; }

    public bool IsExpanded { get; set; } = isExpanded;

    public override string ToString() => $"{Name} ({Count})";
}

/// <summary>Serveur de l'onglet « Courants » ; <see cref="Account"/> est null si le compte n'est plus visible dans CyberArk.</summary>
public sealed class SavedSessionNode(SavedSession session, PvwaAccount? account) : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isMarked;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public SavedSession Session { get; } = session;

    /// <summary>Choisi avec Ctrl+clic ou Maj+clic, pour ouvrir plusieurs serveurs ensemble.</summary>
    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked != value)
            {
                _isMarked = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsMarked)));
            }
        }
    }

    public PvwaAccount? Account { get; } = account;

    public string Title => Session.Name;

    public string ModeText => Session.Mode == ConnectMode.Psm ? Session.Component ?? "PSM" : SessionLibrary.ModeName(Session.Mode);

    public double Opacity => Account is null ? 0.5 : 1;

    public bool IsExpanded { get; set; }

    public override string ToString() =>
        $"{Title}, {ModeText}" + (IsMarked ? ", " + Strings.A11yMarked : "") + (Account is null ? ", " + Strings.A11yUnavailable : "");

    public string Details
    {
        get
        {
            var lines = new List<string> { $"{Session.UserName}@{Session.Address}", $"{Session.PlatformId} · {Session.SafeName}" };
            if (!string.IsNullOrWhiteSpace(Session.RemoteMachine))
            {
                lines.Add(Text.Format(Strings.SavedTargetMachine, Session.RemoteMachine));
            }

            if (!string.IsNullOrWhiteSpace(Session.StartDirectory))
            {
                lines.Add(Text.Format(Strings.SavedSftpFolder, Session.StartDirectory));
            }

            if (Account is null)
            {
                lines.Add(Strings.SavedAccountMissing);
            }
            else
            {
                lines.AddRange(CpmText.Describe(Account.SecretManagement));
            }

            return string.Join("\n", lines);
        }
    }
}

/// <summary>Icône d'un fichier distant : dossier, lien, fichier, « .. ».</summary>
public sealed class FileIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource(value switch
        {
            RemoteEntry { IsParentLink: true } => "IconUp",
            RemoteEntry { IsDirectory: true } => "IconFolder",
            RemoteEntry { IsSymbolicLink: true } => "IconFileLink",
            _ => "IconFile",
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
