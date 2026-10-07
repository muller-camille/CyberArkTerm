using System.Text;
using ZillaTerm.Core.KeePass;

namespace ZillaTerm.Core.Tests.KeePass;

public class KeePassDatabaseTests
{
    /// <summary>Un champ en double ou sans nom (écrit par un autre outil) ne doit pas rendre tout le coffre illisible.</summary>
    [Fact]
    public void DuplicateOrUnnamedFieldsDoNotBreakTheVault()
    {
        const string Xml = """
            <KeePassFile><Meta /><Root><Group><UUID>AAAAAAAAAAAAAAAAAAAAAA==</UUID><Name>Racine</Name>
            <Entry><UUID>AQEBAQEBAQEBAQEBAQEBAQ==</UUID>
            <String><Key>Title</Key><Value>srv-lnx01</Value></String>
            <String><Key>Port</Key><Value>22</Value></String>
            <String><Key>Port</Key><Value>2222</Value></String>
            <String><Value>sans nom</Value></String>
            </Entry></Group></Root></KeePassFile>
            """;
        using var db = new KeePassDatabase();
        db.LoadXml(Encoding.UTF8.GetBytes(Xml), new ChaCha20(new byte[32], new byte[12]));

        var entry = Assert.Single(db.Entries);
        Assert.Equal("srv-lnx01", entry.Title);
        Assert.Equal("22", Assert.Single(entry.CustomFields).Value);
    }

    /// <summary>Dossiers imbriqués sans fin (fichier forgé) : refusés à la lecture au lieu d'épuiser la pile.</summary>
    [Theory]
    [InlineData(KeePassDatabase.MaxGroupDepth, true)]
    [InlineData(KeePassDatabase.MaxGroupDepth + 1, false)]
    [InlineData(20_000, false)]
    public void DeeplyNestedGroupsAreRefused(int depth, bool accepted)
    {
        var xml = new StringBuilder("<KeePassFile><Meta /><Root>");
        for (int i = 0; i < depth; i++)
        {
            xml.Append("<Group><Name>g</Name>");
        }

        for (int i = 0; i < depth; i++)
        {
            xml.Append("</Group>");
        }

        xml.Append("</Root></KeePassFile>");
        using var db = new KeePassDatabase();
        var load = () => db.LoadXml(Encoding.UTF8.GetBytes(xml.ToString()), new ChaCha20(new byte[32], new byte[12]));
        if (accepted)
        {
            load();
            Assert.Equal(depth, db.Groups.Count);
        }
        else
        {
            Assert.Throws<FormatException>(load);
        }
    }
}
