using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CyberArkTerm.App.Tests;

/// <summary>
/// Touches d'accès (lettre soulignée, Alt+lettre) : deux entrées d'un même menu ou deux contrôles d'une même fenêtre
/// ne partagent pas la même lettre, dans aucune langue. Les groupes sont lus dans le XAML ; les menus construits dans le
/// code sont listés ici.
/// </summary>
public sealed partial class AccessKeysTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] SessionActions =
        ["MenuTabReconnect", "MenuTabDuplicate", "MenuTabDetach", "MenuTabAddParallel", "MenuTabRemoveParallel", "MenuTabClose"];

    /// <summary>Menus construits dans le code (MainWindow.Ssh.cs, TerminalView.cs, MainWindow.Parallel.cs…).</summary>
    private static readonly (string Name, string[] Keys)[] CodeMenus =
    [
        ("TabMenu", [.. SessionActions, "MenuTerminalSearch", "MenuTerminalSave", "MenuTabCloseOthers"]),
        ("TerminalMenu", [.. SessionActions, "MenuTerminalCopy", "MenuTerminalPaste", "MenuTerminalSelectAll", "MenuTerminalSearch",
            "MenuTerminalSave", "MenuTerminalClear", "MenuTerminalFont"]),
        ("FontMenu", ["MenuFontBigger", "MenuFontSmaller", "MenuFontDefault"]),
        ("ParallelTabMenu", ["MenuTabDetach", "ParallelClose"]),
        ("KeePassConnectAs", ["MenuKeePassSsh", "MenuKeePassRdp", "MenuKeePassVnc", "MenuKeePassSftp", "MenuKeePassFtp"]),
        ("TailFiles", ["TailFilesAll", "TailFilesForget"]),
    ];

    [Fact]
    public void NoMenuOrWindowSharesAnAccessKey()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "CyberArkTerm.App");
        var groups = new List<(string Name, HashSet<string> Keys)>();
        foreach (var path in Directory.GetFiles(Path.Combine(app, "Views"), "*.xaml"))
        {
            groups.AddRange(Groups(Path.GetFileName(path), XDocument.Load(path).Root!));
        }

        groups.AddRange(CodeMenus.Select(m => ($"code:{m.Name}", m.Keys.ToHashSet())));
        var problems = new List<string>();
        foreach (var suffix in new[] { "", ".fr", ".it" })
        {
            var texts = XDocument.Load(Path.Combine(app, "Localization", $"Strings{suffix}.resx")).Root!.Elements("data")
                .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
            foreach (var (name, keys) in groups)
            {
                foreach (var clash in keys.Where(texts.ContainsKey)
                             .Select(k => (Key: k, Letter: AccessKey(texts[k])))
                             .Where(x => x.Letter is not null)
                             .GroupBy(x => x.Letter)
                             .Where(g => g.Count() > 1))
                {
                    problems.Add($"{name} [{(suffix.Length == 0 ? "en" : suffix[1..])}] '{clash.Key}' : {string.Join(", ", clash.Select(x => x.Key))}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Lettre soulignée : celle qui suit le premier « _ » (« __ » est un « _ » littéral).</summary>
    internal static char? AccessKey(string text)
    {
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '_')
            {
                continue;
            }

            if (text[i + 1] == '_')
            {
                i++;
                continue;
            }

            return char.ToLowerInvariant(text[i + 1]);
        }

        return null;
    }

    [Fact]
    public void AccessKeyReadsTheFirstSingleUnderscore()
    {
        Assert.Equal('f', AccessKey("_Fichier"));
        Assert.Equal('p', AccessKey("Mot de _passe"));
        Assert.Equal('t', AccessKey("a__b _test"));
        Assert.Null(AccessKey("Aucun"));
    }

    /// <summary>Un groupe par menu (sous-menus compris) et un groupe pour le reste de la fenêtre.</summary>
    private static IEnumerable<(string, HashSet<string>)> Groups(string file, XElement root)
    {
        var inMenus = new HashSet<XElement>();
        var groups = new List<(string, HashSet<string>)>();
        void WalkMenu(XElement menu, string name)
        {
            var items = menu.Elements(Wpf + "MenuItem").ToList();
            groups.Add(($"{file}:{name}", items.Select(i => KeyOf((string?)i.Attribute("Header"))).OfType<string>().ToHashSet()));
            foreach (var item in items.Where(i => i.Elements(Wpf + "MenuItem").Any()))
            {
                WalkMenu(item, $"{name}>{KeyOf((string?)item.Attribute("Header"))}");
            }
        }

        foreach (var menu in root.Descendants(Wpf + "ContextMenu"))
        {
            WalkMenu(menu, (string?)menu.Attribute(Xaml + "Key") ?? "menu");
            inMenus.UnionWith(menu.DescendantsAndSelf());
        }

        string[] controls = ["Label", "AccessText", "Button", "CheckBox", "RadioButton", "TabItem", "GroupBox"];
        var window = root.Descendants()
            .Where(e => !inMenus.Contains(e) && controls.Contains(e.Name.LocalName) && e.Name.Namespace == Wpf)
            .SelectMany(e => new[] { "Content", "Text", "Header" }.Select(a => KeyOf((string?)e.Attribute(a))))
            .OfType<string>()
            .ToHashSet();
        groups.Add((file, window));
        return groups;
    }

    private static string? KeyOf(string? value) => value is not null && StaticString().Match(value) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"^\{x:Static loc:Strings\.(\w+)\}$")]
    private static partial Regex StaticString();

    internal static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "CyberArkTerm.App", "Views")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("src/CyberArkTerm.App/Views");
    }
}
