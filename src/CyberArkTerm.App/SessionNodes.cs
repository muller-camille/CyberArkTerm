using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CyberArkTerm.Core;

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
        : $"{Account.PlatformId} · {Account.SafeName}\nMachines : {Account.RemoteMachines}";
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
            _ => "IconOther",
        };
        return Application.Current.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

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
