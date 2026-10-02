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
}

/// <summary>Compte affiché dans l'arbre des sessions.</summary>
public sealed class AccountNode(PvwaAccount account)
{
    public PvwaAccount Account { get; } = account;

    public string Title => $"{Account.UserName}@{Account.Address}";

    public string Details => string.IsNullOrWhiteSpace(Account.RemoteMachines)
        ? $"{Account.PlatformId} · {Account.SafeName}"
        : $"{Account.PlatformId} · {Account.SafeName}\n" + Text.Format(Strings.AccountMachines, Account.RemoteMachines);
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
            RecentSession r => r.Mode == RecentModes.Ssh ? "IconSsh" : "IconConnect",
            SavedSessionNode n => n.Account is { } a ? IconFor(AccountClassifier.Classify(a)) : IconFor(KindOf(n.Session)),
            SavedSession s => IconFor(KindOf(s)),
            KeePassFolderNode => "IconKeePass",
            KeePassHintNode => "IconPermissions",
            KeePassEntryNode e => e.Target.Protocol switch
            {
                RemoteProtocol.Ssh => "IconUnix",
                RemoteProtocol.Rdp => "IconWindows",
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
    public const string Ssh = "SSH";
}

/// <summary>Dossier de l'onglet « Courants ».</summary>
public sealed class SavedFolderNode(string path, List<object> children, bool isExpanded)
{
    public string Path { get; } = path;

    public string Name => SessionFolders.Name(Path);

    public List<object> Children { get; } = children;

    public int Count { get; init; }

    public bool IsExpanded { get; set; } = isExpanded;
}

/// <summary>Serveur de l'onglet « Courants » ; <see cref="Account"/> est null si le compte n'est plus visible dans CyberArk.</summary>
public sealed class SavedSessionNode(SavedSession session, PvwaAccount? account)
{
    public SavedSession Session { get; } = session;

    public PvwaAccount? Account { get; } = account;

    public string Title => Session.Name;

    public string ModeText => Session.Mode == ConnectMode.Ssh ? "SSH" : Session.Component ?? "PSM";

    public double Opacity => Account is null ? 0.5 : 1;

    public bool IsExpanded { get; set; }

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
